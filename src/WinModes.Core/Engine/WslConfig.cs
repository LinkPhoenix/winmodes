using System.Text.RegularExpressions;

namespace WinModes.Core.Engine;

/// <summary>Reads and edits the <c>memory</c> key of the <c>[wsl2]</c> section in a .wslconfig text, leaving everything else as is.</summary>
public static partial class WslConfig
{
    private const string Section = "[wsl2]";

    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wslconfig");

    /// <summary>Memory limit in GB, or null when no limit is set or it is not expressed in whole GB.</summary>
    public static int? ReadMemoryGb(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var inSection = false;
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inSection = line.Equals(Section, StringComparison.OrdinalIgnoreCase);
            }
            else if (inSection && MemoryLine().Match(line) is { Success: true } match)
            {
                return int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    /// <summary>Returns the content with the memory limit set, or removed when <paramref name="gigabytes"/> is null.</summary>
    public static string WithMemoryGb(string content, int? gigabytes)
    {
        ArgumentNullException.ThrowIfNull(content);
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = content.Length == 0 ? [] : content.Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        var setting = gigabytes is { } value ? $"memory={value}GB" : null;

        var sectionIndex = lines.FindIndex(line => line.Trim().Equals(Section, StringComparison.OrdinalIgnoreCase));
        if (sectionIndex < 0)
        {
            if (setting is null)
            {
                return content;
            }

            if (lines.Count > 0 && lines[^1].Length > 0)
            {
                lines.Add("");
            }

            lines.Add(Section);
            lines.Add(setting);
            return string.Join(newline, lines) + newline;
        }

        var end = lines.FindIndex(sectionIndex + 1, line => line.TrimStart().StartsWith('['));
        end = end < 0 ? lines.Count : end;
        var existing = lines.FindIndex(sectionIndex + 1, end - sectionIndex - 1, line => IsMemoryKey().IsMatch(line));

        if (existing >= 0 && setting is not null)
        {
            lines[existing] = setting;
        }
        else if (existing >= 0)
        {
            lines.RemoveAt(existing);
        }
        else if (setting is not null)
        {
            lines.Insert(sectionIndex + 1, setting);
        }

        return string.Join(newline, lines);
    }

    [GeneratedRegex(@"^memory\s*=\s*(\d+)\s*GB\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex MemoryLine();

    [GeneratedRegex(@"^\s*memory\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex IsMemoryKey();
}
