using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WinModes.Core.Tuning;

/// <summary>The real registry. HKLM writes need administrator rights; keys are created when missing and never deleted.</summary>
public sealed class WindowsRegistryAccess : IRegistryAccess
{
    public RegistrySnapshot Read(TweakHive hive, string path, string name)
    {
        using var root = Open(hive);
        using var key = root.OpenSubKey(path);
        var value = key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (key is null || value is null)
        {
            return RegistrySnapshot.Missing;
        }

        return key.GetValueKind(name) switch
        {
            RegistryValueKind.DWord => new RegistrySnapshot(true, TweakValueKind.Number, ((int)value).ToString(CultureInfo.InvariantCulture)),
            RegistryValueKind.String => new RegistrySnapshot(true, TweakValueKind.Text, (string)value),
            _ => new RegistrySnapshot(true, null, null),
        };
    }

    public void Write(TweakHive hive, string path, string name, TweakValueKind kind, string value)
    {
        using var root = Open(hive);
        using var key = root.CreateSubKey(path, writable: true);
        if (kind == TweakValueKind.Number)
        {
            key.SetValue(name, int.Parse(value, CultureInfo.InvariantCulture), RegistryValueKind.DWord);
        }
        else
        {
            key.SetValue(name, value, RegistryValueKind.String);
        }
    }

    public void Delete(TweakHive hive, string path, string name)
    {
        using var root = Open(hive);
        using var key = root.OpenSubKey(path, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    private static RegistryKey Open(TweakHive hive) =>
        RegistryKey.OpenBaseKey(hive == TweakHive.Machine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64);
}

/// <summary>Scheduled tasks through the Task Scheduler COM service, so no command line is ever built.</summary>
public sealed class WindowsTaskControl : ITaskControl
{
    private const string SchedulerProgId = "Schedule.Service";

    public bool? IsEnabled(string taskPath)
    {
        try
        {
            return Use(taskPath, task => (bool)task.Enabled);
        }
        catch (Exception ex) when (ex is COMException or FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException)
        {
            // Not on this PC, or not readable by this user: nothing to offer.
            return null;
        }
    }

    public void SetEnabled(string taskPath, bool enabled) =>
        Use(taskPath, task =>
        {
            task.Enabled = enabled;
            return enabled;
        });

    private static bool Use(string taskPath, Func<dynamic, bool> action)
    {
        var split = taskPath.LastIndexOf('\\');
        var folderPath = split <= 0 ? "\\" : taskPath[..split];
        var type = Type.GetTypeFromProgID(SchedulerProgId) ?? throw new InvalidOperationException("The Task Scheduler is not available.");
        dynamic service = Activator.CreateInstance(type) ?? throw new InvalidOperationException("The Task Scheduler is not available.");
        try
        {
            service.Connect();
            dynamic task = service.GetFolder(folderPath).GetTask(taskPath[(split + 1)..]);
            return action(task);
        }
        finally
        {
            Marshal.FinalReleaseComObject(service);
        }
    }
}
