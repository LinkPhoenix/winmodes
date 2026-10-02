using System.Globalization;
using System.Text.Json;

namespace WinModes.Core.Usage.Tokens;

/// <summary>
/// Reads the token figures from the logs Claude Code keeps in <c>~/.claude/projects</c>. Only numbers, the model, the date and the
/// name of the project folder are taken: the text of the conversation is parsed past and never kept.
/// </summary>
public static class ClaudeTokenLog
{
    public const string Tool = "Claude";

    /// <summary>A cheap test on the raw line, so the long lines of tool output are never decoded.</summary>
    public static readonly byte[][] LinePatterns = [Utf8("\"output_tokens\"")];

    /// <summary>The request of an assistant line; null for any other line, for a line without usage and for the app's own replies.</summary>
    public static TokenEvent? Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Text(root, "type") != "assistant"
                || !root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object
                || !DateTimeOffset.TryParse(Text(root, "timestamp"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
            {
                return null;
            }

            var model = Text(message, "model");
            // Claude Code writes its own notices as assistant lines with this placeholder model and no real usage.
            if (string.IsNullOrEmpty(model) || model.StartsWith('<'))
            {
                return null;
            }

            var counts = new TokenCounts(Number(usage, "input_tokens"), Number(usage, "output_tokens"), Number(usage, "cache_read_input_tokens"), Number(usage, "cache_creation_input_tokens"));
            // A line with no id at all cannot be told from the same request logged again, so it is not counted.
            var key = KeyOf(Text(message, "id"), Text(root, "requestId"));
            return counts.IsEmpty || key == 0 ? null : new TokenEvent(Tool, key, at, model, ProjectName(Text(root, "cwd")), counts);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The name of the folder of a project: enough to tell projects apart without showing a whole path.</summary>
    public static string ProjectName(string? folder)
    {
        var trimmed = (folder ?? "").TrimEnd('\\', '/');
        var name = trimmed.Length == 0 ? "" : Path.GetFileName(trimmed);
        return name.Length > 0 ? name : trimmed;
    }

    /// <summary>
    /// A request is logged once per block of its answer, with the same message and request ids and an output that grows, and a
    /// resumed session can copy it again: the ids are what makes it one request. FNV-1a, so the value is the same on every run.
    /// </summary>
    internal static long KeyOf(string messageId, string requestId)
    {
        if (messageId.Length == 0 && requestId.Length == 0)
        {
            return 0;
        }

        const ulong Offset = 14695981039346656037;
        const ulong Prime = 1099511628211;
        var hash = Offset;
        foreach (var character in messageId + "\u001f" + requestId)
        {
            hash = (hash ^ character) * Prime;
        }

        // Zero means "no key": never produce it for a real request.
        return hash == 0 ? 1 : unchecked((long)hash);
    }

    internal static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    internal static long Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number > 0 ? number : 0;

    internal static byte[] Utf8(string text) => System.Text.Encoding.UTF8.GetBytes(text);
}

/// <summary>
/// Reads the token figures from one Codex session log. Codex writes the running total of the session after each request, so the
/// tokens of a request are the difference with the previous total; the model and the project come from the lines before.
/// </summary>
public sealed class CodexTokenLog
{
    public const string Tool = "Codex";

    private const string UnknownModel = "unknown";

    public static readonly byte[][] LinePatterns =
    [
        ClaudeTokenLog.Utf8("\"token_count\""),
        ClaudeTokenLog.Utf8("\"turn_context\""),
        ClaudeTokenLog.Utf8("\"session_meta\""),
    ];

    /// <summary>The project folder, the model and the running total as they stood after the last line read; kept between two reads of the file.</summary>
    public sealed record State(string Project, string Model, TokenCounts Total);

    private string _project;
    private string _model;
    private TokenCounts _total;

    public CodexTokenLog(State? state = null)
    {
        (_project, _model, _total) = state is null ? ("", UnknownModel, TokenCounts.Empty) : (state.Project, state.Model, state.Total);
    }

    public State Current => new(_project, _model, _total);

    /// <summary>Takes one line; returns the tokens of a request when the line ends one, null otherwise.</summary>
    public TokenEvent? Feed(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            switch (ClaudeTokenLog.Text(root, "type"))
            {
                case "session_meta":
                    _project = ClaudeTokenLog.ProjectName(ClaudeTokenLog.Text(payload, "cwd"));
                    return null;
                case "turn_context":
                    if (ClaudeTokenLog.Text(payload, "model") is { Length: > 0 } model)
                    {
                        _model = model;
                    }

                    return null;
                case "event_msg" when ClaudeTokenLog.Text(payload, "type") == "token_count":
                    return Request(root, payload);
                default:
                    return null;
            }
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private TokenEvent? Request(JsonElement root, JsonElement payload)
    {
        if (!payload.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object
            || !info.TryGetProperty("total_token_usage", out var usage) || usage.ValueKind != JsonValueKind.Object
            || !DateTimeOffset.TryParse(ClaudeTokenLog.Text(root, "timestamp"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
        {
            return null;
        }

        // Codex counts the cache inside the input; the four kinds are made separate here, as for Claude.
        var input = ClaudeTokenLog.Number(usage, "input_tokens");
        var cached = ClaudeTokenLog.Number(usage, "cached_input_tokens");
        var written = ClaudeTokenLog.Number(usage, "cache_write_input_tokens");
        var total = new TokenCounts(Math.Max(input - cached - written, 0), ClaudeTokenLog.Number(usage, "output_tokens"), cached, written);

        var change = total - _total;
        // A total that went down is a new count (another session continued in this file): it is all new.
        if (change.HasNegative)
        {
            change = total;
        }

        _total = total;
        // The same total written again (a rate-limit update) is not a request.
        return change.IsEmpty ? null : new TokenEvent(Tool, 0, at, _model, _project, change);
    }
}
