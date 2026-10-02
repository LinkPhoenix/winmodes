using System.Text.Json;
using WinModes.Core.Usage.Tokens;

namespace WinModes.Core.Tests;

public sealed class TokenStatsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"winmodes-tokens-{Guid.NewGuid():N}");

    public TokenStatsTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static string ClaudeLine(string messageId, string requestId, int output, string timestamp = "2026-10-02T09:00:00.000Z", string model = "claude-sonnet-5-5", string cwd = @"D:\project-tools\win-modes") =>
        JsonSerializer.Serialize(new
        {
            type = "assistant",
            timestamp,
            requestId,
            cwd,
            message = new
            {
                id = messageId,
                model,
                role = "assistant",
                content = new[] { new { type = "text", text = "hello" } },
                usage = new { input_tokens = 2, cache_creation_input_tokens = 100, cache_read_input_tokens = 1000, output_tokens = output },
            },
        });

    private string Claude(string name, params string[] lines)
    {
        var path = Path.Combine(_folder, "claude", "p", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
        return path;
    }

    private string Codex(string name, params string[] lines)
    {
        var path = Path.Combine(_folder, "codex", "sessions", "2026", "10", "02", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
        return path;
    }

    private static string CodexMeta(string cwd = @"D:\project-nextjs\shop") =>
        JsonSerializer.Serialize(new { timestamp = "2026-10-02T08:00:00Z", type = "session_meta", payload = new { id = "abc", cwd } });

    private static string CodexTurn(string model) =>
        JsonSerializer.Serialize(new { timestamp = "2026-10-02T08:00:01Z", type = "turn_context", payload = new { model, cwd = "x" } });

    private static string CodexTotal(int input, int cached, int write, int output, string timestamp = "2026-10-02T08:05:00Z") =>
        JsonSerializer.Serialize(new
        {
            timestamp,
            type = "event_msg",
            payload = new
            {
                type = "token_count",
                info = new
                {
                    total_token_usage = new
                    {
                        input_tokens = input,
                        cached_input_tokens = cached,
                        cache_write_input_tokens = write,
                        output_tokens = output,
                        reasoning_output_tokens = 3,
                        total_tokens = input + output,
                    },
                },
            },
        });

    private TokenIndex Scan(TokenIndex? index = null)
    {
        index ??= new TokenIndex();
        index.Update(Path.Combine(_folder, "claude"), Path.Combine(_folder, "codex"), Now);
        return index;
    }

    [Fact]
    public void ClaudeLine_GivesTokensModelDayAndProject()
    {
        var found = ClaudeTokenLog.Parse(ClaudeLine("msg_1", "req_1", 50));

        Assert.NotNull(found);
        Assert.Equal("Claude", found.Tool);
        Assert.Equal("claude-sonnet-5-5", found.Model);
        Assert.Equal("win-modes", found.Project);
        Assert.Equal(new TokenCounts(2, 50, 1000, 100), found.Counts);
        Assert.Equal(1152, found.Counts.Total);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"type":"user","message":{"role":"user","content":"hi"}}""")]
    [InlineData("""{"type":"assistant","timestamp":"2026-10-02T09:00:00Z","requestId":"r","message":{"id":"m","model":"<synthetic>","usage":{"input_tokens":1,"output_tokens":1}}}""")]
    [InlineData("""{"type":"assistant","timestamp":"2026-10-02T09:00:00Z","message":{"model":"claude-x","usage":{"input_tokens":1,"output_tokens":1}}}""")]
    [InlineData("""{"type":"assistant","timestamp":"2026-10-02T09:00:00Z","requestId":"r","message":{"id":"m","model":"claude-x","usage":{"input_tokens":0,"output_tokens":0}}}""")]
    public void ClaudeLine_WithoutRealUsageIsIgnored(string line) => Assert.Null(ClaudeTokenLog.Parse(line));

    [Fact]
    public void ClaudeRequestLoggedSeveralTimes_CountsOnceWithItsFinalOutput()
    {
        // One answer written once per block, the output growing; a resumed session copies it into another file.
        Claude("a.jsonl", ClaudeLine("msg_1", "req_1", 10), ClaudeLine("msg_1", "req_1", 400), ClaudeLine("msg_2", "req_2", 5));
        Claude("b.jsonl", ClaudeLine("msg_1", "req_1", 400));

        var summary = Assert.Single(Scan().Summarize(new DateOnly(2026, 10, 2), 1));

        Assert.Equal("Claude", summary.Tool);
        Assert.Equal(new TokenCounts(4, 405, 2000, 200), summary.Total);
    }

    [Fact]
    public void CodexTotals_BecomeRequestsByDifference_AndAnIdenticalTotalIsNotARequest()
    {
        Codex("rollout-1.jsonl", CodexMeta(), CodexTurn("gpt-5"),
            CodexTotal(1000, 400, 0, 50), CodexTotal(1000, 400, 0, 50), CodexTotal(3000, 1500, 100, 200));

        var summary = Assert.Single(Scan().Summarize(new DateOnly(2026, 10, 2), 1));

        // Input is what is neither cached nor written: 3000 - 1500 - 100.
        Assert.Equal("Codex", summary.Tool);
        Assert.Equal(new TokenCounts(1400, 200, 1500, 100), summary.Total);
        Assert.Equal("gpt-5", Assert.Single(summary.Models).Name);
        Assert.Equal("shop", Assert.Single(summary.Projects).Name);
    }

    [Fact]
    public void CodexTotalThatGoesDown_IsANewCount()
    {
        Codex("rollout-1.jsonl", CodexMeta(), CodexTurn("gpt-5"), CodexTotal(2000, 0, 0, 100), CodexTotal(300, 0, 0, 20));

        var summary = Assert.Single(Scan().Summarize(new DateOnly(2026, 10, 2), 1));

        Assert.Equal(2000 + 300, summary.Total.Input);
        Assert.Equal(120, summary.Total.Output);
    }

    [Fact]
    public void ALaterScan_ReadsOnlyWhatWasAdded_ForBothTools()
    {
        var claude = Claude("a.jsonl", ClaudeLine("msg_1", "req_1", 10));
        var codex = Codex("rollout-1.jsonl", CodexMeta(), CodexTurn("gpt-5"), CodexTotal(1000, 0, 0, 100));
        var index = Scan();

        File.AppendAllLines(claude, [ClaudeLine("msg_2", "req_2", 20)]);
        File.AppendAllLines(codex, [CodexTotal(1500, 0, 0, 160)]);
        var read = index.Update(Path.Combine(_folder, "claude"), Path.Combine(_folder, "codex"), Now);

        Assert.Equal(2, read);
        var summaries = index.Summarize(new DateOnly(2026, 10, 2), 1).ToDictionary(summary => summary.Tool);
        Assert.Equal(30, summaries["Claude"].Total.Output);
        Assert.Equal(new TokenCounts(1500, 160, 0, 0), summaries["Codex"].Total);

        // Nothing changed: nothing is read.
        Assert.Equal(0, index.Update(Path.Combine(_folder, "claude"), Path.Combine(_folder, "codex"), Now));
    }

    [Fact]
    public void ALineStillBeingWritten_IsReadOnceItIsComplete()
    {
        var path = Claude("a.jsonl", ClaudeLine("msg_1", "req_1", 10));
        var half = ClaudeLine("msg_2", "req_2", 20);
        File.AppendAllText(path, half[..40]);
        var index = Scan();
        Assert.Equal(10, Assert.Single(index.Summarize(new DateOnly(2026, 10, 2), 1)).Total.Output);

        File.AppendAllText(path, half[40..] + "\n");
        index.Update(Path.Combine(_folder, "claude"), Path.Combine(_folder, "codex"), Now);

        Assert.Equal(30, Assert.Single(index.Summarize(new DateOnly(2026, 10, 2), 1)).Total.Output);
    }

    [Fact]
    public void Periods_SplitByLocalDay_AndListEveryDay()
    {
        var today = DateOnly.FromDateTime(Now.LocalDateTime);
        var stamp = (DateTimeOffset day) => day.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        Claude("a.jsonl",
            ClaudeLine("msg_1", "req_1", 10, stamp(Now)),
            ClaudeLine("msg_2", "req_2", 100, stamp(Now.AddDays(-3))),
            ClaudeLine("msg_3", "req_3", 1000, stamp(Now.AddDays(-20))));
        var index = Scan();

        Assert.Equal(10, Assert.Single(index.Summarize(today, 1)).Total.Output);
        var week = Assert.Single(index.Summarize(today, 7));
        Assert.Equal(110, week.Total.Output);
        Assert.Equal(7, week.Days.Count);
        Assert.Equal(today, week.Days[^1].Day);
        Assert.Equal(100, week.Days[^4].Counts.Output);
        Assert.Equal(1110, Assert.Single(index.Summarize(today, 30)).Total.Output);
    }

    [Fact]
    public void Summary_RanksModelsAndProjectsByTokens()
    {
        Claude("a.jsonl",
            ClaudeLine("msg_1", "req_1", 10, model: "claude-haiku-4-5", cwd: @"D:\a\small"),
            ClaudeLine("msg_2", "req_2", 900, model: "claude-sonnet-5-5", cwd: @"D:\a\big"));

        var summary = Assert.Single(Scan().Summarize(new DateOnly(2026, 10, 2), 1));

        Assert.Equal(["claude-sonnet-5-5", "claude-haiku-4-5"], summary.Models.Select(model => model.Name));
        Assert.Equal(["big", "small"], summary.Projects.Select(project => project.Name));
    }

    [Fact]
    public void OldFilesAreIgnored_AndFilesThatDisappearAreForgotten()
    {
        var old = Claude("old.jsonl", ClaudeLine("msg_1", "req_1", 10));
        File.SetLastWriteTimeUtc(old, Now.AddDays(-40).UtcDateTime);
        var recent = Claude("recent.jsonl", ClaudeLine("msg_2", "req_2", 20));
        var index = Scan();

        Assert.Equal(20, Assert.Single(index.Summarize(new DateOnly(2026, 10, 2), 30)).Total.Output);

        File.Delete(recent);
        Scan(index);
        Assert.True(index.IsEmpty);
    }

    [Fact]
    public void SavedIndex_ComesBackWithoutReadingTheLogsAgain()
    {
        Claude("a.jsonl", ClaudeLine("msg_1", "req_1", 10));
        Codex("rollout-1.jsonl", CodexMeta(), CodexTurn("gpt-5"), CodexTotal(1000, 300, 0, 100));
        var index = Scan();
        var path = Path.Combine(_folder, "index.json");
        index.Save(path);

        var restored = TokenIndex.Load(path);

        Assert.False(restored.IsEmpty);
        Assert.Equal(0, restored.Update(Path.Combine(_folder, "claude"), Path.Combine(_folder, "codex"), Now));
        var summaries = restored.Summarize(new DateOnly(2026, 10, 2), 1).ToDictionary(summary => summary.Tool);
        Assert.Equal(new TokenCounts(2, 10, 1000, 100), summaries["Claude"].Total);
        Assert.Equal(new TokenCounts(700, 100, 300, 0), summaries["Codex"].Total);
    }

    [Fact]
    public void Load_IgnoresAMissingOrDamagedFile()
    {
        Assert.True(TokenIndex.Load(Path.Combine(_folder, "missing.json")).IsEmpty);
        var damaged = Path.Combine(_folder, "damaged.json");
        File.WriteAllText(damaged, "{ nope");
        Assert.True(TokenIndex.Load(damaged).IsEmpty);
        File.WriteAllText(damaged, """{"Version":99}""");
        Assert.True(TokenIndex.Load(damaged).IsEmpty);
    }

    [Fact]
    public void Update_ReportsProgress_AndCanBeCancelled()
    {
        Claude("a.jsonl", ClaudeLine("msg_1", "req_1", 10));
        Claude("b.jsonl", ClaudeLine("msg_2", "req_2", 10));
        var steps = new List<TokenScanProgress>();
        new TokenIndex().Update(Path.Combine(_folder, "claude"), Path.Combine(_folder, "codex"), Now, new Progress<TokenScanProgress>(steps.Add));
        Assert.Equal(2, new TokenIndex().Update(Path.Combine(_folder, "claude"), Path.Combine(_folder, "codex"), Now));

        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => new TokenIndex().Update(Path.Combine(_folder, "claude"), Path.Combine(_folder, "codex"), Now, null, source.Token));
    }
}
