using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using WinModes.Core.Engine;
using WinModes.Core.Planning;
using WinModes.Core.Profiles;
using WinModes.Core.Protection;

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
    private const int UacCancelledError = 1223;
    private static readonly TimeSpan AppCloseTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan MemorySettleDelay = TimeSpan.FromSeconds(4);
    private const double MinFreedGbToReport = 0.1;

    private static readonly Dictionary<string, string> PowerSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
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

    public async Task<SwitchReport> ActivateAsync(ModeProfile profile)
    {
        var lines = new List<string>();
        var previous = ReadUserState();
        var usedBeforeGb = SystemMonitor.SampleMemory().UsedGb;

        var helper = await RunHelperAsync("apply", profile.Mode);
        if (helper is not null)
        {
            return new SwitchReport(false, [helper]);
        }

        var session = _journal.FindActive();
        lines.Add(session is null
            ? "Services: nothing to change."
            : $"Services: {session.DoneCount} changed, {session.Entries.Count(entry => entry.Outcome == EntryOutcome.Skipped)} skipped, {session.Entries.Count(entry => entry.Outcome == EntryOutcome.Failed)} failed.");

        // Keep the very first power plan so Undo returns to it even after several switches.
        var originalScheme = previous?.PreviousPowerScheme ?? await GetActivePowerSchemeAsync();
        lines.Add(await SetPowerPlanAsync(profile.Power));
        lines.AddRange(await CloseAppsAsync(profile.Apps.Close));
        lines.AddRange(LaunchApps(profile.Apps.Launch));
        lines.AddRange(await ApplyWslAsync(profile.Wsl));

        WriteUserState(new UserState(profile.Mode, originalScheme, DateTimeOffset.UtcNow));

        // Stopped services and closed apps release their memory over a few seconds.
        await Task.Delay(MemorySettleDelay);
        var usedAfterGb = SystemMonitor.SampleMemory().UsedGb;
        var freedGb = usedBeforeGb - usedAfterGb;
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var outcome = freedGb >= MinFreedGbToReport ? string.Create(culture, $"{freedGb:0.0} GB freed") : "no measurable change";
        lines.Insert(0, string.Create(culture, $"Memory in use: {usedBeforeGb:0.0} GB before, {usedAfterGb:0.0} GB after ({outcome})."));
        // Kept for the summary shown when the mode is deactivated.
        WriteUserState(new UserState(profile.Mode, originalScheme, DateTimeOffset.UtcNow, Math.Max(freedGb, 0)));
        return new SwitchReport(true, lines);
    }

    public async Task<SwitchReport> UndoAsync()
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

            lines.Add("Services restored to their previous state.");
        }

        if (state?.PreviousPowerScheme is { } scheme)
        {
            var result = await RunAsync("powercfg.exe", $"/setactive {scheme}");
            lines.Add(result.ExitCode == 0 ? "Power plan restored." : "The previous power plan could not be restored.");
        }

        lines.Add("Apps that were closed, WSL and Docker are not restarted automatically.");
        if (state is not null)
        {
            lines.Insert(0, DescribeSession(state));
        }

        DeleteUserState();
        return new SwitchReport(true, lines);
    }

    private static string DescribeSession(UserState state)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var active = DateTimeOffset.UtcNow - state.SwitchedUtc;
        var duration = active.TotalHours >= 1 ? $"{(int)active.TotalHours} h {active.Minutes:00}" : $"{Math.Max((int)active.TotalMinutes, 1)} min";
        var freed = state.FreedGb >= MinFreedGbToReport ? string.Create(culture, $" It had freed {state.FreedGb:0.0} GB when activated.") : "";
        return $"{culture.TextInfo.ToTitleCase(state.Mode)} mode was active for {duration}.{freed}";
    }

    /// <summary>Returns null on success, or the reason the helper did not complete.</summary>
    internal static async Task<string?> RunHelperAsync(params string[] arguments)
    {
        var helperPath = Path.Combine(AppContext.BaseDirectory, HelperFileName);
        if (!File.Exists(helperPath))
        {
            return $"{HelperFileName} is missing next to the app.";
        }

        // With the opt-in silent switch, a mode switch goes through its task and no prompt is shown.
        // When the task cannot be used, the switch falls back to the normal prompt.
        if (arguments is ["revert"] or ["apply", _]
            && await Task.Run(() => SilentSwitchTask.Run(helperPath, arguments[0], arguments.Length > 1 ? arguments[1] : "")) is { } exitCode)
        {
            return exitCode == 0 ? null : "The elevated helper reported an error; nothing else was changed.";
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
                return "The elevated helper could not be started.";
            }

            await process.WaitForExitAsync();
            return process.ExitCode == 0 ? null : "The elevated helper reported an error; nothing else was changed.";
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == UacCancelledError)
        {
            return "Cancelled: administrator permission was not given. Nothing was changed.";
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
                return $"Power plan: {plan}.";
            }
        }

        return string.IsNullOrEmpty(power.Plan)
            ? "Power plan: unchanged."
            : $"Power plan: '{power.Plan}' is not available on this PC; left unchanged.";
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

            var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(app.Process));
            try
            {
                if (processes.Length == 0)
                {
                    continue;
                }

                foreach (var process in processes.Where(process => process.MainWindowHandle != IntPtr.Zero))
                {
                    process.CloseMainWindow();
                }

                using var timeout = new CancellationTokenSource(AppCloseTimeout);
                try
                {
                    await Task.WhenAll(processes.Select(process => process.WaitForExitAsync(timeout.Token)));
                    lines.Add($"Closed {app.Id}.");
                }
                catch (OperationCanceledException)
                {
                    // Never force-kill: the app may hold unsaved work or run only in the tray.
                    lines.Add($"{app.Id} is still running; close it yourself if you want.");
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
                lines.Add($"{app.Id}: executable not found, not launched.");
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
                lines.Add($"Launched {app.Id}.");
            }
        }

        return lines;
    }

    private static async Task<List<string>> ApplyWslAsync(WslSettings wsl)
    {
        var lines = new List<string>();
        var probe = new WindowsSystemProbe();

        if (!wsl.Running)
        {
            if (probe.IsProcessRunning("Docker Desktop.exe"))
            {
                var docker = await RunAsync("docker.exe", "desktop stop");
                lines.Add(docker.ExitCode == 0 ? "Docker Desktop stopped." : "Docker Desktop could not be stopped from the command line.");
            }

            if (probe.IsWslRunning())
            {
                var result = await RunAsync("wsl.exe", "--shutdown");
                lines.Add(result.ExitCode == 0 ? "WSL shut down." : "WSL could not be shut down.");
            }
        }
        else if (wsl.Docker == "start" && !probe.IsProcessRunning("Docker Desktop.exe"))
        {
            var docker = await RunAsync("docker.exe", "desktop start");
            lines.Add(docker.ExitCode == 0 ? "Docker Desktop started." : "Docker Desktop could not be started from the command line.");
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
        Directory.CreateDirectory(Path.GetDirectoryName(UserStatePath)!);
        File.WriteAllText(UserStatePath, JsonSerializer.Serialize(state));
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

    private sealed record UserState(string Mode, string? PreviousPowerScheme, DateTimeOffset SwitchedUtc, double FreedGb = 0);
}
