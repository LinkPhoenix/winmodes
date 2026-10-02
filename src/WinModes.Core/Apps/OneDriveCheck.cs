namespace WinModes.Core.Apps;

/// <summary>What WinModes found out about OneDrive on this PC before it may be uninstalled.</summary>
/// <param name="Installed">OneDrive is installed for this user.</param>
/// <param name="Roots">The folders that OneDrive syncs into (personal and work accounts).</param>
/// <param name="SignedIn">At least one account is signed in.</param>
/// <param name="RedirectedFolders">Known folders (Desktop, Documents...) that live inside a OneDrive folder.</param>
/// <param name="OnlineOnlyFiles">How many files exist only in the cloud, counted up to a limit.</param>
/// <param name="CountWasCut">The count stopped at the limit or the time allowed.</param>
public sealed record OneDriveState(
    bool Installed, IReadOnlyList<string> Roots, bool SignedIn, IReadOnlyList<string> RedirectedFolders, int OnlineOnlyFiles, bool CountWasCut)
{
    /// <summary>Uninstalling is refused while a known folder lives in OneDrive: the folder would be left behind a sync that no longer runs.</summary>
    public bool CanUninstall => Installed && RedirectedFolders.Count == 0;

    /// <summary>Things that can still be lost: the user must confirm that they were looked at.</summary>
    public bool NeedsConfirmation => SignedIn || OnlineOnlyFiles > 0;
}

/// <summary>The checks before a OneDrive uninstall, kept free of the registry and the disk so they can be tested.</summary>
public static class OneDriveCheck
{
    // Files that exist only online carry one of these attributes (placeholders of Files On-Demand).
    public const FileAttributes OnlineOnly = (FileAttributes)0x00400000 | (FileAttributes)0x00040000 | FileAttributes.Offline;

    /// <summary>True when a file is a placeholder whose content is only in the cloud.</summary>
    public static bool IsOnlineOnly(FileAttributes attributes) => (attributes & OnlineOnly) != 0;

    /// <summary>
    /// The known folders that live inside one of the OneDrive roots. <paramref name="knownFolders"/> maps a name (Desktop, Documents...) to its
    /// path, with variables already expanded. Every root is compared, not only the default one.
    /// </summary>
    public static IReadOnlyList<string> Redirected(IReadOnlyDictionary<string, string> knownFolders, IReadOnlyList<string> roots)
    {
        ArgumentNullException.ThrowIfNull(knownFolders);
        ArgumentNullException.ThrowIfNull(roots);
        var cleanRoots = roots.Select(Normalize).Where(root => root.Length > 0).ToList();
        return [.. knownFolders
            .Where(folder => cleanRoots.Any(root => IsInside(Normalize(folder.Value), root)))
            .Select(folder => folder.Key)
            .Order(StringComparer.OrdinalIgnoreCase)];
    }

    public static bool IsInside(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path)
    {
        var text = path.Trim().Trim('"');
        if (text.Length == 0)
        {
            return "";
        }

        try
        {
            return Path.GetFullPath(text).TrimEnd(Path.DirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "";
        }
    }

    /// <summary>
    /// The uninstall command OneDrive's own setup program accepts. Only the Windows copy of the setup program is used, so a path
    /// read from the registry can never decide what is run.
    /// </summary>
    public static string? SetupProgram(Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var candidates = new[]
        {
            Path.Combine(windows, "System32", "OneDriveSetup.exe"),
            Path.Combine(windows, "SysWOW64", "OneDriveSetup.exe"),
        };
        return candidates.FirstOrDefault(fileExists);
    }
}
