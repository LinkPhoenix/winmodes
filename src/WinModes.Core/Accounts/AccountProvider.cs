using System.Globalization;
using System.Text;
using System.Text.Json;

namespace WinModes.Core.Accounts;

/// <summary>What a sign-in gives: the short-lived token, the one that renews it, and when the first one stops working.</summary>
public sealed record OAuthTokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, string? AccountId, string? Email);

/// <summary>A request to a token endpoint: where, how the body is encoded, and the body.</summary>
public sealed record TokenRequest(Uri Address, string ContentType, string Body);

/// <summary>
/// The sign-in of one provider, as its own command-line tool does it: browser, PKCE, a redirect to this PC. WinModes asks
/// for its own sign-in so it has its own tokens to renew; it never touches the ones Claude Code or Codex keep.
/// The client ids are the public ones of those tools: the provider shows them as the tool when asking for consent.
/// </summary>
public sealed class AccountProvider
{
    private const string JsonContent = "application/json";
    private const string FormContent = "application/x-www-form-urlencoded";
    private const int DefaultLifetimeSeconds = 3600;

    // Public identifier of the Claude Code OAuth client (not a secret), base64 so a text search does not mistake it for a key.
    private const string ClaudeClientId = "OWQxYzI1MGEtZTYxYi00NGQ5LTg4ZWQtNTk0NGQxOTYyZjVl";
    private const string ChatGptClientId = "app_EMoamEEZ73f0CkXaXp7hrann";

    private readonly string _clientId;
    private readonly Uri _authorizeAddress;
    private readonly Uri _tokenAddress;
    private readonly string _scope;
    private readonly bool _jsonTokenRequests;

    private AccountProvider(string tool, string displayName, string clientId, Uri authorizeAddress, Uri tokenAddress, string scope, int callbackPort, string callbackPath, bool jsonTokenRequests)
    {
        (Tool, DisplayName, _clientId, _authorizeAddress, _tokenAddress, _scope, CallbackPort, CallbackPath, _jsonTokenRequests) =
            (tool, displayName, clientId, authorizeAddress, tokenAddress, scope, callbackPort, callbackPath, jsonTokenRequests);
    }

    /// <summary>Only the permission to read the profile and the usage: no inference, no API keys.</summary>
    public static AccountProvider Claude { get; } = new("Claude", "Claude",
        Encoding.UTF8.GetString(Convert.FromBase64String(ClaudeClientId)),
        new Uri("https://claude.ai/oauth/authorize"), new Uri("https://api.anthropic.com/v1/oauth/token"),
        "user:profile", 54545, "/callback", jsonTokenRequests: true);

    public static AccountProvider ChatGpt { get; } = new("Codex", "ChatGPT", ChatGptClientId,
        new Uri("https://auth.openai.com/oauth/authorize"), new Uri("https://auth.openai.com/oauth/token"),
        "openid profile email offline_access", 1455, "/auth/callback", jsonTokenRequests: false);

    public static IReadOnlyList<AccountProvider> All { get; } = [Claude, ChatGpt];

    /// <summary>The tool name the usage reading uses for this account ("Claude" or "Codex").</summary>
    public string Tool { get; }

    public string DisplayName { get; }

    public int CallbackPort { get; }

    public string CallbackPath { get; }

    /// <summary>Where the browser is sent back; the port is on this PC only.</summary>
    public string RedirectUri => string.Create(CultureInfo.InvariantCulture, $"http://localhost:{CallbackPort}{CallbackPath}");

    public Uri BuildAuthorizeUrl(string challenge, string state)
    {
        var parameters = new List<(string, string)>
        {
            ("response_type", "code"),
            ("client_id", _clientId),
            ("redirect_uri", RedirectUri),
            ("scope", _scope),
            ("code_challenge", challenge),
            ("code_challenge_method", "S256"),
            ("state", state),
        };
        if (_jsonTokenRequests)
        {
            // Claude shows the code in the browser too, in case the redirect cannot reach this PC.
            parameters.Insert(0, ("code", "true"));
        }
        else
        {
            parameters.Add(("id_token_add_organizations", "true"));
            parameters.Add(("codex_cli_simplified_flow", "true"));
        }

        return new Uri($"{_authorizeAddress}?{string.Join("&", parameters.Select(pair => $"{pair.Item1}={Uri.EscapeDataString(pair.Item2)}"))}");
    }

