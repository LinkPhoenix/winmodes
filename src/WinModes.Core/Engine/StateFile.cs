using System.Globalization;
using System.Text.Json;

namespace WinModes.Core.Engine;

/// <summary>
/// Reads the JSON files that hold what a revert needs. A missing file is "nothing recorded"; a damaged one is
/// moved aside so the next save cannot overwrite it; a transient I/O error is thrown, because treating it as
/// "nothing recorded" would let the next save erase the records.
/// </summary>
internal static class StateFile
{
    public const string QuarantineMarker = ".corrupt-";

    public static T? Read<T>(string path, JsonSerializerOptions options)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            if (JsonSerializer.Deserialize<T>(File.ReadAllText(path), options) is { } value)
            {
                return value;
            }
        }
        catch (JsonException)
        {
            // Falls through to the quarantine below.
        }

        Quarantine(path);
        return null;
    }

    private static void Quarantine(string path)
    {
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        File.Move(path, path + QuarantineMarker + stamp);
    }
}
