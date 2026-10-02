using System.Globalization;

namespace WinModes.Core.Usage;

/// <summary>
/// When the next online request to a provider may be sent. A request that works keeps the regular pace; each failure in a row
/// doubles the wait (up to a ceiling), and a provider that says how long to wait (<c>Retry-After</c>) is obeyed, so a refusal or an
/// outage is never answered by asking again at the same pace.
/// </summary>
public sealed class PollBackoff(TimeSpan interval, TimeSpan ceiling)
{
    /// <summary>A provider asking for more than this is not obeyed beyond it: the figures would stay old for too long.</summary>
    public static readonly TimeSpan RetryAfterLimit = TimeSpan.FromHours(6);

    private int _failures;
    private DateTimeOffset _next = DateTimeOffset.MinValue;

    /// <summary>Failures in a row since the last success.</summary>
    public int Failures => _failures;

    /// <summary>The earliest moment the next request may be sent.</summary>
    public DateTimeOffset NextAllowed => _next;

    public bool IsDue(DateTimeOffset now) => now >= _next;

    /// <summary>A request that worked, or nothing to ask (no sign-in): the regular pace again.</summary>
    public void Succeeded(DateTimeOffset now)
    {
        _failures = 0;
        _next = now + interval;
    }

    /// <summary>A request that failed; <paramref name="retryAfter"/> is what the provider asked for, when it did.</summary>
    public void Failed(DateTimeOffset now, TimeSpan? retryAfter = null)
    {
        _failures = Math.Min(_failures + 1, 16);
        var wait = TimeSpan.FromTicks(Math.Min(interval.Ticks * (1L << Math.Min(_failures, 10)), ceiling.Ticks));
        if (retryAfter is { } asked && asked > wait)
        {
            wait = asked < RetryAfterLimit ? asked : RetryAfterLimit;
        }

        _next = now + wait;
    }

    /// <summary>Allows the next request at once: the user signed in or out, or changed what is read.</summary>
    public void Reset()
    {
        _failures = 0;
        _next = DateTimeOffset.MinValue;
    }

    /// <summary>The value of a <c>Retry-After</c> header: a number of seconds or a date. Null when absent, malformed or already past.</summary>
    public static TimeSpan? ParseRetryAfter(string? value, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return seconds > 0 && double.IsFinite(seconds) ? TimeSpan.FromSeconds(Math.Min(seconds, RetryAfterLimit.TotalSeconds)) : null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) && date > now ? date - now : null;
    }
}
