using System.IO;
using Microsoft.Win32;
using WinModes.Core.Apps;

namespace WinModes.App.Services;

/// <summary>
/// Lists what starts with Windows and turns the items of this user on or off the way Task Manager does: by the StartupApproved
/// value, never by deleting the Run entry or the shortcut. The value an item had is journaled before the first change.
/// Items for every user live in HKLM and need the administrator: they are listed but left to Task Manager.
/// </summary>
internal static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32Key = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";
    private const string ApprovedRun = ApprovedRoot + @"\Run";
    private const string ApprovedRun32 = ApprovedRoot + @"\Run32";
    private const string ApprovedFolder = ApprovedRoot + @"\StartupFolder";

    public static StartupJournal Journal { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinModes", "startup-journal.json"));

    public static IReadOnlyList<StartupItem> List()
    {
        var items = new List<StartupItem>();
        items.AddRange(ReadRun(Registry.CurrentUser, RunKey, ApprovedRun, StartupSource.UserRun));
        items.AddRange(ReadRun(Registry.LocalMachine, RunKey, ApprovedRun, StartupSource.MachineRun));
        items.AddRange(ReadRun(Registry.LocalMachine, Run32Key, ApprovedRun32, StartupSource.MachineRun32));
        items.AddRange(ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), Registry.CurrentUser, StartupSource.UserFolder));
        items.AddRange(ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Registry.LocalMachine, StartupSource.CommonFolder));
        return items;
    }

    private static List<StartupItem> ReadRun(RegistryKey hive, string runKey, string approvedKey, StartupSource source)
    {
        var found = new List<StartupItem>();
        try
        {
            using var run = hive.OpenSubKey(runKey);
            using var approved = hive.OpenSubKey(approvedKey);
            foreach (var name in run?.GetValueNames() ?? [])
            {
                if (name.Length > 0)
                {
                    found.Add(new StartupItem(name, run!.GetValue(name)?.ToString() ?? "", source, approved?.GetValue(name) as byte[]));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // A key that cannot be read has nothing to list.
        }

        return found;
    }

    private static List<StartupItem> ReadFolder(string folder, RegistryKey hive, StartupSource source)
    {
        var found = new List<StartupItem>();
        try
        {
            using var approved = hive.OpenSubKey(ApprovedFolder);
            foreach (var file in Directory.EnumerateFiles(folder).Where(file => !Path.GetFileName(file).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)))
            {
                var name = Path.GetFileName(file);
                found.Add(new StartupItem(name, file, source, approved?.GetValue(name) as byte[]));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // No such folder, or not readable: nothing to list.
        }

        return found;
    }

    /// <summary>Turns a startup item of this user on or off. Returns null on success, or the reason it was not done.</summary>
    public static string? SetEnabled(StartupItem item, bool enabled, Func<string, bool> isProtectedStartupId)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.IsUserLevel)
        {
            return Loc.T("This item starts for every user: change it in Task Manager, with administrator rights.");
        }

        if (StartupGuard.IsProtected(item.Name, isProtectedStartupId))
        {
            return Loc.T("This item is protected: security software, audio drivers, WinModes itself and your own tools stay as they are.");
        }

        if (!StartupApproval.CanChange(item.Raw))
        {
            return Loc.T("Windows keeps a value for this item that WinModes does not understand, so it is left as it is.");
        }

        var value = enabled ? StartupApproval.Enable(item.Raw) : StartupApproval.Disable(DateTimeOffset.Now);
        return Write(item, value);
    }

    /// <summary>Puts an item back to what it was before WinModes first changed it.</summary>
    public static string? Reset(StartupItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (Journal.Find(item.Source, item.Name) is not { } change)
        {
            return null;
        }

        var error = change.BeforeBytes is { } before ? Write(item, before, remember: false) : Delete(item);
        if (error is null)
        {
            Journal.Forget(item.Source, item.Name);
        }

        return error;
    }

    private static string KeyOf(StartupItem item) => item.Source == StartupSource.UserFolder ? ApprovedFolder : ApprovedRun;

    private static string? Write(StartupItem item, byte[] value, bool remember = true)
    {
        try
        {
            if (remember)
            {
                Journal.Remember(item.Source, item.Name, item.Raw);
            }

            using var key = Registry.CurrentUser.CreateSubKey(KeyOf(item), writable: true);
            key.SetValue(item.Name, value, RegistryValueKind.Binary);

            // Read back: the change must be there, or the item was not changed.
            return key.GetValue(item.Name) is byte[] written && written.SequenceEqual(value) ? null : Loc.T("Windows did not keep the change.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return ex.Message;
        }
    }

    private static string? Delete(StartupItem item)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyOf(item), writable: true);
            key?.DeleteValue(item.Name, throwOnMissingValue: false);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return ex.Message;
        }
    }
}
