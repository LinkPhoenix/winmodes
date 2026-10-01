using System.Diagnostics;
using System.Windows.Threading;
using WinModes.Core.Automation;
using WinModes.Core.Profiles;

namespace WinModes.App.Services;

/// <summary>
/// Opt-in automatic switching: watches for the programs, power source and time ranges named in the user's rules and
/// activates the matching mode. Off by default; it does nothing until the user turns it on.
/// </summary>
internal sealed class AutoSwitcher
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    private readonly DispatcherTimer _timer = new() { Interval = CheckInterval };
    private readonly Func<string, Func<Task<SwitchReport>>, Task> _switch;
    private AutoSwitchPlanner _planner = new();
    private bool _busy;

    /// <param name="switch">Runs a switch and reports it to the user; receives a title and the action.</param>
    public AutoSwitcher(Func<string, Func<Task<SwitchReport>>, Task> @switch)
    {
        _switch = @switch;
        _timer.Tick += async (_, _) => await CheckAsync();
    }

    public void Apply(AutoSwitchSettings settings)
    {
        var enabled = settings.Enabled && settings.Rules.Count > 0;
        if (enabled && !_timer.IsEnabled)
        {
            // A fresh planner: programs already open when the feature is turned on are acted on once.
            _planner = new AutoSwitchPlanner();
            _timer.Start();
        }
        else if (!enabled)
        {
            _timer.Stop();
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

            var running = await Task.Run(RunningProcessNames);
            var onBattery = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline;
            running.UnionWith(AutoSwitchConditions.ActiveKeys(settings.Rules, onBattery, TimeOnly.FromDateTime(DateTime.Now)));
            var decision = _planner.Evaluate(settings.Rules, running, ModeSwitcher.ActiveMode, settings.RevertWhenClosed);
            switch (decision.Kind)
            {
                case AutoSwitchKind.Activate:
                    await _switch(AutoSwitchConditions.IsCondition(decision.Process!) ? AutoSwitchConditions.Describe(decision.Process!) : $"{decision.Process} started", () => AppServices.Switcher.ActivateAsync(AppServices.Store.Load(decision.Mode!)));
                    break;
                case AutoSwitchKind.Revert:
                    await _switch(AutoSwitchConditions.IsCondition(decision.Process!) ? $"No longer: {AutoSwitchConditions.Describe(decision.Process!)}" : $"{decision.Process} closed", AppServices.Switcher.UndoAsync);
                    break;
            }
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
}
