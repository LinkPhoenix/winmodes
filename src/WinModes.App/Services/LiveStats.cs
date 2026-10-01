using System.ComponentModel;
using System.Windows.Threading;
using WinModes.Core.Planning;

namespace WinModes.App.Services;

/// <summary>One reading shared by the tray meter and the desktop widget.</summary>
internal sealed record StatsReading(double CpuPercent, MemorySample Memory, IReadOnlyList<AiToolUsage> AiTools, double DownMbps, double UpMbps)
{
    public double AiMemoryMb => AiTools.Sum(tool => tool.MemoryMb);
}

internal sealed record AiToolUsage(string Name, int Sessions, double MemoryMb);

/// <summary>
/// Background sampling for features that live outside the main window. It runs only while
/// something listens, so it costs nothing when the tray meter and the widget are both off.
/// </summary>
internal sealed class LiveStats
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    private readonly DispatcherTimer _timer = new() { Interval = Interval };
    private readonly SystemMonitor _monitor = new();
    private bool _sampling;
    private EventHandler<StatsReading>? _updated;

    /// <summary>Time between two samples.</summary>
    public TimeSpan RefreshInterval
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
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
            var reading = await Task.Run(() =>
            {
                var sessions = AiToolCatalog.FindSessions(ProcessActions.Sample());
                AiActivityTracker.Observe(sessions);
                var tools = sessions
                    .GroupBy(session => session.Tool.Name)
                    .Select(group => new AiToolUsage(group.Key, group.Count(), group.Sum(session => session.TotalMemoryMb)))
                    .OrderByDescending(tool => tool.MemoryMb)
                    .ToList();
                var (down, up) = _monitor.SampleNetwork();
                return new StatsReading(_monitor.SampleCpuPercent(), SystemMonitor.SampleMemory(), tools, down, up);
            });
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
}
