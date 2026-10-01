using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WinModes.Core.Localization;

namespace WinModes.Core.Usage;

/// <summary>One usage limit of a plan: how much of the window is used and when it starts over.</summary>
public sealed record LimitWindow(double UsedPercent, int WindowMinutes, DateTimeOffset? ResetsAt)
{
    private const int MinutesPerHour = 60;
    private const int MinutesPerDay = 1440;
    private const int MinutesPerWeek = 10080;

    public double RemainingPercent => Math.Clamp(100 - UsedPercent, 0, 100);

    /// <summary>"5 h", "weekly", "3 d": how the window is called next to its limit.</summary>
    public string WindowName => WindowMinutes switch
    {
        MinutesPerWeek => Loc.T("weekly"),
        >= MinutesPerDay when WindowMinutes % MinutesPerDay == 0 => Loc.F("{0} d", WindowMinutes / MinutesPerDay),
        >= MinutesPerHour => $"{WindowMinutes / MinutesPerHour} h",
        _ => $"{WindowMinutes} min",
    };

    /// <summary>The limit started over after this figure was recorded, so the figure no longer holds.</summary>
    public bool HasReset(DateTimeOffset now) => ResetsAt is { } reset && reset <= now;
}

/// <summary>What is known locally about the user's plan for one AI tool.</summary>
/// <param name="SeenAt">When the tool last recorded the limits; null when it records none.</param>
/// <param name="ResetCredits">Limit resets the account has in reserve; known only from the online reading.</param>
public sealed record SubscriptionStatus(string Tool, string Plan, LimitWindow? Primary, LimitWindow? Secondary, DateTimeOffset? SeenAt, int? ResetCredits = null);

/// <summary>
/// Reads the plan and the usage limits that Codex and Claude Code already keep on this PC. Nothing is asked
/// online and no credential file is opened: Codex writes its limits in its session logs, Claude Code keeps the
/// plan (not the usage) in its settings file.
/// </summary>
public static partial class Subscriptions
{
    private const string CodexTool = "Codex";
    private const string ClaudeTool = "Claude";
    private const string RateLimitsKey = "\"rate_limits\"";
    private const int RecentFiles = 12;
    private const int TailBytes = 512 * 1024;
    private static readonly string[] CodexSessionFolders = ["sessions", "archived_sessions"];

    private static readonly Dictionary<string, string> CodexPlans = new(StringComparer.OrdinalIgnoreCase)
    {
        ["free"] = "Free", ["go"] = "Go", ["plus"] = "Plus", ["pro"] = "Pro", ["prolite"] = "Pro Lite",
        ["team"] = "Team", ["business"] = "Business", ["enterprise"] = "Enterprise", ["edu"] = "Edu",
    };

