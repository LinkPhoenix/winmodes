using System.Runtime.InteropServices;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinModes.Core.Tuning;

/// <summary>A registry value as it is now. <see cref="Kind"/> is null when the value has a type tweaks do not handle.</summary>
public sealed record RegistrySnapshot(bool Exists, TweakValueKind? Kind, string? Value)
{
    public static RegistrySnapshot Missing { get; } = new(false, null, null);

    public bool Matches(TweakValueKind kind, string value) => Exists && Kind == kind && string.Equals(Value, value, StringComparison.Ordinal);
}

/// <summary>Registry access, abstracted so the engine can be tested without touching the system.</summary>
public interface IRegistryAccess
{
    RegistrySnapshot Read(TweakHive hive, string path, string name);
    void Write(TweakHive hive, string path, string name, TweakValueKind kind, string value);
    void Delete(TweakHive hive, string path, string name);
}

/// <summary>Scheduled tasks, abstracted for the same reason.</summary>
public interface ITaskControl
{
    /// <summary>Null when the task does not exist on this PC.</summary>
    bool? IsEnabled(string taskPath);
    void SetEnabled(string taskPath, bool enabled);
}

public enum TweakState { Unavailable, NotApplied, Partial, Applied }

/// <summary>What one registry value held before the tweak, and what the tweak wrote.</summary>
public sealed class TweakValueRecord
{
    public TweakHive Hive { get; init; }
    public required string Path { get; init; }
    public required string Name { get; init; }
    public bool Existed { get; init; }
    public TweakValueKind? PreviousKind { get; init; }
    public string? Previous { get; init; }
    public TweakValueKind WrittenKind { get; init; }
    public required string Written { get; init; }
}

/// <summary>Everything one applied tweak changed, so it can be undone exactly.</summary>
public sealed class TweakRecord
{
    public required string Id { get; init; }
    public DateTimeOffset AppliedUtc { get; set; }
    public List<TweakValueRecord> Values { get; init; } = [];

    /// <summary>Tasks that were enabled and that the tweak disabled.</summary>
    public List<string> DisabledTasks { get; init; } = [];
}

/// <summary>One JSON file of applied tweaks, saved atomically before each change.</summary>
public sealed class TweakJournal(string filePath)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public IReadOnlyList<TweakRecord> Load()
    {
        try
        {
            return File.Exists(filePath) ? JsonSerializer.Deserialize<List<TweakRecord>>(File.ReadAllText(filePath), Options) ?? [] : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Save(IEnumerable<TweakRecord> records)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var temporary = filePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(records.ToList(), Options));
        File.Move(temporary, filePath, overwrite: true);
    }
}

/// <summary>
/// Applies and undoes tweaks for one scope: the user part (HKCU) runs in the app, the machine part
/// (HKLM and scheduled tasks) in the elevated helper. The live value is journaled before each write,
/// and an undo only restores a value that still holds what the tweak wrote.
/// </summary>
public sealed class TweakEngine(IRegistryAccess registry, ITaskControl tasks, TweakJournal journal, bool machineScope)
{
    /// <summary>Compares the whole tweak, both scopes, with the live system. Read-only.</summary>
    public TweakState GetState(Tweak tweak)
    {
        ArgumentNullException.ThrowIfNull(tweak);
        var applied = tweak.Values.Count(value => registry.Read(value.Hive, value.Path, value.Name).Matches(value.Kind, value.Value));
        var total = tweak.Values.Count;

        foreach (var enabled in tweak.Tasks.Select(tasks.IsEnabled))
        {
            if (enabled is null)
            {
                continue;
            }

            total++;
            applied += enabled.Value ? 0 : 1;
        }

        return total == 0 ? TweakState.Unavailable
            : applied == total ? TweakState.Applied
            : applied == 0 ? TweakState.NotApplied
            : TweakState.Partial;
    }

    /// <summary>Ids this scope can undo.</summary>
    public IReadOnlyList<string> JournaledIds() => [.. journal.Load().Select(record => record.Id)];

