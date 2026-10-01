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
