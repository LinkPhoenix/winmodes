using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinModes.Core.Planning;

/// <summary>
/// One running process with its parent, load and launch details. <paramref name="PrivateMemoryMb"/> is the committed private memory;
/// <paramref name="WorkingSetMb"/> is what the process holds in RAM, shared pages included, so the two differ. The working set, the handle count
/// and the base priority come from data the sampler reads anyway: they cost no handle.
/// </summary>
public sealed record ProcessNode(
    int Pid,
    int ParentPid,
    string Name,
    int Threads,
    double PrivateMemoryMb,
    double CpuPercent,
    string? ExecutablePath,
    string? CommandLine,
    string? WorkingDirectory,
    DateTime? StartTime,
    double WorkingSetMb = 0,
    int Handles = 0,
    int BasePriority = 0);

/// <summary>
/// Samples every process: parent links from a toolhelp snapshot, CPU from the time used since the previous
/// sample, and command line / working directory read once per process. Read-only.
/// </summary>
public sealed class ProcessSampler
{
    private const double BytesPerMb = 1024d * 1024;
    private const uint SnapshotProcesses = 0x00000002;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessVmRead = 0x0010;
    private const int MaxPathChars = 32767;
    // 64-bit layout: PEB.ProcessParameters, then RTL_USER_PROCESS_PARAMETERS fields.
    private const int PebProcessParametersOffset = 0x20;
    private const int ParametersCurrentDirectoryOffset = 0x38;
    private const int ParametersCommandLineOffset = 0x70;
    private static readonly IntPtr InvalidHandle = new(-1);

    private readonly Dictionary<int, (DateTime? Start, TimeSpan Cpu)> _previousCpu = [];
    private readonly Dictionary<int, (DateTime? Start, LaunchInfo Info)> _launchInfo = [];
    private readonly Stopwatch _sinceLastSample = new();

