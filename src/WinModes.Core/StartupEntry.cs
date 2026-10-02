namespace WinModes.Core;

/// <summary>Whether the entry that starts WinModes with Windows can work.</summary>
public enum StartupState
{
    /// <summary>No entry: the user did not ask for WinModes to start with Windows.</summary>
    NotRegistered,

    /// <summary>The entry points to a file that exists.</summary>
    Working,

    /// <summary>The entry points to a file that is gone (the app was moved, uninstalled or updated elsewhere): Windows skips it without a word.</summary>
    TargetMissing,
}

/// <summary>Reads the command Windows runs at sign-in for WinModes (the per-user Run entry).</summary>
public static class StartupEntry
{
    /// <summary>The program in a Run command: <c>"C:\Apps\WinModes.exe" --minimized</c> gives <c>C:\Apps\WinModes.exe</c>. Null when there is none.</summary>
    public static string? ProgramOf(string? command)
    {
        var text = command?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        string program;
        if (text[0] == '"')
        {
            var end = text.IndexOf('"', 1);
            program = end > 1 ? text[1..end] : text[1..];
        }
        else
        {
            // Unquoted: the program ends with .exe, or at the first space when it has no extension.
            var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            program = exe >= 0 ? text[..(exe + 4)] : text.Split(' ', 2)[0];
        }

        program = Environment.ExpandEnvironmentVariables(program.Trim());
        return program.Length > 0 ? program : null;
    }

    /// <summary>The state of an entry; <paramref name="fileExists"/> is passed in so the check can be tested.</summary>
    public static StartupState Inspect(string? command, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        return ProgramOf(command) is not { } program ? StartupState.NotRegistered : fileExists(program) ? StartupState.Working : StartupState.TargetMissing;
    }
}
