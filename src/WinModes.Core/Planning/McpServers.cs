namespace WinModes.Core.Planning;

/// <summary>An MCP server that several AI sessions each started a copy of.</summary>
public sealed record McpDuplicate(string Name, int Sessions, int Processes, double MemoryMb);

/// <summary>Recognises MCP server processes among the children of AI sessions.</summary>
public static class McpServers
{
    private static readonly string[] Markers = ["mcp", "modelcontextprotocol"];
    private static readonly char[] TokenSeparators = [' ', '\t', '"', '\''];
    private static readonly string[] ScriptExtensions = [".js", ".mjs", ".cjs", ".exe", ".cmd", ".py", ".ts"];

    public static bool IsServer(ProcessNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return HasMarker(node.CommandLine);
    }

    /// <summary>
    /// Short package-like name taken from the command line, e.g. "@playwright/mcp" or "server-github-mcp".
    /// Folders are dropped, so the name never carries a user path.
    /// </summary>
    public static string NameOf(ProcessNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var token = (node.CommandLine ?? "")
            .Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries)
            // cmd.exe leaves its "^" escapes in the command line; options are not package names.
            .Select(part => part.Trim('^', ',', ';'))
            .FirstOrDefault(part => !part.StartsWith('-') && HasMarker(part));
        if (token is null)
        {
            return node.Name;
        }

        var segments = token.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var index = Array.FindLastIndex(segments, HasMarker);
        // "@scope/package": when the scope is what matched, the package is the next segment.
        if (segments[index].StartsWith('@') && index + 1 < segments.Length)
        {
            index++;
        }

        var name = segments[index];

        // "package@1.2.3" -> "package"; a leading "@" is an npm scope and stays.
        var version = name.IndexOf('@', 1);
        if (version > 0)
        {
            name = name[..version];
        }

        foreach (var extension in ScriptExtensions)
        {
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^extension.Length];
                break;
            }
        }

        var scope = index > 0 && segments[index - 1].StartsWith('@') ? segments[index - 1] + "/" : "";
        return scope + name;
    }

    private static bool HasMarker(string? text) =>
        text is not null && Markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));

    /// <summary>Servers running in two sessions or more, largest memory first.</summary>
    public static IReadOnlyList<McpDuplicate> FindDuplicates(IReadOnlyList<AiSession> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        return [.. sessions
            .SelectMany(session => session.Descendants.Where(IsServer).Select(node => (Session: session.Root.Pid, Name: NameOf(node), Node: node)))
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new McpDuplicate(
                group.Key,
                group.Select(item => item.Session).Distinct().Count(),
                group.Count(),
                group.Sum(item => item.Node.PrivateMemoryMb)))
            .Where(duplicate => duplicate.Sessions > 1)
            .OrderByDescending(duplicate => duplicate.MemoryMb)];
    }
}
