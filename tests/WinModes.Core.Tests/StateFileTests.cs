using WinModes.Core.Engine;
using WinModes.Core.Planning;
using WinModes.Core.Tuning;

namespace WinModes.Core.Tests;

public sealed class StateFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"winmodes-state-{Guid.NewGuid():N}");

    public StateFileTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string[] Quarantined() => Directory.GetFiles(_directory, "*.corrupt-*", SearchOption.AllDirectories);

    [Fact]
    public void TweakStore_MovesACorruptFileAsideInsteadOfOverwritingIt()
    {
        File.WriteAllText(Path.Combine(_directory, "services.json"), "{ truncated");
        var store = new TweakStore(_directory);

        Assert.Empty(store.Load());
        store.Save([new ServiceTweak { Service = "Fax", OriginalStartMode = ServiceStartMode.Automatic }]);

        Assert.Equal("{ truncated", File.ReadAllText(Assert.Single(Quarantined())));
        Assert.Single(store.Load());
    }

    [Fact]
    public void TweakStore_TreatsAMissingFileAsEmptyWithoutQuarantine()
    {
        Assert.Empty(new TweakStore(_directory).Load());
        Assert.Empty(Quarantined());
    }

    [Fact]
    public void TweakStore_ThrowsOnAnUnreadableFileSoTheNextSaveCannotEraseIt()
    {
        var path = Path.Combine(_directory, "services.json");
        File.WriteAllText(path, "[]");
        using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Throws<IOException>(() => new TweakStore(_directory).Load());
    }

    [Fact]
    public void TweakJournal_QuarantinesAFileHoldingNull()
    {
        var path = Path.Combine(_directory, "tweaks.json");
        File.WriteAllText(path, "null");

        Assert.Empty(new TweakJournal(path).Load());
        Assert.Single(Quarantined());
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void JournalStore_QuarantinesADamagedSessionAndKeepsTheOthers()
    {
        var store = new JournalStore(_directory);
        store.Save(new JournalSession { Id = "good", Mode = "test", StartedUtc = DateTimeOffset.UtcNow });
        File.WriteAllText(Path.Combine(_directory, "bad.json"), "not json");

        Assert.Equal("good", Assert.Single(store.LoadAll()).Id);
        Assert.Single(Quarantined());
    }
}
