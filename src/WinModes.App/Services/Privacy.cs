namespace WinModes.App.Services;

/// <summary>
/// Privacy mode: hides what could identify the user or their work (project names, folders,
/// command-line arguments, the account name in paths) so the window can be shared or captured.
/// Figures and tool names stay visible.
/// </summary>
internal static class Privacy
{
    public const string CommandLineArgument = "--privacy";
    public const string HiddenFolder = "folder hidden";

    private static readonly Dictionary<string, int> ProjectNumbers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock Gate = new();

    /// <summary>Raised on the calling thread when the mode is turned on or off.</summary>
    public static event EventHandler? Changed;

    public static bool Enabled { get; private set; }

    public static void Set(bool enabled)
    {
        if (Enabled == enabled)
        {
            return;
        }

        Enabled = enabled;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>A project keeps the same neutral name for the whole run, so sessions stay distinguishable.</summary>
    public static string Project(string name)
    {
        if (!Enabled)
        {
            return name;
        }

        lock (Gate)
        {
            if (!ProjectNumbers.TryGetValue(name, out var number))
            {
                number = ProjectNumbers.Count + 1;
                ProjectNumbers[name] = number;
            }

            return $"Project {number}";
        }
    }

    /// <summary>Paths of the app's own data: only the account name is hidden.</summary>
    public static string Path(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Enabled && home.Length > 0 && path.StartsWith(home, StringComparison.OrdinalIgnoreCase)
            ? string.Concat("%USERPROFILE%", path.AsSpan(home.Length))
            : path;
    }

    /// <summary>Arguments often hold folders, file names and prompts: only the process name is kept.</summary>
    public static string CommandLine(string? commandLine, string processName) =>
        Enabled ? $"{processName} (details hidden)" : commandLine ?? "";
}
