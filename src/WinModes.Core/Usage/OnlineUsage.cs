using System.Globalization;
using System.Text.Json;

namespace WinModes.Core.Usage;

/// <summary>What a usage request needs: the address, and the headers that carry the user's own sign-in.</summary>
public sealed record UsageRequest(Uri Address, IReadOnlyDictionary<string, string> Headers);

/// <summary>
/// The plan usage, asked to each provider with the sign-in of WinModes itself. The token is read when a request is built, sent
/// only to the provider that issued it, and renewed by the app. Nothing is read from the files of Claude Code, Codex or Grok.
/// </summary>
public static class OnlineUsage
{
    private const string CodexTool = "Codex";
    private const string ClaudeTool = "Claude";
    private const string GrokTool = "Grok";
    private const string UserAgent = "WinModes";
    private const int SecondsPerMinute = 60;
    private const int FiveHourMinutes = 300;
    private const int SevenDayMinutes = 10080;

    // The endpoint the open-source Codex client reads its limits from (codex-rs/backend-client).
    private static readonly Uri CodexAddress = new("https://chatgpt.com/backend-api/wham/usage");

    // The endpoint Claude Code reads for its /usage screen. Anthropic does not document it.
    private static readonly Uri ClaudeAddress = new("https://api.anthropic.com/api/oauth/usage");

    // The same endpoint, also asked for the limit resets the account has in reserve and without the spend block.
    private static readonly Uri ClaudeAddressWithResets = new("https://api.anthropic.com/api/oauth/usage?cedar_ember=1");

    // The profile of the account, where Claude Code itself reads the organization type and the rate limit tier of the plan.
    private static readonly Uri ClaudeProfileAddress = new("https://api.anthropic.com/api/oauth/profile");

    // The billing endpoint the Grok CLI reads its weekly credits from. xAI does not document it; OpenCodex and Grok Build read it too.
    private static readonly Uri GrokAddress = new("https://cli-chat-proxy.grok.com/v1/billing?format=credits");

    // Headers the Grok CLI sends with that request: without them the endpoint refuses the token.
    private const string GrokClientVersion = "1.0.46";
    private const string WeeklyPeriod = "USAGE_PERIOD_TYPE_WEEKLY";

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

