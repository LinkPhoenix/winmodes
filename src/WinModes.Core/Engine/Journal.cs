using System.Text.Json;
using System.Text.Json.Serialization;
using WinModes.Core.Planning;

namespace WinModes.Core.Engine;

public enum EntryKind { StopService, StartService }

public enum EntryOutcome { Pending, Done, Skipped, Failed, Reverted, RevertSkipped }

/// <summary>One service change, with the live state recorded before it was touched.</summary>
public sealed class JournalEntry
{
    public required string Target { get; init; }
    public required EntryKind Kind { get; init; }
    public ServiceStartMode BeforeStartMode { get; init; }
    public bool BeforeDelayedAutoStart { get; init; }
    public bool BeforeRunning { get; init; }
    public EntryOutcome Outcome { get; set; }
    public string? Detail { get; set; }

    /// <summary>
    /// A stop that is still Pending may already have changed the start type (a crash between the write and the
    /// save), so it must be revertible; the revert only touches a service whose start type is still Manual.
    /// </summary>
    [JsonIgnore]
    public bool NeedsRevert => Outcome == EntryOutcome.Done || (Outcome == EntryOutcome.Pending && Kind == EntryKind.StopService);
}

/// <summary>One mode switch: what was changed and whether it has been undone.</summary>
public sealed class JournalSession
{
    public required string Id { get; init; }
    public required string Mode { get; init; }
    public DateTimeOffset StartedUtc { get; init; }
    public bool Completed { get; set; }
    public bool Reverted { get; set; }
    public List<JournalEntry> Entries { get; init; } = [];

    [JsonIgnore]
    public int DoneCount => Entries.Count(entry => entry.Outcome == EntryOutcome.Done);

    [JsonIgnore]
    public int RevertibleCount => Entries.Count(entry => entry.NeedsRevert);
}

/// <summary>
/// Stores sessions as one JSON file each. Every save is atomic (temporary file, then replace),
/// and the engine saves the "before" state before acting, so a crash never loses what must be restored.
/// </summary>
public sealed class JournalStore(string directory)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Directory { get; } = directory;

    public void Save(JournalSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        AtomicFile.WriteAllText(PathOf(session.Id), JsonSerializer.Serialize(session, Options));
    }

    public IReadOnlyList<JournalSession> LoadAll()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        var sessions = new List<JournalSession>();
        foreach (var file in System.IO.Directory.GetFiles(Directory, "*.json"))
        {
            // A damaged file is quarantined, so it cannot hide the other sessions or be overwritten.
            if (StateFile.Read<JournalSession>(file, Options) is { } session)
            {
                sessions.Add(session);
            }
        }

        return [.. sessions.OrderByDescending(session => session.StartedUtc)];
    }

    /// <summary>The most recent session that changed something and has not been undone.</summary>
    public JournalSession? FindActive() =>
        LoadAll().FirstOrDefault(session => !session.Reverted && session.RevertibleCount > 0);

    private string PathOf(string id) => Path.Combine(Directory, id + ".json");
}
