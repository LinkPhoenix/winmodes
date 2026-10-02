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

    [Theory]
    [InlineData("v0.9.3-beta.20261002")]
    [InlineData("v0.9.4-beta.20261002.2")]
    public void IsNewer_NeverOffersABetaToAStableBuild(string tag) =>
        Assert.False(ReleaseVersion.IsNewer(tag, Current));

    [Theory]
    [InlineData("v0.9.3-beta.20261002", "2026100201")]
    [InlineData("0.9.3-beta.20261002.2", "2026100202")]
    [InlineData("v0.9.3-beta.20261002.12", "2026100212")]
    public void TryParseRelease_ReadsTheBetaNumber(string tag, string expected)
    {
        Assert.True(ReleaseVersion.TryParseRelease(tag, out var version, out var beta));
        Assert.Equal(new Version(0, 9, 3), version);
        Assert.Equal(long.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), beta);
    }

    [Theory]
    [InlineData("v0.9.3-beta")]
    [InlineData("v0.9.3-beta.1")]
    [InlineData("v0.9.3-beta.20261002.0")]
    [InlineData("v0.9.3-beta.20261002.100")]
    [InlineData("v0.9.3-rc.20261002")]
    [InlineData("v0.9.3-beta.20261002-x")]
    public void TryParseRelease_RejectsMalformedBetaTags(string tag) =>
        Assert.False(ReleaseVersion.TryParseRelease(tag, out _, out _));

    [Fact]
    public void TryParse_StaysStableOnly() =>
        Assert.False(ReleaseVersion.TryParse("v0.9.3-beta.20261002", out _));

    [Theory]
    [InlineData("v0.9.3-beta.20261002", 0, 9, 2, true)]
    [InlineData("v0.9.4-beta.20260101", 0, 9, 3, true)]
    [InlineData("v0.9.3-beta.20261002", 0, 9, 3, false)]
    [InlineData("v0.9.3", 0, 9, 2, true)]
    [InlineData("v0.9.3", 0, 9, 3, false)]
    public void IsNewer_ForAStableBuildOnTheBetaChannel_OffersOnlyBetasOfAHigherVersion(string tag, int major, int minor, int build, bool expected) =>
        Assert.Equal(expected, ReleaseVersion.IsNewer(tag, new Version(major, minor, build), null, includeBetas: true));

    [Theory]
    [InlineData("v0.9.3-beta.20261003", false)]
    [InlineData("v0.9.4-beta.20260101", false)]
    [InlineData("v0.9.3", true)]
    [InlineData("v0.9.2", false)]
    public void IsNewer_ForABetaBuildOnTheStableChannel_WaitsForTheStableVersion(string tag, bool expected) =>
        Assert.Equal(expected, ReleaseVersion.IsNewer(tag, new Version(0, 9, 3, 0), ReleaseVersion.BetaNumber(20261002), includeBetas: false));

    [Theory]
    [InlineData("v0.9.3-beta.20261003", true)]
    [InlineData("v0.9.3-beta.20261002", false)]
    [InlineData("v0.9.3", true)]
    [InlineData("v0.9.4-beta.20260101", true)]
    public void IsNewer_ForABetaBuildOnTheBetaChannel_FollowsTheBetas(string tag, bool expected) =>
        Assert.Equal(expected, ReleaseVersion.IsNewer(tag, new Version(0, 9, 3, 0), ReleaseVersion.BetaNumber(20261002), includeBetas: true));

    [Theory]
    [InlineData("v0.9.3-beta.20261003", true)]
    [InlineData("v0.9.3-beta.20261002.2", true)]
    [InlineData("v0.9.3-beta.20261002", false)]
    [InlineData("v0.9.3-beta.20261001", false)]
    [InlineData("v0.9.3", true)]
    [InlineData("v0.9.2", false)]
    [InlineData("v0.9.4-beta.20260101", true)]
    [InlineData("v0.10.0", true)]
    public void IsNewer_ForABetaBuild_FollowsTheBetasAndTheStableVersion(string tag, bool expected) =>
        Assert.Equal(expected, ReleaseVersion.IsNewer(tag, new Version(0, 9, 3, 0), ReleaseVersion.BetaNumber(20261002)));
}
