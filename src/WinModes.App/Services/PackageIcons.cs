using System.IO;
using System.Xml.Linq;
using WinModes.Core.Apps;

namespace WinModes.App.Services;

/// <summary>
/// Finds the logo of an installed Store app. The manifest of a package names its logo ("Assets\Square44x44Logo.png"); the real files
/// carry a size or a scale in their name, so the best one is picked among them. Read-only, and never leaves the package folder.
/// </summary>
internal static class PackageIcons
{
    private const string ManifestFile = "AppxManifest.xml";

    // Best first: a 48 px logo without a background plate suits a dark or a light list, then plain sizes, then scales.
    private static readonly string[] Preferences =
    [
        "targetsize-48_altform-unplated", "targetsize-48", "targetsize-44_altform-unplated", "targetsize-44", "targetsize-40_altform-unplated",
        "targetsize-32_altform-unplated", "targetsize-32", "scale-200", "scale-150", "scale-125", "scale-100", "",
    ];

    private static readonly string[] LogoAttributes = ["Square44x44Logo", "Square30x30Logo", "Square71x71Logo", "Square150x150Logo", "Logo"];

    /// <summary>The path of the best logo file of the package, or null when there is none to read.</summary>
    public static string? FindLogo(string installLocation)
    {
        try
        {
            if (!AppGuard.IsPackageFolder(installLocation) || !Directory.Exists(installLocation))
            {
                return null;
            }

            var manifest = XDocument.Load(Path.Combine(installLocation, ManifestFile));
            foreach (var attribute in LogoAttributes)
            {
                var relative = manifest.Descendants().Select(element => element.Attribute(attribute)?.Value).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                    // The package logo is an element of the properties, not an attribute.
                    ?? (attribute == "Logo" ? manifest.Descendants().FirstOrDefault(element => element.Name.LocalName == "Logo")?.Value : null);
                if (!string.IsNullOrWhiteSpace(relative) && Resolve(installLocation, relative) is { } found)
                {
                    return found;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or ArgumentException or NotSupportedException)
        {
            // A package whose files cannot be read simply has no logo here.
        }

        return null;
    }

    private static string? Resolve(string installLocation, string relative)
    {
        var root = Path.GetFullPath(installLocation) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var folder = Path.GetDirectoryName(target);
        if (folder is null || !target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(folder))
        {
            return null;
        }

        var stem = Path.GetFileNameWithoutExtension(target);
        var candidates = Directory.EnumerateFiles(folder, stem + "*.png").ToList();
        foreach (var preference in Preferences)
        {
            var wanted = preference.Length == 0 ? stem + ".png" : $"{stem}.{preference}.png";
            if (candidates.FirstOrDefault(path => Path.GetFileName(path).Equals(wanted, StringComparison.OrdinalIgnoreCase)) is { } match)
            {
                return match;
            }
        }

        return candidates.FirstOrDefault();
    }
}
