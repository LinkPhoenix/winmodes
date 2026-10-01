using WinModes.Core.Planning;

namespace WinModes.Core.Tests;

public sealed class AiToolCatalogTests
{
    private static readonly DateTime Start = new(2026, 10, 1, 9, 0, 0);

    private static ProcessNode Node(int pid, int parent, string name, string? commandLine = null, string? directory = null, int startOffset = 0) =>
        new(pid, parent, name, Threads: 1, PrivateMemoryMb: 100, CpuPercent: 1, ExecutablePath: null, commandLine, directory, Start.AddSeconds(startOffset));

    [Fact]
    public void FindSessions_SplitsClaudeCodeSessionsFromTheDesktopAppAndKeepsTheirFolder()
    {
        ProcessNode[] nodes =
        [
            Node(10, 1, "claude"),
            Node(11, 10, "claude", "claude.exe --type=renderer", startOffset: 1),
            Node(20, 10, "claude", "claude.exe --output-format stream-json", @"D:\projects\alpha", startOffset: 2),
            Node(21, 20, "node", "node mcp-server.js", startOffset: 3),
            Node(30, 10, "claude", "claude.exe --output-format stream-json", @"D:\projects\beta", startOffset: 4),
        ];

        var sessions = AiToolCatalog.FindSessions(nodes);

        var desktop = Assert.Single(sessions, session => session.Tool.Id == "claude-desktop");
        Assert.Equal([11], desktop.Descendants.Select(node => node.Pid));

        var code = sessions.Where(session => session.Tool.Id == "claude-code").OrderBy(session => session.Root.Pid).ToList();
        Assert.Equal([@"D:\projects\alpha", @"D:\projects\beta"], code.Select(session => session.Root.WorkingDirectory));
        Assert.Equal([21], code[0].Descendants.Select(node => node.Pid));
        Assert.Equal(200, code[0].TotalMemoryMb);
    }

    [Fact]
    public void BuildChildren_IgnoresAParentThatReusedThePid()
    {
        ProcessNode[] nodes =
        [
            Node(5, 1, "new-owner-of-pid", startOffset: 60),
            Node(6, 5, "orphan", startOffset: 0),
        ];

        Assert.Empty(ProcessSampler.BuildChildren(nodes)[5]);
    }
}
