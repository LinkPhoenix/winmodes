using System.Diagnostics;
using System.ServiceProcess;

namespace WinModes.Core.Planning;

/// <summary>Reads service, process and WSL state from the local machine. Never changes anything.</summary>
public sealed class WindowsSystemProbe : ISystemProbe
{
    // The WSL2 utility VM shows up as this process while any distribution or Docker Desktop runs.
    private const string WslVmProcessName = "vmmemWSL";

    public ServiceState? GetService(string name)
    {
        try
        {
            using var controller = new ServiceController(name);
            var startMode = controller.StartType switch
            {
                System.ServiceProcess.ServiceStartMode.Automatic or System.ServiceProcess.ServiceStartMode.Boot
                    or System.ServiceProcess.ServiceStartMode.System => ServiceStartMode.Automatic,
                System.ServiceProcess.ServiceStartMode.Manual => ServiceStartMode.Manual,
                System.ServiceProcess.ServiceStartMode.Disabled => ServiceStartMode.Disabled,
                _ => ServiceStartMode.Unknown,
            };
            return new ServiceState(startMode, controller.Status == ServiceControllerStatus.Running);
        }
        catch (InvalidOperationException)
        {
            // ServiceController throws this when the service does not exist.
            return null;
        }
    }

    public bool IsProcessRunning(string processFileName)
    {
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(processFileName));
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    public bool IsWslRunning() => IsProcessRunning(WslVmProcessName);
}
