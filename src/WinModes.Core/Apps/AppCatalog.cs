using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinModes.Core.Apps;

/// <summary>How safe it is to remove an app. Anything that is not listed is never offered.</summary>
public enum AppTier
{
    /// <summary>Harmless for nearly everyone.</summary>
    Safe,

    /// <summary>Useful to many people: the user decides, with the consequences written next to it.</summary>
    Consider,
}

/// <summary>Where the user can get an app back.</summary>
public sealed record AppReinstall
{
    /// <summary>Microsoft Store product id.</summary>
    public string? Store { get; init; }

    public string? Winget { get; init; }
}

/// <summary>One preinstalled app or group of packages that WinModes may remove for the current user.</summary>
public sealed record AppEntry
{
    public required string Id { get; init; }
    public required string Title { get; init; }

    /// <summary>Package names, matched from the start of the package name; a trailing * is allowed.</summary>
    public IReadOnlyList<string> Packages { get; init; } = [];

    public string Category { get; init; } = "Other";
    public AppTier Tier { get; init; } = AppTier.Consider;
    public string Why { get; init; } = "";

    /// <summary>What stops working without it, shown before the removal.</summary>
    public string? BreaksIfRemoved { get; init; }

    public AppReinstall Reinstall { get; init; } = new();
}

/// <summary>
/// The apps WinModes may remove, read from data/apps.json. A package is offered only if an entry names it, and never if it is
/// protected: the catalog is an allow-list, and <see cref="AppGuard"/> is checked again at removal.
/// </summary>
public sealed class AppCatalog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private AppCatalog(IReadOnlyList<AppEntry> entries) => Entries = entries;

    public IReadOnlyList<AppEntry> Entries { get; }

    public static AppCatalog Load(string path)
    {
        try
        {
            var entries = File.Exists(path) ? JsonSerializer.Deserialize<List<AppEntry>>(File.ReadAllText(path), Options) ?? [] : [];

            // Fail closed: an entry that names a protected package is dropped, whatever the file says.
            return new AppCatalog([.. entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Id) && entry.Packages.Count > 0 && !entry.Packages.Any(AppGuard.IsProtectedPattern))
                .DistinctBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)]);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppCatalog([]);
        }
    }

    /// <summary>The entry that names an installed package, or null.</summary>
    public AppEntry? Find(InstalledPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return AppGuard.IsRemovable(package) ? Entries.FirstOrDefault(entry => entry.Packages.Any(pattern => AppGuard.Matches(pattern, package.Name))) : null;
    }
}
