using System.Runtime.InteropServices;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinModes.Core.Engine;

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
    public bool WrittenAbsent { get; init; }
}

/// <summary>Everything one applied tweak changed, so it can be undone exactly.</summary>
public sealed class TweakRecord
{
    public required string Id { get; init; }
    public DateTimeOffset AppliedUtc { get; set; }
    public List<TweakValueRecord> Values { get; init; } = [];

    /// <summary>Tasks that were enabled and that the tweak disabled.</summary>
    public List<string> DisabledTasks { get; init; } = [];

    /// <summary>The parts of <paramref name="tweak"/> this record changed, so a single part can be undone later.</summary>
    public IReadOnlySet<int> PartsOf(Tweak tweak)
    {
        ArgumentNullException.ThrowIfNull(tweak);
        return Values.Select(tweak.PartOf).Concat(DisabledTasks.Select(tweak.PartOfTask)).OfType<int>().ToHashSet();
    }
}

/// <summary>One JSON file of applied tweaks, saved atomically before each change.</summary>
public sealed class TweakJournal(string filePath)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public IReadOnlyList<TweakRecord> Load() => StateFile.Read<List<TweakRecord>>(filePath, Options) ?? [];

    public void Save(IEnumerable<TweakRecord> records)
    {
        AtomicFile.WriteAllText(filePath, JsonSerializer.Serialize(records.ToList(), Options));
    }
}

/// <summary>
/// Applies and undoes tweaks for one scope: the user part (HKCU) runs in the app, the machine part
/// (HKLM and scheduled tasks) in the elevated helper. The live value is journaled before each write,
/// and an undo only restores a value that still holds what the tweak wrote.
/// </summary>
public sealed class TweakEngine(IRegistryAccess registry, ITaskControl tasks, TweakJournal journal, bool machineScope)
{
    /// <summary>Reads technical values without changing the system. Errors remain distinct from absent values.</summary>
    public IReadOnlyList<TweakPartObservation> ObserveParts(Tweak tweak) => ObserveParts(tweak, registry, tasks);

    public static IReadOnlyList<TweakPartObservation> ObserveParts(Tweak tweak, IRegistryAccess registry, ITaskControl tasks)
    {
        ArgumentNullException.ThrowIfNull(tweak);
        var observations = new List<TweakPartObservation>();
        foreach (var part in tweak.Parts)
        {
            try
            {
                if (part.Kind == TweakPartKind.Task)
                {
                    observations.Add(tasks is ITaskObservation richer
                        ? richer.Observe(part.Target, part.Index)
                        : tasks.IsEnabled(part.Target) is { } enabled
                            ? new(part.Index, !enabled, enabled ? "Enabled" : "Disabled", "Disabled", true)
                            : new(part.Index, null, "Unavailable or unreadable", "Disabled", false));
                    continue;
                }

                var value = tweak.Values[part.Index];
                var live = registry.Read(value.Hive, value.Path, value.Name);
                var desired = $"{value.Value} ({(value.Kind == TweakValueKind.Number ? "REG_DWORD" : "REG_SZ")})";
                var actual = !live.Exists ? "Value absent" : live.Kind is null ? "Unsupported registry type"
                    : $"{live.Value} ({(live.Kind == TweakValueKind.Number ? "REG_DWORD" : "REG_SZ")})";
                observations.Add(new(part.Index, live.Exists && live.Kind is null ? null : live.Matches(value.Kind, value.Value),
                    actual, desired, true, live.Exists && live.Kind is null ? "Unsupported registry type; left unchanged." : null, live.Exists));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException or InvalidOperationException or COMException)
            {
                observations.Add(new(part.Index, null, "Unreadable", part.Setting ?? "Disabled", true, ex.Message));
            }
        }

        return observations;
    }

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

    /// <summary>
    /// For each part of the tweak (in the order of <see cref="Tweak.Parts"/>): true when it is applied now, false when not, null for a
    /// task this PC does not have. Read-only.
    /// </summary>
    public IReadOnlyList<bool?> GetPartStates(Tweak tweak)
    {
        ArgumentNullException.ThrowIfNull(tweak);
        return
        [
            .. tweak.Values.Select(value => (bool?)registry.Read(value.Hive, value.Path, value.Name).Matches(value.Kind, value.Value)),
            .. tweak.Tasks.Select(task => tasks.IsEnabled(task) is { } enabled ? !enabled : (bool?)null),
        ];
    }

    /// <summary>Ids this scope can undo.</summary>
    public IReadOnlyList<string> JournaledIds() => [.. journal.Load().Select(record => record.Id)];

    /// <summary>The existing records for review of an undo. Reading them does not claim restoration will succeed.</summary>
    public IReadOnlyList<TweakRecord> JournalRecords() => journal.Load();

