using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WinModes.Core.Usage;

/// <summary>The Claude limits last handed to the status line command.</summary>
public sealed record ClaudeLimits(DateTimeOffset SeenAt, LimitWindow? FiveHour, LimitWindow? SevenDay);

/// <summary>
/// Claude Code gives the usage limits of a Pro or Max plan only to its status line command (JSON on stdin,
/// see https://code.claude.com/docs/en/statusline). The command records them in a small file the widget reads.
/// Nothing but the two percentages and their reset times is kept.
/// </summary>
public static class ClaudeStatusLine
{
    private const int FiveHourMinutes = 300;
    private const int SevenDayMinutes = 10080;

    public static string DefaultRecordPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinModes", "claude-limits.json");

    /// <summary>Reads what Claude Code sent; null when it carries no limits (API key, or before the first answer).</summary>
    public static ClaudeLimits? Parse(string statusJson, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(statusJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("rate_limits", out var limits) || limits.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var fiveHour = Window(limits, "five_hour", FiveHourMinutes);
            var sevenDay = Window(limits, "seven_day", SevenDayMinutes);
            return fiveHour is null && sevenDay is null ? null : new ClaudeLimits(now, fiveHour, sevenDay);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The text shown in Claude Code's status row.</summary>
    public static string Line(string statusJson, ClaudeLimits? limits)
    {
        var parts = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(statusJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.Object
                && model.TryGetProperty("display_name", out var name) && name.ValueKind == JsonValueKind.String)
            {
                parts.Add(name.GetString()!);
            }
        }
        catch (JsonException)
        {
            // Nothing to name: the limits alone are shown.
        }

        if (limits?.FiveHour is { } fiveHour)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"5 h: {fiveHour.RemainingPercent:0} % left"));
        }

        if (limits?.SevenDay is { } sevenDay)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"week: {sevenDay.RemainingPercent:0} % left"));
        }

        return string.Join("  |  ", parts);
    }

    public static void Save(string recordPath, ClaudeLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        Directory.CreateDirectory(Path.GetDirectoryName(recordPath)!);
        // Several sessions write at once: replace the file in one step so a reader never sees half of it.
        var temporary = $"{recordPath}.{Environment.ProcessId}.tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(limits));
        File.Move(temporary, recordPath, overwrite: true);
    }

    public static ClaudeLimits? Load(string recordPath)
    {
        try
        {
            return File.Exists(recordPath) ? JsonSerializer.Deserialize<ClaudeLimits>(File.ReadAllText(recordPath)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static LimitWindow? Window(JsonElement limits, string name, int minutes)
    {
        if (!limits.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object
            || !window.TryGetProperty("used_percentage", out var used) || used.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        DateTimeOffset? resetsAt = window.TryGetProperty("resets_at", out var reset) && reset.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeSeconds((long)reset.GetDouble())
            : null;
        return new LimitWindow(used.GetDouble(), minutes, resetsAt);
    }
}

/// <summary>
/// Adds or removes the WinModes status line in Claude Code's user settings. It never replaces a status line
/// the user already has, and removes only its own. The rest of the file is kept as it is.
/// </summary>
public static class ClaudeStatusLineSetup
{
    private const string Key = "statusLine";
    private const string Marker = "WinModes.StatusLine";

    public static string DefaultSettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    public enum State
    {
        /// <summary>Claude Code has no status line.</summary>
        None,

        /// <summary>The WinModes status line is set.</summary>
        Ours,

        /// <summary>Another status line is set: it is left alone.</summary>
        Other,
    }

    public static State Read(string settingsPath)
    {
        try
        {
            var command = Load(settingsPath)?[Key];
            return command is null ? State.None
                : command.ToJsonString().Contains(Marker, StringComparison.OrdinalIgnoreCase) ? State.Ours
                : State.Other;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable settings are never rewritten.
            return State.Other;
        }
    }

    /// <summary>Returns false, changing nothing, when another status line is already set or the file cannot be read.</summary>
    public static bool Install(string settingsPath, string command)
    {
        if (Read(settingsPath) == State.Other)
        {
            return false;
        }

        var settings = Load(settingsPath) ?? [];
        settings[Key] = new JsonObject { ["type"] = "command", ["command"] = command };
        Write(settingsPath, settings);
        return true;
    }

    public static void Remove(string settingsPath)
    {
        if (Read(settingsPath) == State.Ours && Load(settingsPath) is { } settings)
        {
            settings.Remove(Key);
            Write(settingsPath, settings);
        }
    }

    /// <summary>
    /// The command line for Claude Code, which runs it through Git Bash when present and PowerShell otherwise:
    /// forward slashes and no quoting work in both, so a path with spaces is replaced by its short form.
    /// </summary>
    public static string CommandFor(string executablePath, Func<string, string?> shortPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(executablePath);
        ArgumentNullException.ThrowIfNull(shortPath);
        // Only the folder is shortened: the file name has no space and Read() recognises the command by it.
        var folder = Path.GetDirectoryName(executablePath) ?? "";
        if (folder.Contains(' ', StringComparison.Ordinal))
        {
            folder = shortPath(folder) ?? folder;
        }

        var path = Path.Combine(folder, Path.GetFileName(executablePath)).Replace('\\', '/');
        return path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;
    }

    private static JsonObject? Load(string settingsPath) =>
        File.Exists(settingsPath) ? JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject ?? throw new JsonException("Not an object.") : null;

    private static void Write(string settingsPath, JsonObject settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        if (File.Exists(settingsPath))
        {
            File.Copy(settingsPath, settingsPath + ".winmodes.bak", overwrite: true);
        }

        File.WriteAllText(settingsPath, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
