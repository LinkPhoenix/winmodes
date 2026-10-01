using WinModes.Core.Usage;

namespace WinModes.Core.Tests;

public sealed class ProviderCheckTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"winmodes-check-{Guid.NewGuid():N}");

    public ProviderCheckTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Path_(string name) => Path.Combine(_directory, name);

    private string Write(string name, string content)
    {
        var path = Path_(name);
        File.WriteAllText(path, content);
        return path;
    }

    private IReadOnlyList<CheckItem> Claude(bool online = false) =>
        ProviderCheck.Claude(Path_("claude.json"), Path_("settings.json"), Path_("limits.json"), Path_("credentials.json"), Path_("call.json"), online, Now);

    [Theory]
    [InlineData(true, CheckState.Ok)]
    [InlineData(false, CheckState.Warning)]
    public void Claude_ListsTheWinModesAccountBeforeTheOtherSignIn(bool signedIn, CheckState expected)
    {
        var items = ProviderCheck.Claude(Path_("claude.json"), Path_("settings.json"), Path_("limits.json"), Path_("credentials.json"), Path_("call.json"), false, Now, signedIn);

        Assert.Equal(["Plan", "Status line", "Status line calls", "Usage record", "WinModes account", "Online reading"], items.Select(item => item.Label));
        Assert.Equal(expected, StateOf(items, "WinModes account"));
    }

    [Fact]
    public void Codex_OmitsTheAccountItemWhenTheCallerDoesNotKnowIt()
    {
        var items = ProviderCheck.Codex(Path_("no-codex"), Path_("auth.json"), false, Now);

        Assert.DoesNotContain(items, item => item.Label == "WinModes account");
        Assert.Contains(ProviderCheck.Codex(Path_("no-codex"), Path_("auth.json"), false, Now, ownSession: true), item => item.Label == "WinModes account");
    }

    private static CheckState StateOf(IReadOnlyList<CheckItem> items, string label) => items.Single(item => item.Label == label).State;

    [Fact]
    public void Claude_WithNothingOnThePc_FailsOnThePlanAndWarnsOnTheRest()
    {
        var items = Claude();

        Assert.Equal(["Plan", "Status line", "Status line calls", "Usage record", "Online reading"], items.Select(item => item.Label));
        Assert.Equal(CheckState.Failed, StateOf(items, "Plan"));
        Assert.Equal(CheckState.Warning, StateOf(items, "Status line"));
        Assert.Equal(CheckState.Warning, StateOf(items, "Usage record"));
        Assert.Equal(CheckState.Warning, StateOf(items, "Online reading"));
    }

    [Fact]
    public void Claude_FullySetUp_IsOkEverywhere()
    {
        Write("claude.json", """{"oauthAccount":{"organizationType":"claude_max","organizationRateLimitTier":"default_claude_max_5x"}}""");
        Write("settings.json", """{"statusLine":{"type":"command","command":"C:/x/WinModes.StatusLine.exe"}}""");
        ClaudeStatusLine.Save(Path_("limits.json"), new ClaudeLimits(Now.AddMinutes(-5), new LimitWindow(40, 300, Now.AddHours(2)), null));
        ClaudeStatusLine.SaveCall(Path_("call.json"), ClaudeStatusLine.Describe("""{"model":{},"rate_limits":{}}""", Now.AddMinutes(-5)));
        Write("credentials.json", $$$"""{"claudeAiOauth":{"accessToken":"secret","expiresAt":{{{Now.AddHours(3).ToUnixTimeMilliseconds()}}}}}""");

        var items = Claude(online: true);

        Assert.All(items, item => Assert.Equal(CheckState.Ok, item.State));
        Assert.Contains("Max 5x plan found.", items[0].Detail, StringComparison.Ordinal);
        Assert.Contains("Updated 5 min ago.", items[3].Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", string.Join(' ', items.Select(item => item.Detail)), StringComparison.Ordinal);
    }

    [Fact]
    public void Claude_ExpiredSignIn_SaysWhenItExpiredAndStaleRecordIsAWarning()
    {
        Write("credentials.json", $$$"""{"claudeAiOauth":{"accessToken":"t","expiresAt":{{{Now.AddHours(-1).ToUnixTimeMilliseconds()}}}}}""");
        ClaudeStatusLine.Save(Path_("limits.json"), new ClaudeLimits(Now.AddHours(-5), new LimitWindow(40, 300, Now.AddHours(-4)), null));

        var items = Claude();

        Assert.Equal(CheckState.Warning, StateOf(items, "Online reading"));
        Assert.Contains("expired on", items[4].Detail, StringComparison.Ordinal);
        Assert.Equal(CheckState.Warning, StateOf(items, "Usage record"));
        Assert.Contains("Last updated", items[3].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusCall_KeepsTheFieldNamesAndNeverTheValues()
    {
        var call = ClaudeStatusLine.Describe("""{"session_id":"abc-secret","cwd":"C:/private","rate_limits":{"five_hour":{"used_percentage":1}}}""", Now);
        ClaudeStatusLine.SaveCall(Path_("call.json"), call);

        Assert.True(call.HasRateLimits);
        Assert.Equal(["cwd", "rate_limits", "session_id"], call.Fields);
        var stored = File.ReadAllText(Path_("call.json"));
        Assert.DoesNotContain("secret", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("private", stored, StringComparison.Ordinal);
        Assert.Equal(call.At, ClaudeStatusLine.LoadCall(Path_("call.json"))!.At);
        Assert.False(ClaudeStatusLine.Describe("not json", Now).HasRateLimits);
        Assert.Null(ClaudeStatusLine.LoadCall(Path_("missing.json")));
    }

    [Fact]
    public void Claude_ACallWithoutLimits_ExplainsWhyThereIsNoUsage()
    {
        ClaudeStatusLine.SaveCall(Path_("call.json"), ClaudeStatusLine.Describe("""{"model":{}}""", Now.AddMinutes(-2)));

        var item = Claude().Single(item => item.Label == "Status line calls");

        Assert.Equal(CheckState.Warning, item.State);
        Assert.Contains("sent no usage limits", item.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Claude_AnotherStatusLine_IsReportedAsOther()
    {
        Write("settings.json", """{"statusLine":{"type":"command","command":"~/my-line.sh"}}""");

        Assert.Contains("another status line", Claude().Single(item => item.Label == "Status line").Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void SignIn_ReportsTheStateWithoutTheToken()
    {
        Assert.Equal(SignInState.Missing, OnlineUsage.InspectClaudeSignIn(Path_("none.json"), Now).State);
        Assert.Equal(SignInState.Unusable, OnlineUsage.InspectClaudeSignIn(Write("a.json", "not json"), Now).State);
        Assert.Equal(SignInState.Unusable, OnlineUsage.InspectClaudeSignIn(Write("b.json", """{"other":1}"""), Now).State);
        Assert.Equal(SignInState.Valid, OnlineUsage.InspectClaudeSignIn(Write("c.json", """{"claudeAiOauth":{"accessToken":"t"}}"""), Now).State);

        Assert.Equal(SignInState.Missing, OnlineUsage.InspectCodexSignIn(Path_("none.json")).State);
        Assert.Equal(SignInState.Unusable, OnlineUsage.InspectCodexSignIn(Write("d.json", """{"OPENAI_API_KEY":"k","tokens":null}""")).State);
        Assert.Equal(SignInState.Valid, OnlineUsage.InspectCodexSignIn(Write("e.json", """{"tokens":{"access_token":"t"}}""")).State);
    }

    [Fact]
    public void Codex_WithoutItsFolder_Fails()
    {
        var items = ProviderCheck.Codex(Path_("no-codex"), Path_("no-auth.json"), readOnline: false, Now);

        Assert.Equal(CheckState.Failed, StateOf(items, "Codex folder"));
        Assert.Equal(CheckState.Warning, StateOf(items, "Online reading"));
    }

    [Fact]
    public void Codex_WithARecentRecord_IsOk_AndAnOldOneIsAWarning()
    {
        var home = Path_("codex");
        Directory.CreateDirectory(Path.Combine(home, "sessions"));
        var record = "{\"timestamp\":\"" + Now.AddMinutes(-3).ToString("O") + "\",\"payload\":{\"rate_limits\":{\"primary\":{\"used_percent\":10,\"window_minutes\":300,\"resets_at\":" + Now.AddHours(1).ToUnixTimeSeconds() + "},\"plan_type\":\"plus\"}}}";
        File.WriteAllText(Path.Combine(home, "sessions", "a.jsonl"), record);
        Write("auth.json", """{"tokens":{"access_token":"t"}}""");

        var fresh = ProviderCheck.Codex(home, Path_("auth.json"), readOnline: true, Now);

        Assert.All(fresh, item => Assert.Equal(CheckState.Ok, item.State));
        Assert.Contains("Plus plan found.", fresh.Single(item => item.Label == "Plan").Detail, StringComparison.Ordinal);

        var later = ProviderCheck.Codex(home, Path_("auth.json"), readOnline: true, Now.AddDays(2));

        Assert.Equal(CheckState.Warning, StateOf(later, "Usage record"));
    }
}
