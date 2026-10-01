using WinModes.Core.Automation;

namespace WinModes.Core.Tests;

public sealed class AutoSwitchPlannerTests
{
    private static readonly AutoSwitchRule[] Rules = [new("cs2.exe", "game"), new("Code", "code")];

    private static HashSet<string> Running(params string[] names) => new(names, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Normalize_StripsFolderQuotesAndExtension()
    {
        Assert.Equal("cs2", AutoSwitchPlanner.Normalize(" \"C:\\Games\\cs2.EXE\" "));
        Assert.Equal("Code", AutoSwitchPlanner.Normalize("Code"));
    }

    [Fact]
    public void Evaluate_ActivatesTheModeOfTheFirstRuleWhoseProgramRuns()
    {
        var planner = new AutoSwitchPlanner();

        var decision = planner.Evaluate(Rules, Running("explorer", "CS2", "code"), activeMode: null, revertWhenClosed: true);

        Assert.Equal(new AutoSwitchDecision(AutoSwitchKind.Activate, "game", "cs2"), decision);
    }

    [Fact]
    public void Evaluate_DoesNothingWhenNoRuleMatchesOrTheModeIsAlreadyActive()
    {
        var planner = new AutoSwitchPlanner();

        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running("explorer"), null, true).Kind);
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running("cs2"), "game", true).Kind);
    }

    [Fact]
    public void Evaluate_ActsOncePerRunOfAProgram()
    {
        var planner = new AutoSwitchPlanner();
        planner.Evaluate(Rules, Running("cs2"), null, true);

        // The user undid the mode by hand while the game is still open: do not switch again.
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running("cs2"), null, true).Kind);

        // The game was closed and started again: act again.
        planner.Evaluate(Rules, Running(), null, true);
        Assert.Equal(AutoSwitchKind.Activate, planner.Evaluate(Rules, Running("cs2"), null, true).Kind);
    }

    [Fact]
    public void Evaluate_RevertsItsOwnModeWhenTheProgramExits()
    {
        var planner = new AutoSwitchPlanner();
        planner.Evaluate(Rules, Running("cs2"), null, true);

        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running("cs2"), "game", true).Kind);
        Assert.Equal(new AutoSwitchDecision(AutoSwitchKind.Revert, "game", "cs2"), planner.Evaluate(Rules, Running(), "game", true));
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running(), null, true).Kind);
    }

    [Fact]
    public void Evaluate_NeverRevertsAModeItDidNotActivate()
    {
        var planner = new AutoSwitchPlanner();
        planner.Evaluate(Rules, Running("cs2"), null, true);

        // The user switched to another mode by hand in the meantime.
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running(), "work", true).Kind);
    }

    [Fact]
    public void Evaluate_KeepsTheModeWhenRevertIsOff()
    {
        var planner = new AutoSwitchPlanner();
        planner.Evaluate(Rules, Running("cs2"), null, false);

        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running(), "game", false).Kind);
    }
}
