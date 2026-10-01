namespace WinModes.Core.Tests;

public sealed class ErrorLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"winmodes-log-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Append_WritesTheSourceAndTheExceptionAndAddsToThePreviousEntries()
    {
        var log = new ErrorLog(Path.Combine(_directory, "sub", "errors.log"));

        log.Append("ui", new InvalidOperationException("first"));
        log.Append("task", new IOException("second"));

        var text = File.ReadAllText(log.FilePath);
        Assert.Contains("[ui] System.InvalidOperationException: first", text, StringComparison.Ordinal);
        Assert.Contains("[task] System.IO.IOException: second", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Append_KeepsOneOldLogOnceTheLimitIsPassed()
    {
        var log = new ErrorLog(Path.Combine(_directory, "errors.log"), maxBytes: 200);

        for (var index = 0; index < 5; index++)
        {
            log.Append("ui", new InvalidOperationException($"error {index}"));
        }

        Assert.True(File.Exists(log.FilePath + ".old"));
        Assert.Contains("error 4", File.ReadAllText(log.FilePath), StringComparison.Ordinal);
        Assert.True(new FileInfo(log.FilePath).Length < 2000);
    }

    [Fact]
    public void Append_NeverThrowsWhenTheLogCannotBeWritten()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "errors.log");
        using var held = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);

        new ErrorLog(path).Append("ui", new InvalidOperationException("x"));
    }
}
