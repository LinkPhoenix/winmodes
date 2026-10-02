using WinModes.Core.Usage;

namespace WinModes.Core.Tests;

public sealed class PollBackoffTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Ceiling = TimeSpan.FromHours(1);

    private static PollBackoff Poll() => new(Interval, Ceiling);

    [Fact]
    public void FirstRequest_IsDueAtOnce_AndASuccessKeepsTheRegularPace()
    {
        var poll = Poll();
        Assert.True(poll.IsDue(Now));

        poll.Succeeded(Now);

        Assert.False(poll.IsDue(Now.AddMinutes(4)));
        Assert.True(poll.IsDue(Now.AddMinutes(5)));
    }

    [Fact]
    public void FailuresInARow_DoubleTheWaitUpToTheCeiling()
    {
        var poll = Poll();
        var waits = new List<double>();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            poll.Failed(Now);
            waits.Add((poll.NextAllowed - Now).TotalMinutes);
        }

        // 10, 20, 40, then the one hour ceiling.
        Assert.Equal([10d, 20d, 40d, 60d, 60d, 60d], waits);
        Assert.Equal(6, poll.Failures);
    }

    [Fact]
    public void ASuccess_EndsTheBackoff()
    {
        var poll = Poll();
        poll.Failed(Now);
        poll.Failed(Now);

        poll.Succeeded(Now);

        Assert.Equal(0, poll.Failures);
        Assert.Equal(Now + Interval, poll.NextAllowed);
    }

    [Fact]
    public void RetryAfter_IsObeyedWhenLongerThanTheBackoff_AndCapped()
    {
        var poll = Poll();

        poll.Failed(Now, TimeSpan.FromMinutes(45));
        Assert.Equal(Now.AddMinutes(45), poll.NextAllowed);

        // Shorter than the wait the backoff already chose: the longer one stands.
        poll.Failed(Now, TimeSpan.FromSeconds(30));
        Assert.Equal(Now.AddMinutes(20), poll.NextAllowed);

        poll.Failed(Now, TimeSpan.FromDays(3));
        Assert.Equal(Now + PollBackoff.RetryAfterLimit, poll.NextAllowed);
    }

    [Fact]
    public void Reset_AllowsTheNextRequestAtOnce()
    {
        var poll = Poll();
        poll.Failed(Now, TimeSpan.FromHours(2));

        poll.Reset();

        Assert.True(poll.IsDue(Now));
        Assert.Equal(0, poll.Failures);
    }

    [Theory]
    [InlineData("120", 120)]
    [InlineData(" 7.5 ", 7.5)]
    [InlineData("Fri, 02 Oct 2026 12:05:00 GMT", 300)]
    [InlineData("99999999", 21600)]
    public void RetryAfter_AcceptsSecondsOrADate(string header, double expectedSeconds) =>
        Assert.Equal(expectedSeconds, PollBackoff.ParseRetryAfter(header, Now)!.Value.TotalSeconds, 0.5);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("-5")]
    [InlineData("0")]
    [InlineData("NaN")]
    [InlineData("Fri, 02 Oct 2026 11:00:00 GMT")]
    public void RetryAfter_IgnoresAMissingMalformedOrPastValue(string? header) => Assert.Null(PollBackoff.ParseRetryAfter(header, Now));
}