    private static readonly Dictionary<string, string> ClaudePlans = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude_pro"] = "Pro", ["claude_max"] = "Max", ["claude_team"] = "Team", ["claude_enterprise"] = "Enterprise",
    };

    public static string DefaultCodexHome { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

    public static string DefaultClaudeSettings { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");

    /// <summary>The most recent limits Codex recorded, or null when it never recorded any.</summary>
    public static SubscriptionStatus? ReadCodex(string codexHome)
    {
        try
        {
            return CodexSessionFolders
                .Select(folder => new DirectoryInfo(Path.Combine(codexHome, folder)))
                .Where(folder => folder.Exists)
                .SelectMany(folder => folder.EnumerateFiles("*.jsonl", SearchOption.AllDirectories))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(RecentFiles)
                .Select(file => LastRecord(file.FullName))
                .Where(status => status is not null)
                .MaxBy(status => status!.SeenAt);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Reads one line of a Codex session log; null when it carries no usable limits.</summary>
    public static SubscriptionStatus? ParseCodexRecord(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object
                || !payload.TryGetProperty("rate_limits", out var limits) || limits.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            DateTimeOffset? seenAt = root.TryGetProperty("timestamp", out var stamp) && stamp.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(stamp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;
            var plan = limits.TryGetProperty("plan_type", out var planType) && planType.ValueKind == JsonValueKind.String ? planType.GetString() : null;
            var primary = Window(limits, "primary", seenAt);
            var secondary = Window(limits, "secondary", seenAt);
            return primary is null && secondary is null && plan is null
                ? null
                : new SubscriptionStatus(CodexTool, CodexPlanName(plan), primary ?? secondary, primary is null ? null : secondary, seenAt);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>The Claude plan from Claude Code's settings file; null when no subscription is recorded.</summary>
    /// <summary>
    /// The Claude plan with the limits recorded by the WinModes status line, when it is installed in Claude Code.
    /// </summary>
    public static SubscriptionStatus? ReadClaude(string settingsPath, string limitsRecordPath)
    {
        var plan = ReadClaude(settingsPath);
        var limits = ClaudeStatusLine.Load(limitsRecordPath);
        if (limits is null)
        {
            return plan;
        }

        return new SubscriptionStatus(ClaudeTool, plan?.Plan ?? Loc.T("Plan unknown"),
            limits.FiveHour ?? limits.SevenDay, limits.FiveHour is null ? null : limits.SevenDay, limits.SeenAt);
    }

    public static SubscriptionStatus? ReadClaude(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath))
            {
                return null;
            }

            using var stream = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            return ParseClaude(document.RootElement);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static SubscriptionStatus? ParseClaude(JsonElement settings)
    {
        if (settings.ValueKind != JsonValueKind.Object || !settings.TryGetProperty("oauthAccount", out var account) || account.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? Text(string name) => account.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        var type = Text("organizationType");
        var tier = Text("userRateLimitTier") is { Length: > 0 } own ? own : Text("organizationRateLimitTier");
        if (string.IsNullOrEmpty(type) || !ClaudePlans.TryGetValue(type, out var plan))
        {
            return null;
        }

        // "default_claude_max_5x" -> "Max 5x".
        var multiplier = TierMultiplier().Match(tier ?? "");
        return new SubscriptionStatus(ClaudeTool, multiplier.Success ? $"{plan} {multiplier.Groups[1].Value}" : plan, null, null, null);
    }

    /// <summary>
    /// Short text for one status: the headline ("34 % left"), a line that says when the limit resets and how
    /// old the figure is, and the share left for a bar (null when it is not known).
    /// </summary>
    public static (string Value, string Detail, double? RemainingPercent) Describe(SubscriptionStatus status, DateTimeOffset now, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (status.Primary is not { } primary)
        {
            return ("", Loc.T("Usage is not stored on this PC"), null);
        }

        string Local(DateTimeOffset moment) => moment.ToOffset(now.Offset).ToString("d MMM HH:mm", culture);

        var hasReset = primary.HasReset(now);
        var detail = Capitalize(hasReset ? Loc.F("{0} limit reset on {1}; no use recorded since", primary.WindowName, Local(primary.ResetsAt!.Value))
            : primary.ResetsAt is { } reset ? Loc.F("{0} limit resets in {1} ({2})", primary.WindowName, Span(reset - now), Local(reset))
            : Loc.F("{0} limit", primary.WindowName));
        if (status.Secondary is { } secondary && !secondary.HasReset(now))
        {
            detail += "\n" + Capitalize(Loc.In(culture, "{0} limit: {1:0} % left", secondary.WindowName, secondary.RemainingPercent))
                + (secondary.ResetsAt is { } second ? Loc.F(", resets in {0}", Span(second - now)) : "");
        }

        if (status.ResetCredits is { } credits)
        {
            detail += "\n" + (credits <= 0 ? Loc.T("No limit reset in reserve") : Loc.N(credits, "1 limit reset in reserve", "{0} limit resets in reserve"));
        }

        if (status.SeenAt is { } seen && now - seen > StaleAfter)
        {
            detail += "\n" + Loc.F("As of {0}", Local(seen));
        }

        // A figure from before the reset is not shown as if it still held.
        return hasReset ? (Loc.T("reset"), detail, null) : (Loc.In(culture, "{0:0} % left", primary.RemainingPercent), detail, primary.RemainingPercent);
    }

    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(1);

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string Span(TimeSpan span) =>
        span.TotalDays >= 1 ? Loc.F("{0} d {1} h", (int)span.TotalDays, span.Hours)
        : span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes} min"
        : $"{Math.Max((int)span.TotalMinutes, 1)} min";

    public static string CodexPlanName(string? raw) => PlanName(CodexPlans, raw);

    private static string PlanName(Dictionary<string, string> known, string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? Loc.T("Plan unknown")
        : known.TryGetValue(raw, out var name) ? name
        : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(raw.Replace('_', ' '));

    private static LimitWindow? Window(JsonElement limits, string name, DateTimeOffset? seenAt)
    {
        if (!limits.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object
            || !window.TryGetProperty("used_percent", out var used) || used.ValueKind != JsonValueKind.Number
            || UsageNumbers.Percent(used) is not { } usedPercent)
        {
            return null;
        }

        double? Number(string property) => window.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;

        // Newer logs give the reset as a Unix time, older ones as seconds counted from the record.
        DateTimeOffset? resetsAt = Number("resets_at") is { } unix ? UsageNumbers.FromUnixSeconds(unix)
            : Number("resets_in_seconds") is { } seconds && seenAt is { } seen ? UsageNumbers.AddSeconds(seen, seconds)
            : null;
        return new LimitWindow(usedPercent, UsageNumbers.Minutes(Number("window_minutes") ?? 0), resetsAt);
    }

    /// <summary>The last limits written in a session log. Only the end of the file is read: logs grow to many megabytes.</summary>
    private static SubscriptionStatus? LastRecord(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = (int)Math.Min(stream.Length, TailBytes);
            stream.Seek(-length, SeekOrigin.End);
            var buffer = new byte[length];
            stream.ReadExactly(buffer);

            // The first line of the tail may be cut: it then fails to parse and is skipped.
            return Encoding.UTF8.GetString(buffer)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Reverse()
                .Where(line => line.Contains(RateLimitsKey, StringComparison.Ordinal))
                .Select(ParseCodexRecord)
                .FirstOrDefault(status => status is not null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"_(\d+x)$")]
    private static partial Regex TierMultiplier();
}
