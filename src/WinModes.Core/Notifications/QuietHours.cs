namespace WinModes.Core.Notifications;

/// <summary>A daily span of time, which may cross midnight, during which optional notifications are held back.</summary>
public static class QuietHours
{
    public const int MinutesPerDay = 24 * 60;

    /// <summary>
    /// True when <paramref name="now"/> is in the span from <paramref name="fromMinutes"/> (included) to <paramref name="toMinutes"/>
    /// (excluded), both counted in minutes since midnight. The span runs through midnight when it ends before it starts
    /// (22:00 to 07:00); a span that starts and ends at the same time is empty.
    /// </summary>
    public static bool IsQuiet(int fromMinutes, int toMinutes, TimeOnly now)
    {
        var from = Math.Clamp(fromMinutes, 0, MinutesPerDay - 1);
        var to = Math.Clamp(toMinutes, 0, MinutesPerDay - 1);
        var minute = now.Hour * 60 + now.Minute;
        return from != to && (from < to ? minute >= from && minute < to : minute >= from || minute < to);
    }
}
