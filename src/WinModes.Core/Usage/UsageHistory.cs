using System.Globalization;
using System.Text.Json;

namespace WinModes.Core.Usage;

/// <summary>What one tool used in one project folder over a period.</summary>
public sealed record UsageSummary(string Tool, string Project, TimeSpan Duration, double AverageMemoryMb, double PeakMemoryMb)
{
    /// <summary>Memory held over time, the figure used to rank projects.</summary>
    public double GbHours => AverageMemoryMb / 1024 * Duration.TotalHours;
}

/// <summary>One sample of a running session group.</summary>
public sealed record UsageSample(string Tool, string Project, double MemoryMb);

/// <summary>
/// Opt-in record of how much memory each AI tool used per project. One small JSON file per day,
/// kept on this PC only; files older than the retention period are deleted.
/// </summary>
public sealed class UsageHistory(string directory)
{
    public const int RetentionDays = 30;
    private const string FilePattern = "yyyy-MM-dd";
    // A longer gap means the PC slept or sampling was off: that time is not counted.
    private static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(30);

    private readonly Dictionary<string, Entry> _today = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();
    private DateOnly _day;
    private DateTime? _lastSample;

    /// <summary>Adds the sessions seen now. Samples of the same tool and project are summed.</summary>
    public void Record(DateTime now, IEnumerable<UsageSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        lock (_gate)
        {
            var day = DateOnly.FromDateTime(now);
            if (day != _day)
            {
                // A new day (or the first sample): write the previous one and continue from the file of the new day.
                FlushLocked();
                _day = day;
                _today.Clear();
                foreach (var entry in ReadDay(day))
                {
                    _today[Key(entry.Tool, entry.Project)] = entry;
                }
            }

            var elapsed = _lastSample is { } last ? now - last : TimeSpan.Zero;
            _lastSample = now;
            if (elapsed <= TimeSpan.Zero || elapsed > MaxGap)
            {
                return;
            }

            foreach (var group in samples.GroupBy(sample => Key(sample.Tool, sample.Project), StringComparer.OrdinalIgnoreCase))
            {
                var first = group.First();
                var memory = group.Sum(sample => sample.MemoryMb);
                var entry = _today.GetValueOrDefault(group.Key) ?? new Entry { Tool = first.Tool, Project = first.Project };
                entry.Seconds += elapsed.TotalSeconds;
                entry.MbSeconds += memory * elapsed.TotalSeconds;
                entry.PeakMb = Math.Max(entry.PeakMb, memory);
                _today[group.Key] = entry;
            }
        }
    }

    /// <summary>Writes today's figures to disk and removes files past the retention period.</summary>
    public void Flush()
    {
        lock (_gate)
        {
            FlushLocked();
        }
    }

    /// <summary>Totals per tool and project over the last <paramref name="days"/> days, today included.</summary>
    public IReadOnlyList<UsageSummary> Summarize(DateOnly today, int days)
    {
        lock (_gate)
        {
            FlushLocked();
        }

        return [.. Enumerable.Range(0, Math.Max(days, 1))
            .SelectMany(offset => ReadDay(today.AddDays(-offset)))
            .GroupBy(entry => Key(entry.Tool, entry.Project), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var seconds = group.Sum(entry => entry.Seconds);
                return new UsageSummary(
                    group.First().Tool,
                    group.First().Project,
                    TimeSpan.FromSeconds(seconds),
                    seconds <= 0 ? 0 : group.Sum(entry => entry.MbSeconds) / seconds,
                    group.Max(entry => entry.PeakMb));
            })
            .OrderByDescending(summary => summary.GbHours)];
    }

    /// <summary>Deletes every recorded day.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _today.Clear();
            _lastSample = null;
            if (Directory.Exists(directory))
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
                {
                    File.Delete(file);
                }
            }
        }
    }

    private static string Key(string tool, string project) => $"{tool}|{project}";

    private string PathOf(DateOnly day) => Path.Combine(directory, day.ToString(FilePattern, CultureInfo.InvariantCulture) + ".json");

    private List<Entry> ReadDay(DateOnly day)
    {
        try
        {
            var path = PathOf(day);
            return File.Exists(path) ? JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path)) ?? [] : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void FlushLocked()
    {
        if (_day == default || _today.Count == 0)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(PathOf(_day), JsonSerializer.Serialize(_today.Values.ToList()));

            var oldest = _day.AddDays(-RetentionDays);
            foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
            {
                if (DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(file), FilePattern, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                    && day < oldest)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // History is a convenience: a failed write is retried at the next flush.
        }
    }

    private sealed class Entry
    {
        public string Tool { get; set; } = "";
        public string Project { get; set; } = "";
        public double Seconds { get; set; }
        public double MbSeconds { get; set; }
        public double PeakMb { get; set; }
    }
}
