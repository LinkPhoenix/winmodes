using System.Globalization;
using WinModes.Core.Automation;

namespace WinModes.Core.Tests;

public sealed class AutoSwitchPlannerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly AutoSwitchTiming Timing = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(10));
    private static readonly AutoSwitchRule[] Rules = [new("cs2.exe", "game"), new("Code", "code"), new("Cursor", "code")];

    private static HashSet<string> Running(params string[] names) => new(names, StringComparer.OrdinalIgnoreCase);

    private static DateTimeOffset At(int seconds) => Start.AddSeconds(seconds);

    /// <summary>A planner on which the start delay has already passed for the given programs, so the first decision is an activation.</summary>
    private static (AutoSwitchPlanner Planner, AutoSwitchDecision Decision) Activated(string mode, params string[] running)
    {
        var planner = new AutoSwitchPlanner(Timing);
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running(running), null, false, true, At(0)).Kind);
        var decision = planner.Evaluate(Rules, Running(running), null, false, true, At(10));
        Assert.Equal(mode, decision.Mode);
        return (planner, decision);
    }

    [Fact]
    public void Normalize_StripsFolderQuotesAndExtension()
    {
        Assert.Equal("cs2", AutoSwitchPlanner.Normalize(" \"C:\\Games\\cs2.EXE\" "));
        Assert.Equal("Code", AutoSwitchPlanner.Normalize("Code"));
    }

    [Fact]
    public void Evaluate_ActivatesTheModeOfTheFirstRuleWhoseProgramRuns_AfterTheStartDelay()
    {
        var planner = new AutoSwitchPlanner(Timing);

        var early = planner.Evaluate(Rules, Running("explorer", "CS2", "code"), null, false, true, At(0));
        var later = planner.Evaluate(Rules, Running("explorer", "CS2", "code"), null, false, true, At(9));
        var decision = planner.Evaluate(Rules, Running("explorer", "CS2", "code"), null, false, true, At(10));

        Assert.Equal(AutoSwitchKind.None, early.Kind);
        Assert.Equal(AutoSwitchKind.None, later.Kind);
        Assert.Equal(new AutoSwitchDecision(AutoSwitchKind.Activate, "game", "cs2"), decision);
    }

    [Fact]
    public void Evaluate_ReportsTheWaitWhileAProgramIsAboutToStartItsMode()
    {
        var planner = new AutoSwitchPlanner(Timing);

        planner.Evaluate(Rules, Running("Code"), null, false, true, At(0));

        Assert.Equal(new AutoSwitchStatus(AutoSwitchState.Starting, "code", "Code", At(10)), planner.Status);
    }

    [Fact]
    public void Evaluate_IgnoresAProgramThatClosesBeforeTheStartDelay()
    {
        var planner = new AutoSwitchPlanner(Timing);
        planner.Evaluate(Rules, Running("cs2"), null, false, true, At(0));
        planner.Evaluate(Rules, Running(), null, false, true, At(5));

        // It comes back: the wait starts again, it does not count the first launch.
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running("cs2"), null, false, true, At(10)).Kind);
        Assert.Equal(AutoSwitchKind.Activate, planner.Evaluate(Rules, Running("cs2"), null, false, true, At(20)).Kind);
    }

    [Fact]
    public void Evaluate_DoesNothingWhenNoRuleMatches()
    {
        var planner = new AutoSwitchPlanner(Timing);

        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running("explorer"), null, false, true, At(0)).Kind);
        Assert.Equal(AutoSwitchState.Idle, planner.Status.State);
    }

    [Fact]
    public void Evaluate_ActsOncePerRunOfAProgram()
    {
        var (planner, _) = Activated("game", "cs2");

        // The user undid the mode by hand while the game is still open: do not switch again, whatever the time.
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running("cs2"), null, false, true, At(30)).Kind);
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running("cs2"), null, false, true, At(300)).Kind);
        Assert.Equal(AutoSwitchState.Declined, planner.Status.State);

        // The game was closed and started again: act again.
        planner.Evaluate(Rules, Running(), null, false, true, At(310));
        planner.Evaluate(Rules, Running("cs2"), null, false, true, At(320));
        Assert.Equal(AutoSwitchKind.Activate, planner.Evaluate(Rules, Running("cs2"), null, false, true, At(330)).Kind);
    }

    [Fact]
    public void Evaluate_RevertsOnlyAfterTheGracePeriod()
    {
        var (planner, _) = Activated("game", "cs2");

        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running("cs2"), "game", true, true, At(20)).Kind);
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running(), "game", true, true, At(30)).Kind);
        Assert.Equal(new AutoSwitchStatus(AutoSwitchState.Ending, "game", "cs2", At(90)), planner.Status);
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running(), "game", true, true, At(89)).Kind);

        Assert.Equal(new AutoSwitchDecision(AutoSwitchKind.Revert, "game", "cs2"), planner.Evaluate(Rules, Running(), "game", true, true, At(90)));
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running(), null, false, true, At(100)).Kind);
    }

    [Fact]
    public void Evaluate_CancelsTheReturnWhenAToolComesBackWithinTheGracePeriod()
    {
        var (planner, _) = Activated("code", "Code");
        planner.Evaluate(Rules, Running(), "code", true, true, At(30));

        // Someone leaves Code for Cursor within the minute: the mode never goes away.
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running("Cursor"), "code", true, true, At(50)).Kind);
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running("Cursor"), "code", true, true, At(500)).Kind);
        Assert.Equal(AutoSwitchState.Active, planner.Status.State);

        // The wait starts again from zero the next time nothing triggers the mode.
        planner.Evaluate(Rules, Running(), "code", true, true, At(510));
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running(), "code", true, true, At(560)).Kind);
        Assert.Equal(AutoSwitchKind.Revert, planner.Evaluate(Rules, Running(), "code", true, true, At(570)).Kind);
    }

    [Fact]
    public void Evaluate_KeepsAModeWhileAnyOfItsToolsRuns()
    {
        var (planner, _) = Activated("code", "Code", "Cursor");

        // The first tool closes; the second one still holds the mode, however long it takes.
        for (var t = 20; t <= 400; t += 20)
        {
            Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running("Cursor"), "code", true, true, At(t)).Kind);
        }

        Assert.Equal("Cursor", planner.Status.Trigger);
    }

    [Fact]
    public void Evaluate_NeverTouchesAModeChosenByHand()
    {
        var planner = new AutoSwitchPlanner(Timing);

        // The user switched to Work by hand: opening a tool does not replace it, and closing it does not undo it.
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running("Code"), "work", false, true, At(0)).Kind);
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running("Code"), "work", false, true, At(100)).Kind);
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running(), "work", false, true, At(200)).Kind);
        Assert.Equal(new AutoSwitchStatus(AutoSwitchState.Manual, "work"), planner.Status);
    }

    [Fact]
    public void Evaluate_LeavesAModeChosenByHandAloneEvenWhenItIsTheSameMode()
    {
        var planner = new AutoSwitchPlanner(Timing);

        planner.Evaluate(Rules, Running("Code"), "code", false, true, At(0));

        // Code was started by hand; when the editor closes it is not ours to undo.
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running(), "code", false, true, At(1000)).Kind);
    }

    [Fact]
    public void Evaluate_KeepsTheModeOfAHigherRuleWhileItsProgramRuns()
    {
        var (planner, _) = Activated("game", "cs2", "Code");

        // Both programs still run; the lower rule must not override the game's mode, however many checks pass.
        for (var t = 20; t <= 200; t += 20)
        {
            Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running("cs2", "Code"), "game", true, true, At(t)).Kind);
        }
    }

    [Fact]
    public void Evaluate_DoesNotLetALowerRuleThatStartsLaterOverrideAHigherOne()
    {
        var (planner, _) = Activated("game", "cs2");

        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running("cs2", "Code"), "game", true, true, At(20)).Kind);
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running("cs2", "Code"), "game", true, true, At(200)).Kind);
    }

    [Fact]
    public void Evaluate_HandsOverToALowerRuleOnlyAfterTheGracePeriodOfTheHigherOne()
    {
        var (planner, _) = Activated("game", "cs2", "Code");
        planner.Evaluate(Rules, Running("cs2", "Code"), "game", true, true, At(20));

        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running("Code"), "game", true, true, At(30)).Kind);
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running("Code"), "game", true, true, At(89)).Kind);
        Assert.Equal(new AutoSwitchDecision(AutoSwitchKind.Activate, "code", "Code"), planner.Evaluate(Rules, Running("Code"), "game", true, true, At(90)));
    }

    [Fact]
    public void Evaluate_HandsOverToAHigherRuleThatStartsLater_AfterTheStartDelay()
    {
        var (planner, _) = Activated("code", "Code");

        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running("Code", "cs2"), "code", true, true, At(20)).Kind);
        Assert.Equal(new AutoSwitchDecision(AutoSwitchKind.Activate, "game", "cs2"), planner.Evaluate(Rules, Running("Code", "cs2"), "code", true, true, At(30)));
    }

    [Fact]
    public void Evaluate_LetsALowerRuleActAfterTheUserLeftTheHigherMode()
    {
        var (planner, _) = Activated("game", "cs2");

        // The user switched to another mode by hand; the game's mode is no longer ours to protect.
        planner.Evaluate(Rules, Running("cs2", "Code"), "work", false, true, At(20));
        planner.Evaluate(Rules, Running("cs2", "Code"), null, false, true, At(30));
        Assert.Equal("code", planner.Evaluate(Rules, Running("cs2", "Code"), null, false, true, At(40)).Mode);
    }

    [Theory]
    [InlineData("09:00", "18:00", "09:00", true)]
    [InlineData("09:00", "18:00", "17:59", true)]
    [InlineData("09:00", "18:00", "18:00", false)]
    [InlineData("22:00", "06:00", "23:30", true)]
    [InlineData("22:00", "06:00", "05:00", true)]
    [InlineData("22:00", "06:00", "12:00", false)]
    public void Schedule_IsActiveInsideItsRange_IncludingAcrossMidnight(string from, string to, string now, bool expected)
    {
        var key = AutoSwitchConditions.Schedule(TimeOnly.Parse(from, CultureInfo.InvariantCulture), TimeOnly.Parse(to, CultureInfo.InvariantCulture));

        Assert.Equal(expected, AutoSwitchConditions.IsScheduleActive(key, TimeOnly.Parse(now, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Conditions_DriveTheSameRulesAsPrograms()
    {
        AutoSwitchRule[] rules = [new(AutoSwitchConditions.Battery, "work"), new(AutoSwitchConditions.Schedule(new(9, 0), new(18, 0)), "code")];
        var planner = new AutoSwitchPlanner(Timing);

        var onBattery = Running([.. AutoSwitchConditions.ActiveKeys(rules, onBattery: true, new TimeOnly(20, 0))]);
        planner.Evaluate(rules, onBattery, null, false, true, At(0));
        Assert.Equal(new AutoSwitchDecision(AutoSwitchKind.Activate, "work", AutoSwitchConditions.Battery), planner.Evaluate(rules, onBattery, null, false, true, At(10)));

        // Plugged in again, outside the time range: the mode it activated is undone after the grace period.
        var pluggedIn = Running([.. AutoSwitchConditions.ActiveKeys(rules, onBattery: false, new TimeOnly(20, 0))]);
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(rules, pluggedIn, "work", true, true, At(20)).Kind);
        Assert.Equal(AutoSwitchKind.Revert, planner.Evaluate(rules, pluggedIn, "work", true, true, At(80)).Kind);

        Assert.Equal("On battery", AutoSwitchConditions.Describe(AutoSwitchConditions.Battery));
        Assert.Equal("From 09:00 to 18:00", AutoSwitchConditions.Describe(rules[1].Process));
        Assert.False(AutoSwitchConditions.TryParseSchedule("@time 25:00-18:00", out _, out _));
    }

    [Fact]
    public void Evaluate_KeepsTheModeWhenRevertIsOff()
    {
        var (planner, _) = Activated("game", "cs2");

        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running(), "game", true, false, At(20)).Kind);
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running(), "game", true, false, At(2000)).Kind);
        Assert.Equal(AutoSwitchState.Kept, planner.Status.State);
    }

    [Fact]
    public void Evaluate_RevertsAModeStartedBeforeARestartOnceItsToolsAreGone()
    {
        // A new planner (the app was restarted) finds a mode that automatic switching started and nothing to hold it.
        var planner = new AutoSwitchPlanner(Timing);

        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running("explorer"), "code", true, true, At(0)).Kind);
        var decision = planner.Evaluate(Rules, Running("explorer"), "code", true, true, At(60));

        Assert.Equal(AutoSwitchKind.Revert, decision.Kind);
        Assert.Equal("code", decision.Mode);
        Assert.Null(decision.Process);
    }

    [Fact]
    public void Hold_KeepsAutomaticSwitchingQuietUntilTheTimeHasPassed()
    {
        var planner = new AutoSwitchPlanner(Timing);
        planner.HoldUntil(At(30));

        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running("Code"), null, false, true, At(0)).Kind);
        Assert.Equal(new AutoSwitchStatus(AutoSwitchState.Waiting, null, null, At(30)), planner.Status);
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running("Code"), null, false, true, At(35)).Kind);
        Assert.Equal(AutoSwitchKind.Activate, planner.Evaluate(Rules, Running("Code"), null, false, true, At(45)).Kind);
    }

    [Fact]
    public void SwitchFailed_StopsAnotherAttemptForTheFailureHold()
    {
        var (planner, _) = Activated("code", "Code");

        // The permission prompt was refused: nothing is active, and nothing is tried again soon, even for a new tool.
        planner.SwitchFailed(At(12));
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running("Code"), null, false, true, At(20)).Kind);
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running("Code", "Cursor"), null, false, true, At(300)).Kind);
        Assert.Equal(AutoSwitchState.Waiting, planner.Status.State);
        planner.Evaluate(Rules, Running("Cursor"), null, false, true, At(700));
        Assert.Equal(AutoSwitchKind.Activate, planner.Evaluate(Rules, Running("Cursor"), null, false, true, At(720)).Kind);
    }

    [Fact]
    public void Pause_StopsEverythingUntilItEnds_AndTheModeThatIsOnStaysOn()
    {
        var (planner, _) = Activated("code", "Code");
        planner.PauseUntil(At(600));

        // Nothing triggers the mode any more, but while paused it is neither ended nor replaced.
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running(), "code", true, true, At(100)).Kind);
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running("cs2"), "code", true, true, At(500)).Kind);
        Assert.Equal(new AutoSwitchStatus(AutoSwitchState.Paused, "code", Until: At(600)), planner.Status);

        // Once the pause is over the grace period starts from then, not from the time the tool closed.
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running(), "code", true, true, At(600)).Kind);
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running(), "code", true, true, At(659)).Kind);
        Assert.Equal(AutoSwitchKind.Revert, planner.Evaluate(Rules, Running(), "code", true, true, At(660)).Kind);
    }

    [Fact]
    public void Pause_WithoutAnEndLastsUntilItIsResumed()
    {
        var planner = new AutoSwitchPlanner(Timing);
        planner.PauseUntil(DateTimeOffset.MaxValue);

        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(Rules, Running("Code"), null, false, true, At(100000)).Kind);
        Assert.Null(planner.Status.Until);

        planner.PauseUntil(null);
        planner.Evaluate(Rules, Running("Code"), null, false, true, At(100010));
        Assert.Equal(AutoSwitchKind.Activate, planner.Evaluate(Rules, Running("Code"), null, false, true, At(100020)).Kind);
    }

    [Fact]
    public void Tool_RulesFollowTheSessionsOfAnAiTool()
    {
        AutoSwitchRule[] rules = [new(AutoSwitchConditions.Tool("claude-code"), "code"), new(AutoSwitchConditions.Tool("codex"), "code")];
        var planner = new AutoSwitchPlanner(Timing);

        IReadOnlySet<string> Keys(params string[] tools) => Running([.. AutoSwitchConditions.ActiveKeys(rules, false, new TimeOnly(12, 0), new HashSet<string>(tools))]);

        planner.Evaluate(rules, Keys("claude-code"), null, false, true, At(0));
        Assert.Equal(new AutoSwitchDecision(AutoSwitchKind.Activate, "code", "@tool claude-code"), planner.Evaluate(rules, Keys("claude-code"), null, false, true, At(10)));

        // Codex opens, Claude Code closes: the mode stays; it ends a minute after the last one.
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(rules, Keys("claude-code", "codex"), "code", true, true, At(30)).Kind);
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(rules, Keys("codex"), "code", true, true, At(40)).Kind);
        Assert.Equal(AutoSwitchKind.None, planner.Evaluate(rules, Keys("codex"), "code", true, true, At(1000)).Kind);
        planner.Evaluate(rules, Keys(), "code", true, true, At(1010));
        Assert.Equal(AutoSwitchKind.Revert, planner.Evaluate(rules, Keys(), "code", true, true, At(1070)).Kind);
    }

    [Fact]
    public void Tool_KeysAreBuiltAndParsedAndDescribedByName()
    {
        Assert.True(AutoSwitchConditions.TryParseTool(AutoSwitchConditions.Tool("claude-code"), out var id));
        Assert.Equal("claude-code", id);
        Assert.False(AutoSwitchConditions.TryParseTool("@battery", out _));
        Assert.False(AutoSwitchConditions.TryParseTool("@tool ", out _));
        Assert.Equal("Claude Code is running", AutoSwitchConditions.Describe(AutoSwitchConditions.Tool("claude-code")));
        Assert.Equal("Claude Code", AutoSwitchConditions.Name(AutoSwitchConditions.Tool("claude-code")));
        Assert.True(AutoSwitchConditions.NeedsTools([new(AutoSwitchConditions.Tool("codex"), "code")]));
        Assert.False(AutoSwitchConditions.NeedsTools([new("cs2", "game")]));
        Assert.All(AutoSwitchConditions.DefaultCodingToolIds, tool => Assert.Contains(tool, AutoSwitchConditions.CodingToolIds));
    }
}
