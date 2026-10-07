using System.Globalization;
using WinModes.Core.Usage;

namespace WinModes.Core.Tests;

public sealed class SubscriptionsTests
{
    // What the Codex usage endpoint answers: weekly window, resetting at 1790719307.
    private const string CodexAnswer =
        """{"plan_type":"prolite","rate_limit":{"allowed":true,"primary_window":{"used_percent":75.0,"limit_window_seconds":604800,"reset_at":1790719307}}}""";

    private static readonly DateTimeOffset Reset = DateTimeOffset.FromUnixTimeSeconds(1790719307);
    private static readonly DateTimeOffset Seen = new(2026, 9, 25, 9, 18, 35, TimeSpan.Zero);

    [Theory]
    [InlineData("claude_max", "default_claude_max_5x", "Max 5x")]
    [InlineData("claude_max", "default_claude_max_20x", "Max 20x")]
    [InlineData("claude_pro", "default_claude_ai", "Pro")]
    [InlineData("claude_team", "", "Team")]
    public void ClaudeProfile_GivesThePlan(string type, string tier, string expected) =>
        Assert.Equal(expected, Subscriptions.ClaudePlanFromProfile($$$"""{"account":{"email":"x"},"organization":{"organization_type":"{{{type}}}","rate_limit_tier":"{{{tier}}}"}}"""));

    [Theory]
    [InlineData("""{"organization":{"organization_type":"api"}}""")]
    [InlineData("""{"organization":null}""")]
    [InlineData("""{"account":{}}""")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void ClaudeProfile_WithoutASubscriptionGivesNothing(string json) => Assert.Null(Subscriptions.ClaudePlanFromProfile(json));

    [Fact]
    public void Describe_SaysWhatIsLeftAndWhenTheLimitResets()
    {
        var status = OnlineUsage.ParseCodex(CodexAnswer, Seen)!;

        var (value, detail, remaining) = Subscriptions.Describe(status, Reset.AddHours(-50).AddMinutes(-5), CultureInfo.InvariantCulture);

        Assert.Equal("25 % left", value);
        Assert.Equal(25, remaining);
        Assert.StartsWith("Weekly limit resets in 2 d 2 h (", detail, StringComparison.Ordinal);
        Assert.Contains("As of 25 Sep 09:18", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_DoesNotShowAFigureFromBeforeTheReset()
    {
        var status = OnlineUsage.ParseCodex(CodexAnswer, Seen)!;

        var (value, detail, remaining) = Subscriptions.Describe(status, Reset.AddHours(3), CultureInfo.InvariantCulture);

        Assert.Equal("reset", value);
        Assert.Null(remaining);
        Assert.Contains("no use recorded since", detail, StringComparison.Ordinal);
    }
}
