using System.ComponentModel;
using System.Diagnostics;

namespace WinModes.App.Services;

/// <summary>GPU load and disk activity at one moment. A null value means Windows does not expose that counter here.</summary>
internal sealed record GpuDiskReading(double? GpuPercent, double? DiskActivePercent, double DiskReadMbPerSecond, double DiskWriteMbPerSecond);

/// <summary>
/// Reads the Windows performance counters Task Manager uses for the GPU and the disks. Read-only.
/// Counters can be missing or damaged on some PCs: each one is then reported as unavailable instead of failing.
/// </summary>
internal sealed class GpuDiskMonitor : IDisposable
{
    private const string GpuCategory = "GPU Engine";
    private const string GpuCounter = "Utilization Percentage";
    private const string EngineTypeMarker = "engtype_";
    private const string DiskCategory = "PhysicalDisk";
    private const string AllDisks = "_Total";
    private const double BytesPerMb = 1024d * 1024;

    /// <summary>One instance for the whole run: the counters stay open between two visits of the dashboard.</summary>
    public static GpuDiskMonitor Shared { get; } = new();

    private Dictionary<string, CounterSample> _lastGpu = [];
    private bool _gpuAvailable = true;
    private PerformanceCounter? _diskIdle;
    private PerformanceCounter? _diskRead;
    private PerformanceCounter? _diskWrite;
    private bool _diskAvailable = true;

    /// <summary>Values since the previous call; the first call returns zeros.</summary>
    public GpuDiskReading Sample()
    {
        var gpu = SampleGpu();
        var (active, read, write) = SampleDisk();
        return new GpuDiskReading(gpu, active, read, write);
    }

    private double? SampleGpu()
    {
        if (!_gpuAvailable)
        {
            return null;
        }

        try
        {
            var data = new PerformanceCounterCategory(GpuCategory).ReadCategory();
            if (data[GpuCounter] is not { } instances)
            {
                _gpuAvailable = false;
                return null;
            }

            var current = new Dictionary<string, CounterSample>(StringComparer.Ordinal);
            var byEngine = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (InstanceData instance in instances.Values)
            {
                current[instance.InstanceName] = instance.Sample;
                if (!_lastGpu.TryGetValue(instance.InstanceName, out var previous))
                {
                    continue;
                }

                // One instance per process and engine: the load of an engine type is the sum over processes.
                var marker = instance.InstanceName.IndexOf(EngineTypeMarker, StringComparison.Ordinal);
                var engine = marker < 0 ? "" : instance.InstanceName[(marker + EngineTypeMarker.Length)..];
                byEngine[engine] = byEngine.GetValueOrDefault(engine) + CounterSample.Calculate(previous, instance.Sample);
            }

            _lastGpu = current;
            // Like Task Manager: the busiest engine (3D, video decode, copy...) is the GPU figure.
            return byEngine.Count == 0 ? 0 : Math.Clamp(byEngine.Values.Max(), 0, 100);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        {
            _gpuAvailable = false;
            return null;
        }
    }

    private (double? ActivePercent, double ReadMb, double WriteMb) SampleDisk()
    {
        if (!_diskAvailable)
        {
            return (null, 0, 0);
        }

        try
        {
            _diskIdle ??= new PerformanceCounter(DiskCategory, "% Idle Time", AllDisks, readOnly: true);
            _diskRead ??= new PerformanceCounter(DiskCategory, "Disk Read Bytes/sec", AllDisks, readOnly: true);
            _diskWrite ??= new PerformanceCounter(DiskCategory, "Disk Write Bytes/sec", AllDisks, readOnly: true);
            var idle = _diskIdle.NextValue();
            // The idle counter reads 0 on its first sample; that is not a busy disk.
            var active = idle <= 0 ? 0 : Math.Clamp(100 - idle, 0, 100);
            return (active, _diskRead.NextValue() / BytesPerMb, _diskWrite.NextValue() / BytesPerMb);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        {
            _diskAvailable = false;
            return (null, 0, 0);
        }
    }

    public void Dispose()
    {
        _diskIdle?.Dispose();
        _diskRead?.Dispose();
        _diskWrite?.Dispose();
    }
}
