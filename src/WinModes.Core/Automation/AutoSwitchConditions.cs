using System.Globalization;
using WinModes.Core.Localization;
using WinModes.Core.Planning;

namespace WinModes.Core.Automation;

/// <summary>
/// Triggers other than "a program runs". They are stored in the rule's <see cref="AutoSwitchRule.Process"/>
/// field with a leading "@", which no process name can have, so old settings files stay valid.
/// </summary>
public static class AutoSwitchConditions
{
    public const char Prefix = '@';
    public const string Battery = "@battery";
    private const string TimePrefix = "@time ";
    private const string ToolPrefix = "@tool ";
    private const string TimeFormat = "HH:mm";

    /// <summary>
    /// The tools meant for coding, in the order they are listed. A rule on one of them holds while a session of that tool runs,
    /// so "Claude Code" is told apart from the Claude chat app, which has the same process name.
    /// </summary>
    public static IReadOnlyList<string> CodingToolIds { get; } = ["claude-code", "codex", "cursor", "t3code", "opencode", "windsurf", "vscode"];

    /// <summary>The tools proposed when automatic switching is first turned on. A code editor alone is not asked for: it is often opened for a note.</summary>
    public static IReadOnlyList<string> DefaultCodingToolIds { get; } = ["claude-code", "codex", "cursor", "t3code", "opencode", "windsurf"];

    public static string Tool(string toolId)
    {
        ArgumentNullException.ThrowIfNull(toolId);
        return ToolPrefix + toolId;
    }

    public static bool TryParseTool(string key, out string toolId)
    {
        ArgumentNullException.ThrowIfNull(key);
        toolId = key.StartsWith(ToolPrefix, StringComparison.Ordinal) ? key[ToolPrefix.Length..].Trim() : "";
        return toolId.Length > 0;
    }

    public static bool IsCondition(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key.StartsWith(Prefix);
    }

    public static string Schedule(TimeOnly from, TimeOnly to) =>
        $"{TimePrefix}{from.ToString(TimeFormat, CultureInfo.InvariantCulture)}-{to.ToString(TimeFormat, CultureInfo.InvariantCulture)}";

    public static bool TryParseSchedule(string key, out TimeOnly from, out TimeOnly to)
    {
        ArgumentNullException.ThrowIfNull(key);
        from = default;
        to = default;
        if (!key.StartsWith(TimePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = key[TimePrefix.Length..].Split('-');
        return parts.Length == 2
            && TimeOnly.TryParseExact(parts[0], TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out from)
            && TimeOnly.TryParseExact(parts[1], TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out to)
            && from != to;
    }

    /// <summary>True from the start time up to, but not including, the end time. A range may cross midnight.</summary>
    public static bool IsScheduleActive(string key, TimeOnly now) =>
        TryParseSchedule(key, out var from, out var to) && now.IsBetween(from, to);

    /// <summary>The conditions among the rules that hold right now, to add to the running process names.</summary>
    /// <param name="runningToolIds">Ids (see <see cref="AiToolCatalog"/>) of the tools that have a session open.</param>
    public static IEnumerable<string> ActiveKeys(IEnumerable<AutoSwitchRule> rules, bool onBattery, TimeOnly now, IReadOnlySet<string>? runningToolIds = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules
            .Where(rule => rule.Enabled)
            .Select(rule => rule.Process)
            .Where(key => (key == Battery && onBattery) || IsScheduleActive(key, now)
                || (TryParseTool(key, out var toolId) && runningToolIds?.Contains(toolId) == true))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>True when at least one rule needs the sessions of the AI tools to be looked for.</summary>
    public static bool NeedsTools(IEnumerable<AutoSwitchRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules.Any(rule => rule.Enabled && TryParseTool(rule.Process, out _));
    }

    /// <summary>Plain-language trigger, e.g. "On battery" or "cs2 is running".</summary>
    public static string Describe(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key == Battery)
        {
            return Loc.T("On battery");
        }

        if (TryParseTool(key, out var toolId))
        {
            return Loc.F("{0} is running", AiToolCatalog.Tools.FirstOrDefault(tool => tool.Id == toolId)?.Name ?? toolId);
        }

        return TryParseSchedule(key, out var from, out var to)
            ? Loc.F("From {0} to {1}", from.ToString(TimeFormat, CultureInfo.InvariantCulture), to.ToString(TimeFormat, CultureInfo.InvariantCulture))
            : Loc.F("{0} is running", key);
    }

    /// <summary>What ended, e.g. "Claude Code closed" or "No longer: On battery", for a notification.</summary>
    public static string DescribeEnd(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key == Battery || TryParseSchedule(key, out _, out _) ? Loc.F("No longer: {0}", Describe(key)) : Loc.F("{0} closed", Name(key));
    }

    /// <summary>Short name of the tool or program behind a rule, e.g. "Claude Code" or "cs2".</summary>
    public static string Name(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (TryParseTool(key, out var toolId))
        {
            return AiToolCatalog.Tools.FirstOrDefault(tool => tool.Id == toolId)?.Name ?? toolId;
        }

        return key;
    }
}
