using System.Text.RegularExpressions;

namespace WinModes.Core.Planning;

/// <summary>
/// Reads the local MCP servers declared in Codex's config.toml: the name, the command and its arguments.
/// Nothing else is read (no environment values, no remote URLs), and a file that cannot be read gives no server.
/// </summary>
public static partial class CodexMcpConfig
{
    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.toml");

    public static IReadOnlyList<McpDefinition> Load(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadLines(path)) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static IReadOnlyList<McpDefinition> Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var servers = new List<(string Name, string? Command, IReadOnlyList<string> Args)>();
        var inServer = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                // Sub-tables such as [mcp_servers.x.env] do not match and end the server's own keys.
                var header = ServerHeader().Match(line);
                inServer = header.Success;
                if (inServer)
                {
                    servers.Add((header.Groups[1].Value + header.Groups[2].Value + header.Groups[3].Value, null, []));
                }
            }
            else if (inServer && KeyValue().Match(line) is { Success: true } pair)
            {
                var values = QuotedString().Matches(pair.Groups[2].Value).Select(Unquote).ToList();
                var current = servers[^1];
                servers[^1] = pair.Groups[1].Value == "command"
                    ? (current.Name, values.FirstOrDefault(), current.Args)
                    : (current.Name, current.Command, values);
            }
        }

        // A server without a command is remote (url): it has no local process to recognise.
        return [.. servers.Where(server => !string.IsNullOrWhiteSpace(server.Command)).Select(server => new McpDefinition(server.Name, server.Command!, server.Args))];
    }

    private static string Unquote(Match match) =>
        match.Groups[1].Success ? match.Groups[1].Value.Replace(@"\\", @"\", StringComparison.Ordinal).Replace("\\\"", "\"", StringComparison.Ordinal) : match.Groups[2].Value;

    [GeneratedRegex("""^\[mcp_servers\.(?:"([^"]+)"|'([^']+)'|([A-Za-z0-9_-]+))\]$""")]
    private static partial Regex ServerHeader();

    [GeneratedRegex(@"^(command|args)\s*=\s*(.+)$")]
    private static partial Regex KeyValue();

    // TOML basic string (escapes) or literal string (taken as is).
    [GeneratedRegex("""
        "((?:[^"\\]|\\.)*)"|'([^']*)'
        """, RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex QuotedString();
}
