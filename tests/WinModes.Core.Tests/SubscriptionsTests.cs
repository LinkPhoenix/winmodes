using System.Globalization;
using System.Text.Json;
using WinModes.Core.Usage;

namespace WinModes.Core.Tests;

public sealed class SubscriptionsTests
{
    private const string CodexRecord =
        """{"timestamp":"2026-09-25T09:18:35.034Z","type":"event_msg","payload":{"type":"token_count","info":null,"rate_limits":{"limit_id":"codex","primary":{"used_percent":75.0,"window_minutes":10080,"resets_at":1790719307},"secondary":null,"credits":{"has_credits":false,"unlimited":false,"balance":"0"},"plan_type":"prolite"}}}""";

    private static readonly DateTimeOffset Reset = DateTimeOffset.FromUnixTimeSeconds(1790719307);

    [Fact]
    public void CodexRecord_GivesPlanUsageAndReset()
    {
        var status = Subscriptions.ParseCodexRecord(CodexRecord);

        Assert.NotNull(status);
        Assert.Equal("Pro Lite", status.Plan);
        Assert.Equal(new LimitWindow(75, 10080, Reset), status.Primary);
        Assert.Null(status.Secondary);
        Assert.Equal(new DateTimeOffset(2026, 9, 25, 9, 18, 35, 34, TimeSpan.Zero), status.SeenAt);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"timestamp":"2026-09-25T09:18:35Z","payload":{"type":"agent_message","message":"the \"rate_limits\" field"}}""")]
    [InlineData("""{"payload":{"rate_limits":null}}""")]
    public void CodexRecord_WithoutLimitsIsIgnored(string line) => Assert.Null(Subscriptions.ParseCodexRecord(line));

    [Fact]
    public void OlderCodexRecord_CountsTheResetFromTheRecord()
    {
        var status = Subscriptions.ParseCodexRecord(
            """{"timestamp":"2026-01-01T10:00:00Z","payload":{"rate_limits":{"primary":{"used_percent":10,"window_minutes":300,"resets_in_seconds":3600},"secondary":{"used_percent":40,"window_minutes":10080}}}}""");

        Assert.NotNull(status);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 11, 0, 0, TimeSpan.Zero), status.Primary!.ResetsAt);
        Assert.Equal("5 h", status.Primary.WindowName);
        Assert.Equal(60, status.Secondary!.RemainingPercent);
    }

    [Fact]
    public void ReadCodex_KeepsTheLatestRecordAcrossSessionFiles()
    {
        var home = Path.Combine(Path.GetTempPath(), $"winmodes-codex-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(home, "sessions", "2026", "09", "20"));
            Directory.CreateDirectory(Path.Combine(home, "archived_sessions"));
            File.WriteAllLines(Path.Combine(home, "sessions", "2026", "09", "20", "old.jsonl"),
                [CodexRecord.Replace("2026-09-25", "2026-09-20", StringComparison.Ordinal).Replace("75.0", "20.0", StringComparison.Ordinal)]);
            File.WriteAllLines(Path.Combine(home, "archived_sessions", "new.jsonl"), ["{\"type\":\"other\"}", CodexRecord, "{\"type\":\"other\"}"]);

            Assert.Equal(75, Subscriptions.ReadCodex(home)!.Primary!.UsedPercent);
            Assert.Null(Subscriptions.ReadCodex(Path.Combine(home, "missing")));
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Theory]
    [InlineData("claude_max", "default_claude_max_5x", "Max 5x")]
    [InlineData("claude_max", "default_claude_max_20x", "Max 20x")]
    [InlineData("claude_pro", "default_claude_ai", "Pro")]
    public void ClaudeSettings_GiveThePlanWithoutUsage(string type, string tier, string expected)
    {
        using var document = JsonDocument.Parse($$"""{"oauthAccount":{"organizationType":"{{type}}","organizationRateLimitTier":"{{tier}}","userRateLimitTier":null} }""");

        var status = Subscriptions.ParseClaude(document.RootElement);

        Assert.NotNull(status);
        Assert.Equal(expected, status.Plan);
        Assert.Null(status.Primary);
        Assert.Equal("Usage is not stored on this PC", Subscriptions.Describe(status, DateTimeOffset.UtcNow, CultureInfo.InvariantCulture).Detail);
    }

    [Fact]
    public void ClaudeSettings_WithoutASubscriptionGiveNothing()
    {
        using var document = JsonDocument.Parse("""{"oauthAccount":{"organizationType":"api"},"other":1}""");

        Assert.Null(Subscriptions.ParseClaude(document.RootElement));
    }

    [Fact]
    public void Describe_SaysWhatIsLeftAndWhenTheLimitResets()
    {
        var status = Subscriptions.ParseCodexRecord(CodexRecord)!;

        var (value, detail, remaining) = Subscriptions.Describe(status, Reset.AddHours(-50).AddMinutes(-5), CultureInfo.InvariantCulture);

        Assert.Equal("25 % left", value);
        Assert.Equal(25, remaining);
        Assert.StartsWith("Weekly limit resets in 2 d 2 h (", detail, StringComparison.Ordinal);
        Assert.Contains("As of 25 Sep 09:18", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_DoesNotShowAFigureFromBeforeTheReset()
    {
        var status = Subscriptions.ParseCodexRecord(CodexRecord)!;

        var (value, detail, remaining) = Subscriptions.Describe(status, Reset.AddHours(3), CultureInfo.InvariantCulture);

        Assert.Equal("reset", value);
        Assert.Null(remaining);
        Assert.Contains("no use recorded since", detail, StringComparison.Ordinal);
    }
}
