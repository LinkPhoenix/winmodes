using WinModes.Core.Engine;

namespace WinModes.Core.Tests;

public sealed class WslConfigTests
{
    private const string Existing = "[wsl2]\n# shared limit\nmemory=12GB\nprocessors=8\n\n[experimental]\nautoMemoryReclaim=dropCache\n";

    [Fact]
    public void ReadMemoryGb_ReadsTheWsl2SectionOnly()
    {
        Assert.Equal(12, WslConfig.ReadMemoryGb(Existing));
        Assert.Null(WslConfig.ReadMemoryGb("[experimental]\nmemory=4GB\n"));
        Assert.Null(WslConfig.ReadMemoryGb(""));
    }

    [Fact]
    public void WithMemoryGb_ReplacesTheValueAndKeepsEverythingElse()
    {
        var updated = WslConfig.WithMemoryGb(Existing, 8);

        Assert.Equal(Existing.Replace("memory=12GB", "memory=8GB", StringComparison.Ordinal), updated);
    }

    [Fact]
    public void WithMemoryGb_AddsTheSectionWhenTheFileIsEmptyOrHasNone()
    {
        Assert.Equal("[wsl2]\nmemory=6GB\n", WslConfig.WithMemoryGb("", 6));
        Assert.Equal(6, WslConfig.ReadMemoryGb(WslConfig.WithMemoryGb("[experimental]\nsparseVhd=true", 6)));
    }

    [Fact]
    public void WithMemoryGb_RemovesTheLimitWhenNull()
    {
        var updated = WslConfig.WithMemoryGb(Existing, null);

        Assert.Null(WslConfig.ReadMemoryGb(updated));
        Assert.Contains("processors=8", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void WithMemoryGb_KeepsWindowsLineEndings()
    {
        var updated = WslConfig.WithMemoryGb("[wsl2]\r\nmemory=12GB\r\nswap=4GB\r\n", 8);

        Assert.Equal("[wsl2]\r\nmemory=8GB\r\nswap=4GB\r\n", updated);
    }
}
