using System.Security.AccessControl;
using System.Security.Principal;

namespace WinModes.Core.Engine;

/// <summary>Machine-wide storage. Only administrators can write there, so the journal cannot be forged to abuse a revert.</summary>
public static class AppPaths
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WinModes");

    public static string JournalDirectory { get; } = Path.Combine(DataDirectory, "journal");

    /// <summary>Creates the journal directory with an explicit ACL: administrators and SYSTEM write, users read.</summary>
    public static void EnsureProtectedJournalDirectory()
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

        var directory = new DirectoryInfo(JournalDirectory);
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
