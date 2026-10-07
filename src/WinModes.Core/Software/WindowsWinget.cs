using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace WinModes.Core.Software;

public interface ISoftwarePackageRunner
{
    Task<int?> InstallAsync(SoftwareEntry entry);
}

/// <summary>Only fixed catalog packages are installed. WinGet owns its agreement and installer dialogs.</summary>
public sealed class WindowsWinget : ISoftwarePackageRunner
{
    private static string Executable => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");

    public static SoftwareInventory ReadInventory()
    {
        try
        {
            var start = new ProcessStartInfo(Executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "list", "--disable-interactivity" }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
            if (!process.WaitForExit(90_000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                return SoftwareInventory.Unavailable("WinGet detection timed out. Try again.");
            }
            Task.WhenAll(output, error).GetAwaiter().GetResult();
            if (process.ExitCode != 0) return SoftwareInventory.Unavailable("WinGet could not read installed apps. Open WinGet once to review its source agreements, then try again.");
            if (output.Result.Length > 8 * 1024 * 1024) return SoftwareInventory.Unavailable("WinGet returned an invalid inventory.");
            if (!output.Result.Split('\n').Any(line => Regex.IsMatch(line.Trim(), @"^-{10,}$", RegexOptions.None, TimeSpan.FromSeconds(1))))
                return SoftwareInventory.Unavailable("WinGet returned an invalid inventory.");
            return SoftwareInventory.ParseList(output.Result);
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return SoftwareInventory.Unavailable("WinGet detection is unavailable. Check that App Installer is installed and try again.");
        }
    }

    public async Task<int?> InstallAsync(SoftwareEntry entry)
    {
        if (!SoftwareCatalog.Entries.Contains(entry) || entry.WingetId is null) throw new ArgumentException("Unknown installation target.", nameof(entry));
        var start = new ProcessStartInfo(Executable) { UseShellExecute = false, CreateNoWindow = false };
        foreach (var argument in new[] { "install", "--id", entry.WingetId, "--exact", "--source", entry.Source, "--interactive", "--no-upgrade", "--skip-dependencies" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("WinGet could not start.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(45));
        try { await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { return null; } // Do not kill an installer while it may be writing files.
        return process.ExitCode;
    }
}
