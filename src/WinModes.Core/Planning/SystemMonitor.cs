using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace WinModes.Core.Planning;

/// <summary>Memory used by every process sharing one executable name.</summary>
public sealed record ProcessGroup(string Name, int Count, double PrivateMemoryMb, string? ExecutablePath);

/// <summary>One installed Windows service as shown in the Services page.</summary>
public sealed record ServiceInfo(string Name, string DisplayName, bool IsRunning, ServiceStartMode StartMode, string? ExecutablePath);

/// <summary>Physical memory and commit charge, in GB.</summary>
public sealed record MemorySample(double TotalGb, double AvailableGb, double CommittedGb, double CommitLimitGb, double CachedGb)
{
    public double UsedGb => TotalGb - AvailableGb;
    public double UsedPercent => TotalGb <= 0 ? 0 : UsedGb / TotalGb * 100;
}

/// <summary>Read-only sampling of CPU load, processes and services. Never changes anything.</summary>
public sealed class SystemMonitor
{
    private const double BytesPerMb = 1024d * 1024;

    private ulong _lastIdle;
    private ulong _lastTotal;

    /// <summary>CPU load in percent since the previous call; the first call returns 0.</summary>
    public double SampleCpuPercent()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        // Kernel time already includes idle time.
        var total = kernel + user;
        var deltaTotal = total - _lastTotal;
        var deltaIdle = idle - _lastIdle;
        var isFirstSample = _lastTotal == 0;
        _lastIdle = idle;
        _lastTotal = total;

        return isFirstSample || deltaTotal == 0 ? 0 : Math.Clamp((1 - (double)deltaIdle / deltaTotal) * 100, 0, 100);
    }

    public static IReadOnlyList<ProcessGroup> GetProcessGroups()
    {
        var processes = Process.GetProcesses();
        try
        {
            return [.. processes
                .GroupBy(process => process.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Select(group => new ProcessGroup(group.Key, group.Count(), group.Sum(ReadPrivateBytes) / BytesPerMb, group.Select(ReadPath).FirstOrDefault(path => path is not null)))
                .OrderByDescending(group => group.PrivateMemoryMb)];
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    public static IReadOnlyList<ServiceInfo> GetServices()
    {
        var services = ServiceController.GetServices();
        try
        {
            return [.. services
                .Select(service => new ServiceInfo(
                    service.ServiceName,
                    service.DisplayName,
                    service.Status == ServiceControllerStatus.Running,
                    ReadStartMode(service),
                    ReadServiceBinary(service.ServiceName)))
                .OrderBy(service => service.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
        }
        finally
        {
            foreach (var service in services)
            {
                service.Dispose();
            }
        }
    }

    /// <summary>Cheap enough to call every second: one kernel call, no enumeration.</summary>
    public static MemorySample SampleMemory()
    {
        var info = new PerformanceInformation { Size = (uint)Marshal.SizeOf<PerformanceInformation>() };
        if (!GetPerformanceInfo(ref info, info.Size))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        // Every counter is expressed in pages.
        var gbPerPage = (double)info.PageSize / (1024d * 1024 * 1024);
        return new MemorySample(
            (double)info.PhysicalTotal * gbPerPage,
            (double)info.PhysicalAvailable * gbPerPage,
            (double)info.CommitTotal * gbPerPage,
            (double)info.CommitLimit * gbPerPage,
            (double)info.SystemCache * gbPerPage);
    }

    private static string? ReadPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Protected and system processes do not expose their module to a normal user.
            return null;
        }
    }

    private static string? ReadServiceBinary(string serviceName)
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
        if (key?.GetValue("ImagePath") is not string imagePath)
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(imagePath).Trim();
        if (expanded.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
        {
            expanded = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), expanded[12..]);
        }

        // ImagePath is a command line: keep the executable, drop its arguments.
        if (expanded.StartsWith('"'))
        {
            var closing = expanded.IndexOf('"', 1);
            return closing > 1 ? expanded[1..closing] : null;
        }

        var extension = expanded.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return extension > 0 ? expanded[..(extension + 4)] : null;
    }

    private static long ReadPrivateBytes(Process process)
    {
        try
        {
            return process.PrivateMemorySize64;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // The process exited or is protected; it simply does not count.
            return 0;
        }
    }

    private static ServiceStartMode ReadStartMode(ServiceController service)
    {
        try
        {
            return service.StartType switch
            {
                System.ServiceProcess.ServiceStartMode.Manual => ServiceStartMode.Manual,
                System.ServiceProcess.ServiceStartMode.Disabled => ServiceStartMode.Disabled,
                _ => ServiceStartMode.Automatic,
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return ServiceStartMode.Unknown;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInformation
    {
        public uint Size;
        public nuint CommitTotal;
        public nuint CommitLimit;
        public nuint CommitPeak;
        public nuint PhysicalTotal;
        public nuint PhysicalAvailable;
        public nuint SystemCache;
        public nuint KernelTotal;
        public nuint KernelPaged;
        public nuint KernelNonpaged;
        public nuint PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPerformanceInfo(ref PerformanceInformation info, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idleTime, out ulong kernelTime, out ulong userTime);
}
