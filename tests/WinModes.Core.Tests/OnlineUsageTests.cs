using System.Globalization;
using WinModes.Core.Usage;

namespace WinModes.Core.Tests;

public sealed class OnlineUsageTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"winmodes-online-{Guid.NewGuid():N}");

    public OnlineUsageTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Write(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void CodexAnswer_GivesBothWindowsAndTheResetsInReserve()
    {
        var status = OnlineUsage.ParseCodex(
            """{"plan_type":"prolite","rate_limit":{"allowed":true,"primary_window":{"used_percent":40,"limit_window_seconds":18000,"reset_after_seconds":3600,"reset_at":1790856000},"secondary_window":{"used_percent":99,"limit_window_seconds":604800,"reset_at":1791055000}},"rate_limit_reset_credits":{"available_count":2}}""",
            Now);

        Assert.NotNull(status);
        Assert.Equal("Pro Lite", status.Plan);
        Assert.Equal(new LimitWindow(40, 300, DateTimeOffset.FromUnixTimeSeconds(1790856000)), status.Primary);
        Assert.Equal(10080, status.Secondary!.WindowMinutes);
        Assert.Equal(2, status.ResetCredits);
        Assert.Contains("2 limit resets in reserve", Subscriptions.Describe(status, Now, CultureInfo.InvariantCulture).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ClaudeAnswer_GivesBothWindows()
    {
        var status = OnlineUsage.ParseClaude(
            """{"five_hour":{"utilization":12.0,"resets_at":"2026-10-01T15:00:00+00:00"},"seven_day":{"utilization":30.5,"resets_at":"2026-10-05T08:00:00+00:00"},"seven_day_opus":null}""",
            "Max 5x", Now);

        Assert.NotNull(status);
        Assert.Equal(new LimitWindow(12, 300, new DateTimeOffset(2026, 10, 1, 15, 0, 0, TimeSpan.Zero)), status.Primary);
        Assert.Equal(69.5, status.Secondary!.RemainingPercent);
        Assert.Equal(Now, status.SeenAt);
    }

    [Fact]
    public void ClaudeAnswer_GivesTheWeeklyLimitOfEachModelAndTheExtraUsage()
    {
        var status = OnlineUsage.ParseClaude(
            """
            {"five_hour":{"utilization":12.0,"resets_at":"2026-10-01T15:00:00+00:00"},
             "seven_day":{"utilization":30.5,"resets_at":"2026-10-05T08:00:00+00:00"},
             "seven_day_opus":{"utilization":88,"resets_at":"2026-10-05T08:00:00+00:00"},
             "seven_day_sonnet":{"utilization":19,"resets_at":null},
             "seven_day_fable":null,
             "extra_usage":{"is_enabled":true,"monthly_limit":5000,"used_credits":1250,"decimal_places":2,"currency":"EUR"}}
            """,
            "Max 5x", Now);

        Assert.NotNull(status);
        Assert.Equal(["Opus", "Sonnet"], status.ModelLimits!.Select(limit => limit.Model));
        Assert.Equal(12, status.ModelLimits![0].Window.RemainingPercent);
        Assert.Equal(10080, status.ModelLimits[0].Window.WindowMinutes);
        Assert.Equal(new ExtraUsage(12.50m, 50m, "EUR"), status.Extra);
        Assert.Equal(25, status.Extra!.UsedPercent);
    }

    [Fact]
    public void ClaudeAnswer_GivesTheScopedWeeklyLimitsOfTheLimitsList_ForKnownModelsOnly()
    {
        var status = OnlineUsage.ParseClaude(
            """
            {"five_hour":{"utilization":1},
             "seven_day_opus":{"utilization":40,"resets_at":"2026-10-05T08:00:00+00:00"},
             "limits":[
               {"kind":"session","percent":17,"resets_at":"2026-10-02T03:40:00+00:00","scope":null},
               {"kind":"weekly_scoped","percent":55,"resets_at":"2026-10-06T01:00:00+00:00","scope":{"model":{"display_name":"Claude Sonnet 5"},"surface":null}},
               {"kind":"weekly_scoped","percent":70,"scope":{"model":{"display_name":"Opus"}}},
               {"kind":"weekly_scoped","percent":99,"scope":{"model":{"display_name":"<script>alert(1)</script>"}}},
               {"kind":"weekly_all","percent":91,"scope":null}]}
            """,
            "Max 5x", Now);

        // Opus comes from its own block (the list repeats it); Sonnet only from the list; an unknown name is not shown.
        Assert.Equal(["Opus", "Sonnet"], status!.ModelLimits!.Select(limit => limit.Model));
        Assert.Equal(60, status.ModelLimits![0].Window.RemainingPercent);
        Assert.Equal(45, status.ModelLimits[1].Window.RemainingPercent);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 1, 0, 0, TimeSpan.Zero), status.ModelLimits[1].Window.ResetsAt);
    }

    [Theory]
    [InlineData("""{"five_hour":{"utilization":1},"extra_usage":{"is_enabled":false,"monthly_limit":5000,"used_credits":10}}""")]
    [InlineData("""{"five_hour":{"utilization":1},"extra_usage":{"is_enabled":true,"monthly_limit":0,"used_credits":10}}""")]
    [InlineData("""{"five_hour":{"utilization":1},"extra_usage":{"is_enabled":true}}""")]
    [InlineData("""{"five_hour":{"utilization":1},"extra_usage":null}""")]
    [InlineData("""{"five_hour":{"utilization":1}}""")]
    public void ClaudeAnswer_WithoutUsableExtraUsage_GivesNone(string body)
    {
        var status = OnlineUsage.ParseClaude(body, "Pro", Now);

        Assert.NotNull(status);
        Assert.Null(status.Extra);
        Assert.Null(status.ModelLimits);
    }

    [Fact]
    public void ClaudeExtraUsage_DefaultsToDollarsAndTwoDecimals_AndNeverExceedsItsLimit()
    {
        var status = OnlineUsage.ParseClaude("""{"five_hour":{"utilization":1},"extra_usage":{"is_enabled":true,"monthly_limit":2000,"used_credits":9000}}""", "Pro", Now);

        Assert.Equal(new ExtraUsage(90m, 20m, "USD"), status!.Extra);
        Assert.Equal(100, status.Extra!.UsedPercent);
    }

    [Theory]
    [InlineData("<html>Sign in</html>")]
    [InlineData("""{"error":{"type":"authentication_error"}}""")]
    [InlineData("[]")]
    public void UnexpectedAnswer_GivesNothing(string body)
    {
        Assert.Null(OnlineUsage.ParseCodex(body, Now));
        Assert.Null(OnlineUsage.ParseClaude(body, "Pro", Now));
    }

    [Fact]
    public void Requests_GoOnlyToTheProviderWithItsOwnSignIn()
    {
        var codex = OnlineUsage.CodexRequest(Write("auth.json", """{"tokens":{"access_token":"codex-token","account_id":"account-1"}}"""));
        var claude = OnlineUsage.ClaudeRequest(
            Write("credentials.json", $$"""{"claudeAiOauth":{"accessToken":"claude-token","expiresAt":{{Now.AddHours(1).ToUnixTimeMilliseconds()}} } }"""), Now);

        Assert.Equal("chatgpt.com", codex!.Address.Host);
        Assert.Equal("Bearer codex-token", codex.Headers["Authorization"]);
        Assert.Equal("account-1", codex.Headers["ChatGPT-Account-Id"]);
        Assert.Equal("api.anthropic.com", claude!.Address.Host);
        Assert.Equal("Bearer claude-token", claude.Headers["Authorization"]);
    }

    [Fact]
    public void MissingOrExpiredSignIn_GivesNoRequest()
    {
        Assert.Null(OnlineUsage.CodexRequest(Path.Combine(_directory, "none.json")));
        Assert.Null(OnlineUsage.CodexRequest(Write("key.json", """{"OPENAI_API_KEY":"k","tokens":null}""")));
        Assert.Null(OnlineUsage.ClaudeRequest(
            Write("old.json", $$"""{"claudeAiOauth":{"accessToken":"t","expiresAt":{{Now.AddMinutes(-1).ToUnixTimeMilliseconds()}} } }"""), Now));
    }
}
