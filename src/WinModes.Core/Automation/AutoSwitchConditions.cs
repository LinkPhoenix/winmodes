using System.Globalization;
using WinModes.Core.Localization;

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
    private const string TimeFormat = "HH:mm";

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
    public static IEnumerable<string> ActiveKeys(IEnumerable<AutoSwitchRule> rules, bool onBattery, TimeOnly now)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules
            .Select(rule => rule.Process)
            .Where(key => (key == Battery && onBattery) || IsScheduleActive(key, now))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Plain-language trigger, e.g. "On battery" or "cs2 is running".</summary>
    public static string Describe(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key == Battery)
        {
            return Loc.T("On battery");
        }

        return TryParseSchedule(key, out var from, out var to)
            ? Loc.F("From {0} to {1}", from.ToString(TimeFormat, CultureInfo.InvariantCulture), to.ToString(TimeFormat, CultureInfo.InvariantCulture))
            : Loc.F("{0} is running", key);
    }
}
