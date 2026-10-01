using System.Globalization;
using System.Text.Json;

namespace WinModes.Core.Usage;

/// <summary>What a usage request needs: the address, and the headers that carry the user's own sign-in.</summary>
public sealed record UsageRequest(Uri Address, IReadOnlyDictionary<string, string> Headers);

/// <summary>
/// Opt-in online reading of the plan usage, with the sign-in Claude Code and Codex already keep on this PC.
/// The token is read when a request is built, sent only to the provider that issued it, and never stored,
/// logged or refreshed here. An expired sign-in gives no request: the tool itself renews it when it runs.
/// </summary>
public static class OnlineUsage
{
    private const string CodexTool = "Codex";
    private const string ClaudeTool = "Claude";
    private const string UserAgent = "WinModes";
    private const int SecondsPerMinute = 60;
    private const int FiveHourMinutes = 300;
    private const int SevenDayMinutes = 10080;

    // The endpoint the open-source Codex client reads its limits from (codex-rs/backend-client).
    private static readonly Uri CodexAddress = new("https://chatgpt.com/backend-api/wham/usage");

    // The endpoint Claude Code reads for its /usage screen. Anthropic does not document it.
    private static readonly Uri ClaudeAddress = new("https://api.anthropic.com/api/oauth/usage");

    // The same endpoint, also asked for the limit resets the account has in reserve and without the spend block.
    private static readonly Uri ClaudeAddressWithResets = new("https://api.anthropic.com/api/oauth/usage?cedar_ember=1&skip_spend=1");

    public static string DefaultCodexAuth { get; } = Path.Combine(Subscriptions.DefaultCodexHome, "auth.json");

    public static string DefaultClaudeCredentials { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");

    public static UsageRequest? CodexRequest(string authPath)
    {
        using var document = Open(authPath);
        if (document is null || !document.RootElement.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Object
            || Text(tokens, "access_token") is not { Length: > 0 } token)
        {
            return null;
        }

        var headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}", ["User-Agent"] = UserAgent };
        if (Text(tokens, "account_id") is { Length: > 0 } account)
        {
            headers["ChatGPT-Account-Id"] = account;
        }

        return new UsageRequest(CodexAddress, headers);
    }

