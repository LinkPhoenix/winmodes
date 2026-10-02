namespace WinModes.Core.Automation;

/// <summary>Rules about the list of rules itself: which one comes first when several programs run.</summary>
public static class AutoSwitchRules
{
    private const string GameMode = "game";
    private const string WorkMode = "work";

    /// <summary>
    /// Puts the rules in priority order, which is the order of the list. A program that starts Game mode outranks everything else, so a game
    /// opened next to Claude Code still gets its mode; a program that only says "I am working" comes last, so it never hides another trigger.
    /// Everything else keeps its place. Conditions (battery, hours) are never moved.
    /// </summary>
    public static IReadOnlyList<AutoSwitchRule> Prioritize(IEnumerable<AutoSwitchRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var all = rules.ToList();
        var games = all.Where(rule => IsProgram(rule) && IsMode(rule, GameMode));
        var work = all.Where(rule => IsProgram(rule) && IsMode(rule, WorkMode));
        var rest = all.Where(rule => !(IsProgram(rule) && (IsMode(rule, GameMode) || IsMode(rule, WorkMode))));
        return [.. games, .. rest, .. work];
    }

    private static bool IsProgram(AutoSwitchRule rule) => !AutoSwitchConditions.IsCondition(rule.Process);

    private static bool IsMode(AutoSwitchRule rule, string mode) => string.Equals(rule.Mode, mode, StringComparison.OrdinalIgnoreCase);
}
