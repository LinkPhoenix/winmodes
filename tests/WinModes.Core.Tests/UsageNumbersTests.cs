using WinModes.Core.Usage;

namespace WinModes.Core.Tests;

public sealed class UsageNumbersTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"winmodes-numbers-{Guid.NewGuid():N}");

    public UsageNumbersTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData("1e300")]
    [InlineData("-1e300")]
    [InlineData("99999999999999999999")]
    public void ClaudeStatusLine_KeepsTheWindowWhenTheResetTimeIsOutOfRange(string reset)
    {
        var limits = ClaudeStatusLine.Parse("{\"rate_limits\":{\"five_hour\":{\"used_percentage\":12,\"resets_at\":" + reset + "}}}", Now);

        var window = Assert.IsType<ClaudeLimits>(limits).FiveHour;
        Assert.Equal(12, window!.UsedPercent);
        Assert.Null(window.ResetsAt);
    }

    [Fact]
    public void ClaudeStatusLine_IgnoresAWindowWhosePercentageIsNotARepresentableNumber()
    {
        Assert.Null(ClaudeStatusLine.Parse("""{"rate_limits":{"five_hour":{"used_percentage":1e999}}}""", Now));
    }

    [Fact]
    public void CodexAnswer_SurvivesOutOfRangeNumbers()
    {
        var status = OnlineUsage.ParseCodex(
            """{"plan_type":"plus","rate_limit":{"primary_window":{"used_percent":5,"limit_window_seconds":1e300,"reset_at":1e300}}}""", Now);

        Assert.NotNull(status);
        Assert.Equal(new LimitWindow(5, 0, null), status.Primary);
    }

    [Fact]
    public void CodexAnswer_SurvivesOutOfRangeResetTimes()
    {
        var status = OnlineUsage.ParseCodex(
            """{"plan_type":"pro","rate_limit":{"primary_window":{"used_percent":7,"limit_window_seconds":1e300,"reset_at":1e300}}}""", Now);

        Assert.Equal(new LimitWindow(7, 0, null), status?.Primary);
    }
}
