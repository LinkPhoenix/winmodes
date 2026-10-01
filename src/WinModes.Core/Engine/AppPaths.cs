using System.Security.AccessControl;
using System.Security.Principal;

namespace WinModes.Core.Engine;

/// <summary>Machine-wide storage. Only administrators can write there, so the journal cannot be forged to abuse a revert.</summary>
public static class AppPaths
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WinModes");

    public static string JournalDirectory { get; } = Path.Combine(DataDirectory, "journal");

    /// <summary>Service changes the user made by hand or from the Optimize page, with the values to restore.</summary>
    public static string TweaksDirectory { get; } = Path.Combine(DataDirectory, "tweaks");

    /// <summary>Machine-wide tweaks (HKLM values, scheduled tasks) applied by the elevated helper.</summary>
    public static string MachineTweakJournal { get; } = Path.Combine(TweaksDirectory, "registry.json");

    /// <summary>Errors of the elevated helper, which has no window to report them in. Readable by users.</summary>
    public static string LogDirectory { get; } = Path.Combine(DataDirectory, "logs");

    public static string HelperErrorLog { get; } = Path.Combine(LogDirectory, "helper-errors.log");

    /// <summary>Creates the journal directory with an explicit ACL: administrators and SYSTEM write, users read.</summary>
    public static void EnsureProtectedJournalDirectory() => EnsureProtectedDirectory(JournalDirectory);

    /// <summary>
    /// Creates a directory under <see cref="DataDirectory"/> that only administrators and SYSTEM can write, and
    /// the same for <see cref="DataDirectory"/> itself, which inherits write access for users from ProgramData.
    /// A folder that already exists and belongs to someone else is refused: its owner could change the rights back.
    /// </summary>
    public static void EnsureProtectedDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!SamePath(path, DataDirectory))
        {
            EnsureProtectedDirectory(DataDirectory);
        }

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags Inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, Inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, Inherit, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute, Inherit, PropagationFlags.None, AccessControlType.Allow));

        var directory = new DirectoryInfo(path);
        if (directory.Exists)
        {
            var owner = directory.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            if (!IsTrustedOwner(owner))
            {
                throw new InvalidOperationException($"'{path}' belongs to {owner?.Value ?? "an unknown owner"}, not to administrators; refusing to use it.");
            }

            // Re-apply on every run: the folder may have been changed since.
            directory.SetAccessControl(security);
        }
        else
        {
            directory.Create(security);
        }
    }

    /// <summary>Only administrators and SYSTEM may own the machine-wide folders.</summary>
    public static bool IsTrustedOwner(SecurityIdentifier? owner) =>
        owner is not null && (owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) || owner.IsWellKnown(WellKnownSidType.LocalSystemSid));

    private static bool SamePath(string first, string second) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)), StringComparison.OrdinalIgnoreCase);
}
