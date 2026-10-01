using System.Text;
using System.Text.Json;
using WinModes.Core.Accounts;
using WinModes.Core.Usage;

namespace WinModes.Core.Tests;

public sealed class AccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    // The example of RFC 7636, appendix B.
    [Fact]
    public void Pkce_ChallengeMatchesTheRfcExample() =>
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", Pkce.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));

    [Fact]
    public void Pkce_SecretsAreRandomAndUrlSafe()
    {
        var first = Pkce.NewVerifier();

        Assert.NotEqual(first, Pkce.NewVerifier());
        Assert.Equal(43, first.Length);
        Assert.DoesNotContain('=', first);
        Assert.NotEqual(Pkce.NewState(), Pkce.NewState());
    }

    [Fact]
    public void Claude_AuthorizeUrlAsksOnlyForTheProfile()
    {
        var url = AccountProvider.Claude.BuildAuthorizeUrl("challenge", "state-1").ToString();

        Assert.StartsWith("https://claude.ai/oauth/authorize?", url);
        Assert.Contains("scope=user%3Aprofile", url);
        Assert.DoesNotContain("user%3Ainference", url);
        Assert.Contains("code_challenge=challenge", url);
        Assert.Contains("code_challenge_method=S256", url);
        Assert.Contains("state=state-1", url);
        Assert.Contains("redirect_uri=http%3A%2F%2Flocalhost%3A54545%2Fcallback", url);
    }

    [Fact]
    public void ChatGpt_AuthorizeUrlAndRedirect()
    {
        var url = AccountProvider.ChatGpt.BuildAuthorizeUrl("c", "s").ToString();

        Assert.StartsWith("https://auth.openai.com/oauth/authorize?", url);
        Assert.Contains("client_id=app_EMoamEEZ73f0CkXaXp7hrann", url);
        Assert.Equal("http://localhost:1455/auth/callback", AccountProvider.ChatGpt.RedirectUri);
    }

    [Fact]
    public void Claude_TokenRequestsAreJson()
    {
        var exchange = AccountProvider.Claude.ExchangeRequest("the-code", "the-state", "the-verifier");
        var refresh = AccountProvider.Claude.RefreshRequest("old-refresh");

        Assert.Equal("application/json", exchange.ContentType);
        using var body = JsonDocument.Parse(exchange.Body);
        Assert.Equal("authorization_code", body.RootElement.GetProperty("grant_type").GetString());
        Assert.Equal("the-code", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("the-state", body.RootElement.GetProperty("state").GetString());
        Assert.Equal("the-verifier", body.RootElement.GetProperty("code_verifier").GetString());
        using var renewal = JsonDocument.Parse(refresh.Body);
        Assert.Equal("refresh_token", renewal.RootElement.GetProperty("grant_type").GetString());
        Assert.Equal("old-refresh", renewal.RootElement.GetProperty("refresh_token").GetString());
    }

    [Fact]
    public void ChatGpt_TokenRequestsAreForms()
    {
        var exchange = AccountProvider.ChatGpt.ExchangeRequest("a b&c", "ignored", "verifier");

        Assert.Equal("application/x-www-form-urlencoded", exchange.ContentType);
        Assert.Contains("code=a%20b%26c", exchange.Body);
        Assert.Contains("grant_type=authorization_code", exchange.Body);
        Assert.DoesNotContain("state=", exchange.Body);
        Assert.Contains("refresh_token=r%2Bt", AccountProvider.ChatGpt.RefreshRequest("r+t").Body);
    }

    [Fact]
    public void Claude_ParsesTokensAndTheAccount()
    {
        var tokens = AccountProvider.Claude.ParseTokens(
            """{"access_token":"a","refresh_token":"r","expires_in":28800,"account":{"uuid":"u-1","email_address":"me@example.com"}}""", Now);

        Assert.Equal(new OAuthTokens("a", "r", Now.AddSeconds(28800), "u-1", "me@example.com"), tokens);
    }

    [Fact]
    public void Renewal_KeepsTheOldRefreshTokenWhenNoneIsReturned()
    {
        var tokens = AccountProvider.Claude.ParseTokens("""{"access_token":"new","expires_in":3600}""", Now, "old-refresh");

        Assert.Equal("old-refresh", tokens!.RefreshToken);
    }

    [Theory]
    [InlineData("""{"expires_in":3600}""")]
    [InlineData("""{"access_token":""}""")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void ParseTokens_RefusesWhatHoldsNoAccessToken(string json) => Assert.Null(AccountProvider.Claude.ParseTokens(json, Now));

    [Theory]
    [InlineData("-5")]
    [InlineData("99999999999")]
    [InlineData("\"soon\"")]
    public void ParseTokens_ADoubtfulLifetimeCountsAsOneHour(string lifetime)
    {
        var tokens = AccountProvider.Claude.ParseTokens($$"""{"access_token":"a","refresh_token":"r","expires_in":{{lifetime}}}""", Now);

        Assert.Equal(Now.AddHours(1), tokens!.ExpiresAt);
    }

    [Fact]
    public void ChatGpt_TakesTheAccountAndEmailFromTheIdToken()
    {
        var idToken = Jwt("""{"email":"me@example.com","https://api.openai.com/auth":{"chatgpt_account_id":"acc-9"}}""");

        var tokens = AccountProvider.ChatGpt.ParseTokens($$"""{"access_token":"a","refresh_token":"r","expires_in":600,"id_token":"{{idToken}}"}""", Now);

        Assert.Equal("acc-9", tokens!.AccountId);
        Assert.Equal("me@example.com", tokens.Email);
    }

    [Fact]
    public void ChatGptAccountId_AcceptsTheTopLevelClaimAndRefusesGarbage()
    {
        Assert.Equal("top", AccountProvider.ChatGptAccountId(null, Jwt("""{"chatgpt_account_id":"top"}""")));
        Assert.Null(AccountProvider.ChatGptAccountId("a.b.c", "not-a-token"));
        Assert.Null(AccountProvider.ChatGptAccountId(null, null));
    }

    [Fact]
    public void Vault_KeepsTokensEncryptedForTheUser()
    {
        var path = Path.Combine(Path.GetTempPath(), $"winmodes-vault-{Guid.NewGuid():N}.dat");
        try
        {
            var vault = new AccountVault(path);
            var tokens = new OAuthTokens("secret-access", "secret-refresh", Now, "id", "me@example.com");

            vault.Set("Claude", tokens);

            var onDisk = File.ReadAllText(path);
            Assert.DoesNotContain("secret", onDisk);
            Assert.DoesNotContain("secret", Encoding.UTF8.GetString(Convert.FromBase64String(onDisk)));
            Assert.Equal(tokens, new AccountVault(path).Get("Claude"));
            Assert.Null(vault.Get("Codex"));

            vault.Remove("Claude");
            Assert.Null(new AccountVault(path).Get("Claude"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Vault_ADamagedFileCountsAsSignedOut()
    {
        var path = Path.Combine(Path.GetTempPath(), $"winmodes-vault-{Guid.NewGuid():N}.dat");
        try
        {
            File.WriteAllText(path, "this is not an encrypted vault");

            Assert.Null(new AccountVault(path).Get("Claude"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SignedInRequests_CarryTheTokenAndTheAccount()
    {
        var codex = OnlineUsage.CodexRequestFor("tok", "acc");
        var claude = OnlineUsage.ClaudeRequestFor("tok");

        Assert.Equal("Bearer tok", codex.Headers["Authorization"]);
        Assert.Equal("acc", codex.Headers["ChatGPT-Account-Id"]);
        Assert.Equal("Bearer tok", claude.Headers["Authorization"]);
        Assert.Contains("cedar_ember=1", claude.Address.Query);
        Assert.False(OnlineUsage.CodexRequestFor("tok", null).Headers.ContainsKey("ChatGPT-Account-Id"));
    }

    [Fact]
    public void Claude_ResetsInReserveAreTheGrantsThatAreNotPaused()
    {
        const string Json = """
            {"five_hour":{"utilization":10,"resets_at":"2026-10-02T15:00:00Z"},
             "cedar_ember":{"eligible":true,"grants":[
               {"id":"a","resets_total":3,"resets_left":2},
               {"id":"b","resets_total":2,"resets_left":1,"paused":true},
               {"id":"c","resets_total":1,"resets_left":1,"paused":false}]}}
            """;

        Assert.Equal(3, OnlineUsage.ParseClaude(Json, "Max", Now)!.ResetCredits);
    }

    [Theory]
    [InlineData("""{"five_hour":{"utilization":10},"cedar_ember":{"eligible":false,"ineligible_reason":"surface","grants":[{"id":"a","resets_total":1,"resets_left":1}]}}""")]
    [InlineData("""{"five_hour":{"utilization":10},"cedar_ember":{"eligible":true,"grants":[]}}""")]
    [InlineData("""{"five_hour":{"utilization":10}}""")]
    public void Claude_NoResetsWhenNotEligibleOrNoneLeft(string json) => Assert.Null(OnlineUsage.ParseClaude(json, "Max", Now)!.ResetCredits);

    private static string Jwt(string payload) =>
        "e30." + Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".sig";
}

public sealed class CallbackListenerTests
{
    private static readonly HttpClient Browser = new();

    private static int FreePort()
    {
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    [Fact]
    public async Task Redirect_WithTheExpectedState_GivesTheCode()
    {
        var port = FreePort();
        using var listener = new CallbackListener(port, "/auth/callback");
        var waiting = listener.WaitAsync("good-state", TimeSpan.FromSeconds(10), CancellationToken.None);

        using var response = await Browser.GetAsync(new Uri($"http://localhost:{port}/auth/callback?code=the-code&state=good-state"));

        var result = await waiting;
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("the-code", result!.Code);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ForgedOrStrayRequests_AreIgnoredUntilTheRealRedirect()
    {
        var port = FreePort();
        using var listener = new CallbackListener(port, "/callback");
        var waiting = listener.WaitAsync("good-state", TimeSpan.FromSeconds(10), CancellationToken.None);

        using var wrongState = await Browser.GetAsync(new Uri($"http://localhost:{port}/callback?code=evil&state=other"));
        using var wrongPath = await Browser.GetAsync(new Uri($"http://localhost:{port}/favicon.ico"));
        using var real = await Browser.GetAsync(new Uri($"http://localhost:{port}/callback?code=real%23tail&state=good-state"));

        Assert.Equal(System.Net.HttpStatusCode.NotFound, wrongState.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, wrongPath.StatusCode);
        Assert.Equal("real", (await waiting)!.Code);
    }

    [Fact]
    public async Task Refusal_IsReportedWithoutCode()
    {
        var port = FreePort();
        using var listener = new CallbackListener(port, "/callback");
        var waiting = listener.WaitAsync("s", TimeSpan.FromSeconds(10), CancellationToken.None);

        using var response = await Browser.GetAsync(new Uri($"http://localhost:{port}/callback?error=access_denied&state=s"));

        var result = await waiting;
        Assert.False(result!.Succeeded);
        Assert.Equal("access_denied", result.Error);
    }

    [Fact]
    public async Task NoRedirect_TimesOut()
    {
        using var listener = new CallbackListener(FreePort(), "/callback");

        Assert.Null(await listener.WaitAsync("s", TimeSpan.FromMilliseconds(200), CancellationToken.None));
    }

    [Fact]
    public async Task Cancelling_StopsTheWait()
    {
        using var listener = new CallbackListener(FreePort(), "/callback");
        using var source = new CancellationTokenSource();
        var waiting = listener.WaitAsync("s", TimeSpan.FromSeconds(30), source.Token);

        await source.CancelAsync();

        Assert.Null(await waiting);
    }
}
