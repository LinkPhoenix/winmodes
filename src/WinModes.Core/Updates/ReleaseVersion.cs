using System.Globalization;
using System.Text.RegularExpressions;

namespace WinModes.Core.Updates;

/// <summary>
/// Compares the running version with a release tag such as "v0.3.1". A beta tag looks like "v0.9.3-beta.20261002", with a
/// revision ("-beta.20261002.2") when several betas come out the same day. A beta comes before the stable version of the same number.
/// </summary>
public static partial class ReleaseVersion
{
    private const int RevisionsPerDay = 100;

    [GeneratedRegex(@"^(?<core>\d+\.\d+\.\d+)(?:-beta\.(?<day>\d{8})(?:\.(?<revision>\d{1,2}))?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();

    /// <summary>Reads "v1.2.3" or "1.2.3"; anything else (a beta tag included) is rejected.</summary>
    public static bool TryParse(string? tag, out Version version) =>
        TryParseRelease(tag, out version, out var beta) && beta is null;

    /// <summary>
    /// Reads a stable tag or a beta tag. <paramref name="beta"/> is null for a stable version and otherwise a number that grows with
    /// each beta of the same version (the day, then the revision of the day).
    /// </summary>
    public static bool TryParseRelease(string? tag, out Version version, out long? beta)
    {
        version = new Version(0, 0, 0);
        beta = null;
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var match = TagPattern().Match(tag.Trim().TrimStart('v', 'V'));
        if (!match.Success || !Version.TryParse(match.Groups["core"].Value, out var parsed))
        {
            return false;
        }

        version = parsed;
        if (match.Groups["day"].Success)
        {
            var revision = match.Groups["revision"].Success ? int.Parse(match.Groups["revision"].Value, CultureInfo.InvariantCulture) : 1;
            if (revision is < 1 or >= RevisionsPerDay)
            {
                return false;
            }

            beta = (long.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture) * RevisionsPerDay) + revision;
        }

        return true;
    }

    /// <summary>The number of a beta from its day and revision, in the form <see cref="TryParseRelease"/> gives.</summary>
    public static long BetaNumber(int day, int revision = 1) => (day * (long)RevisionsPerDay) + revision;

    /// <summary>True only when the tag is a valid stable version strictly above the current one.</summary>
    public static bool IsNewer(string? tag, Version current) => IsNewer(tag, current, null);

    /// <summary>
    /// True when the tag is newer than the running build. A stable build only moves to a stable version. A beta build (the
    /// <paramref name="currentBeta"/> number) moves to a newer beta, or to any stable version of the same number or above.
    /// </summary>
    public static bool IsNewer(string? tag, Version current, long? currentBeta)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!TryParseRelease(tag, out var latest, out var latestBeta))
        {
            return false;
        }

        var running = new Version(current.Major, current.Minor, Math.Max(current.Build, 0));
        if (currentBeta is not { } runningBeta)
        {
            return latestBeta is null && latest > running;
        }

        if (latest != running)
        {
            return latest > running;
        }

        return latestBeta is null || latestBeta > runningBeta;
    }
}
