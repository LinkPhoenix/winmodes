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

    /// <summary>Creates the journal directory with an explicit ACL: administrators and SYSTEM write, users read.</summary>
    public static void EnsureProtectedJournalDirectory() => EnsureProtectedDirectory(JournalDirectory);

    /// <summary>Creates a directory under <see cref="DataDirectory"/> that only administrators and SYSTEM can write.</summary>
    public static void EnsureProtectedDirectory(string path)
    {
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
            // Re-apply on every run: the folder may have been created by something else.
            directory.SetAccessControl(security);
        }
        else
        {
            Directory.CreateDirectory(DataDirectory);
            directory.Create(security);
        }
    }
}
