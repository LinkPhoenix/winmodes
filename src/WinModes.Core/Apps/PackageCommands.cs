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
