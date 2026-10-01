namespace WinModes.Core.Planning;

/// <summary>An MCP server that several AI sessions each started a copy of.</summary>
public sealed record McpDuplicate(string Name, int Sessions, int Processes, double MemoryMb);

/// <summary>One running MCP server of a session: its launcher process and everything it started.</summary>
public sealed record McpInstance(string Name, ProcessNode Root, int Processes, double MemoryMb);

/// <summary>A server declared in a tool's configuration, used to recognise and name its processes.</summary>
public sealed record McpDefinition(string Name, string Command, IReadOnlyList<string> Args)
{
    // Programs that run something else: on their own they say nothing about which server it is.
    private static readonly string[] Launchers =
        ["npx", "npm", "pnpm", "bunx", "bun", "node", "deno", "uvx", "uv", "python", "py", "docker", "cmd", "powershell", "pwsh", "wsl"];

    public bool Matches(ProcessNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var command = Path.GetFileNameWithoutExtension(Command.Replace('\\', '/'));
        var isLauncher = Launchers.Contains(command, StringComparer.OrdinalIgnoreCase);
        var keys = Args.Where(arg => arg.Length > 0 && !arg.StartsWith('-')).ToList();
        if (keys.Count == 0)
        {
            return !isLauncher && Path.GetFileNameWithoutExtension(node.Name).Equals(command, StringComparison.OrdinalIgnoreCase);
        }

        var commandLine = node.CommandLine ?? "";
        return keys.All(key => commandLine.Contains(key, StringComparison.OrdinalIgnoreCase))
            && (isLauncher || commandLine.Contains(command, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Recognises MCP server processes among the children of AI sessions.</summary>
public static class McpServers
{
    private const string SubcommandName = "mcp";
    private const string ModulesFolder = "node_modules";
    private static readonly string[] Markers = ["mcp", "modelcontextprotocol"];
    private static readonly char[] TokenSeparators = [' ', '\t', '"', '\''];
    private static readonly string[] ScriptExtensions = [".js", ".mjs", ".cjs", ".exe", ".cmd", ".py", ".ts"];

    private static volatile IReadOnlyList<McpDefinition> _configured = [];

    /// <summary>
    /// Servers declared in the tools' own configuration (e.g. Codex). They catch servers whose command line
    /// does not say "mcp" and give every server the name the user chose.
    /// </summary>
    public static IReadOnlyList<McpDefinition> Configured
    {
        get => _configured;
        set => _configured = value ?? [];
    }

    public static bool IsServer(ProcessNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return Tokens(node.CommandLine).Any(HasMarker) || _configured.Any(definition => definition.Matches(node));
    }

    /// <summary>
    /// A process of the tool itself is not a server, even when its command line mentions MCP (Electron's
    /// "WebMCP" switches, Codex's "mcp_servers" settings), unless the tool's configuration declares it.
    /// </summary>
    public static bool IsServer(AiSession session, ProcessNode node)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(node);
        return _configured.Any(definition => definition.Matches(node))
            || (!session.Tool.Matches(node) && Tokens(node.CommandLine).Any(HasMarker));
    }

    // cmd.exe leaves its "^" escapes in the command line; options ("--enable-features=WebMCP") name no server.
    private static List<string> Tokens(string? commandLine) =>
        [.. (commandLine ?? "")
            .Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim('^', ',', ';'))
            .Where(part => part.Length > 0 && !part.StartsWith('-'))];

    /// <summary>
    /// The configured name when the process matches a declared server, else a short package-like name taken
    /// from the command line, e.g. "@playwright/mcp" or "prisma mcp". Folders are dropped, so the name never
    /// carries a user path.
    /// </summary>
    public static string NameOf(ProcessNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (_configured.FirstOrDefault(definition => definition.Matches(node)) is { } known)
        {
            return known.Name;
        }

        var tokens = Tokens(node.CommandLine);
        var position = tokens.FindIndex(HasMarker);
        if (position < 0)
        {
            return node.Name;
        }

        // "prisma@latest mcp", "docker mcp gateway run": the server is a subcommand, the program names it.
        if (tokens[position].Equals(SubcommandName, StringComparison.OrdinalIgnoreCase))
        {
            return position > 0 ? $"{PackageName(tokens[position - 1])} {SubcommandName}" : node.Name;
        }

        var segments = tokens[position].Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var index = Array.FindLastIndex(segments, HasMarker);
        // "@scope/package": when the scope is what matched, the package is the next segment.
        if (segments[index].StartsWith('@') && index + 1 < segments.Length)
        {
            index++;
        }

        var scope = index > 0 && segments[index - 1].StartsWith('@') ? segments[index - 1] + "/" : "";
        return scope + Strip(segments[index]);
    }

    /// <summary>
    /// The servers of a session. A server started through wrappers ("cmd /c npx ..." then node) is one server:
    /// the outermost process names it and everything below counts towards its memory.
    /// </summary>
    public static IReadOnlyList<McpInstance> Servers(AiSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var byPid = session.Descendants.GroupBy(node => node.Pid).ToDictionary(group => group.Key, group => group.First());
        var children = session.Descendants.ToLookup(node => node.ParentPid);
        var servers = session.Descendants.Where(node => IsServer(session, node)).ToList();
        var serverPids = servers.Select(node => node.Pid).ToHashSet();

        return [.. servers
            .Where(node => !HasServerAncestor(node, byPid, serverPids))
            .Select(top =>
            {
                var tree = Subtree(top, children);
                return new McpInstance(NameOf(top), top, tree.Count, tree.Sum(node => node.PrivateMemoryMb));
            })];
    }

    /// <summary>Servers running in two sessions or more, largest memory first.</summary>
    public static IReadOnlyList<McpDuplicate> FindDuplicates(IReadOnlyList<AiSession> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        return [.. sessions
            .SelectMany(session => Servers(session).Select(server => (Session: session.Root.Pid, Server: server)))
            .GroupBy(item => item.Server.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new McpDuplicate(
                group.Key,
                group.Select(item => item.Session).Distinct().Count(),
                group.Count(),
                group.Sum(item => item.Server.MemoryMb)))
            .Where(duplicate => duplicate.Sessions > 1)
            .OrderByDescending(duplicate => duplicate.MemoryMb)];
    }

    private static bool HasServerAncestor(ProcessNode node, Dictionary<int, ProcessNode> byPid, HashSet<int> serverPids)
    {
        var seen = new HashSet<int> { node.Pid };
        var current = node;
        // "seen" stops on a reused pid that would make the chain loop.
        while (byPid.TryGetValue(current.ParentPid, out var parent) && seen.Add(parent.Pid))
        {
            if (serverPids.Contains(parent.Pid))
            {
                return true;
            }

            current = parent;
        }

        return false;
    }

    private static List<ProcessNode> Subtree(ProcessNode top, ILookup<int, ProcessNode> children)
    {
        var result = new List<ProcessNode> { top };
        var seen = new HashSet<int> { top.Pid };
        for (var i = 0; i < result.Count; i++)
        {
            result.AddRange(children[result[i].Pid].Where(child => seen.Add(child.Pid)));
        }

        return result;
    }

    /// <summary>"C:\...\node_modules\prisma\build\index.js" -> "prisma"; "shadcn@latest" -> "shadcn"; "docker.exe" -> "docker".</summary>
    private static string PackageName(string token)
    {
        var segments = token.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var modules = Array.FindLastIndex(segments, segment => segment.Equals(ModulesFolder, StringComparison.OrdinalIgnoreCase));
        if (modules >= 0 && modules + 1 < segments.Length)
        {
            var package = segments[modules + 1];
            return package.StartsWith('@') && modules + 2 < segments.Length ? $"{package}/{Strip(segments[modules + 2])}" : Strip(package);
        }

        var scope = segments.Length > 1 && segments[^2].StartsWith('@') ? segments[^2] + "/" : "";
        return scope + Strip(segments[^1]);
    }

    private static string Strip(string name)
    {
        // "package@1.2.3" -> "package"; a leading "@" is an npm scope and stays.
        var version = name.IndexOf('@', 1);
        if (version > 0)
        {
            name = name[..version];
        }

        var extension = ScriptExtensions.FirstOrDefault(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
        return extension is null ? name : name[..^extension.Length];
    }

    private static bool HasMarker(string? text) =>
        text is not null && Markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
