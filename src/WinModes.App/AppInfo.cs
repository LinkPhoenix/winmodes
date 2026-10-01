using System.Reflection;

namespace WinModes.App;

/// <summary>Facts about this build, shown in the title bar and on the About page.</summary>
internal static class AppInfo
{
    /// <summary>Version as major.minor.patch.</summary>
    public static string Version { get; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
}