    public TokenRequest ExchangeRequest(string code, string state, string verifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        ArgumentException.ThrowIfNullOrEmpty(verifier);
        var fields = new List<(string, string)>
        {
            ("grant_type", "authorization_code"),
            ("client_id", _clientId),
            ("code", code),
            ("redirect_uri", RedirectUri),
            ("code_verifier", verifier),
        };
        if (_jsonTokenRequests)
        {
            fields.Insert(3, ("state", state));
        }

        return Encode(fields);
    }

    public TokenRequest RefreshRequest(string refreshToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);
        return Encode([("grant_type", "refresh_token"), ("client_id", _clientId), ("refresh_token", refreshToken)]);
    }

    /// <summary>Reads a token endpoint's answer; null when it holds no access token. A renewal that omits the refresh token keeps the old one.</summary>
    public OAuthTokens? ParseTokens(string json, DateTimeOffset now, string? previousRefreshToken = null)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Text(root, "access_token") is not { Length: > 0 } access)
            {
                return null;
            }

            var refresh = Text(root, "refresh_token") is { Length: > 0 } renewed ? renewed : previousRefreshToken ?? "";
            // A missing, negative or absurd lifetime counts as one hour, so a bad answer never makes a token look eternal.
            var lifetime = root.TryGetProperty("expires_in", out var seconds) && seconds.ValueKind == JsonValueKind.Number && seconds.TryGetDouble(out var value)
                && value is >= 0 and <= 31_536_000 ? value : DefaultLifetimeSeconds;
            string? accountId = null, email = null;
            if (_jsonTokenRequests)
            {
                if (root.TryGetProperty("account", out var account) && account.ValueKind == JsonValueKind.Object)
                {
                    accountId = Text(account, "uuid");
                    email = Text(account, "email_address");
                }
            }
            else
            {
                accountId = ChatGptAccountId(Text(root, "id_token"), access);
                email = JwtClaim(Text(root, "id_token"), "email");
            }

            return new OAuthTokens(access, refresh, now.AddSeconds(lifetime), accountId, email);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The ChatGPT account id, from the claims of the id token or the access token (decoded here, never trusted as proof).</summary>
    public static string? ChatGptAccountId(string? idToken, string? accessToken)
    {
        foreach (var token in new[] { idToken, accessToken })
        {
            if (JwtPayload(token) is not { } payload)
            {
                continue;
            }

            using (payload)
            {
                var root = payload.RootElement;
                if (Text(root, "chatgpt_account_id") is { Length: > 0 } direct)
                {
                    return direct;
                }

                if (root.TryGetProperty("https://api.openai.com/auth", out var auth) && auth.ValueKind == JsonValueKind.Object
                    && Text(auth, "chatgpt_account_id") is { Length: > 0 } nested)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private TokenRequest Encode(IReadOnlyList<(string Name, string Value)> fields)
    {
        if (_jsonTokenRequests)
        {
            return new TokenRequest(_tokenAddress, JsonContent, JsonSerializer.Serialize(fields.ToDictionary(field => field.Name, field => field.Value)));
        }

        return new TokenRequest(_tokenAddress, FormContent, string.Join("&", fields.Select(field => $"{field.Name}={Uri.EscapeDataString(field.Value)}")));
    }

    private static string? JwtClaim(string? token, string name)
    {
        if (JwtPayload(token) is not { } payload)
        {
            return null;
        }

        using (payload)
        {
            return Text(payload.RootElement, name);
        }
    }

    private static JsonDocument? JwtPayload(string? token)
    {
        var parts = token?.Split('.');
        if (parts is not { Length: 3 } || parts[1].Length == 0)
        {
            return null;
        }

        try
        {
            var base64 = parts[1].Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            var document = JsonDocument.Parse(Convert.FromBase64String(base64));
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return document;
            }

            document.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
