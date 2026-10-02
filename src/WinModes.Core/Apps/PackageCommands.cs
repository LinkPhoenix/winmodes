using System.Text;
using System.Text.Json;

namespace WinModes.Core.Apps;

/// <summary>
/// The PowerShell text that lists, removes and re-registers AppX packages. Windows PowerShell 5.1 is used because the Appx module
/// is part of it on every PC. Only names that pass <see cref="AppGuard"/> are ever put into a script, and a script is handed to
/// PowerShell encoded, so no text of a package can become a command.
/// </summary>
public static class PackageCommands
{
    /// <summary>One JSON object per package of the current user.</summary>
    public const string ListScript = """
        $ErrorActionPreference = 'Stop'
        $packages = @(Get-AppxPackage | ForEach-Object {
            [pscustomobject]@{
                Name = $_.Name; FullName = $_.PackageFullName; Family = $_.PackageFamilyName; Version = $_.Version.ToString()
                InstallLocation = $_.InstallLocation; IsFramework = [bool]$_.IsFramework; NonRemovable = [bool]$_.NonRemovable
            }
        })
        ConvertTo-Json -InputObject $packages -Compress
        """;

    /// <summary>The apps of the Start menu with the name Windows shows for them; the app id starts with the package family.</summary>
    public const string StartAppsScript = """
        $ErrorActionPreference = 'Stop'
        $apps = @(Get-StartApps | Where-Object { $_.AppID -like '*!*' } | ForEach-Object { [pscustomobject]@{ Name = $_.Name; AppId = $_.AppID } })
        ConvertTo-Json -InputObject $apps -Compress
        """;

    private const int MaxStartAppNameLength = 120;

    /// <summary>The name of each packaged app of the Start menu, by package family. Empty when the text cannot be read.</summary>
    public static IReadOnlyDictionary<string, string> ParseStartApps(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
        {
            return names;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var items = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.EnumerateArray().ToList() : [document.RootElement];
            foreach (var item in items.Where(item => item.ValueKind == JsonValueKind.Object))
            {
                var name = item.TryGetProperty("Name", out var nameValue) && nameValue.ValueKind == JsonValueKind.String ? nameValue.GetString()?.Trim() : null;
                var appId = item.TryGetProperty("AppId", out var idValue) && idValue.ValueKind == JsonValueKind.String ? idValue.GetString() : null;
                var family = appId?.Split('!')[0];

                // The first entry of a family wins; the family has to look like one, since it is compared with package names only.
                if (!string.IsNullOrWhiteSpace(name) && name.Length <= MaxStartAppNameLength && !string.IsNullOrWhiteSpace(family) && AppGuard.IsValidFullName(family))
                {
                    names.TryAdd(family, name);
                }
            }
        }
        catch (JsonException)
        {
            names.Clear();
        }

        return names;
    }

    public static IReadOnlyList<InstalledPackage> ParseList(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var items = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.EnumerateArray().ToList() : [document.RootElement];
            return [.. items.Select(Read).OfType<InstalledPackage>()];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static InstalledPackage? Read(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string Text(string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        bool Flag(string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

        var package = new InstalledPackage(Text("Name"), Text("FullName"), Text("Family"), Text("Version"), Text("InstallLocation"), Flag("IsFramework"), Flag("NonRemovable"));
        return AppGuard.IsValidName(package.Name) && AppGuard.IsValidFullName(package.FullName) ? package : null;
    }

    /// <summary>Removes the package for the current user only; the files stay for the other users and for a later restore.</summary>
    public static string RemoveScript(string fullName)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        if (!AppGuard.IsValidFullName(fullName))
        {
            throw new ArgumentException("The package name is not valid.", nameof(fullName));
        }

        return $"$ErrorActionPreference = 'Stop'; Remove-AppxPackage -Package '{fullName}'";
    }

    /// <summary>Registers a package again from the files that are still in its install folder.</summary>
    public static string RestoreScript(string installLocation)
    {
        ArgumentNullException.ThrowIfNull(installLocation);
        if (!AppGuard.IsPackageFolder(installLocation) || installLocation.Contains('\'', StringComparison.Ordinal))
        {
            throw new ArgumentException("The install folder is not a package folder.", nameof(installLocation));
        }

        return $"$ErrorActionPreference = 'Stop'; Add-AppxPackage -Register '{Path.Combine(installLocation, "AppxManifest.xml")}' -DisableDevelopmentMode";
    }

    /// <summary>The argument that gives a script to powershell.exe as base64 text.</summary>
    public static string Encode(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        return Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    }
}
