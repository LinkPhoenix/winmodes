using System.Text.Json;
using WinModes.Core.Engine;

namespace WinModes.Core.Apps;

/// <summary>A startup item that WinModes turned off or on, with the approval value it had before.</summary>
public sealed record StartupChange
{
    public required StartupSource Source { get; init; }
    public required string Name { get; init; }

    /// <summary>The approval value before the first change, in base64; null when the item had none.</summary>
    public string? Before { get; init; }

    public DateTimeOffset ChangedUtc { get; init; }

    public byte[]? BeforeBytes => Before is null ? null : Convert.FromBase64String(Before);
}

/// <summary>The startup items changed by WinModes. The first value an item had is kept, so Reset always goes back to it.</summary>
public sealed class StartupJournal(string filePath)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public IReadOnlyList<StartupChange> Load() => StateFile.Read<List<StartupChange>>(filePath, Options) ?? [];

    public StartupChange? Find(StartupSource source, string name) =>
        Load().FirstOrDefault(change => change.Source == source && change.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Records an item before its first change. A second change does not overwrite what it was originally.</summary>
    public void Remember(StartupSource source, string name, byte[]? before)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Find(source, name) is not null)
        {
            return;
        }

        var all = Load().ToList();
        all.Add(new StartupChange { Source = source, Name = name, Before = before is null ? null : Convert.ToBase64String(before), ChangedUtc = DateTimeOffset.UtcNow });
        AtomicFile.WriteAllText(filePath, JsonSerializer.Serialize(all, Options));
    }

    public void Forget(StartupSource source, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var all = Load().Where(change => !(change.Source == source && change.Name.Equals(name, StringComparison.OrdinalIgnoreCase))).ToList();
        AtomicFile.WriteAllText(filePath, JsonSerializer.Serialize(all, Options));
    }
}
