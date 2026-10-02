using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;
using System.Text.Json;
using Microsoft.Win32;
using WinModes.Core;
using WinModes.Core.Apps;
using WinModes.Core.Engine;

namespace WinModes.App.Services;

/// <summary>
/// Looks at OneDrive and, if the user asks, uninstalls it with its own setup program. It refuses while a known folder (Desktop,
/// Documents, Pictures...) lives inside OneDrive, asks for a confirmation when an account is signed in or files exist only online, never
/// touches the setup program of Windows (so a reinstall stays one click), and never deletes a file of the user.
/// </summary>
internal static class OneDriveService
{
    private const int MaxEntries = 60_000;
    private static readonly TimeSpan ScanTime = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan UninstallTime = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan ShutdownWait = TimeSpan.FromSeconds(4);
    private const string WingetId = "Microsoft.OneDrive";

    private static readonly string[] OneDriveProcesses = ["OneDrive", "FileCoAuth", "OneDriveSetup"];

    private static readonly (string Name, string Value)[] KnownFolderValues =
    [
        ("Desktop", "Desktop"), ("Documents", "Personal"), ("Pictures", "My Pictures"), ("Music", "My Music"), ("Videos", "My Video"),
        ("Downloads", "{374DE290-123F-4565-9164-39C4925E467B}"), ("Screenshots", "{B7BEDE81-DF94-4682-A7D8-57A52620B86F}"),
    ];

    private static readonly string RemovedFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinModes", "onedrive-removed.json");

    private sealed record RemovedRecord(DateTimeOffset RemovedUtc);

    public static bool WasRemovedByWinModes => File.Exists(RemovedFile);

