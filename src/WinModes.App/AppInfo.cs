using System.Reflection;
using WinModes.Core.Updates;

namespace WinModes.App;

/// <summary>Facts about this build, shown in the title bar and on the About page.</summary>
internal static class AppInfo
{
    /// <summary>Version as major.minor.patch.</summary>
    public static string Version { get; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Version as published: "0.9.3", or "0.9.3-beta.20261002" for a beta build.</summary>
    public static string FullVersion { get; } = ReadFullVersion();

    /// <summary>The number that orders the betas of one version, or null for a stable build.</summary>
    public static long? BetaNumber { get; } = ReleaseVersion.TryParseRelease(FullVersion, out _, out var beta) ? beta : null;

    public static bool IsBeta => BetaNumber is not null;

    private static string ReadFullVersion()
    {
        var informational = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        // The SDK appends "+<commit>" to the informational version.
        var text = informational?.Split('+', 2)[0];
        return ReleaseVersion.TryParseRelease(text, out _, out _) && text is not null ? text : Version;
    }
}
