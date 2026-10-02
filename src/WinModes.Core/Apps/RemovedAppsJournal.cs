using System.Text.Json;
using WinModes.Core.Engine;

namespace WinModes.Core.Apps;

/// <summary>An app WinModes removed for the current user, with what is needed to bring it back.</summary>
public sealed record RemovedApp
{
    public required string EntryId { get; init; }
    public required string Title { get; init; }
    public required string Name { get; init; }
    public required string FullName { get; init; }
    public string Family { get; init; } = "";
    public string Version { get; init; } = "";

    /// <summary>The folder the package files were in; they usually stay there, which is what makes a restore without download possible.</summary>
    public string InstallLocation { get; init; } = "";

    public DateTimeOffset RemovedUtc { get; init; }
    public AppReinstall Reinstall { get; init; } = new();
}

/// <summary>The apps removed by WinModes, kept in a file saved in one step so a crash cannot lose the list.</summary>
public sealed class RemovedAppsJournal(string filePath)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public IReadOnlyList<RemovedApp> Load() => StateFile.Read<List<RemovedApp>>(filePath, Options) ?? [];

    /// <summary>Records a removal before it is done: if the removal then fails, the line is dropped by <see cref="Forget"/>.</summary>
    public void Add(RemovedApp app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var all = Load().Where(existing => !existing.Name.Equals(app.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        all.Add(app);
        AtomicFile.WriteAllText(filePath, JsonSerializer.Serialize(all, Options));
    }

    public void Forget(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var all = Load().Where(existing => !existing.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        AtomicFile.WriteAllText(filePath, JsonSerializer.Serialize(all, Options));
    }
}
