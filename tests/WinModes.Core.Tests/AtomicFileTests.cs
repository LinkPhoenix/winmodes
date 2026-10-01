namespace WinModes.Core.Tests;

public sealed class AtomicFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"winmodes-atomic-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void WriteAllText_CreatesTheFolderAndReplacesTheFileWithoutLeavingATemporaryOne()
    {
        var path = Path.Combine(_directory, "deep", "state.json");

        AtomicFile.WriteAllText(path, "first");
        AtomicFile.WriteAllText(path, "second");

        Assert.Equal("second", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void WriteAllText_LeavesTheOldFileAndNoTemporaryOneWhenTheTargetCannotBeReplaced()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "state.json");
        File.WriteAllText(path, "old");
        using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.ThrowsAny<SystemException>(() => AtomicFile.WriteAllText(path, "new"));

        Assert.Equal([path], Directory.GetFiles(_directory));
        held.Dispose();
        Assert.Equal("old", File.ReadAllText(path));
    }

    [Fact]
    public void WriteAllText_RejectsAnEmptyPath() => Assert.Throws<ArgumentException>(() => AtomicFile.WriteAllText("", "x"));
}
