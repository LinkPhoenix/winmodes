using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using WinModes.Core.Planning;

namespace WinModes.App.Services;

/// <summary>Questions about AI sessions shared by the AI tools page and the background features.</summary>
internal static class AiSessions
{
    /// <summary>True when the session works in a project folder rather than being a desktop app.</summary>
    public static bool HasProjectFolder(AiSession session)
    {
        var folder = session.Root.WorkingDirectory;
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return false;
        }

        // A desktop app runs from its own install folder or from System32: that is not a project folder.
        var isInstallFolder = session.Root.ExecutablePath is { } executable && executable.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
        var isSystemFolder = folder.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase);
        return !isInstallFolder && !isSystemFolder;
    }

    /// <summary>Folder name of the project, or "" for a desktop app.</summary>
    public static string ProjectName(AiSession session)
    {
        if (!HasProjectFolder(session))
        {
            return "";
        }

        var folder = session.Root.WorkingDirectory!;
        return Path.GetFileName(folder.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : folder;
    }

    /// <summary>Ends the session and every process it started. Returns false when Windows refused.</summary>
    public static bool End(AiSession session)
    {
        try
        {
            using var process = Process.GetProcessById(session.Root.Pid);
            // Guard against a recycled process id: only end the process the session was built from.
            if (session.Root.StartTime is { } started && Math.Abs((process.StartTime - started).TotalSeconds) > 2)
            {
                return false;
            }

            process.Kill(entireProcessTree: true);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or AggregateException)
        {
            return false;
        }
    }
}
