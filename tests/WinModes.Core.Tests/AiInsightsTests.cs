using WinModes.Core.Planning;
using WinModes.Core.Usage;

namespace WinModes.Core.Tests;

public sealed class AiInsightsTests
{
    private static ProcessNode Node(int pid, string name, string? commandLine, double memoryMb = 100) =>
        new(pid, 0, name, 1, memoryMb, 0, null, commandLine, null, null);

    private static AiSession Session(int pid, params ProcessNode[] children) =>
        new(AiToolCatalog.Tools[0], Node(pid, "claude", "claude"), children);

    [Theory]
    [InlineData(@"""C:\Program Files\nodejs\node.exe"" C:\Users\someone\AppData\npm\npx-cli.js -y @modelcontextprotocol/server-github", "@modelcontextprotocol/server-github")]
    [InlineData(@"node C:\Users\someone\node_modules\@playwright\mcp\cli.js", "@playwright/mcp")]
    [InlineData(@"npx -y context7-mcp@1.2.3 --stdio", "context7-mcp")]
    [InlineData(@"C:\tools\my-mcp-server.exe --port 1", "my-mcp-server")]
    [InlineData(@"cmd /c npx --mcp-debug @upstash/context7-mcp@latest^", "@upstash/context7-mcp")]
    [InlineData(@"npx -y prisma@latest mcp", "prisma mcp")]
    [InlineData(@"docker.exe mcp gateway run --profile codex", "docker mcp")]
    [InlineData(@"node C:\Users\someone\AppData\npm-cache\node_modules\prisma\build\index.js mcp", "prisma mcp")]
    public void NameOf_KeepsThePackageAndDropsFolders(string commandLine, string expected)
    {
        var name = McpServers.NameOf(Node(1, "node", commandLine));

        Assert.Equal(expected, name);
        Assert.DoesNotContain("someone", name, StringComparison.Ordinal);
    }

    [Fact]
    public void FindDuplicates_ReportsServersRunningInSeveralSessions()
    {
        var sessions = new[]
        {
            Session(1, Node(10, "node", "npx context7-mcp", 80), Node(11, "node", "npx only-here-mcp", 50), Node(12, "cmd", "cmd /c build")),
            Session(2, Node(20, "node", "npx context7-mcp", 90)),
            Session(3, Node(30, "node", "npx context7-mcp", 30)),
        };

        var duplicate = Assert.Single(McpServers.FindDuplicates(sessions));

        Assert.Equal(new McpDuplicate("context7-mcp", 3, 3, 200), duplicate);
    }

    [Fact]
    public void CodexConfig_NamesItsServersAndCountsAWrappedServerOnce()
    {
        var definitions = CodexMcpConfig.Parse(
        [
            "[mcp_servers]",
            "[mcp_servers.context7]",
            "command = \"npx\"",
            "args = [\"-y\", \"@upstash/context7-mcp@latest\"]",
            "[mcp_servers.context7.env]",
            "command = \"not-a-command\"",
            "[mcp_servers.remote]",
            "url = \"https://example.test/mcp\"",
            "[mcp_servers.node_repl]",
            "args = []",
            @"command = 'C:\Users\someone\AppData\Local\OpenAI\Codex\bin\node_repl.exe'",
            "[projects.'d:\\somewhere']",
            "command = \"ignored\"",
        ]);
        Assert.Equal(["context7", "node_repl"], definitions.Select(definition => definition.Name));

        ProcessNode Child(int pid, int parent, string name, string commandLine) => new(pid, parent, name, 1, 100, 0, null, commandLine, null, null);
        var session = Session(1,
            Child(10, 1, "cmd", "cmd /c npx -y @upstash/context7-mcp@latest"),
            Child(11, 10, "node", @"node C:\x\npx-cli.js -y @upstash/context7-mcp@latest"),
            Child(12, 11, "node", @"node C:\x\node_modules\@upstash\context7-mcp\dist\index.js"),
            Child(20, 1, "node_repl", @"C:\Users\someone\AppData\Local\OpenAI\Codex\bin\node_repl.exe"),
            Child(30, 1, "conhost", "conhost.exe 0x4"),
            // The tool's own processes mention MCP in switches and settings: they are not servers.
            Child(40, 1, "claude", "claude.exe --type=renderer --enable-blink-features=WebMCP"),
            Child(41, 1, "claude", "claude.exe stream-json app-server -c plugins.x.mcp_servers.y.enabled=true"),
            Child(42, 1, "helper", "helper.exe --enable-features=DevToolsWebMCPSupport"));

        McpServers.Configured = definitions;
        try
        {
            var servers = McpServers.Servers(session);

            Assert.Equal(["context7", "node_repl"], servers.Select(server => server.Name));
            Assert.Equal(3, servers[0].Processes);
            Assert.Equal(300, servers[0].MemoryMb);
        }
        finally
        {
            McpServers.Configured = [];
        }

        // Without the configuration the wrapped server is still one server, named after its package.
        Assert.Equal("@upstash/context7-mcp", Assert.Single(McpServers.Servers(session)).Name);
    }

    [Fact]
    public void SystemReport_ListsTotalsWithoutFoldersOrCommandLines()
    {
        var root = new ProcessNode(1, 0, "claude", 1, 2048, 0, @"C:\Users\someone\claude.exe", "claude --secret-flag", @"C:\Users\someone\secret-project", null);
        var session = new AiSession(AiToolCatalog.Tools[0], root, [Node(2, "node", @"node C:\Users\someone\x\context7-mcp\cli.js", 512)]);

        var report = WinModes.Core.Reports.SystemReport.Build(new WinModes.Core.Reports.SystemReportData(
            "0.4.0", new DateTime(2026, 10, 1, 9, 30, 0), "Windows 11", 16, new MemorySample(32, 12, 30, 80, 6), "code", 400, 140,
            [new ProcessGroup("claude", 3, 4096, @"C:\Users\someone\claude.exe")], [session]));

        Assert.Contains("| Claude Code | 1 | 2 | 1 | 2.5 GB |", report, StringComparison.Ordinal);
        Assert.Contains("| claude | 3 | 4.0 GB |", report, StringComparison.Ordinal);
        Assert.Contains("Active mode: code", report, StringComparison.Ordinal);
        Assert.DoesNotContain("someone", report, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", report, StringComparison.Ordinal);
    }

    [Fact]
    public void UsageHistory_AveragesPeaksAndSumsTimePerProject()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winmodes-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var history = new UsageHistory(directory);
            var start = new DateTime(2026, 10, 1, 10, 0, 0);
            history.Record(start, [new("Claude Code", "alpha", 1000)]);
            history.Record(start.AddSeconds(10), [new("Claude Code", "alpha", 1000), new("Claude Code", "alpha", 500), new("Codex", "beta", 200)]);
            history.Record(start.AddSeconds(20), [new("Claude Code", "alpha", 500)]);
            // A long gap (sleep) is not counted as usage.
            history.Record(start.AddHours(2), [new("Claude Code", "alpha", 9000)]);

            var summaries = history.Summarize(new DateOnly(2026, 10, 1), 7);

            var alpha = Assert.Single(summaries, summary => summary.Project == "alpha");
            Assert.Equal(TimeSpan.FromSeconds(20), alpha.Duration);
            Assert.Equal(1000, alpha.AverageMemoryMb, 3);
            Assert.Equal(1500, alpha.PeakMemoryMb);
            Assert.Equal(TimeSpan.FromSeconds(10), Assert.Single(summaries, summary => summary.Project == "beta").Duration);

            // What was flushed is read back by a new instance.
            Assert.Equal(2, new UsageHistory(directory).Summarize(new DateOnly(2026, 10, 1), 1).Count);
            Assert.Empty(new UsageHistory(directory).Summarize(new DateOnly(2026, 9, 1), 1));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