    public IReadOnlyList<ProcessNode> Sample()
    {
        var elapsed = _sinceLastSample.IsRunning ? _sinceLastSample.Elapsed : TimeSpan.Zero;
        _sinceLastSample.Restart();

        var parents = ReadParentsAndThreads();
        var processes = Process.GetProcesses();
        var nodes = new List<ProcessNode>(processes.Length);
        var seen = new HashSet<int>();

        try
        {
            foreach (var process in processes)
            {
                seen.Add(process.Id);
                var start = TryRead(() => (DateTime?)process.StartTime);
                var cpuTime = TryRead(() => (TimeSpan?)process.TotalProcessorTime);
                var cpuPercent = 0d;

                if (cpuTime is { } current)
                {
                    if (elapsed > TimeSpan.Zero && _previousCpu.TryGetValue(process.Id, out var previous) && previous.Start == start)
                    {
                        cpuPercent = Math.Clamp(
                            (current - previous.Cpu).TotalMilliseconds / (elapsed.TotalMilliseconds * Environment.ProcessorCount) * 100, 0, 100);
                    }

                    _previousCpu[process.Id] = (start, current);
                }

                // A PID can be reused; the start time tells a new process from the cached one.
                if (!_launchInfo.TryGetValue(process.Id, out var cached) || cached.Start != start)
                {
                    cached = (start, ReadLaunchInfo(process));
                    _launchInfo[process.Id] = cached;
                }

                parents.TryGetValue(process.Id, out var link);
                nodes.Add(new ProcessNode(
                    process.Id,
                    link.ParentPid,
                    process.ProcessName,
                    link.Threads,
                    (TryRead(() => (long?)process.PrivateMemorySize64) ?? 0) / BytesPerMb,
                    cpuPercent,
                    cached.Info.Path,
                    cached.Info.CommandLine,
                    cached.Info.WorkingDirectory,
                    start,
                    (TryRead(() => (long?)process.WorkingSet64) ?? 0) / BytesPerMb,
                    TryRead(() => (int?)process.HandleCount) ?? 0,
                    link.BasePriority));
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        foreach (var pid in _previousCpu.Keys.Where(pid => !seen.Contains(pid)).ToList())
        {
            _previousCpu.Remove(pid);
            _launchInfo.Remove(pid);
        }

        return nodes;
    }

    /// <summary>Children of each process, ignoring parent links broken by PID reuse.</summary>
    public static ILookup<int, ProcessNode> BuildChildren(IReadOnlyList<ProcessNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var byPid = nodes.ToDictionary(node => node.Pid);
        return nodes
            .Where(node => node.Pid != 0 && HasLiveParent(node, byPid))
            .ToLookup(node => node.ParentPid);
    }

    public static bool HasLiveParent(ProcessNode node, IReadOnlyDictionary<int, ProcessNode> byPid)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(byPid);
        if (node.ParentPid == 0 || node.ParentPid == node.Pid || !byPid.TryGetValue(node.ParentPid, out var parent))
        {
            return false;
        }

        // A parent that started after its child is another process that reused the PID.
        return parent.StartTime is null || node.StartTime is null || parent.StartTime <= node.StartTime;
    }

    private static Dictionary<int, (int ParentPid, int Threads, int BasePriority)> ReadParentsAndThreads()
    {
        var result = new Dictionary<int, (int, int, int)>();
        var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot == InvalidHandle)
        {
            return result;
        }

        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (Process32FirstW(snapshot, ref entry))
            {
                do
                {
                    result[(int)entry.ProcessId] = ((int)entry.ParentProcessId, (int)entry.Threads, entry.BasePriority);
                }
                while (Process32NextW(snapshot, ref entry));
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return result;
    }

    private static LaunchInfo ReadLaunchInfo(Process process)
    {
        var path = TryReadReference(() => process.MainModule?.FileName);
        var handle = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, (uint)process.Id);
        if (handle == IntPtr.Zero)
        {
            // System and protected processes do not let a normal user read their parameters.
            return new LaunchInfo(path, null, null);
        }

        try
        {
            var basic = default(ProcessBasicInformation);
            if (NtQueryInformationProcess(handle, 0, ref basic, (uint)Marshal.SizeOf<ProcessBasicInformation>(), out _) != 0
                || basic.PebBaseAddress == IntPtr.Zero
                || !TryReadPointer(handle, basic.PebBaseAddress + PebProcessParametersOffset, out var parameters))
            {
                return new LaunchInfo(path, null, null);
            }

            var directory = ReadUnicodeString(handle, parameters + ParametersCurrentDirectoryOffset)?.TrimEnd('\\');
            return new LaunchInfo(path, ReadUnicodeString(handle, parameters + ParametersCommandLineOffset), directory);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static string? ReadUnicodeString(IntPtr handle, IntPtr address)
    {
        var header = new byte[16];
        if (!ReadProcessMemory(handle, address, header, header.Length, out _))
        {
            return null;
        }

        var length = BitConverter.ToUInt16(header, 0);
        var buffer = new IntPtr(BitConverter.ToInt64(header, 8));
        if (length == 0 || length > MaxPathChars * 2 || buffer == IntPtr.Zero)
        {
            return null;
        }

        var bytes = new byte[length];
        return ReadProcessMemory(handle, buffer, bytes, bytes.Length, out _) ? System.Text.Encoding.Unicode.GetString(bytes) : null;
    }

    private static bool TryReadPointer(IntPtr handle, IntPtr address, out IntPtr value)
    {
        var bytes = new byte[IntPtr.Size];
        var ok = ReadProcessMemory(handle, address, bytes, bytes.Length, out _);
        value = ok ? new IntPtr(BitConverter.ToInt64(bytes, 0)) : IntPtr.Zero;
        return ok && value != IntPtr.Zero;
    }

    private static T? TryRead<T>(Func<T?> read) where T : struct
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    private static string? TryReadReference(Func<string?> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    private sealed record LaunchInfo(string? Path, string? CommandLine, string? WorkingDirectory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, [Out] byte[] buffer, nint size, out nint read);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("ntdll.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int NtQueryInformationProcess(
        IntPtr process, int informationClass, ref ProcessBasicInformation information, uint length, out uint returned);
}
