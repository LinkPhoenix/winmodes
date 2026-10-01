using WinModes.Core.Updates;

namespace WinModes.Core.Tests;

public sealed class ReleaseVersionTests
{
    private static readonly Version Current = new(0, 3, 1);

    [Theory]
    [InlineData("v0.3.2", true)]
    [InlineData("0.4.0", true)]
    [InlineData("v1.0.0", true)]
    [InlineData("v0.3.1", false)]
    [InlineData("v0.3.0", false)]
    [InlineData("v0.10.0", true)]
    public void IsNewer_ComparesNumerically(string tag, bool expected) =>
        Assert.Equal(expected, ReleaseVersion.IsNewer(tag, Current));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v1.0")]
    [InlineData("v1.0.0-beta")]
    [InlineData("v1.0.0.0")]
    public void IsNewer_RejectsAnythingThatIsNotAPlainVersion(string? tag) =>
        Assert.False(ReleaseVersion.IsNewer(tag, Current));

    [Fact]
    public void IsNewer_IgnoresTheRevisionOfTheRunningBuild() =>
        Assert.False(ReleaseVersion.IsNewer("v0.3.1", new Version(0, 3, 1, 0)));
}
