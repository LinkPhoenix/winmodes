using System.Diagnostics;
using System.Windows.Threading;
using WinModes.Core.Automation;
using WinModes.Core.Planning;
using WinModes.Core.Profiles;

namespace WinModes.App.Services;

/// <summary>
/// Opt-in automatic switching: watches for the programs, AI coding tools, power source and time ranges named in the user's rules
/// and activates the matching mode, then undoes it a little after the last trigger is gone. Off by default.
/// </summary>
/// <remarks>
/// It asks as little of Windows as it can: one light look at the process list every few seconds, nothing at all while the
/// feature is off, no switch during the first seconds after the app starts (Windows is busy signing in), one switch at a time, and no
/// new attempt for a while after a switch that was refused or failed, so the permission prompt cannot come back in a loop.
/// </remarks>
internal sealed class AutoSwitcher
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    // Right after the app starts at sign-in, Windows is still loading the user's programs; a mode switch then only slows it down.
    private static readonly TimeSpan WarmUp = TimeSpan.FromSeconds(30);

    private readonly DispatcherTimer _timer = new() { Interval = CheckInterval };
    private readonly Func<string, Func<Task<SwitchReport>>, Task<bool>> _switch;
    private readonly ProcessSampler _sampler = new();
    private AutoSwitchPlanner _planner = new();
    private DateTimeOffset? _pausedUntil;
    private bool _busy;

    /// <param name="switch">Runs a switch and reports it to the user; receives a title and the action, and tells whether it worked.</param>
    public AutoSwitcher(Func<string, Func<Task<SwitchReport>>, Task<bool>> @switch)
    {
        _switch = @switch;
        _timer.Tick += async (_, _) => await CheckAsync();
        Current = this;
    }

    /// <summary>The switcher of the running app, for the pages and the tray menu.</summary>
    public static AutoSwitcher? Current { get; private set; }

    /// <summary>Until when the user paused automatic switching; null when it is not paused.</summary>
    public DateTimeOffset? PausedUntil => _pausedUntil is { } until && until > DateTimeOffset.Now ? until : null;

    public bool IsPaused => PausedUntil is not null;

    /// <summary>Stops automatic switching for a while (null: until resumed). The mode that is on stays on.</summary>
    public void PauseFor(TimeSpan? duration)
    {
        _pausedUntil = duration is { } span ? DateTimeOffset.Now + span : DateTimeOffset.MaxValue;
        _planner.PauseUntil(_pausedUntil);
        Publish(new AutoSwitchStatus(AutoSwitchState.Paused, ModeSwitcher.ActiveMode, Until: duration is null ? null : _pausedUntil));
    }

    public void Resume()
    {
        _pausedUntil = null;
        _planner.PauseUntil(null);
        StatusChanged?.Invoke();
    }

    /// <summary>What automatic switching is doing now; raised on the thread that made the check.</summary>
    public static AutoSwitchStatus Status { get; private set; } = new(AutoSwitchState.Idle);

    public static event Action? StatusChanged;

    public void Apply(AutoSwitchSettings settings)
    {
        var enabled = settings.Enabled && settings.Rules.Count > 0;
        if (enabled && !_timer.IsEnabled)
        {
            // A fresh planner: programs already open when the feature is turned on are acted on once.
            _planner = new AutoSwitchPlanner(TimingOf(settings));
            _planner.HoldUntil(AppStartedAt() + WarmUp);
            _planner.PauseUntil(_pausedUntil);
            _timer.Start();
        }
        else if (enabled)
        {
            _planner.Timing = TimingOf(settings);
        }
        else
        {
            _timer.Stop();
            Publish(new AutoSwitchStatus(AutoSwitchState.Idle));
        }
    }

    private static AutoSwitchTiming TimingOf(AutoSwitchSettings settings) =>
        AutoSwitchTiming.Default.WithGrace(TimeSpan.FromSeconds(Math.Clamp(settings.GraceSeconds, 10, 3600)));

    private static DateTimeOffset AppStartedAt()
    {
        try
        {
            using var self = Process.GetCurrentProcess();
            return self.StartTime;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return DateTimeOffset.Now;
        }
    }

    /// <summary>Names of the running processes, as the rules expect them.</summary>
    public static HashSet<string> RunningProcessNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                names.Add(process.ProcessName);
            }
        }

        return names;
    }

    /// <summary>The process names, and the ids of the AI tools with a session open when a rule asks for them (that look is a little heavier).</summary>
    private (HashSet<string> Names, HashSet<string> Tools) Scan(bool lookForTools)
    {
        if (!lookForTools)
        {
            return (RunningProcessNames(), []);
        }

        var nodes = _sampler.Sample();
        var names = new HashSet<string>(nodes.Select(node => node.Name), StringComparer.OrdinalIgnoreCase);
        var tools = AiToolCatalog.FindSessions(nodes).Select(session => session.Tool.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (names, tools);
    }

    private static void Publish(AutoSwitchStatus status)
    {
        if (status != Status)
        {
            Status = status;
            StatusChanged?.Invoke();
        }
    }

    private async Task CheckAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            var settings = AppSettings.Load().AutoSwitch;
            if (!settings.Enabled)
            {
                return;
            }

            var (running, tools) = await Task.Run(() => Scan(AutoSwitchConditions.NeedsTools(settings.Rules)));
            var onBattery = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline;
            running.UnionWith(AutoSwitchConditions.ActiveKeys(settings.Rules, onBattery, TimeOnly.FromDateTime(DateTime.Now), tools));
            var now = DateTimeOffset.Now;
            var decision = _planner.Evaluate(settings.Rules, running, ModeSwitcher.ActiveMode, ModeSwitcher.ActiveModeIsAutomatic, settings.RevertWhenClosed, now);
            Publish(_planner.Status);
            await ActAsync(decision);
        }
        catch (Exception ex) when (ex is ProfileException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // A rule naming a deleted mode, or a process list that could not be read: skip this check.
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ActAsync(AutoSwitchDecision decision)
    {
        bool worked;
        switch (decision.Kind)
        {
            case AutoSwitchKind.Activate:
                var profile = AppServices.Store.Load(decision.Mode!);
                worked = await _switch(Loc.F("{0} mode on: {1}", profile.Label, AutoSwitchConditions.Describe(decision.Process!)),
                    () => AppServices.Switcher.ActivateAsync(profile, automatic: true));
                break;
            case AutoSwitchKind.Revert:
                var ended = decision.Process is null ? Loc.T("Back to normal") : Loc.F("Back to normal: {0}", AutoSwitchConditions.DescribeEnd(decision.Process));
                worked = await _switch(ended, AppServices.Switcher.UndoAsync);
                break;
            default:
                return;
        }

        if (!worked)
        {
            // The permission prompt was refused or the helper failed: say nothing more and try nothing for a while.
            _planner.SwitchFailed(DateTimeOffset.Now);
        }

        Publish(_planner.Status);
    }
}