    /// <summary>Remove an explicitly selected supported policy, recording the live value before deletion.</summary>
    public TuneResult ReleasePolicy(Tweak tweak, IReadOnlySet<int>? parts, WinModes.Core.Planning.ISystemProbe probe)
    {
        TuneResult Result(TuneOutcome outcome, string detail) => new(tweak.Id, TuneAction.ReleasePolicy, outcome, detail);
        if (parts is null || parts.Count != 1 || HasUnknownPart(tweak, parts) || TweakGuard.Validate(tweak).Count > 0)
            return Result(TuneOutcome.Skipped, "Select one supported policy value.");
        var index = parts.Single();
        if (index >= tweak.Values.Count || !InScope(tweak.Values[index]))
            return Result(TuneOutcome.Skipped, "No documented recovery for this value.");
        var environment = probe.GetPolicyEnvironment();
        var value = tweak.Values[index];
        if (!PolicyRecovery.Supports(value, environment)) return Result(TuneOutcome.Skipped, "No documented recovery for this value.");
        if (!environment.CanReleaseValue(value)) return Result(TuneOutcome.Skipped, PolicyRecovery.Explain(environment));
        try
        {
            var live = registry.Read(value.Hive, value.Path, value.Name);
            if (!live.Exists || live.Kind is null) return Result(TuneOutcome.Skipped, "Value absent or unreadable; left unchanged.");
            var records = journal.Load().ToList();
            var record = records.FirstOrDefault(item => item.Id.Equals(tweak.Id, StringComparison.OrdinalIgnoreCase));
            if (record?.Values.Any(item => IsSameValue(item, value)) == true)
                return Result(TuneOutcome.Skipped, "Undo the recorded change before releasing this policy.");
            if (record is null) records.Add(record = new TweakRecord { Id = tweak.Id });
            record.AppliedUtc = DateTimeOffset.UtcNow;
            var recovery = new TweakValueRecord { Hive = value.Hive, Path = value.Path, Name = value.Name,
                Existed = true, PreviousKind = live.Kind, Previous = live.Value, WrittenKind = value.Kind, Written = "", WrittenAbsent = true };
            record.Values.Add(recovery);
            journal.Save(records);
            // Recheck the exact value after journaling; do not overwrite a competing writer's choice.
            if (registry.Read(value.Hive, value.Path, value.Name) != live)
            {
                record.Values.Remove(recovery);
                if (record.Values.Count == 0 && record.DisabledTasks.Count == 0) records.Remove(record);
                journal.Save(records);
                return Result(TuneOutcome.Skipped, "The value changed during review; left unchanged. Refresh before trying again.");
            }
            registry.Delete(value.Hive, value.Path, value.Name);
            return registry.Read(value.Hive, value.Path, value.Name).Exists
                ? Result(TuneOutcome.Failed, "Windows retained or reapplied the policy. The recovery record was kept.")
                : Result(TuneOutcome.Done, "Policy value removed and recorded. Other policies, edition limits or removed components may still restrict the feature.");
        }
        catch (Exception ex) when (IsAccessFailure(ex)) { return Result(TuneOutcome.Failed, ex.Message); }
    }

