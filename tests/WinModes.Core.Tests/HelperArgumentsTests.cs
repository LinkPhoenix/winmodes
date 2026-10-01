using WinModes.Core.Engine;
using WinModes.Core.Tuning;

namespace WinModes.Core.Tests;

public sealed class HelperArgumentsTests
{
    private static HelperCommand? Parse(params string[] arguments) => HelperArguments.Parse(arguments);

    [Fact]
    public void Parse_AcceptsTheModeVerbs()
    {
        Assert.Equal(new HelperCommand(HelperCommandKind.Revert), Parse("revert"));
        Assert.Equal(new HelperCommand(HelperCommandKind.Apply, Mode: "code"), Parse("apply", "code"));
        Assert.Equal(HelperCommandKind.TaskInstall, Parse("task", "install")!.Kind);
        Assert.Equal(HelperCommandKind.TaskRemove, Parse("task", "remove")!.Kind);
    }

    [Theory]
    [InlineData]
    [InlineData("apply")]
    [InlineData("apply", "a", "b")]
    [InlineData("revert", "now")]
    [InlineData("task")]
    [InlineData("task", "run")]
    [InlineData("format", "c:")]
    [InlineData("change")]
    public void Parse_RefusesUnknownOrIncompleteCommands(params string[] arguments) => Assert.Null(HelperArguments.Parse(arguments));

    [Fact]
    public void Parse_FromTheTask_AcceptsOnlyAModeSwitch()
    {
        Assert.Equal(new HelperCommand(HelperCommandKind.Apply, FromTask: true, Mode: "game"), Parse("auto", "apply", "game"));
        // The task passes an empty mode for "revert".
        Assert.Equal(new HelperCommand(HelperCommandKind.Revert, FromTask: true), Parse("auto", "revert", ""));

        Assert.Null(Parse("auto", "task", "install"));
        Assert.Null(Parse("auto", "change", ":stop", "Fax"));
        Assert.Null(Parse("auto"));
    }

    [Fact]
    public void Parse_ChangeVerb_GroupsNamesUnderTheirAction()
    {
        var command = Parse("change", ":manual", "Fax", "SysMain", ":tweak", "widgets", ":restore");

        Assert.Equal(HelperCommandKind.Change, command!.Kind);
        Assert.Equal(
            [new ChangeGroup(TuneAction.Manual, ["Fax", "SysMain"]), new ChangeGroup(TuneAction.Tweak, ["widgets"]), new ChangeGroup(TuneAction.Restore, [])],
            command.Groups!,
            new GroupComparer());
    }

    [Theory]
    [InlineData("change", "Fax")]
    [InlineData("change", ":1", "Fax")]
    [InlineData("change", ":nothing", "Fax")]
    [InlineData("change", ":stop", "Fax; calc")]
    [InlineData("change", ":stop", "..\evil")]
    [InlineData("change", ":stop", "a\"b")]
    [InlineData("change", ":stop", " Fax")]
    public void Parse_ChangeVerb_RefusesNumbersUnknownActionsAndOddNames(params string[] arguments) => Assert.Null(HelperArguments.Parse(arguments));

    [Fact]
    public void Parse_ChangeVerb_CapsTheLengthAndTheCountOfNames()
    {
        Assert.Null(Parse("change", ":stop", new string('a', HelperArguments.MaxNameLength + 1)));
        Assert.NotNull(Parse("change", ":stop", new string('a', HelperArguments.MaxNameLength)));

        var many = Enumerable.Repeat("change", 1).Append(":stop").Concat(Enumerable.Range(0, HelperArguments.MaxNames + 1).Select(index => $"S{index}")).ToArray();
        Assert.Null(HelperArguments.Parse(many));
        Assert.NotNull(HelperArguments.Parse(many[..^1]));
    }

    [Theory]
    [InlineData("code", true)]
    [InlineData("postgresql-x64-18", true)]
    [InlineData("com.docker.service", true)]
    [InlineData("CDPUserSvc_1a2b3c", true)]
    [InlineData("My Mode", true)]
    [InlineData("Intel(R) Capability Licensing Service TCP IP Interface", true)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData(null, false)]
    [InlineData("a/b", false)]
    [InlineData("a;b", false)]
    [InlineData("a:b", false)]
    [InlineData("a\b", false)]
    [InlineData("é", false)]
    public void IsValidName_AllowsOnlyTheCharactersOfServiceAndModeNames(string? name, bool expected) =>
        Assert.Equal(expected, HelperArguments.IsValidName(name));

    private sealed class GroupComparer : IEqualityComparer<ChangeGroup>
    {
        public bool Equals(ChangeGroup? x, ChangeGroup? y) => x!.Action == y!.Action && x.Names.SequenceEqual(y.Names);

        public int GetHashCode(ChangeGroup obj) => obj.Action.GetHashCode();
    }
}

public sealed class SilentSwitchTaskTests
{
    [Fact]
    public void IsTrustedLocation_AcceptsOnlyAFileInsideProgramFiles()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        Assert.True(SilentSwitchTask.IsTrustedLocation(Path.Combine(programFiles, "WinModes", "WinModes.Elevated.exe")));
        Assert.False(SilentSwitchTask.IsTrustedLocation(programFiles + " Evil" + Path.DirectorySeparatorChar + "WinModes.Elevated.exe"));
        Assert.False(SilentSwitchTask.IsTrustedLocation(Path.Combine(programFiles, "..", "Users", "x", "WinModes.Elevated.exe")));
        Assert.False(SilentSwitchTask.IsTrustedLocation(@"D:\project-tools\win-modes\WinModes.Elevated.exe"));
    }

    [Theory]
    [InlineData("delete", "")]
    [InlineData("apply", "x\" /c calc")]
    public void Run_RefusesAVerbOrModeTheHelperWouldNotAccept(string verb, string mode) =>
        Assert.Null(SilentSwitchTask.Run(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "x.exe"), verb, mode));
}
