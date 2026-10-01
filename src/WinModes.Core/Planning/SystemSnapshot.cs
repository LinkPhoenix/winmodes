using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace WinModes.Core.Planning;

/// <summary>Read-only figures shown in the app header: memory, process count and running services.</summary>
public sealed record SystemSnapshot(double TotalMemoryGb, double UsedMemoryGb, int ProcessCount, int RunningServiceCount)
{
    private const double BytesPerGb = 1024d * 1024 * 1024;

    public double UsedMemoryPercent => TotalMemoryGb <= 0 ? 0 : UsedMemoryGb / TotalMemoryGb * 100;
    public double FreeMemoryGb => TotalMemoryGb - UsedMemoryGb;

    public static SystemSnapshot Capture()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status))
        {
            throw new InvalidOperationException("GlobalMemoryStatusEx failed.");
        }

        var processes = Process.GetProcesses();
        var services = ServiceController.GetServices();
        try
        {
            return new SystemSnapshot(
                status.TotalPhysical / BytesPerGb,
                (status.TotalPhysical - status.AvailablePhysical) / BytesPerGb,
                processes.Length,
                services.Count(service => service.Status == ServiceControllerStatus.Running));
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }

            foreach (var service in services)
            {
                service.Dispose();
            }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}
