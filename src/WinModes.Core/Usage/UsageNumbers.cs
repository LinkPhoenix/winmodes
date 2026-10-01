using System.Text.Json;

namespace WinModes.Core.Usage;

/// <summary>
/// Number and time conversions for the usage files and answers. They come from other programs and the network,
/// so a value out of range is "unknown" (null or 0), never an exception that would stop a status line or a poll.
/// </summary>
internal static class UsageNumbers
{
    // Unix seconds of 0001-01-01 and 9999-12-31: the range DateTimeOffset can hold.
    private const double MinUnixSeconds = -62135596800;
    private const double MaxUnixSeconds = 253402300799;

    public static double? Percent(JsonElement element) =>
        element.TryGetDouble(out var value) && double.IsFinite(value) ? value : null;

    public static DateTimeOffset? FromUnixSeconds(double seconds) =>
        double.IsFinite(seconds) && seconds is >= MinUnixSeconds and <= MaxUnixSeconds
            ? DateTimeOffset.FromUnixTimeSeconds((long)seconds)
            : null;

    public static DateTimeOffset? FromUnixMilliseconds(JsonElement element) =>
        element.TryGetInt64(out var milliseconds) && milliseconds / 1000.0 is >= MinUnixSeconds and <= MaxUnixSeconds
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : null;

    public static DateTimeOffset? AddSeconds(DateTimeOffset start, double seconds)
    {
        if (!double.IsFinite(seconds) || Math.Abs(seconds) > MaxUnixSeconds)
        {
            return null;
        }

        try
        {
            return start.AddSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public static int Minutes(double value) => double.IsFinite(value) && value is >= 0 and <= int.MaxValue ? (int)value : 0;
}