    public static UsageRequest? ClaudeRequest(string credentialsPath, DateTimeOffset now)
    {
        using var document = Open(credentialsPath);
        if (document is null || !document.RootElement.TryGetProperty("claudeAiOauth", out var oauth) || oauth.ValueKind != JsonValueKind.Object
            || Text(oauth, "accessToken") is not { Length: > 0 } token)
        {
            return null;
        }

        if (oauth.TryGetProperty("expiresAt", out var expires) && expires.ValueKind == JsonValueKind.Number
            && UsageNumbers.FromUnixMilliseconds(expires) is { } expiry && expiry <= now)
        {
            return null;
        }

        return new UsageRequest(ClaudeAddress, new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {token}",
            ["anthropic-beta"] = "oauth-2025-04-20",
            ["User-Agent"] = UserAgent,
        });
    }

    /// <summary>A Codex request with the token of WinModes' own sign-in; the token is sent only to the provider that issued it.</summary>
    public static UsageRequest CodexRequestFor(string accessToken, string? accountId)
    {
        ArgumentException.ThrowIfNullOrEmpty(accessToken);
        var headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {accessToken}", ["User-Agent"] = UserAgent };
        if (!string.IsNullOrEmpty(accountId))
        {
            headers["ChatGPT-Account-Id"] = accountId;
        }

        return new UsageRequest(CodexAddress, headers);
    }

    /// <summary>A Claude request with the token of WinModes' own sign-in.</summary>
    public static UsageRequest ClaudeRequestFor(string accessToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(accessToken);
        return new UsageRequest(ClaudeAddressWithResets, new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {accessToken}",
            ["anthropic-beta"] = "oauth-2025-04-20",
            ["User-Agent"] = UserAgent,
        });
    }

    /// <summary>Reads Codex's answer; null when it is not the expected shape.</summary>
    public static SubscriptionStatus? ParseCodex(string json, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            LimitWindow? primary = null, secondary = null;
            if (root.TryGetProperty("rate_limit", out var limit) && limit.ValueKind == JsonValueKind.Object)
            {
                primary = CodexWindow(limit, "primary_window");
                secondary = CodexWindow(limit, "secondary_window");
            }

            int? resets = root.TryGetProperty("rate_limit_reset_credits", out var credits) && credits.ValueKind == JsonValueKind.Object
                && credits.TryGetProperty("available_count", out var count) && count.ValueKind == JsonValueKind.Number ? count.GetInt32() : null;
            var plan = Text(root, "plan_type");
            return primary is null && secondary is null && plan is null
                ? null
                : new SubscriptionStatus(CodexTool, Subscriptions.CodexPlanName(plan), primary ?? secondary, primary is null ? null : secondary, now, resets);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Reads Claude's answer; the plan name comes from the local settings.</summary>
    public static SubscriptionStatus? ParseClaude(string json, string plan, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var fiveHour = ClaudeWindow(root, "five_hour", FiveHourMinutes);
            var sevenDay = ClaudeWindow(root, "seven_day", SevenDayMinutes);
            return fiveHour is null && sevenDay is null
                ? null
                : new SubscriptionStatus(ClaudeTool, plan, fiveHour ?? sevenDay, fiveHour is null ? null : sevenDay, now, ClaudeResets(root));
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// The limit resets the account has left in the "cedar_ember" block: the sum over the grants that are not paused. Null when
    /// the block is absent, the account is not eligible (the answer depends on who asks) or nothing is left.
    /// </summary>
    private static int? ClaudeResets(JsonElement root)
    {
        if (!root.TryGetProperty("cedar_ember", out var block) || block.ValueKind != JsonValueKind.Object
            || !block.TryGetProperty("eligible", out var eligible) || eligible.ValueKind != JsonValueKind.True
            || !block.TryGetProperty("grants", out var grants) || grants.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var total = 0;
        foreach (var grant in grants.EnumerateArray())
        {
            if (grant.ValueKind == JsonValueKind.Object && !(grant.TryGetProperty("paused", out var paused) && paused.ValueKind == JsonValueKind.True)
                && grant.TryGetProperty("resets_left", out var left) && left.ValueKind == JsonValueKind.Number && left.TryGetInt32(out var count) && count > 0)
            {
                total += count;
            }
        }

        return total > 0 ? total : null;
    }

    private static LimitWindow? CodexWindow(JsonElement limit, string name)
    {
        if (!limit.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object
            || !window.TryGetProperty("used_percent", out var used) || used.ValueKind != JsonValueKind.Number
            || UsageNumbers.Percent(used) is not { } usedPercent)
        {
            return null;
        }

        var minutes = window.TryGetProperty("limit_window_seconds", out var seconds) && seconds.ValueKind == JsonValueKind.Number
            && seconds.TryGetDouble(out var windowSeconds) ? UsageNumbers.Minutes(windowSeconds / SecondsPerMinute) : 0;
        DateTimeOffset? resetsAt = window.TryGetProperty("reset_at", out var reset) && reset.ValueKind == JsonValueKind.Number
            && reset.TryGetDouble(out var unix) ? UsageNumbers.FromUnixSeconds(unix) : null;
        return new LimitWindow(usedPercent, minutes, resetsAt);
    }

    private static LimitWindow? ClaudeWindow(JsonElement root, string name, int minutes)
    {
        if (!root.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object
            || !window.TryGetProperty("utilization", out var used) || used.ValueKind != JsonValueKind.Number
            || UsageNumbers.Percent(used) is not { } usedPercent)
        {
            return null;
        }

        DateTimeOffset? resetsAt = Text(window, "resets_at") is { } text
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
        return new LimitWindow(usedPercent, minutes, resetsAt);
    }

    /// <summary>Whether a Claude sign-in exists and is still valid. Only its state and expiry are returned, never the token.</summary>
    public static SignInCheck InspectClaudeSignIn(string credentialsPath, DateTimeOffset now)
    {
        using var document = Open(credentialsPath);
        if (document is null)
        {
            return new SignInCheck(File.Exists(credentialsPath) ? SignInState.Unusable : SignInState.Missing);
        }

        if (!document.RootElement.TryGetProperty("claudeAiOauth", out var oauth) || oauth.ValueKind != JsonValueKind.Object
            || Text(oauth, "accessToken") is not { Length: > 0 })
        {
            return new SignInCheck(SignInState.Unusable);
        }

        var expiry = oauth.TryGetProperty("expiresAt", out var expires) && expires.ValueKind == JsonValueKind.Number
            ? UsageNumbers.FromUnixMilliseconds(expires)
            : null;
        return expiry is { } at && at <= now ? new SignInCheck(SignInState.Expired, at) : new SignInCheck(SignInState.Valid, expiry);
    }

    /// <summary>Whether a Codex sign-in exists. Codex gives no expiry here: a stale token shows as a failed request.</summary>
    public static SignInCheck InspectCodexSignIn(string authPath)
    {
        if (!File.Exists(authPath))
        {
            return new SignInCheck(SignInState.Missing);
        }

        return CodexRequest(authPath) is null ? new SignInCheck(SignInState.Unusable) : new SignInCheck(SignInState.Valid);
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static JsonDocument? Open(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return document;
            }

            document.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
