namespace WinModes.Core.Planning;

/// <summary>One AI or coding tool and the rule that recognises its processes.</summary>
public sealed record AiTool(string Id, string Name, Func<ProcessNode, bool> Matches);

/// <summary>A tool process that is not the child of another process of the same tool, with everything it started.</summary>
public sealed record AiSession(AiTool Tool, ProcessNode Root, IReadOnlyList<ProcessNode> Descendants)
{
    public double TotalMemoryMb => Root.PrivateMemoryMb + Descendants.Sum(node => node.PrivateMemoryMb);
    public double TotalCpuPercent => Root.CpuPercent + Descendants.Sum(node => node.CpuPercent);
}

/// <summary>Finds the sessions of known AI tools in a process sample and attaches their child processes.</summary>
public static class AiToolCatalog
{
    // Order matters: the first matching tool wins, so specific rules come before generic ones.
    public static IReadOnlyList<AiTool> Tools { get; } =
    [
        new("claude-code", "Claude Code", node => Named(node, "claude") && (Has(node.CommandLine, "stream-json") || Has(node.ExecutablePath, "ClaudeCode"))),
        new("claude-desktop", "Claude desktop", node => Named(node, "claude")),
        new("codex", "Codex", node => Named(node, "codex") || Has(node.ExecutablePath, "OpenAI.Codex")
            || (Named(node, "node") && Has(node.CommandLine, @"\codex\"))),
        new("cursor", "Cursor", node => Named(node, "Cursor")),
        new("vscode", "VS Code", node => Named(node, "Code") || Named(node, "Code - Insiders")),
        new("t3code", "T3 Code", node => Has(node.ExecutablePath, @"\T3 Code") || Named(node, "t3code")),
        new("opencode", "OpenCode", node => Named(node, "opencode") || Has(node.ExecutablePath, @"\OpenCode\")),
        new("windsurf", "Windsurf", node => Named(node, "Windsurf")),
        new("ollama", "Ollama", node => node.Name.StartsWith("ollama", StringComparison.OrdinalIgnoreCase)),
        new("lmstudio", "LM Studio", node => Named(node, "LM Studio")),
    ];

    public static IReadOnlyList<AiSession> FindSessions(IReadOnlyList<ProcessNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var byPid = nodes.ToDictionary(node => node.Pid);
        var children = ProcessSampler.BuildChildren(nodes);
        var toolOf = nodes
            .Select(node => (node, tool: Tools.FirstOrDefault(tool => tool.Matches(node))))
            .Where(pair => pair.tool is not null)
            .ToDictionary(pair => pair.node.Pid, pair => pair.tool!);

        // A session root is a tool process whose parent does not belong to the same tool.
        var roots = nodes
            .Where(node => toolOf.TryGetValue(node.Pid, out var tool)
                && !(ProcessSampler.HasLiveParent(node, byPid) && toolOf.TryGetValue(node.ParentPid, out var parentTool) && parentTool == tool))
            .ToList();
        var rootPids = roots.Select(root => root.Pid).ToHashSet();

        return [.. roots.Select(root => new AiSession(toolOf[root.Pid], root, CollectDescendants(root.Pid, children, rootPids)))];
    }

    private static List<ProcessNode> CollectDescendants(int pid, ILookup<int, ProcessNode> children, HashSet<int> otherRoots)
    {
        var result = new List<ProcessNode>();
        var pending = new Stack<int>();
        pending.Push(pid);
        while (pending.Count > 0)
        {
            foreach (var child in children[pending.Pop()])
            {
                // Another session starts here (e.g. a Claude Code session under the desktop app): it is listed on its own.
                if (otherRoots.Contains(child.Pid))
                {
                    continue;
                }

                result.Add(child);
                pending.Push(child.Pid);
            }
        }

        return result;
    }

    private static bool Named(ProcessNode node, string name) => node.Name.Equals(name, StringComparison.OrdinalIgnoreCase);

    private static bool Has(string? text, string value) => text?.Contains(value, StringComparison.OrdinalIgnoreCase) == true;
}
