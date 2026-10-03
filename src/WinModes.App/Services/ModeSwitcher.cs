using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using WinModes.Core;
using WinModes.Core.Engine;
using WinModes.Core.Planning;
using WinModes.Core.Profiles;
using WinModes.Core.Protection;
using WinModes.Core.Tuning;

namespace WinModes.App.Services;

/// <summary>Outcome of a switch, shown to the user line by line.</summary>
internal sealed record SwitchReport(bool Succeeded, IReadOnlyList<string> Lines);

/// <summary>
/// Runs a mode switch. Service changes go through the elevated helper (one UAC prompt);
/// everything that belongs to the user session (power plan, apps, WSL, Docker) runs here, unelevated.
/// </summary>
internal sealed partial class ModeSwitcher(ProtectionPolicy policy)
{
    private const string HelperFileName = "WinModes.Elevated.exe";
    private const string AutomaticSource = "auto";
    private const int UacCancelledError = 1223;
    private static readonly TimeSpan AppCloseTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan MemorySettleDelay = TimeSpan.FromSeconds(4);
    private const double MinFreedGbToReport = 0.1;

    private readonly OperationGate _switchGate = new();

    private static readonly Dictionary<string, string> PowerSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["power-saver"] = "a1841308-3541-4fab-bc81-f71556f20b4a",
        ["balanced"] = "381b4222-f694-41f0-9685-ff5bb260df2e",
        ["high-performance"] = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c",
        ["ultimate-performance"] = "e9a42b02-d5df-448d-aa00-03f14749eb61",
    };

    private static readonly string UserStatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinModes", "user-state.json");

    private readonly JournalStore _journal = new(AppPaths.JournalDirectory);

    public JournalStore Journal => _journal;

    private static string HelperPath => Path.Combine(AppContext.BaseDirectory, HelperFileName);

    /// <summary>The silent switch is only offered for a copy installed under Program Files.</summary>
    public static bool CanSwitchSilently => SilentSwitchTask.IsTrustedLocation(HelperPath);

    public static bool SwitchesSilently => SilentSwitchTask.IsInstalled(HelperPath);

    /// <summary>Registers or removes the silent-switch task (one prompt). Returns null on success, or the reason.</summary>
    public static Task<string?> SetSilentSwitchAsync(bool enabled) => RunHelperAsync("task", enabled ? "install" : "remove");

    /// <summary>Name of the active mode, or null when Windows is in its normal state.</summary>
    public static string? ActiveMode => ReadUserState()?.Mode;

    /// <summary>True when the active mode was started by automatic switching, which then also ends it; a mode chosen by hand is never ended by it.</summary>
    public static bool ActiveModeIsAutomatic => ReadUserState()?.Source == AutomaticSource;

    /// <summary>Raised, from any thread, when a switch has just finished (done or not), so open pages show the new state at once.</summary>
    public static event Action? Changed;

    /// <param name="automatic">The switch is made by automatic switching, which remembers it so it can undo it later.</param>
    public Task<SwitchReport> ActivateAsync(ModeProfile profile, bool automatic = false) => RunExclusiveAsync(Loc.F("Activate {0} mode", profile.Label), () => ActivateCoreAsync(profile, automatic));

    public Task<SwitchReport> UndoAsync() => RunExclusiveAsync(Loc.T("Undo mode"), UndoCoreAsync);

    /// <summary>The page, the tray, the hotkeys and the auto-switcher all end up here: one switch at a time.</summary>
    private async Task<SwitchReport> RunExclusiveAsync(string title, Func<Task<SwitchReport>> operation)
    {
        if (!OperationStatus.TryBegin(title, 0, out var statusId))
        {
            return new SwitchReport(false, [Loc.T("Another operation is already running.")]);
        }
        try
        {
            var (ran, report) = await _switchGate.TryRunAsync(operation);
            if (ran) { Changed?.Invoke(); }
            var result = ran ? report! : new SwitchReport(false, [Loc.T("Another mode switch is already running. Wait for it to finish.")]);
            OperationStatus.Complete(statusId, string.Join("\n", result.Lines), failed: !result.Succeeded);
            return result;
        }
        finally
        {
            if (OperationStatus.Current is { IsRunning: true } current && current.Id == statusId)
            {
                OperationStatus.Complete(statusId, Loc.T("The operation did not finish."), failed: true);
            }
        }
    }

    private async Task<SwitchReport> ActivateCoreAsync(ModeProfile profile, bool automatic)
    {
        var lines = new List<string>();
        var previous = ReadUserState();
        var usedBeforeGb = await SampleUsedGbAsync();

        var helper = await RunHelperAsync("apply", profile.Mode);
        if (helper is not null)
        {
            return new SwitchReport(false, [helper]);
        }

        var session = _journal.FindActive();
        lines.Add(session is null
            ? Loc.T("Services: nothing to change.")
            : Loc.F("Services: {0} changed, {1} skipped, {2} failed.", session.DoneCount,
                session.Entries.Count(entry => entry.Outcome == EntryOutcome.Skipped), session.Entries.Count(entry => entry.Outcome == EntryOutcome.Failed)));

        // Keep the very first power plan so Undo returns to it even after several switches.
        var originalScheme = previous?.PreviousPowerScheme ?? await GetActivePowerSchemeAsync();
        lines.Add(await SetPowerPlanAsync(profile.Power));
        var (powerLines, powerClone) = await ApplyPowerValuesAsync(profile, previous?.PowerClone);
        lines.AddRange(powerLines);
        lines.AddRange(await CloseAppsAsync(profile.Apps.Close));
        lines.AddRange(LaunchApps(profile.Apps.Launch));
        lines.AddRange(await ApplyWslAsync(profile.Wsl));

        var (tweakLines, ownedTweaks) = await ApplyTweaksAsync(previous?.TweakIds ?? [], profile.TweakIds);
        lines.AddRange(tweakLines);

        var source = automatic ? AutomaticSource : null;
        WriteUserState(new UserState(profile.Mode, originalScheme, DateTimeOffset.UtcNow, Source: source, TweakIds: ownedTweaks, PowerClone: powerClone));

        // Stopped services and closed apps release their memory over a few seconds.
        await Task.Delay(MemorySettleDelay);
        var usedAfterGb = await SampleUsedGbAsync();
        var freedGb = usedBeforeGb - usedAfterGb;
        var outcome = freedGb >= MinFreedGbToReport ? Loc.F("{0:0.0} GB freed", freedGb) : Loc.T("no measurable change");
        lines.Insert(0, Loc.F("Memory in use: {0:0.0} GB before, {1:0.0} GB after ({2}).", usedBeforeGb, usedAfterGb, outcome));
        // Kept for the summary shown when the mode is deactivated.
        WriteUserState(new UserState(profile.Mode, originalScheme, DateTimeOffset.UtcNow, Math.Max(freedGb, 0), source, ownedTweaks, powerClone));
        return new SwitchReport(true, lines);
    }

    private async Task<SwitchReport> UndoCoreAsync()
    {
        var lines = new List<string>();
        var state = ReadUserState();

        if (_journal.FindActive() is not null)
        {
            var helper = await RunHelperAsync("revert");
            if (helper is not null)
            {
                return new SwitchReport(false, [helper]);
            }

            lines.Add(Loc.T("Services restored to their previous state."));
        }

        if (state?.PowerClone is { } clone)
        {
            lines.Add(await RestorePlanAndDeleteCloneAsync(clone, state.PreviousPowerScheme));
        }
        else if (state?.PreviousPowerScheme is { } scheme)
        {
            var result = await RunAsync("powercfg.exe", $"/setactive {scheme}");
            lines.Add(Loc.T(result.ExitCode == 0 ? "Power plan restored." : "The previous power plan could not be restored."));
        }

        if (state?.TweakIds is { Count: > 0 } owned)
        {
            lines.AddRange(await Task.Run(() => owned.Select(UndoTweak).OfType<string>().ToList()));
        }

        lines.Add(Loc.T("Apps that were closed, WSL and Docker are not restarted automatically."));
        if (state is not null)
        {
            lines.Insert(0, DescribeSession(state));
        }

        DeleteUserState();
        return new SwitchReport(true, lines);
    }

    /// <summary>
    /// Applies the settings of the new mode and puts back the ones only the previous mode had set. Only a setting that this switch
    /// really changed is remembered as the mode's own: one already set before (by the user, or on the Optimize page) is left alone.
    /// </summary>
    private static Task<(List<string> Lines, List<string> Owned)> ApplyTweaksAsync(IReadOnlyList<string> ownedByPrevious, IReadOnlyList<string> wanted) =>
        Task.Run(() =>
        {
            var lines = new List<string>();
            var (undo, apply) = ModeTweaks.Diff(ownedByPrevious, wanted);
            lines.AddRange(undo.Select(UndoTweak).OfType<string>());
            var owned = ownedByPrevious.Except(undo, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var id in apply)
            {
                if (ServiceTuning.Catalog.Find(id) is not { } tweak || tweak.NeedsElevation || !tweak.Restart.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var result = ServiceTuning.UserTweaks.Apply(tweak);
                if (result.Outcome == TuneOutcome.Done)
                {
                    owned.Add(tweak.Id);
                    lines.Add(Loc.F("Setting: {0}.", Loc.T(tweak.Title)));
                }
            }

            return (lines, owned);
        });

    private static string? UndoTweak(string id)
    {
        var result = ServiceTuning.UserTweaks.Undo(id);
        return result.Outcome == TuneOutcome.Done && ServiceTuning.Catalog.Find(id) is { } tweak ? Loc.F("Setting put back: {0}.", Loc.T(tweak.Title)) : null;
    }

    private static string DescribeSession(UserState state)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var active = DateTimeOffset.UtcNow - state.SwitchedUtc;
        var duration = active.TotalHours >= 1 ? $"{(int)active.TotalHours} h {active.Minutes:00}" : $"{Math.Max((int)active.TotalMinutes, 1)} min";
        var freed = state.FreedGb >= MinFreedGbToReport ? " " + Loc.F("It had freed {0:0.0} GB when activated.", state.FreedGb) : "";
        return Loc.F("{0} mode was active for {1}.", culture.TextInfo.ToTitleCase(state.Mode), duration) + freed;
    }

    /// <summary>Returns null on success, or the reason the helper did not complete.</summary>
    internal static async Task<string?> RunHelperAsync(params string[] arguments)
    {
        var helperPath = Path.Combine(AppContext.BaseDirectory, HelperFileName);
        if (!File.Exists(helperPath))
        {
            return Loc.F("{0} is missing next to the app.", HelperFileName);
        }

        // With the opt-in silent switch, a mode switch goes through its task and no prompt is shown.
        // When the task cannot be used, the switch falls back to the normal prompt.
        if (arguments is ["revert"] or ["apply", _]
            && await Task.Run(() => SilentSwitchTask.Run(helperPath, arguments[0], arguments.Length > 1 ? arguments[1] : "")) is { } exitCode)
        {
            return exitCode == 0 ? null : Loc.T("The elevated helper reported an error; nothing else was changed.");
        }

        try
        {
            // UseShellExecute lets Windows show the UAC prompt required by the helper's manifest.
            var start = new ProcessStartInfo(helperPath) { UseShellExecute = true };
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start);
            if (process is null)
            {
                return Loc.T("The elevated helper could not be started.");
            }

            await process.WaitForExitAsync();
            return process.ExitCode == 0 ? null : Loc.T("The elevated helper reported an error; nothing else was changed.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == UacCancelledError)
        {
            return Loc.T("Cancelled: administrator permission was not given. Nothing was changed.");
        }
    }

    private static async Task<string?> GetActivePowerSchemeAsync()
    {
        var result = await RunAsync("powercfg.exe", "/getactivescheme");
        var match = GuidPattern().Match(result.Output);
        return match.Success ? match.Value : null;
    }

    private static async Task<string> SetPowerPlanAsync(PowerSettings power)
    {
        foreach (var plan in new[] { power.Plan, power.Fallback })
        {
            if (plan is not null && PowerSchemes.TryGetValue(plan, out var guid)
                && (await RunAsync("powercfg.exe", $"/setactive {guid}")).ExitCode == 0)
            {
                return Loc.F("Power plan: {0}.", plan);
            }
        }

        return string.IsNullOrEmpty(power.Plan)
            ? Loc.T("Power plan: unchanged.")
            : Loc.F("Power plan: '{0}' is not available on this PC; left unchanged.", power.Plan);
    }

    /// <summary>
    /// A mode that changes power values never edits a plan of the user: it changes a copy of its plan and uses the copy. Returns the
    /// lines for the report and the GUID of the copy now in use (null when there is none). A copy left by the mode it replaces is deleted.
    /// </summary>
    private static async Task<(List<string> Lines, string? Clone)> ApplyPowerValuesAsync(ModeProfile profile, string? previousClone)
    {
        var lines = new List<string>();
        string? clone = null;
        if (profile.Power.Values.Count > 0 && await GetActivePowerSchemeAsync() is { } basePlan)
        {
            clone = await MakePowerCloneAsync(basePlan, profile, lines);
        }

        if (previousClone is not null && !previousClone.Equals(clone, StringComparison.OrdinalIgnoreCase))
        {
            await RunAsync("powercfg.exe", $"/delete {previousClone}");
        }

        return (lines, clone);
    }

    private static async Task<string?> MakePowerCloneAsync(string basePlan, ModeProfile profile, List<string> lines)
    {
        var duplicate = await RunAsync("powercfg.exe", $"/duplicatescheme {basePlan}");
        var match = GuidPattern().Match(duplicate.Output);
        if (duplicate.ExitCode != 0 || !match.Success)
        {
            lines.Add(Loc.T("Power values: the power plan could not be copied; left unchanged."));
            return null;
        }

        var clone = match.Value;
        var failed = (await RunAsync("powercfg.exe", $"/changename {clone} \"{PowerCloneName(profile.Label)}\"")).ExitCode != 0;
        foreach (var value in profile.Power.Values)
        {
            foreach (var command in PowerCatalog.Commands(clone, value))
            {
                failed |= (await RunAsync("powercfg.exe", string.Join(' ', command))).ExitCode != 0;
            }
        }

        // The user's own plan is made active again, and the unfinished copy removed: either all the values apply or none.
        if (failed || (await RunAsync("powercfg.exe", $"/setactive {clone}")).ExitCode != 0)
        {
            await RunAsync("powercfg.exe", $"/setactive {basePlan}");
            await RunAsync("powercfg.exe", $"/delete {clone}");
            lines.Add(Loc.T("Power values: they could not be applied; the power plan is left unchanged."));
            return null;
        }

        lines.Add(Loc.F("Power values: {0} (on a copy of the power plan).", string.Join(", ", profile.Power.Values.Select(DescribePowerValue))));
        return clone;
    }

    private static string DescribePowerValue(PowerValue value) =>
        PowerCatalog.Find(value.Setting) is { } info
            ? Loc.T(info.Title) + ": " + string.Join(" / ", new[] { value.Ac, value.Dc }.Where(number => number is not null).Select(number => info.Seconds ? PowerCatalog.DescribeSeconds(number!.Value) : number!.Value.ToString(System.Globalization.CultureInfo.CurrentCulture)))
            : value.Setting;

    /// <summary>The copy gets a name the app recognises, so that one left by a crash can be found and removed.</summary>
    internal static string PowerCloneName(string label) => PowerClonePrefix + new string([.. label.Where(character => char.IsLetterOrDigit(character) || character == ' ')]).Trim();

    private const string PowerClonePrefix = "WinModes ";

    /// <summary>Back to the plan the user had, and the copy deleted. If the user chose another plan meanwhile, theirs is kept.</summary>
    private static async Task<string> RestorePlanAndDeleteCloneAsync(string clone, string? originalPlan)
    {
        var active = await GetActivePowerSchemeAsync();
        if (active is null || active.Equals(clone, StringComparison.OrdinalIgnoreCase))
        {
            var target = originalPlan ?? PowerSchemes["balanced"];
            var restored = (await RunAsync("powercfg.exe", $"/setactive {target}")).ExitCode == 0
                || (await RunAsync("powercfg.exe", $"/setactive {PowerSchemes["balanced"]}")).ExitCode == 0;
            if (!restored)
            {
                return Loc.T("The previous power plan could not be restored.");
            }
        }

        await RunAsync("powercfg.exe", $"/delete {clone}");
        return Loc.T("Power plan restored.");
    }

    /// <summary>Deletes a copy of a plan left behind by a mode that never ended cleanly. Never touches a plan the user made.</summary>
    internal static async Task RemoveOrphanPowerPlansAsync()
    {
        var keep = ReadUserState()?.PowerClone;
        var list = await RunAsync("powercfg.exe", "/list");
        foreach (Match plan in PowerPlanLine().Matches(list.Output))
        {
            var guid = plan.Groups["guid"].Value;
            var name = plan.Groups["name"].Value;
            if (name.StartsWith(PowerClonePrefix, StringComparison.Ordinal) && !plan.Groups["active"].Success
                && !guid.Equals(keep, StringComparison.OrdinalIgnoreCase))
            {
                await RunAsync("powercfg.exe", $"/delete {guid}");
            }
        }
    }

    private async Task<List<string>> CloseAppsAsync(IReadOnlyList<AppClose> apps)
    {
        var lines = new List<string>();
        foreach (var app in apps)
        {
            // Fail closed: a protected app is never closed, whatever the profile says.
            if (policy.IsProtectedProcess(app.Process) || policy.IsProtectedApp(app.Id))
            {
                continue;
            }

            // Listing processes and reading their windows is slow enough to freeze the interface if done on its thread.
            var processes = await Task.Run(() => Process.GetProcessesByName(Path.GetFileNameWithoutExtension(app.Process)));
            try
            {
                if (processes.Length == 0)
                {
                    continue;
                }

                await Task.Run(() =>
                {
                    foreach (var process in processes.Where(process => process.MainWindowHandle != IntPtr.Zero))
                    {
                        process.CloseMainWindow();
                    }
                });

                using var timeout = new CancellationTokenSource(AppCloseTimeout);
                try
                {
                    await Task.WhenAll(processes.Select(process => process.WaitForExitAsync(timeout.Token)));
                    lines.Add(Loc.F("Closed {0}.", app.Id));
                }
                catch (OperationCanceledException)
                {
                    // Never force-kill: the app may hold unsaved work or run only in the tray.
                    lines.Add(Loc.F("{0} is still running; close it yourself if you want.", app.Id));
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }

        return lines;
    }

    private static List<string> LaunchApps(IReadOnlyList<AppLaunch> apps)
    {
        var lines = new List<string>();
        foreach (var app in apps)
        {
            var isExecutable = Path.IsPathFullyQualified(app.Path) && app.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(app.Path);
            if (!isExecutable)
            {
                lines.Add(Loc.F("{0}: executable not found, not launched.", app.Id));
                continue;
            }

            var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(app.Path));
            var alreadyRunning = processes.Length > 0;
            foreach (var process in processes)
            {
                process.Dispose();
            }

            if (!alreadyRunning)
            {
                using var started = Process.Start(new ProcessStartInfo(app.Path) { UseShellExecute = true });
                lines.Add(Loc.F("Launched {0}.", app.Id));
            }
        }

        return lines;
    }

    private static Task<double> SampleUsedGbAsync() => Task.Run(() => SystemMonitor.SampleMemory().UsedGb);

    private static async Task<List<string>> ApplyWslAsync(WslSettings wsl)
    {
        var lines = new List<string>();
        var probe = new WindowsSystemProbe();

        if (!wsl.Running)
        {
            if (await Task.Run(() => probe.IsProcessRunning("Docker Desktop.exe")))
            {
                var docker = await RunAsync("docker.exe", "desktop stop");
                lines.Add(Loc.T(docker.ExitCode == 0 ? "Docker Desktop stopped." : "Docker Desktop could not be stopped from the command line."));
            }

            if (await Task.Run(probe.IsWslRunning))
            {
                var result = await RunAsync("wsl.exe", "--shutdown");
                lines.Add(Loc.T(result.ExitCode == 0 ? "WSL shut down." : "WSL could not be shut down."));
            }
        }
        else if (wsl.Docker == "start" && !await Task.Run(() => probe.IsProcessRunning("Docker Desktop.exe")))
        {
            var docker = await RunAsync("docker.exe", "desktop start");
            lines.Add(Loc.T(docker.ExitCode == 0 ? "Docker Desktop started." : "Docker Desktop could not be started from the command line."));
        }

        return lines;
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string fileName, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null)
            {
                return (-1, "");
            }

            using var timeout = new CancellationTokenSource(CommandTimeout);
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, output);
        }
        catch (Exception ex) when (ex is Win32Exception or OperationCanceledException or InvalidOperationException)
        {
            return (-1, "");
        }
    }

    private static UserState? ReadUserState()
    {
        try
        {
            return File.Exists(UserStatePath) ? JsonSerializer.Deserialize<UserState>(File.ReadAllText(UserStatePath)) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    private static void WriteUserState(UserState state)
    {
        AtomicFile.WriteAllText(UserStatePath, JsonSerializer.Serialize(state));
    }

    private static void DeleteUserState()
    {
        if (File.Exists(UserStatePath))
        {
            File.Delete(UserStatePath);
        }
    }

    [GeneratedRegex("[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();

    // "Power Scheme GUID: 381b4222-...  (Balanced) *" in any language: the name is between brackets and an asterisk marks the active plan.
    [GeneratedRegex(@"(?<guid>[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12})\s+\((?<name>[^)]*)\)(?<active>\s*\*)?")]
    private static partial Regex PowerPlanLine();

    /// <param name="PowerClone">GUID of the copy of a power plan the mode made and uses; deleted when the mode ends.</param>
    /// <param name="TweakIds">Settings of the Optimize catalog that this mode set and will put back; null in a file written before modes had settings.</param>
    private sealed record UserState(
        string Mode, string? PreviousPowerScheme, DateTimeOffset SwitchedUtc, double FreedGb = 0, string? Source = null, IReadOnlyList<string>? TweakIds = null,
        string? PowerClone = null);
}
