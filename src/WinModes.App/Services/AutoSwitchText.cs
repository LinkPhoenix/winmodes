using WinModes.Core.Automation;

namespace WinModes.App.Services;

/// <summary>What automatic switching is doing, in words, for the Modes and Automation pages.</summary>
internal static class AutoSwitchText
{
    private const int SecondsPerMinute = 60;

    /// <param name="status">The last status of <see cref="AutoSwitcher"/>.</param>
    /// <param name="activeMode">The mode that is on now, or null.</param>
    public static string Describe(AutoSwitchStatus status, string? activeMode, DateTimeOffset now)
    {
        var mode = status.Mode is null ? "" : LabelOf(status.Mode);
        var left = status.Until is { } until ? Remaining(until - now) : "";
        var trigger = status.Trigger is null ? "" : AutoSwitchConditions.Describe(status.Trigger);
        return status.State switch
        {
            AutoSwitchState.Starting => Loc.F("{0} mode starts in {1}: {2}.", mode, left, trigger),
            AutoSwitchState.Active => Loc.F("{0} mode is on: {1}.", mode, trigger),
            AutoSwitchState.Ending when string.Equals(status.Mode, activeMode, StringComparison.OrdinalIgnoreCase) =>
                Loc.F("{0} mode ends in {1} unless a program or tool comes back.", mode, left),
            AutoSwitchState.Ending => Loc.F("Switching to {0} mode in {1}: {2}.", mode, left, trigger),
            AutoSwitchState.Kept => Loc.F("{0} mode stays on: returning to normal is turned off.", mode),
            AutoSwitchState.Manual => Loc.F("{0} mode was chosen by you: automatic switching leaves it alone.", mode),
            AutoSwitchState.Declined => Loc.T("You ended the mode while its program is still open. It starts again the next time that program opens."),
            AutoSwitchState.Waiting => Loc.F("Paused for {0}: WinModes has just started, or the last switch did not work.", left),
            AutoSwitchState.Paused when status.Until is null => Loc.T("Paused by you until you resume. The mode that is on stays on."),
            AutoSwitchState.Paused => Loc.F("Paused by you for {0}. The mode that is on stays on.", left),
            _ => Loc.T("Waiting for a coding tool or a rule to apply."),
        };
    }

    /// <summary>"42 s" or "1 min 05 s".</summary>
    public static string Remaining(TimeSpan span)
    {
        var seconds = (int)Math.Ceiling(Math.Max(span.TotalSeconds, 0));
        return seconds < SecondsPerMinute
            ? Loc.F("{0} s", seconds)
            : seconds % SecondsPerMinute == 0
                ? Loc.F("{0} min", seconds / SecondsPerMinute)
                : Loc.F("{0} min {1:00} s", seconds / SecondsPerMinute, seconds % SecondsPerMinute);
    }

    // Read again from the profile files only now and then: this runs every second while a page counts down.
    private static readonly TimeSpan LabelsLifetime = TimeSpan.FromSeconds(15);
    private static Dictionary<string, string> _labels = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _labelsRead = DateTime.MinValue;

    private static string LabelOf(string mode)
    {
        if (!_labels.ContainsKey(mode) && DateTime.UtcNow - _labelsRead > LabelsLifetime)
        {
            _labels = ModeCatalog.Load().ToDictionary(entry => entry.Profile.Mode, entry => entry.Profile.Label, StringComparer.OrdinalIgnoreCase);
            _labelsRead = DateTime.UtcNow;
        }

        return _labels.GetValueOrDefault(mode, mode);
    }
}
