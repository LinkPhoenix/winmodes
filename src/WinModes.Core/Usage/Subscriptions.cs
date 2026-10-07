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

/// <summary>The weekly limit that applies to one model only (Claude counts Opus, Sonnet and Fable apart).</summary>
public sealed record ModelLimit(string Model, LimitWindow Window);

/// <summary>Money spent beyond the plan: the amounts are in the currency of the account, for the current month.</summary>
public sealed record ExtraUsage(decimal Used, decimal Limit, string Currency)
{
    /// <summary>The share of the monthly limit already spent, 0 to 100.</summary>
    public double UsedPercent => Limit <= 0 ? 0 : (double)Math.Clamp(Used / Limit * 100, 0, 100);
}

/// <summary>What is known locally about the user's plan for one AI tool.</summary>
/// <param name="SeenAt">When the tool last recorded the limits; null when it records none.</param>
/// <param name="ResetCredits">Limit resets the account has in reserve; known only from the online reading.</param>
/// <param name="ModelLimits">Weekly limits of single models; known only from the online reading.</param>
/// <param name="Extra">Extra usage billed beyond the plan, when the account turned it on; known only from the online reading.</param>
public sealed record SubscriptionStatus(string Tool, string Plan, LimitWindow? Primary, LimitWindow? Secondary, DateTimeOffset? SeenAt, int? ResetCredits = null,
    IReadOnlyList<ModelLimit>? ModelLimits = null, ExtraUsage? Extra = null);

/// <summary>
/// Words for the plan and the usage limits the providers give to the sign-in of WinModes. Nothing is read from the files
/// of Claude Code or Codex: the figures come from the providers' own answers (see <see cref="OnlineUsage"/>).
/// </summary>
public static partial class Subscriptions
{
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

    /// <summary>
    /// The Claude plan from the profile the account gives (<c>/api/oauth/profile</c>): the organization type and the rate limit
    /// tier, the same two fields Claude Code keeps in its own settings. Null when the answer holds no known subscription.
    /// </summary>
    public static string? ClaudePlanFromProfile(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("organization", out var organization)
                || organization.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? Text(string name) => organization.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

            if (Text("organization_type") is not { Length: > 0 } type || !ClaudePlans.TryGetValue(type, out var plan))
            {
                return null;
            }

            // "default_claude_max_5x" -> "Max 5x".
            var multiplier = TierMultiplier().Match(Text("rate_limit_tier") ?? "");
            return multiplier.Success ? $"{plan} {multiplier.Groups[1].Value}" : plan;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
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
            return ("", "", null);
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

    /// <summary>A moment as local clock time, in the offset of <paramref name="now"/>: "2 Oct 14:30".</summary>
    public static string LocalTime(DateTimeOffset moment, DateTimeOffset now, CultureInfo culture) => moment.ToOffset(now.Offset).ToString("d MMM HH:mm", culture);

    public static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    public static string Span(TimeSpan span) =>
        span.TotalDays >= 1 ? Loc.F("{0} d {1} h", (int)span.TotalDays, span.Hours)
        : span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes} min"
        : $"{Math.Max((int)span.TotalMinutes, 1)} min";

    public static string CodexPlanName(string? raw) => PlanName(CodexPlans, raw);

    private static string PlanName(Dictionary<string, string> known, string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? Loc.T("Plan unknown")
        : known.TryGetValue(raw, out var name) ? name
        : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(raw.Replace('_', ' '));

    [GeneratedRegex(@"_(\d+x)$")]
    private static partial Regex TierMultiplier();
}
