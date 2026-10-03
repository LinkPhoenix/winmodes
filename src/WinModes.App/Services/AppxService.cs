using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WinModes.Core;
using WinModes.Core.Apps;

namespace WinModes.App.Services;

/// <summary>
/// Lists, removes and restores the preinstalled apps of the Debloat page. A removal is for the current user only, needs no
/// administrator, and is journaled first. Only packages that an entry of data/apps.json names and that <see cref="AppGuard"/>
/// does not protect are ever touched.
/// </summary>
internal static partial class AppxService
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(3);
    private const string Utf8Output = "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; ";
    private const int ErrorLimit = 400;

    private static readonly string? Root = RepositoryLocator.Find(AppContext.BaseDirectory) ?? RepositoryLocator.Find(Environment.CurrentDirectory);

    private static readonly Lazy<AppCatalog> LazyCatalog = new(() => AppCatalog.Load(Root is null ? "" : Path.Combine(Root, "data", "apps.json")));

    public static AppCatalog Catalog => LazyCatalog.Value;

    public static RemovedAppsJournal Journal { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinModes", "removed-apps.json"));

    [GeneratedRegex("^[A-Za-z0-9]{12}$")]
    private static partial Regex StoreIdPattern();

    /// <summary>A failed reading must never make installed apps appear absent.</summary>
    public static async Task<InventoryRead<IReadOnlyList<InstalledPackage>>> ListAsync()
    {
        var (exitCode, output) = await RunAsync(PackageCommands.ListScript);
        IReadOnlyList<InstalledPackage> packages = [];
        var succeeded = exitCode == 0 && PackageCommands.TryParseList(output, out packages);
        return new(succeeded, packages);
    }

    /// <summary>The name Windows shows for each packaged app of the Start menu, by package family. Empty when PowerShell could not answer.</summary>
    public static async Task<InventoryRead<IReadOnlyDictionary<string, string>>> StartAppNamesAsync()
    {
        var (exitCode, output) = await RunAsync(PackageCommands.StartAppsScript);
        var succeeded = false;
        if (exitCode == 0)
        {
            try
            {
                using var document = JsonDocument.Parse(output);
                succeeded = document.RootElement.ValueKind is JsonValueKind.Array or JsonValueKind.Object;
            }
            catch (JsonException)
            {
                // Preserve the last names when the command output cannot be read.
            }
        }

        return new(succeeded, succeeded ? PackageCommands.ParseStartApps(output) : new Dictionary<string, string>());
    }

    /// <summary>Removes one app for the current user. Returns null on success, or the reason it was not done.</summary>
    public static async Task<string?> RemoveAsync(InstalledPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        // Checked again here, whatever the page decided: the catalog is an allow-list and the guard has the last word.
        if (Catalog.Find(package) is not { } entry)
        {
            return Loc.T("This app is protected or not in the list: WinModes does not remove it.");
        }

        Journal.Add(new RemovedApp
        {
            EntryId = entry.Id,
            Title = entry.Title,
            Name = package.Name,
            FullName = package.FullName,
            Family = package.Family,
            Version = package.Version,
            InstallLocation = package.InstallLocation,
            RemovedUtc = DateTimeOffset.UtcNow,
            Reinstall = entry.Reinstall,
        });

        var (exitCode, output) = await RunAsync(PackageCommands.RemoveScript(package.FullName));
        if (exitCode == 0)
        {
            return null;
        }

        // Nothing was removed: nothing to restore later.
        Journal.Forget(package.Name);
        return Short(output);
    }

    /// <summary>Registers an app again from the files that are still on the PC. Returns null on success, or the reason.</summary>
    public static async Task<string?> RestoreAsync(RemovedApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!AppGuard.IsPackageFolder(app.InstallLocation) || !Directory.Exists(app.InstallLocation))
        {
            return Loc.T("The files of this app are no longer on the PC. Install it again from the Microsoft Store.");
        }

        var (exitCode, output) = await RunAsync(PackageCommands.RestoreScript(app.InstallLocation));
        if (exitCode != 0)
        {
            return Short(output);
        }

        Journal.Forget(app.Name);
        return null;
    }

    /// <summary>Opens the Store page of an app; the id is checked, since it comes from a file.</summary>
    public static void OpenInStore(string productId)
    {
        if (StoreIdPattern().IsMatch(productId))
        {
            using var started = Process.Start(new ProcessStartInfo($"ms-windows-store://pdp/?productid={productId}") { UseShellExecute = true });
        }
    }

    private static string Short(string text)
    {
        var clean = text.Trim();
        return clean.Length <= ErrorLimit ? clean : clean[..ErrorLimit] + "…";
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string script)
    {
        try
        {
            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-EncodedCommand", PackageCommands.Encode(Utf8Output + script) })
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start);
            if (process is null)
            {
                return (-1, "");
            }

            using var timeout = new CancellationTokenSource(CommandTimeout);
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var text = await output;
            return (process.ExitCode, process.ExitCode == 0 ? text : (await error) + text);
        }
        catch (Exception ex) when (ex is Win32Exception or OperationCanceledException or InvalidOperationException)
        {
            return (-1, ex.Message);
        }
    }
}

internal sealed record InventoryRead<T>(bool Succeeded, T Value);
