using System.Runtime.InteropServices;
using System.Security.Principal;

namespace WinModes.Core.Engine;

/// <summary>
/// Opt-in scheduled task that starts the elevated helper with the highest privileges, so a mode switch needs no
/// permission prompt. Registering it needs administrator rights (one prompt); after that the user who registered
/// it can start it on demand. The task can only ask for "apply &lt;mode&gt;" or "revert": the helper refuses
/// anything else that comes through it.
/// </summary>
public static class SilentSwitchTask
{
    /// <summary>First argument of the helper when the task starts it.</summary>
    public const string HelperVerb = "auto";

    private const string TaskName = "WinModes silent switch";
    private const string RootFolder = "\\";
    private const string SchedulerProgId = "Schedule.Service";
    // The two values come from Run(): quoting the mode keeps a name with spaces in one argument.
    private const string ActionArguments = HelperVerb + " $(Arg0) \"$(Arg1)\"";
    private const string RunTimeLimit = "PT5M";

    // Task Scheduler constants (taskschd.h).
    private const int ActionExec = 0;
    private const int LogonInteractiveToken = 3;
    private const int RunLevelHighest = 1;
    private const int CreateOrUpdate = 6;
    private const int InstancesQueue = 1;
    private const int StateQueued = 2;
    private const int StateRunning = 4;

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// A task that runs elevated without a prompt must not point at a file a non-administrator can replace,
    /// so it is only offered for a copy installed under Program Files.
    /// </summary>
    public static bool IsTrustedLocation(string helperPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(helperPath);
        var full = Path.GetFullPath(helperPath);
        return new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
            .Select(Environment.GetFolderPath)
            .Where(folder => folder.Length > 0)
            .Any(folder => full.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Registers or replaces the task. Needs administrator rights.</summary>
    public static void Install(string helperPath)
    {
        if (!IsTrustedLocation(helperPath))
        {
            throw new InvalidOperationException("The silent switch is only available for a copy installed under Program Files.");
        }

        Use(service =>
        {
            dynamic definition = service.NewTask(0);
            definition.RegistrationInfo.Description = "Lets WinModes switch modes without a permission prompt. Turn it off on the WinModes Automation page.";
            definition.Principal.UserId = WindowsIdentity.GetCurrent().Name;
            definition.Principal.LogonType = LogonInteractiveToken;
            definition.Principal.RunLevel = RunLevelHighest;
            definition.Settings.AllowDemandStart = true;
            definition.Settings.DisallowStartIfOnBatteries = false;
            definition.Settings.StopIfGoingOnBatteries = false;
            definition.Settings.MultipleInstances = InstancesQueue;
            definition.Settings.ExecutionTimeLimit = RunTimeLimit;
            dynamic action = definition.Actions.Create(ActionExec);
            action.Path = helperPath;
            action.Arguments = ActionArguments;
            service.GetFolder(RootFolder).RegisterTaskDefinition(TaskName, definition, CreateOrUpdate, null, null, LogonInteractiveToken, null);
            return true;
        });
    }

    /// <summary>Removes the task; nothing happens when it does not exist. Needs administrator rights.</summary>
    public static void Remove() =>
        Use(service =>
        {
            try
            {
                service.GetFolder(RootFolder).DeleteTask(TaskName, 0);
            }
            catch (FileNotFoundException)
            {
                // Already gone.
            }

            return true;
        });

    /// <summary>True when the task exists and starts exactly this helper.</summary>
    public static bool IsInstalled(string helperPath)
    {
        try
        {
            return Use(service =>
            {
                dynamic task = service.GetFolder(RootFolder).GetTask(TaskName);
                dynamic action = task.Definition.Actions.Item(1);
                return string.Equals((string)action.Path, helperPath, StringComparison.OrdinalIgnoreCase)
                    && (string)action.Arguments == ActionArguments;
            });
        }
        catch (Exception ex) when (IsSchedulerFailure(ex))
        {
            return false;
        }
    }

    /// <summary>
    /// Starts the helper through the task and waits for it. Returns its exit code, or null when the task could
    /// not be used (not installed, not startable by this user, no answer in time): the caller then asks normally.
    /// </summary>
    public static int? Run(string helperPath, string verb, string mode = "")
    {
        // The mode is quoted into the task's arguments, so a name with a quote must never get that far.
        if (verb is not ("apply" or "revert") || (mode.Length > 0 && !HelperArguments.IsValidName(mode)) || !IsInstalled(helperPath))
        {
            return null;
        }

        try
        {
            return Use<int?>(service =>
            {
                dynamic task = service.GetFolder(RootFolder).GetTask(TaskName);
                var before = (DateTime)task.LastRunTime;
                task.Run(new[] { verb, mode });

                // The scheduler starts the task asynchronously: wait until this run has started, then until it ended.
                var started = false;
                var startDeadline = DateTime.UtcNow + StartTimeout;
                var endDeadline = DateTime.UtcNow + RunTimeout;
                while (DateTime.UtcNow < endDeadline)
                {
                    Thread.Sleep(PollInterval);
                    // Read a fresh copy each time: the values of an older one are not guaranteed to follow.
                    task = service.GetFolder(RootFolder).GetTask(TaskName);
                    started = started || (DateTime)task.LastRunTime != before;
                    if (!started)
                    {
                        if (DateTime.UtcNow > startDeadline)
                        {
                            return null;
                        }

                        continue;
                    }

                    var state = (int)task.State;
                    if (state != StateRunning && state != StateQueued)
                    {
                        return (int)task.LastTaskResult;
                    }
                }

                return null;
            });
        }
        catch (Exception ex) when (IsSchedulerFailure(ex))
        {
            return null;
        }
    }

    private static bool IsSchedulerFailure(Exception ex) =>
        ex is COMException or FileNotFoundException or DirectoryNotFoundException or UnauthorizedAccessException
            or InvalidOperationException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException;

    private static T Use<T>(Func<dynamic, T> action)
    {
        var type = Type.GetTypeFromProgID(SchedulerProgId) ?? throw new InvalidOperationException("The Task Scheduler is not available.");
        dynamic service = Activator.CreateInstance(type) ?? throw new InvalidOperationException("The Task Scheduler is not available.");
        try
        {
            service.Connect();
            return action(service);
        }
        finally
        {
            Marshal.FinalReleaseComObject(service);
        }
    }
}