    private static string LocalOneDrive => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "OneDrive", "OneDrive.exe");

    private static string MachineOneDrive => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft OneDrive", "OneDrive.exe");

    public static string? SetupProgram => OneDriveCheck.SetupProgram(File.Exists);

    /// <summary>A file that carries the official OneDrive icon: the program itself while it is installed, else the setup program of Windows.</summary>
    public static string? IconPath => new[] { LocalOneDrive, MachineOneDrive, SetupProgram }.FirstOrDefault(path => path is not null && File.Exists(path));

    /// <summary>The last inspection of this run, so a page can show it at once while a new one is made (counting files can take seconds).</summary>
    public static OneDriveState? Last { get; private set; }

    public static async Task<OneDriveState> InspectAsync()
    {
        var state = await Task.Run(Inspect);
        Last = state;
        return state;
    }

    private static OneDriveState Inspect()
    {
        var installed = File.Exists(LocalOneDrive) || File.Exists(MachineOneDrive);
        var roots = FindRoots();
        var knownFolders = KnownFolderValues
            .Select(pair => (pair.Name, Path: ReadKnownFolder(pair.Value)))
            .Where(pair => pair.Path is not null)
            .ToDictionary(pair => pair.Name, pair => pair.Path!);
        var (online, cut) = CountOnlineOnly(roots);
        return new OneDriveState(installed, roots, IsSignedIn(), OneDriveCheck.Redirected(knownFolders, roots), online, cut);
    }

    private static List<string> FindRoots()
    {
        var roots = new List<string>();
        foreach (var variable in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value)
            {
                roots.Add(value);
            }
        }

        try
        {
            using var accounts = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts");
            foreach (var name in accounts?.GetSubKeyNames() ?? [])
            {
                using var account = accounts!.OpenSubKey(name);
                if (account?.GetValue("UserFolder") is string folder && folder.Length > 0)
                {
                    roots.Add(folder);
                }
            }

            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            roots.AddRange(Directory.EnumerateDirectories(profile, "OneDrive*"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // What cannot be read is simply not counted: the known folders are checked against the roots found.
        }

        return [.. roots.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static bool IsSignedIn()
    {
        try
        {
            using var accounts = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts");
            foreach (var name in accounts?.GetSubKeyNames() ?? [])
            {
                using var account = accounts!.OpenSubKey(name);
                if (account?.GetValue("UserEmail") is string { Length: > 0 })
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Unknown counts as signed in: the user is asked to confirm.
            return true;
        }

        return false;
    }

    private static string? ReadKnownFolder(string valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
            return key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string raw ? Environment.ExpandEnvironmentVariables(raw) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    /// <summary>Counts files that exist only online. Files are not opened, so none is downloaded; the count stops at a limit.</summary>
    private static (int Count, bool Cut) CountOnlineOnly(IReadOnlyList<string> roots)
    {
        var count = 0;
        var seen = 0;
        var clock = Stopwatch.StartNew();
        foreach (var root in roots.Where(Directory.Exists))
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
            var entries = new FileSystemEnumerable<bool>(root, (ref FileSystemEntry entry) => OneDriveCheck.IsOnlineOnly(entry.Attributes) && !entry.IsDirectory, options);
            foreach (var online in entries)
            {
                seen++;
                count += online ? 1 : 0;
                if (seen >= MaxEntries || clock.Elapsed > ScanTime)
                {
                    return (count, true);
                }
            }
        }

        return (count, false);
    }

    /// <summary>Uninstalls OneDrive with its own setup program. Returns null on success, or the reason it was not done.</summary>
    public static async Task<string?> UninstallAsync()
    {
        var state = await InspectAsync();
        if (!state.CanUninstall)
        {
            return state.Installed
                ? Loc.F("Your {0} folder is inside OneDrive. Move it back out of OneDrive first.", string.Join(", ", state.RedirectedFolders))
                : Loc.T("OneDrive is not installed for your account.");
        }

        if (SetupProgram is not { } setup)
        {
            return Loc.T("The OneDrive setup program of Windows was not found.");
        }

        await StopOneDriveAsync();
        var (exitCode, _) = await RunAsync(setup, ["/uninstall"], UninstallTime);
        if (exitCode != 0 || File.Exists(LocalOneDrive))
        {
            return Loc.T("OneDrive could not be uninstalled. It may be installed for every user of this PC, which needs administrator rights.");
        }

        AtomicFile.WriteAllText(RemovedFile, JsonSerializer.Serialize(new RemovedRecord(DateTimeOffset.UtcNow)));
        return null;
    }

    /// <summary>Installs OneDrive again: the setup program of Windows when it is there, otherwise winget.</summary>
    public static async Task<string?> RestoreAsync()
    {
        var (exitCode, _) = SetupProgram is { } setup
            ? await RunAsync(setup, [], UninstallTime)
            : await RunAsync("winget.exe", ["install", "--id", WingetId, "--exact", "--source", "winget", "--accept-source-agreements", "--accept-package-agreements", "--disable-interactivity"], UninstallTime);
        if (exitCode != 0 && !File.Exists(LocalOneDrive))
        {
            return Loc.T("OneDrive could not be installed again. Install it from the Microsoft Store or with winget.");
        }

        File.Delete(RemovedFile);
        return null;
    }

    private static async Task StopOneDriveAsync()
    {
        // Ask OneDrive to close itself first; the processes that are still there afterwards are ended.
        if (File.Exists(LocalOneDrive))
        {
            await RunAsync(LocalOneDrive, ["/shutdown"], ShutdownWait);
        }

        foreach (var name in OneDriveProcesses)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                    {
                        // Already gone, or not ours to end.
                    }
                }
            }
        }
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string file, IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        try
        {
            var start = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start);
            if (process is null)
            {
                return (-1, "");
            }

            using var cts = new CancellationTokenSource(timeout);
            var output = process.StandardOutput.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);
            return (process.ExitCode, await output);
        }
        catch (Exception ex) when (ex is Win32Exception or OperationCanceledException or InvalidOperationException)
        {
            return (-1, ex.Message);
        }
    }
}
