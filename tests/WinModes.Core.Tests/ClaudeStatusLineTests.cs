using System.Globalization;
using System.Text.Json.Nodes;
using WinModes.Core.Usage;

namespace WinModes.Core.Tests;

public sealed class ClaudeStatusLineTests : IDisposable
{
    private const string StatusJson =
        """{"model":{"display_name":"Opus"},"rate_limits":{"five_hour":{"used_percentage":23.5,"resets_at":1738425600},"seven_day":{"used_percentage":41.2,"resets_at":1738857600}}}""";

    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1738425600).AddHours(-2);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"winmodes-statusline-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Limits_AreRecordedAndShownWithThePlan()
    {
        var record = Path.Combine(_directory, "limits.json");
        var limits = ClaudeStatusLine.Parse(StatusJson, Now);
        Assert.NotNull(limits);
        ClaudeStatusLine.Save(record, limits);

        Assert.Equal("Opus  |  5 h: 77 % left  |  week: 59 % left", ClaudeStatusLine.Line(StatusJson, limits));

        var status = Subscriptions.ReadClaude(Path.Combine(_directory, "missing.json"), record);
        Assert.NotNull(status);
        Assert.Equal(new LimitWindow(23.5, 300, DateTimeOffset.FromUnixTimeSeconds(1738425600)), status.Primary);
        var (value, detail, _) = Subscriptions.Describe(status, Now, CultureInfo.InvariantCulture);
        Assert.Equal("77 % left", value);
        Assert.StartsWith("5 h limit resets in 2 h 0 min", detail, StringComparison.Ordinal);
        Assert.Contains("Weekly limit: 59 % left, resets in 5 d 2 h", detail, StringComparison.Ordinal);

        // Once the 5-hour window has reset, the weekly figure is still shown.
        var later = Subscriptions.Describe(status, Now.AddHours(3), CultureInfo.InvariantCulture);
        Assert.Equal("reset", later.Value);
        Assert.Contains("Weekly limit: 59 % left", later.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"model":{"display_name":"Opus"}}""")]
    [InlineData("""{"rate_limits":{}}""")]
    [InlineData("not json")]
    public void SessionWithoutLimits_RecordsNothing(string json) => Assert.Null(ClaudeStatusLine.Parse(json, Now));

    [Fact]
    public void Setup_AddsAndRemovesOnlyItsOwnStatusLine()
    {
        Directory.CreateDirectory(_directory);
        var settings = Path.Combine(_directory, "settings.json");
        File.WriteAllText(settings, """{"theme":"dark","hooks":{"a":[1]}}""");
        var command = ClaudeStatusLineSetup.CommandFor(@"C:\Program Files\WinModes\WinModes.StatusLine.exe", _ => @"C:\PROGRA~1\WinModes");
        Assert.Equal("C:/PROGRA~1/WinModes/WinModes.StatusLine.exe", command);

        Assert.Equal(ClaudeStatusLineSetup.State.None, ClaudeStatusLineSetup.Read(settings));
        Assert.True(ClaudeStatusLineSetup.Install(settings, command));
        Assert.Equal(ClaudeStatusLineSetup.State.Ours, ClaudeStatusLineSetup.Read(settings));
        var written = JsonNode.Parse(File.ReadAllText(settings))!;
        Assert.Equal("dark", (string?)written["theme"]);
        Assert.Equal(command, (string?)written["statusLine"]!["command"]);
        Assert.True(File.Exists(settings + ".winmodes.bak"));

        ClaudeStatusLineSetup.Remove(settings);
        Assert.Equal(ClaudeStatusLineSetup.State.None, ClaudeStatusLineSetup.Read(settings));
        Assert.Equal(1, (int?)JsonNode.Parse(File.ReadAllText(settings))!["hooks"]!["a"]![0]);
    }

    [Fact]
    public void Setup_LeavesAnotherStatusLineAlone()
    {
        Directory.CreateDirectory(_directory);
        var settings = Path.Combine(_directory, "settings.json");
        const string Original = """{"statusLine":{"type":"command","command":"~/.claude/statusline.sh"}}""";
        File.WriteAllText(settings, Original);

        Assert.Equal(ClaudeStatusLineSetup.State.Other, ClaudeStatusLineSetup.Read(settings));
        Assert.False(ClaudeStatusLineSetup.Install(settings, "D:/x/WinModes.StatusLine.exe"));
        ClaudeStatusLineSetup.Remove(settings);
        Assert.Equal(Original, File.ReadAllText(settings));
    }
}
