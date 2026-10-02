using System.Text;
using System.Text.Json;

namespace WinModes.Core.Usage.Tokens;

/// <summary>Where a scan is: how many log files were looked at out of those that changed.</summary>
public readonly record struct TokenScanProgress(int FilesDone, int FilesTotal);

/// <summary>
/// The token figures found in the logs of Claude Code and Codex over the last days. Logs are large (gigabytes) and only grow, so
/// each file is read once and then only from where the last read stopped; what is kept is a few numbers per request, saved in a
/// small file. Nothing from the conversations is kept.
/// </summary>
public sealed class TokenIndex
{
    /// <summary>Days of figures kept; the longest period shown is 30.</summary>
    public const int RetainDays = 31;

    private const int FormatVersion = 1;
    private const int BufferSize = 1 << 20;
    private const int MaxLineBytes = 32 << 20;

    private static readonly string[] CodexFolders = ["sessions", "archived_sessions"];

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinModes", "token-index.json");

    public static string DefaultClaudeFolder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    private readonly Lock _gate = new();
    private readonly Dictionary<string, FileMark> _claudeFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CodexFile> _codexFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, Row> _claudeRows = [];

    /// <summary>Where the read of a file stopped and how it looked then.</summary>
    private sealed record FileMark(long Offset, long Length, long Stamp);

    private sealed record Row(int Day, string Model, string Project, TokenCounts Counts);

    private readonly record struct BucketKey(int Day, string Model, string Project);

    private sealed class CodexFile(FileMark mark, CodexTokenLog.State state, Dictionary<BucketKey, TokenCounts> buckets)
    {
        public FileMark Mark { get; set; } = mark;

        public CodexTokenLog.State State { get; set; } = state;

        public Dictionary<BucketKey, TokenCounts> Buckets { get; } = buckets;
    }

    /// <summary>Nothing has been read yet, or the saved figures were lost: the first scan reads the logs of the last days.</summary>
    public bool IsEmpty
    {
        get
        {
            lock (_gate)
            {
                return _claudeFiles.Count == 0 && _codexFiles.Count == 0;
            }
        }
    }

    /// <summary>
    /// Reads what the logs gained since the last scan. Files not written in the last <see cref="RetainDays"/> days are ignored.
    /// Returns the number of files read. Run it on a worker thread: the first scan reads gigabytes.
    /// </summary>
    public int Update(string claudeFolder, string codexHome, DateTimeOffset now, IProgress<TokenScanProgress>? progress = null, CancellationToken cancellation = default)
    {
        var cutoff = now.AddDays(-RetainDays).UtcDateTime;
        var claude = Recent(claudeFolder, cutoff);
        var codex = CodexFolders.SelectMany(folder => Recent(Path.Combine(codexHome, folder), cutoff)).ToList();

        var work = new List<(FileInfo File, bool IsClaude)>();
        lock (_gate)
        {
            work.AddRange(claude.Where(file => NeedsReading(_claudeFiles.GetValueOrDefault(file.FullName), file)).Select(file => (file, true)));
            work.AddRange(codex.Where(file => NeedsReading(_codexFiles.GetValueOrDefault(file.FullName)?.Mark, file)).Select(file => (file, false)));
        }

        var done = 0;
        progress?.Report(new TokenScanProgress(0, work.Count));
        foreach (var (file, isClaude) in work)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                if (isClaude)
                {
                    ReadClaude(file, cancellation);
                }
                else
                {
                    ReadCodex(file, cancellation);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A file in use or removed meanwhile: the next scan tries again.
            }

            progress?.Report(new TokenScanProgress(++done, work.Count));
        }