    /// <summary>A Grok request with the token of WinModes' own sign-in; <paramref name="userId"/> is the subject of its id token.</summary>
    public static UsageRequest GrokRequestFor(string accessToken, string userId)
    {
        ArgumentException.ThrowIfNullOrEmpty(accessToken);
        ArgumentException.ThrowIfNullOrEmpty(userId);
        return new UsageRequest(GrokAddress, new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {accessToken}",
            ["x-xai-token-auth"] = "xai-grok-cli",
            ["x-authenticateresponse"] = "authenticate-response",
            ["x-userid"] = userId,
            ["x-grok-client-version"] = GrokClientVersion,
            ["User-Agent"] = UserAgent,
        });
    }

    /// <summary>
    /// Reads the weekly credits of a SuperGrok account: { config: { creditUsagePercent, currentPeriod: { type, end } } }.
    /// A missing percent is 0 (protobuf leaves out a default value). Null when the answer is not a weekly period.
    /// </summary>
    public static SubscriptionStatus? ParseGrok(string json, string plan, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("config", out var config) || config.ValueKind != JsonValueKind.Object
                || !config.TryGetProperty("currentPeriod", out var period) || period.ValueKind != JsonValueKind.Object
                || Text(period, "type") != WeeklyPeriod)
            {
                return null;
            }

            var used = 0.0;
            if (config.TryGetProperty("creditUsagePercent", out var percent))
            {
                var parsed = percent.ValueKind switch
                {
                    JsonValueKind.Number => UsageNumbers.Percent(percent),
                    JsonValueKind.String when double.TryParse(percent.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var text) && double.IsFinite(text) => text,
                    _ => null,
                };
                if (parsed is not { } value)
                {
                    return null;
                }

                used = value;
            }

            DateTimeOffset? resetsAt = period.TryGetProperty("end", out var end)
                ? end.ValueKind switch
                {
                    JsonValueKind.String when DateTimeOffset.TryParse(end.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) => date,
                    JsonValueKind.Number when end.TryGetDouble(out var seconds) => UsageNumbers.FromUnixSeconds(seconds),
                    _ => null,
                }
                : null;
            return new SubscriptionStatus(GrokTool, plan, new LimitWindow(used, SevenDayMinutes, resetsAt), null, now, Extra: GrokOnDemand(config));
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The request for the profile of a Claude account, which holds the name of its plan.</summary>
    public static UsageRequest ClaudeProfileRequestFor(string accessToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(accessToken);
        return new UsageRequest(ClaudeProfileAddress, new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {accessToken}",
            ["anthropic-beta"] = "oauth-2025-04-20",
            ["User-Agent"] = UserAgent,
        });
    }

    /// <summary>
    /// Usage billed beyond the plan ("on demand"): spent and cap, as { val } amounts in cents. Null when the cap is 0, which is the
    /// case of an account that did not turn it on.
    /// </summary>
    private static ExtraUsage? GrokOnDemand(JsonElement config)
    {
        const decimal CentsPerUnit = 100;

        decimal? Amount(string name)
        {
            if (!config.TryGetProperty(name, out var block) || block.ValueKind != JsonValueKind.Object || !block.TryGetProperty("val", out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetDecimal(out var number) && number >= 0 => number,
                JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var text) && text >= 0 => text,
                _ => null,
            };
        }

        return Amount("onDemandCap") is { } cap && cap > 0 ? new ExtraUsage((Amount("onDemandUsed") ?? 0) / CentsPerUnit, cap / CentsPerUnit, "USD") : null;
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
                : new SubscriptionStatus(ClaudeTool, plan, fiveHour ?? sevenDay, fiveHour is null ? null : sevenDay, now, ClaudeResets(root), ClaudeModelLimits(root), ClaudeExtra(root));
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

    /// <summary>
    /// The weekly limits of single models (Opus, Sonnet, Fable), from the "seven_day_*" blocks and from the scoped entries of the
    /// "limits" list, which is where an account that has such a limit finds it. Only these three names are accepted: the label
    /// comes from the answer, so anything else is ignored rather than shown.
    /// </summary>
    private static List<ModelLimit>? ClaudeModelLimits(JsonElement root)
    {
        var limits = new List<ModelLimit>();
        foreach (var (key, model) in ClaudeModelKeys)
        {
            if (ClaudeWindow(root, key, SevenDayMinutes) is { } window)
            {
                limits.Add(new ModelLimit(model, window));
            }
        }

        if (root.TryGetProperty("limits", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in list.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object && Text(entry, "kind") == "weekly_scoped"
                    && entry.TryGetProperty("percent", out var percent) && percent.ValueKind == JsonValueKind.Number && UsageNumbers.Percent(percent) is { } used
                    && entry.TryGetProperty("scope", out var scope) && scope.ValueKind == JsonValueKind.Object
                    && scope.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.Object
                    && ClaudeModelKeys.Select(known => known.Model).FirstOrDefault(known => (Text(model, "display_name") ?? "").Contains(known, StringComparison.OrdinalIgnoreCase)) is { } name
                    && !limits.Any(limit => limit.Model == name))
                {
                    DateTimeOffset? resetsAt = Text(entry, "resets_at") is { } text
                        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
                    limits.Add(new ModelLimit(name, new LimitWindow(used, SevenDayMinutes, resetsAt)));
                }
            }
        }

        return limits.Count > 0 ? limits : null;
    }

    private static readonly (string Key, string Model)[] ClaudeModelKeys = [("seven_day_opus", "Opus"), ("seven_day_sonnet", "Sonnet"), ("seven_day_fable", "Fable")];

    /// <summary>
    /// The extra usage of the account: spent and monthly limit, given in the smallest unit of the currency (cents for dollars).
    /// Null when the block is missing, the feature is off for the account or there is no limit.
    /// </summary>
    private static ExtraUsage? ClaudeExtra(JsonElement root)
    {
        const int DefaultDecimals = 2;
        const int MaxDecimals = 6;

        if (!root.TryGetProperty("extra_usage", out var block) || block.ValueKind != JsonValueKind.Object
            || !block.TryGetProperty("is_enabled", out var enabled) || enabled.ValueKind != JsonValueKind.True)
        {
            return null;
        }

        decimal? Amount(string name) => block.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) && number >= 0 ? number : null;

        var decimals = block.TryGetProperty("decimal_places", out var places) && places.ValueKind == JsonValueKind.Number && places.TryGetInt32(out var count) ? Math.Clamp(count, 0, MaxDecimals) : DefaultDecimals;
        var scale = (decimal)Math.Pow(10, decimals);
        return Amount("monthly_limit") is { } limit && limit > 0
            ? new ExtraUsage((Amount("used_credits") ?? 0) / scale, limit / scale, Text(block, "currency") is { Length: > 0 } currency ? currency : "USD")
            : null;
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

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