    /// <param name="parts">The parts to apply (see <see cref="Tweak.Parts"/>); null applies the whole tweak.</param>
    public TuneResult Apply(Tweak tweak, IReadOnlySet<int>? parts = null)
    {
        ArgumentNullException.ThrowIfNull(tweak);
        TuneResult Result(TuneOutcome outcome, string? detail) => new(tweak.Id, TuneAction.Tweak, outcome, detail);

        // Fail closed: checked again here, whatever loaded the catalog.
        if (TweakGuard.Validate(tweak) is { Count: > 0 } violations)
        {
            return Result(TuneOutcome.Skipped, violations[0]);
        }

        if (HasUnknownPart(tweak, parts))
        {
            return Result(TuneOutcome.Skipped, "Unknown part of the tweak.");
        }

        var values = tweak.Values.Where((value, index) => InScope(value) && IsChosen(parts, index)).ToList();
        var taskPaths = machineScope ? tweak.Tasks.Where((_, index) => IsChosen(parts, tweak.Values.Count + index)).ToList() : [];
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

            if (record.Values.Any(existing => existing.WrittenAbsent && values.Any(value => IsSameValue(existing, value))))
                return Result(TuneOutcome.Skipped, "Undo the recorded policy recovery before applying this setting again.");

            record.AppliedUtc = DateTimeOffset.UtcNow;
            var changes = 0;
            var refused = new List<string>();

            foreach (var (value, live) in before.Where(pair => !pair.Live.Matches(pair.Value.Kind, pair.Value.Value)))
            {
                // Keep the very first "before": applying twice must not overwrite it with our own value.
                TweakValueRecord? added = null;
                if (!record.Values.Any(existing => IsSameValue(existing, value)))
                {
                    record.Values.Add(added = new TweakValueRecord
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

                // Read it back: a policy or a protection driver of Windows can refuse a write without raising an error.
                if (!registry.Read(value.Hive, value.Path, value.Name).Matches(value.Kind, value.Value))
                {
                    if (added is not null)
                    {
                        record.Values.Remove(added);
                        journal.Save(records);
                    }

                    refused.Add(value.Name);
                    continue;
                }

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

            if (refused.Count > 0)
            {
                var names = string.Join(", ", refused);
                return changes == 0
                    ? Result(TuneOutcome.Failed, $"Windows kept its own value for {names}: nothing was changed.")
                    : Result(TuneOutcome.Done, $"Windows kept its own value for {names}; the rest was changed.");
            }

            return changes == 0 ? Result(TuneOutcome.Skipped, "Already applied.") : Result(TuneOutcome.Done, null);
        }
        catch (Exception ex) when (IsAccessFailure(ex))
        {
            return Result(TuneOutcome.Failed, ex.Message);
        }
    }

    public TuneResult Undo(string tweakId) => Undo(tweakId, tweak: null, parts: null);

    /// <summary>Undoes only the chosen parts of a tweak that was applied; null undoes all of it.</summary>
    public TuneResult Undo(Tweak tweak, IReadOnlySet<int>? parts)
    {
        ArgumentNullException.ThrowIfNull(tweak);
        return HasUnknownPart(tweak, parts)
            ? new TuneResult(tweak.Id, TuneAction.Untweak, TuneOutcome.Skipped, "Unknown part of the tweak.")
            : Undo(tweak.Id, tweak, parts);
    }

    private TuneResult Undo(string tweakId, Tweak? tweak, IReadOnlySet<int>? parts)
    {
        TuneResult Result(TuneOutcome outcome, string? detail) => new(tweakId, TuneAction.Untweak, outcome, detail);

        var records = journal.Load().ToList();
        var record = records.FirstOrDefault(existing => existing.Id.Equals(tweakId, StringComparison.OrdinalIgnoreCase));
        if (record is null)
        {
            return Result(TuneOutcome.Skipped, "Nothing to undo.");
        }

        // Which journaled changes to undo: all of them, or those of the chosen parts.
        var values = record.Values.Where(value => parts is null || tweak?.PartOf(value) is { } part && parts.Contains(part)).ToList();
        var disabledTasks = record.DisabledTasks.Where(task => parts is null || tweak?.PartOfTask(task) is { } part && parts.Contains(part)).ToList();
        if (values.Count == 0 && disabledTasks.Count == 0)
        {
            return Result(TuneOutcome.Skipped, "Nothing to undo.");
        }

        try
        {
            var leftAlone = 0;
            foreach (var value in Enumerable.Reverse(values))
            {
                // Someone else changed the value since: their choice wins.
                var live = registry.Read(value.Hive, value.Path, value.Name);
                if (value.WrittenAbsent ? live.Exists : !live.Matches(value.WrittenKind, value.Written))
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

                if ((value.WrittenAbsent ? !live.Exists : live.Matches(value.WrittenKind, value.Written))
                    && registry.Read(value.Hive, value.Path, value.Name) is { } restored
                    && (value.Existed ? !restored.Matches(value.PreviousKind!.Value, value.Previous!) : restored.Exists))
                    return Result(TuneOutcome.Failed, "Windows did not restore the recorded value. The recovery record was kept.");
            }

            foreach (var task in disabledTasks.Where(task => tasks.IsEnabled(task) == false))
            {
                tasks.SetEnabled(task, enabled: true);
            }

            record.Values.RemoveAll(values.Contains);
            record.DisabledTasks.RemoveAll(disabledTasks.Contains);
            if (record.Values.Count == 0 && record.DisabledTasks.Count == 0)
            {
                records.Remove(record);
            }

            journal.Save(records);
            return Result(TuneOutcome.Done, leftAlone == 0 ? null : $"{leftAlone} values were changed by something else since and were left as they are.");
        }
        catch (Exception ex) when (IsAccessFailure(ex))
        {
            return Result(TuneOutcome.Failed, ex.Message);
        }
    }

    private static bool IsChosen(IReadOnlySet<int>? parts, int index) => parts is null || parts.Contains(index);

    private static bool HasUnknownPart(Tweak tweak, IReadOnlySet<int>? parts) =>
        parts is not null && parts.Any(index => index < 0 || index >= tweak.Parts.Count);

    private bool InScope(TweakValue value) => (value.Hive == TweakHive.Machine) == machineScope;

    private static bool IsSameValue(TweakValueRecord record, TweakValue value) =>
        record.Hive == value.Hive
        && record.Path.Equals(value.Path, StringComparison.OrdinalIgnoreCase)
        && record.Name.Equals(value.Name, StringComparison.OrdinalIgnoreCase);

    private static bool IsAccessFailure(Exception ex) =>
        ex is UnauthorizedAccessException or SecurityException or IOException or COMException or InvalidOperationException;
}
