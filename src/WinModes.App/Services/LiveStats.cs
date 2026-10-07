using System.ComponentModel;
using System.Windows.Threading;
using WinModes.Core.Monitoring;
using WinModes.Core.Planning;

namespace WinModes.App.Services;

/// <summary>
/// One reading shared by the tray meter and the desktop widget. A source nobody asked for was not read: its figures are zero and its list empty,
/// which no feature draws, since a feature only draws what it asked for.
/// </summary>
internal sealed record StatsReading(
    double CpuPercent, MemorySample Memory, IReadOnlyList<AiToolUsage> AiTools, double DownMbps, double UpMbps, IReadOnlyList<AiSession> Sessions)
{
    public double AiMemoryMb => AiTools.Sum(tool => tool.MemoryMb);

    /// <summary>What a tick gives when nothing has to be read: the widget still gets its tick to refresh its plans and its mode.</summary>
    public static StatsReading Empty { get; } = new(0, new MemorySample(0, 0, 0, 0, 0), [], 0, 0, []);
}

internal sealed record AiToolUsage(string Name, int Sessions, double MemoryMb, string? ExecutablePath);

/// <summary>
/// Background sampling for features that live outside the main window. It runs only while something listens, and it reads only the
/// sources that something asked for (<see cref="SetDemand"/>) and the user allows (<see cref="Allowed"/>): the CPU, the memory, the network
/// and the search for AI tools are four separate readings, each one skipped when nobody shows it.
/// </summary>
internal sealed class LiveStats
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);
    private const StatsSources EverySource = StatsSources.Cpu | StatsSources.Memory | StatsSources.Network | StatsSources.AiTools;

    private readonly DispatcherTimer _timer = new() { Interval = Interval };

    // One monitor per source, so a source that is read again after a pause starts from nothing instead of averaging the whole pause.
    private SystemMonitor _cpuMonitor = new();
    private SystemMonitor _networkMonitor = new();
    private readonly Dictionary<object, StatsSources> _demands = [];
    private StatsSources _lastRead;
    private bool _sampling;
    private EventHandler<StatsReading>? _updated;

    /// <summary>Time between two samples.</summary>
    public TimeSpan RefreshInterval
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
    }

    /// <summary>What the user lets be read at all.</summary>
    public StatsSources Allowed { get; set; } = EverySource;

    /// <summary>What <paramref name="owner"/> (the app, a page showing a preview) needs from the next samples; none removes its demand.</summary>
    public void SetDemand(object owner, StatsSources sources)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (sources == StatsSources.None)
        {
            _demands.Remove(owner);
        }
        else
        {
            _demands[owner] = sources;
        }
    }

    public LiveStats() => _timer.Tick += async (_, _) => await SampleAsync();

    public event EventHandler<StatsReading> Updated
    {
        add
        {
            _updated += value;
            if (!_timer.IsEnabled)
            {
                _timer.Start();
                _ = SampleAsync();
            }
        }
        remove
        {
            _updated -= value;
            if (_updated is null)
            {
                _timer.Stop();
            }
        }
    }

    private async Task SampleAsync()
    {
        if (_sampling)
        {
            return;
        }

        _sampling = true;
        try
        {
            var wanted = _demands.Values.Aggregate(StatsSources.None, (all, one) => all | one) & Allowed;
            StartFresh(wanted & ~_lastRead);
            _lastRead = wanted;

            // Nothing to read: no thread is used and no process is touched.
            var reading = wanted == StatsSources.None ? StatsReading.Empty : await Task.Run(() => Read(wanted));
            _updated?.Invoke(this, reading);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or System.Net.NetworkInformation.NetworkInformationException)
        {
            // A failed sample is skipped; the next tick tries again.
        }
        finally
        {
            _sampling = false;
        }
    }

    /// <summary>A source that was not read at the previous tick has nothing to compare with: its monitor starts again.</summary>
    private void StartFresh(StatsSources started)
    {
        if (started.HasFlag(StatsSources.Cpu))
        {
            _cpuMonitor = new SystemMonitor();
        }

        if (started.HasFlag(StatsSources.Network))
        {
            _networkMonitor = new SystemMonitor();
        }
    }

    private StatsReading Read(StatsSources sources)
    {
        IReadOnlyList<AiSession> sessions = [];
        if (sources.HasFlag(StatsSources.AiTools))
        {
            sessions = AiToolCatalog.FindSessions(ProcessActions.Sample());
            AiActivityTracker.Observe(sessions);
        }

        var tools = sessions
            .GroupBy(session => session.Tool.Name)
            .Select(group => new AiToolUsage(group.Key, group.Count(), group.Sum(session => session.TotalMemoryMb), group.First().Root.ExecutablePath))
            .OrderByDescending(tool => tool.MemoryMb)
            .ToList();
        var (down, up) = sources.HasFlag(StatsSources.Network) ? _networkMonitor.SampleNetwork() : (0, 0);
        return new StatsReading(
            sources.HasFlag(StatsSources.Cpu) ? _cpuMonitor.SampleCpuPercent() : 0,
            sources.HasFlag(StatsSources.Memory) ? SystemMonitor.SampleMemory() : StatsReading.Empty.Memory,
            tools, down, up, sessions);
    }
}
