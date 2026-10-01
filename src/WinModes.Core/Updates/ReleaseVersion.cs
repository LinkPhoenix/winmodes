namespace WinModes.Core.Updates;

/// <summary>Compares the running version with a release tag such as "v0.3.1".</summary>
public static class ReleaseVersion
{
    /// <summary>Reads "v1.2.3" or "1.2.3"; anything else (pre-release suffixes included) is rejected.</summary>
    public static bool TryParse(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var text = tag.Trim().TrimStart('v', 'V');
        if (text.Split('.').Length != 3 || !Version.TryParse(text, out var parsed))
        {
            return false;
        }

        version = parsed;
        return true;
    }

    /// <summary>True only when the tag is a valid version strictly above the current one.</summary>
    public static bool IsNewer(string? tag, Version current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return TryParse(tag, out var latest)
            && latest > new Version(current.Major, current.Minor, Math.Max(current.Build, 0));
    }
}