    public TuneResult Apply(Tweak tweak)
    {
        ArgumentNullException.ThrowIfNull(tweak);
        TuneResult Result(TuneOutcome outcome, string? detail) => new(tweak.Id, TuneAction.Tweak, outcome, detail);

        // Fail closed: checked again here, whatever loaded the catalog.
        if (TweakGuard.Validate(tweak) is { Count: > 0 } violations)
        {
            return Result(TuneOutcome.Skipped, violations[0]);
        }

        var values = tweak.Values.Where(InScope).ToList();
        var taskPaths = machineScope ? tweak.Tasks : [];
        try
        {
            var before = values.Select(value => (Value: value, Live: registry.Read(value.Hive, value.Path, value.Name))).ToList();
            if (before.FirstOrDefault(pair => pair.Live is { Exists: true, Kind: null }) is { Value: not null } unknown)
            {
                return Result(TuneOutcome.Skipped, $"{unknown.Value.Name} holds a value of an unexpected type; left as is.");
            }

            var records = journal.Load().ToList();
            var record = records.FirstOrDefault(existing => existing.Id.Equals(tweak.Id, StringComparison.OrdinalIgnoreCase));
            if (record is null)
            {
                records.Add(record = new TweakRecord { Id = tweak.Id });
            }

            record.AppliedUtc = DateTimeOffset.UtcNow;
            var changes = 0;

            foreach (var (value, live) in before.Where(pair => !pair.Live.Matches(pair.Value.Kind, pair.Value.Value)))
            {
                // Keep the very first "before": applying twice must not overwrite it with our own value.
                if (!record.Values.Any(existing => IsSameValue(existing, value)))
                {
                    record.Values.Add(new TweakValueRecord
                    {
                        Hive = value.Hive,
                        Path = value.Path,
                        Name = value.Name,
                        Existed = live.Exists,
                        PreviousKind = live.Kind,
                        Previous = live.Value,
                        WrittenKind = value.Kind,
                        Written = value.Value,
                    });
                }

                journal.Save(records);
                registry.Write(value.Hive, value.Path, value.Name, value.Kind, value.Value);
                changes++;
            }

            foreach (var task in taskPaths.Where(task => tasks.IsEnabled(task) == true))
            {
                if (!record.DisabledTasks.Contains(task, StringComparer.OrdinalIgnoreCase))
                {
                    record.DisabledTasks.Add(task);
                }

                journal.Save(records);
                tasks.SetEnabled(task, enabled: false);
                changes++;
            }

            if (record.Values.Count == 0 && record.DisabledTasks.Count == 0)
            {
                records.Remove(record);
                journal.Save(records);
            }

            return changes == 0 ? Result(TuneOutcome.Skipped, "Already applied.") : Result(TuneOutcome.Done, null);
        }
        catch (Exception ex) when (IsAccessFailure(ex))
        {
            return Result(TuneOutcome.Failed, ex.Message);
        }
    }

    public TuneResult Undo(string tweakId)
    {
        TuneResult Result(TuneOutcome outcome, string? detail) => new(tweakId, TuneAction.Untweak, outcome, detail);

        var records = journal.Load().ToList();
        var record = records.FirstOrDefault(existing => existing.Id.Equals(tweakId, StringComparison.OrdinalIgnoreCase));
        if (record is null)
        {
            return Result(TuneOutcome.Skipped, "Nothing to undo.");
        }

        try
        {
            var leftAlone = 0;
            foreach (var value in Enumerable.Reverse(record.Values))
            {
                // Someone else changed the value since: their choice wins.
                if (!registry.Read(value.Hive, value.Path, value.Name).Matches(value.WrittenKind, value.Written))
                {
                    leftAlone++;
                }
                else if (value is { Existed: true, PreviousKind: { } kind, Previous: { } previous })
                {
                    registry.Write(value.Hive, value.Path, value.Name, kind, previous);
                }
                else
                {
                    registry.Delete(value.Hive, value.Path, value.Name);
                }
            }

            foreach (var task in record.DisabledTasks.Where(task => tasks.IsEnabled(task) == false))
            {
                tasks.SetEnabled(task, enabled: true);
            }

            records.Remove(record);
            journal.Save(records);
            return Result(TuneOutcome.Done, leftAlone == 0 ? null : $"{leftAlone} values were changed by something else since and were left as they are.");
        }
        catch (Exception ex) when (IsAccessFailure(ex))
        {
            return Result(TuneOutcome.Failed, ex.Message);
        }
    }

    private bool InScope(TweakValue value) => (value.Hive == TweakHive.Machine) == machineScope;

    private static bool IsSameValue(TweakValueRecord record, TweakValue value) =>
        record.Hive == value.Hive
        && record.Path.Equals(value.Path, StringComparison.OrdinalIgnoreCase)
        && record.Name.Equals(value.Name, StringComparison.OrdinalIgnoreCase);

    private static bool IsAccessFailure(Exception ex) =>
        ex is UnauthorizedAccessException or SecurityException or IOException or COMException or InvalidOperationException;
}
