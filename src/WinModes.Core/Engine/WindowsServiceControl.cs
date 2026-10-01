using System.ComponentModel;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Microsoft.Win32;
using WinModes.Core.Planning;
using ServiceStartMode = WinModes.Core.Planning.ServiceStartMode;

namespace WinModes.Core.Engine;

/// <summary>Controls real Windows services. Requires administrator rights for every write.</summary>
public sealed class WindowsServiceControl : IServiceControl
{
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(30);

    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceChangeConfig = 0x0002;
    private const uint ServiceNoChange = 0xFFFFFFFF;
    private const uint ServiceAutoStart = 2;
    private const uint ServiceDemandStart = 3;
    private const uint ServiceDisabled = 4;
    private const uint ServiceConfigDelayedAutoStartInfo = 3;

    private readonly WindowsSystemProbe _probe = new();

    public ServiceState? GetState(string name) => _probe.GetService(name);

    public bool IsDelayedAutoStart(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
        return key?.GetValue("DelayedAutostart") is int value && value == 1;
    }

    public IReadOnlyList<string> GetRunningDependents(string name)
    {
        using var controller = new ServiceController(name);
        var dependents = controller.DependentServices;
        try
        {
            return [.. dependents.Where(service => service.Status != ServiceControllerStatus.Stopped).Select(service => service.ServiceName)];
        }
        finally
        {
            foreach (var dependent in dependents)
            {
                dependent.Dispose();
            }
        }
    }

    public void SetStartMode(string name, ServiceStartMode mode, bool delayedAutoStart)
    {
        var startType = mode switch
        {
            ServiceStartMode.Automatic => ServiceAutoStart,
            ServiceStartMode.Manual => ServiceDemandStart,
            ServiceStartMode.Disabled => ServiceDisabled,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported start mode."),
        };

        var manager = OpenSCManager(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var service = OpenService(manager, name, ServiceChangeConfig);
            if (service == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                if (!ChangeServiceConfig(service, ServiceNoChange, startType, ServiceNoChange, null, null, IntPtr.Zero, null, null, null, null))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                if (mode == ServiceStartMode.Automatic)
                {
                    var info = new DelayedAutoStartInfo { DelayedAutoStart = delayedAutoStart };
                    // Some services reject the delayed flag (e.g. in a load-order group); the start type is already set.
                    _ = ChangeServiceConfig2(service, ServiceConfigDelayedAutoStartInfo, ref info);
                }
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    public void StopService(string name)
    {
        using var controller = new ServiceController(name);
        if (controller.Status == ServiceControllerStatus.Stopped)
        {
            return;
        }

        // Stop(false) never stops dependent services; the engine checks dependents before calling.
        controller.Stop(stopDependentServices: false);
        controller.WaitForStatus(ServiceControllerStatus.Stopped, StatusTimeout);
    }

    public void StartService(string name)
    {
        using var controller = new ServiceController(name);
        if (controller.Status == ServiceControllerStatus.Running)
        {
            return;
        }

        controller.Start();
        controller.WaitForStatus(ServiceControllerStatus.Running, StatusTimeout);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DelayedAutoStartInfo
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool DelayedAutoStart;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint access);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr OpenService(IntPtr manager, string serviceName, uint access);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceConfig(
        IntPtr service, uint serviceType, uint startType, uint errorControl, string? binaryPathName, string? loadOrderGroup,
        IntPtr tagId, string? dependencies, string? serviceStartName, string? password, string? displayName);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeServiceConfig2(IntPtr service, uint infoLevel, ref DelayedAutoStartInfo info);

    [DllImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);
}
