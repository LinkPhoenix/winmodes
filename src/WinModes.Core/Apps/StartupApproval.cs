namespace WinModes.Core.Apps;

/// <summary>
/// The value that Task Manager keeps for each startup item in the registry (the StartupApproved keys): 12 bytes, a status then a
/// time. WinModes reads and writes it exactly as Task Manager does, so an item turned off here shows as disabled there and back,
/// and nothing is ever deleted. The layout was read on a live Windows 11 registry and in five open-source startup managers.
/// </summary>
public static class StartupApproval
{
    public const int Length = 12;

    private const byte EnabledStatus = 0x02;
    private const byte DisabledStatus = 0x03;
    private const int TimeOffset = 4;
    private const int TimeLength = 8;

    /// <summary>A missing value means enabled; an even first byte is enabled, an odd one disabled.</summary>
    public static bool IsEnabled(byte[]? raw) => raw is null || raw.Length == 0 || (raw[0] & 1) == 0;

    /// <summary>When the item was turned off, as Task Manager shows it; null if the value has no time.</summary>
    public static DateTimeOffset? DisabledAt(byte[]? raw)
    {
        if (raw is not { Length: >= Length } || IsEnabled(raw))
        {
            return null;
        }

        var ticks = BitConverter.ToInt64(raw, TimeOffset);
        try
        {
            return ticks > 0 ? DateTimeOffset.FromFileTime(ticks) : null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>A value that turns an item off, stamped with the time as Task Manager does.</summary>
    public static byte[] Disable(DateTimeOffset now)
    {
        var raw = new byte[Length];
        raw[0] = DisabledStatus;
        BitConverter.GetBytes(now.ToFileTime()).CopyTo(raw, TimeOffset);
        return raw;
    }

    /// <summary>A value that turns an item on. A status Windows wrote itself (06 for some items) is kept, so nothing else changes.</summary>
    public static byte[] Enable(byte[]? previous)
    {
        var raw = new byte[Length];
        raw[0] = previous is { Length: > 0 } && (previous[0] & 1) == 0 ? previous[0] : EnabledStatus;
        return raw;
    }

    /// <summary>True for a value WinModes understands well enough to change: 12 bytes with a known status.</summary>
    public static bool CanChange(byte[]? raw) => raw is null || (raw.Length == Length && raw[0] is 0x02 or 0x03 or 0x06 or 0x07);

    // The unused bytes of a time field must stay zero for an enabled item.
    public static bool HasNoTime(byte[] raw) => raw.Length >= TimeOffset + TimeLength && raw.AsSpan(TimeOffset, TimeLength).IndexOfAnyExcept((byte)0) < 0;
}

/// <summary>Where a startup item comes from.</summary>
public enum StartupSource
{
    /// <summary>HKCU Run: this user.</summary>
    UserRun,

    /// <summary>The Startup folder of this user.</summary>
    UserFolder,

    /// <summary>HKLM Run: every user. Changing it needs administrator rights.</summary>
    MachineRun,

    /// <summary>HKLM Run of 32-bit programs.</summary>
    MachineRun32,

    /// <summary>The Startup folder shared by every user.</summary>
    CommonFolder,
}

/// <summary>One program that starts with Windows.</summary>
/// <param name="Name">The value name, or the file name for a folder item: the key of the approval value.</param>
/// <param name="Command">The command line or the shortcut path.</param>
/// <param name="Source">Where it comes from.</param>
/// <param name="Raw">The approval value as it is, or null when there is none.</param>
public sealed record StartupItem(string Name, string Command, StartupSource Source, byte[]? Raw)
{
    public bool Enabled => StartupApproval.IsEnabled(Raw);

    /// <summary>Items of this user can be changed by WinModes itself; the others need the administrator.</summary>
    public bool IsUserLevel => Source is StartupSource.UserRun or StartupSource.UserFolder;

    /// <summary>The path of the program: the first quoted part of the command, or the first word that ends in .exe.</summary>
    public string? Program
    {
        get
        {
            var text = Command.Trim();

            // A shortcut of a Startup folder is a file, not a command line.
            if (text.Length == 0 || Source is StartupSource.UserFolder or StartupSource.CommonFolder)
            {
                return null;
            }

            if (text[0] == '"')
            {
                var end = text.IndexOf('"', 1);
                return end > 1 ? text[1..end] : null;
            }

            var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return exe > 0 ? text[..(exe + 4)] : text.Split(' ', 2)[0];
        }
    }
}

/// <summary>What WinModes never turns off at start-up.</summary>
public static class StartupGuard
{
    // Security software and the Windows Security icon: nothing here is ever offered for turning off.
    private static readonly string[] ProtectedNames =
    [
        "SecurityHealth", "Windows Defender*", "WindowsDefender*", "Bitdefender*", "Bdagent", "BdVpn*", "Adguard*", "AdGuard*", "ABSpawnhlp",
        "RtkAudUService", "WinModes",
    ];

    /// <summary>True when the item must stay as it is: security, audio drivers, WinModes itself, or the protected tools of the user.</summary>
    public static bool IsProtected(string name, Func<string, bool> isProtectedStartupId)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(isProtectedStartupId);
        return isProtectedStartupId(name) || ProtectedNames.Any(pattern => AppGuard.Matches(pattern, name));
    }
}