        Prune(claude.Select(file => file.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase), codex.Select(file => file.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase), DateOnly.FromDateTime(now.LocalDateTime));
        return work.Count;
    }

    /// <summary>The tokens each tool used over the <paramref name="days"/> days ending <paramref name="today"/>, most used first.</summary>
    public IReadOnlyList<TokenSummary> Summarize(DateOnly today, int days)
    {
        var first = today.DayNumber - Math.Max(days, 1) + 1;
        var perTool = new Dictionary<string, List<(int Day, string Model, string Project, TokenCounts Counts)>>(StringComparer.Ordinal);
        lock (_gate)
        {
            foreach (var row in _claudeRows.Values)
            {
                Add(perTool, ClaudeTokenLog.Tool, row.Day, row.Model, row.Project, row.Counts);
            }

            foreach (var file in _codexFiles.Values)
            {
                foreach (var (key, counts) in file.Buckets)
                {
                    Add(perTool, CodexTokenLog.Tool, key.Day, key.Model, key.Project, counts);
                }
            }
        }

        return [.. perTool
            .Select(pair => Summary(pair.Key, pair.Value.Where(item => item.Day >= first && item.Day <= today.DayNumber).ToList(), first, today.DayNumber))
            .Where(summary => !summary.Total.IsEmpty)
            .OrderByDescending(summary => summary.Total.Total)];
    }

    /// <summary>Forgets everything, so the next scan reads the logs again from the start.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _claudeFiles.Clear();
            _codexFiles.Clear();
            _claudeRows.Clear();
        }
    }

    private static void Add(Dictionary<string, List<(int, string, string, TokenCounts)>> perTool, string tool, int day, string model, string project, TokenCounts counts)
    {
        if (!perTool.TryGetValue(tool, out var list))
        {
            perTool[tool] = list = [];
        }

        list.Add((day, model, project, counts));
    }

    private static TokenSummary Summary(string tool, List<(int Day, string Model, string Project, TokenCounts Counts)> items, int firstDay, int lastDay)
    {
        TokenCounts Sum(IEnumerable<TokenCounts> counts) => counts.Aggregate(TokenCounts.Empty, (all, each) => all + each);

        List<TokenGroup> Groups(Func<(int Day, string Model, string Project, TokenCounts Counts), string> by) => [.. items
            .GroupBy(by, StringComparer.Ordinal)
            .Select(group => new TokenGroup(group.Key, Sum(group.Select(item => item.Counts))))
            .OrderByDescending(group => group.Counts.Total)];

        var perDay = items.GroupBy(item => item.Day).ToDictionary(group => group.Key, group => Sum(group.Select(item => item.Counts)));
        var days = Enumerable.Range(firstDay, lastDay - firstDay + 1)
            .Select(day => new TokenDay(DateOnly.FromDayNumber(day), perDay.GetValueOrDefault(day)))
            .ToList();
        return new TokenSummary(tool, Sum(items.Select(item => item.Counts)), Groups(item => item.Model), Groups(item => item.Project), days);
    }

    private static List<FileInfo> Recent(string folder, DateTime cutoffUtc)
    {
        try
        {
            return Directory.Exists(folder)
                ? [.. new DirectoryInfo(folder).EnumerateFiles("*.jsonl", SearchOption.AllDirectories).Where(file => file.LastWriteTimeUtc >= cutoffUtc)]
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool NeedsReading(FileMark? mark, FileInfo file) => mark is null || mark.Length != file.Length || mark.Stamp != file.LastWriteTimeUtc.Ticks;

    private void ReadClaude(FileInfo file, CancellationToken cancellation)
    {
        FileMark? mark;
        lock (_gate)
        {
            mark = _claudeFiles.GetValueOrDefault(file.FullName);
        }

        // A file that shrank was rewritten: read it from the start; requests already known are recognised by their key.
        var start = mark is not null && mark.Offset <= file.Length ? mark.Offset : 0;
        var events = new List<TokenEvent>();
        var end = ReadLines(file.FullName, start, ClaudeTokenLog.LinePatterns, line =>
        {
            if (ClaudeTokenLog.Parse(line) is { } found)
            {
                events.Add(found);
            }
        }, cancellation);

        lock (_gate)
        {
            foreach (var found in events)
            {
                var row = new Row(DayOf(found.At), found.Model, found.Project, found.Counts);
                // The same request again keeps the larger figures: its output grows while it streams.
                _claudeRows[found.Key] = _claudeRows.TryGetValue(found.Key, out var known) && found.Key != 0 ? row with { Counts = known.Counts.Max(row.Counts) } : row;
            }

            _claudeFiles[file.FullName] = new FileMark(end, file.Length, file.LastWriteTimeUtc.Ticks);
        }
    }

    private void ReadCodex(FileInfo file, CancellationToken cancellation)
    {
        CodexFile? known;
        lock (_gate)
        {
            known = _codexFiles.GetValueOrDefault(file.FullName);
        }

        var fresh = known is null || known.Mark.Offset > file.Length;
        var reader = new CodexTokenLog(fresh ? null : known!.State);
        var buckets = fresh ? [] : new Dictionary<BucketKey, TokenCounts>(known!.Buckets);
        var end = ReadLines(file.FullName, fresh ? 0 : known!.Mark.Offset, CodexTokenLog.LinePatterns, line =>
        {
            if (reader.Feed(line) is { } found)
            {
                var key = new BucketKey(DayOf(found.At), found.Model, found.Project);
                buckets[key] = buckets.GetValueOrDefault(key) + found.Counts;
            }
        }, cancellation);

        lock (_gate)
        {
            var entry = new CodexFile(new FileMark(end, file.Length, file.LastWriteTimeUtc.Ticks), reader.Current, buckets);
            _codexFiles[file.FullName] = entry;
        }
    }

    /// <summary>
    /// Passes the complete lines of a file, from a byte offset, to <paramref name="handle"/> when they hold one of the
    /// <paramref name="patterns"/> (checked on the bytes, before any decoding). Returns the offset after the last complete line:
    /// a line still being written is read again next time.
    /// </summary>
    private static long ReadLines(string path, long start, byte[][] patterns, Action<string> handle, CancellationToken cancellation)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, BufferSize, FileOptions.SequentialScan);
        stream.Seek(start, SeekOrigin.Begin);

        var chunk = new byte[BufferSize];
        var pending = new List<byte>();
        var skipping = false;
        var consumed = start;
        var position = start;

        bool Wanted(ReadOnlySpan<byte> line)
        {
            foreach (var pattern in patterns)
            {
                if (line.IndexOf(pattern) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        void Finish(ReadOnlySpan<byte> line)
        {
            if (!skipping && Wanted(line))
            {
                handle(Encoding.UTF8.GetString(line));
            }

            pending.Clear();
            skipping = false;
        }

        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            var offset = 0;
            while (offset < read)
            {
                var newline = chunk.AsSpan(offset, read - offset).IndexOf((byte)'\n');
                var segment = newline < 0 ? chunk.AsSpan(offset, read - offset) : chunk.AsSpan(offset, newline);
                if (newline < 0)
                {
                    if (!skipping)
                    {
                        if (pending.Count + segment.Length > MaxLineBytes)
                        {
                            skipping = true;
                            pending.Clear();
                        }
                        else
                        {
                            pending.AddRange(segment);
                        }
                    }

                    break;
                }

                if (pending.Count == 0)
                {
                    Finish(segment);
                }
                else
                {
                    pending.AddRange(segment);
                    Finish(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(pending));
                }

                offset += newline + 1;
                consumed = position + offset;
            }

            position += read;
        }

        return consumed;
    }

    private static int DayOf(DateTimeOffset moment) => DateOnly.FromDateTime(moment.LocalDateTime).DayNumber;

    private void Prune(HashSet<string> claudeFiles, HashSet<string> codexFiles, DateOnly today)
    {
        var oldest = today.DayNumber - RetainDays + 1;
        lock (_gate)
        {
            foreach (var path in _claudeFiles.Keys.Where(path => !claudeFiles.Contains(path)).ToList())
            {
                _claudeFiles.Remove(path);
            }

            foreach (var path in _codexFiles.Keys.Where(path => !codexFiles.Contains(path)).ToList())
            {
                _codexFiles.Remove(path);
            }

            foreach (var key in _claudeRows.Where(pair => pair.Value.Day < oldest).Select(pair => pair.Key).ToList())
            {
                _claudeRows.Remove(key);
            }

            foreach (var file in _codexFiles.Values)
            {
                foreach (var key in file.Buckets.Keys.Where(key => key.Day < oldest).ToList())
                {
                    file.Buckets.Remove(key);
                }
            }
        }
    }

    // ---- Saving: names are listed once and the rows hold numbers, so a month of figures stays a few megabytes. ----

    private sealed class Saved
    {
        public int Version { get; set; }

        public List<string> Names { get; set; } = [];

        public List<long[]> Claude { get; set; } = [];

        public Dictionary<string, long[]> ClaudeFiles { get; set; } = [];

        public Dictionary<string, SavedCodex> CodexFiles { get; set; } = [];
    }

    private sealed class SavedCodex
    {
        public long[] Mark { get; set; } = [];

        public int Project { get; set; }

        public int Model { get; set; }

        public long[] Total { get; set; } = [];

        public List<long[]> Buckets { get; set; } = [];
    }

    /// <summary>The saved index; an empty one when the file is missing, damaged or from another format, since a scan rebuilds it.</summary>
    public static TokenIndex Load(string path)
    {
        var index = new TokenIndex();
        try
        {
            if (!File.Exists(path) || JsonSerializer.Deserialize<Saved>(File.ReadAllText(path), Options) is not { Version: FormatVersion } saved)
            {
                return index;
            }

            string Name(long number) => saved.Names[(int)number];
            TokenCounts Counts(long[] numbers, int at) => new(numbers[at], numbers[at + 1], numbers[at + 2], numbers[at + 3]);
            FileMark Mark(long[] numbers) => new(numbers[0], numbers[1], numbers[2]);

            foreach (var row in saved.Claude)
            {
                index._claudeRows[row[0]] = new Row((int)row[1], Name(row[2]), Name(row[3]), Counts(row, 4));
            }

            foreach (var (file, mark) in saved.ClaudeFiles)
            {
                index._claudeFiles[file] = Mark(mark);
            }

            foreach (var (file, codex) in saved.CodexFiles)
            {
                var buckets = codex.Buckets.ToDictionary(bucket => new BucketKey((int)bucket[0], Name(bucket[1]), Name(bucket[2])), bucket => Counts(bucket, 3));
                index._codexFiles[file] = new CodexFile(Mark(codex.Mark), new CodexTokenLog.State(Name(codex.Project), Name(codex.Model), Counts(codex.Total, 0)), buckets);
            }

            return index;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            return new TokenIndex();
        }
    }

    /// <summary>Writes the index; a failure is ignored, the next scan only has more to read.</summary>
    public void Save(string path)
    {
        Saved saved;
        lock (_gate)
        {
            var names = new Dictionary<string, int>(StringComparer.Ordinal);
            long Name(string text)
            {
                if (!names.TryGetValue(text, out var number))
                {
                    names[text] = number = names.Count;
                }

                return number;
            }

            long[] CountsOf(TokenCounts counts) => [counts.Input, counts.Output, counts.CacheRead, counts.CacheWrite];
            long[] MarkOf(FileMark mark) => [mark.Offset, mark.Length, mark.Stamp];

            saved = new Saved { Version = FormatVersion };
            foreach (var (key, row) in _claudeRows)
            {
                saved.Claude.Add([key, row.Day, Name(row.Model), Name(row.Project), .. CountsOf(row.Counts)]);
            }

            foreach (var (file, mark) in _claudeFiles)
            {
                saved.ClaudeFiles[file] = MarkOf(mark);
            }

            foreach (var (file, codex) in _codexFiles)
            {
                saved.CodexFiles[file] = new SavedCodex
                {
                    Mark = MarkOf(codex.Mark),
                    Project = (int)Name(codex.State.Project),
                    Model = (int)Name(codex.State.Model),
                    Total = CountsOf(codex.State.Total),
                    Buckets = [.. codex.Buckets.Select(pair => (long[])[pair.Key.Day, Name(pair.Key.Model), Name(pair.Key.Project), .. CountsOf(pair.Value)])],
                };
            }

            saved.Names = [.. names.OrderBy(pair => pair.Value).Select(pair => pair.Key)];
        }

        try
        {
            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(saved, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing to do: the figures are rebuilt from the logs.
        }
    }
}
