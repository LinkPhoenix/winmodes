using System.Globalization;
using WinModes.Core.Notifications;
using WinModes.Core.Usage;

namespace WinModes.Core.Tests;

public sealed class PlanNoticesTests : IDisposable
{
    private const int FiveHours = 300;
    private const int Week = 10080;
    private const double Low = 10;
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly PlanNoticeChoice Everything = new(true, true, true, true);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"winmodes-notices-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static SubscriptionStatus Codex(double used, DateTimeOffset? resetsAt, int? credits = null, double? weeklyUsed = null) =>
        new("Codex", "Pro", new LimitWindow(used, FiveHours, resetsAt),
            weeklyUsed is { } weekly ? new LimitWindow(weekly, Week, Now.AddDays(3)) : null, Now, credits);

    private static IReadOnlyList<PlanNotice> Run(NoticeLedger ledger, SubscriptionStatus status, DateTimeOffset now, PlanNoticeChoice? choice = null) =>
        PlanNoticeEngine.Evaluate(ledger, [status], _ => choice ?? Everything, Low, now);

    [Fact]
    public void LowLimit_IsAnnouncedOncePerCycle_EvenAfterARestart()
    {
        var ledger = new NoticeLedger();
        var status = Codex(95, Now.AddHours(2));

        Assert.Equal(PlanNoticeKind.Low, Assert.Single(Run(ledger, status, Now)).Kind);
        Assert.Empty(Run(ledger, status, Now.AddMinutes(1)));

        // The app was closed and started again: the ledger comes back from disk.
        var path = Path.Combine(_folder, "notices.json");
        ledger.Save(path);
        var restored = NoticeLedger.Load(path);

        Assert.Empty(Run(restored, Codex(96, Now.AddHours(2)), Now.AddMinutes(30)));
    }

    [Fact]
    public void UsedUpLimit_IsAnnouncedOnceAndNotAgainAtStart()
    {
        var ledger = new NoticeLedger();
        var status = Codex(100, Now.AddHours(3));

        var notice = Assert.Single(Run(ledger, status, Now));
        Assert.Equal(PlanNoticeKind.Reached, notice.Kind);

        var path = Path.Combine(_folder, "notices.json");
        ledger.Save(path);
        Assert.Empty(Run(NoticeLedger.Load(path), status, Now.AddMinutes(5)));
    }

    [Fact]
    public void LowThenUsedUp_AreTwoNotices()
    {
        var ledger = new NoticeLedger();

        Assert.Equal(PlanNoticeKind.Low, Assert.Single(Run(ledger, Codex(92, Now.AddHours(2)), Now)).Kind);
        Assert.Equal(PlanNoticeKind.Reached, Assert.Single(Run(ledger, Codex(100, Now.AddHours(2)), Now.AddMinutes(20))).Kind);
        Assert.Empty(Run(ledger, Codex(100, Now.AddHours(2)), Now.AddMinutes(40)));
    }

    [Fact]
    public void ResetTimeJitter_IsNotANewCycle()
    {
        var ledger = new NoticeLedger();
        Run(ledger, Codex(100, Now.AddHours(2)), Now);

        Assert.Empty(Run(ledger, Codex(100, Now.AddHours(2).AddSeconds(40)), Now.AddMinutes(1)));
    }

    [Fact]
    public void ResetAfterUsedUp_IsAnnouncedOnce_ThenTheNextCycleCanWarnAgain()
    {
        var ledger = new NoticeLedger();
        var end = Now.AddHours(1);
        Run(ledger, Codex(100, end), Now);

        // The clock passed the reset but no new figure was read yet.
        var afterReset = Run(ledger, Codex(100, end), end.AddMinutes(1));
        Assert.Equal(PlanNoticeKind.Reset, Assert.Single(afterReset).Kind);
        Assert.Empty(Run(ledger, Codex(100, end), end.AddMinutes(2)));

        // The next cycle shows up and fills again: a new warning is allowed.
        var next = end.AddHours(5);
        Assert.Empty(Run(ledger, Codex(10, next), end.AddMinutes(3)));
        Assert.Equal(PlanNoticeKind.Low, Assert.Single(Run(ledger, Codex(95, next), end.AddHours(3))).Kind);
    }

    [Fact]
    public void ResetSeenInANewFigure_IsAnnouncedWithoutWaitingForTheClock()
    {
        var ledger = new NoticeLedger();
        Run(ledger, Codex(100, Now.AddHours(1)), Now);

        var notices = Run(ledger, Codex(0, Now.AddHours(6)), Now.AddMinutes(30));

        // Not past the old reset yet, but the figure already belongs to the next cycle.
        Assert.Equal(PlanNoticeKind.Reset, Assert.Single(notices).Kind);
    }

    [Fact]
    public void ResetLongAgo_IsNotAnnounced()
    {
        var ledger = new NoticeLedger();
        Run(ledger, Codex(100, Now.AddHours(1)), Now);

        var notices = Run(ledger, Codex(5, Now.AddHours(9)), Now.AddHours(6));

        Assert.Empty(notices);
        Assert.Empty(ledger.Windows);
    }

    [Fact]
    public void LowWithoutUsedUpAlert_IsNotFollowedByAResetNotice()
    {
        var ledger = new NoticeLedger();
        var end = Now.AddHours(1);
        Run(ledger, Codex(95, end), Now);

        Assert.Empty(Run(ledger, Codex(0, end.AddHours(5)), end.AddMinutes(1)));
    }

    [Fact]
    public void AFigureFromBeforeTheReset_AnnouncesNothing()
    {
        var ledger = new NoticeLedger();

        Assert.Empty(Run(ledger, Codex(100, Now.AddHours(-1)), Now));
        Assert.Empty(ledger.Windows);
    }

    [Fact]
    public void ChoicesTurnNoticesOff()
    {
        var ledger = new NoticeLedger();

        Assert.Empty(Run(ledger, Codex(100, Now.AddHours(2)), Now, new PlanNoticeChoice(false, false, false, false)));
        Assert.Empty(ledger.Windows);
    }

    [Fact]
    public void UsedUp_FallsBackToTheLowNoticeWhenOnlyThatIsWanted()
    {
        var ledger = new NoticeLedger();

        var notice = Assert.Single(Run(ledger, Codex(100, Now.AddHours(2)), Now, new PlanNoticeChoice(true, false, false, false)));
        Assert.Equal(PlanNoticeKind.Low, notice.Kind);
    }

    [Fact]
    public void EachWindowHasItsOwnMarks()
    {
        var ledger = new NoticeLedger();

        var notices = Run(ledger, Codex(10, Now.AddHours(2), weeklyUsed: 97), Now);

        var notice = Assert.Single(notices);
        Assert.Equal(Week, notice.Window!.WindowMinutes);
        Assert.Empty(Run(ledger, Codex(10, Now.AddHours(2), weeklyUsed: 97), Now.AddMinutes(1)));
    }

    [Fact]
    public void ResetCredits_FirstFigureIsASilentBaseline_ARiseIsAnnounced()
    {
        var ledger = new NoticeLedger();

        Assert.Empty(Run(ledger, Codex(10, Now.AddHours(2), credits: 1), Now));
        Assert.Empty(Run(ledger, Codex(10, Now.AddHours(2), credits: 1), Now.AddMinutes(5)));

        var notice = Assert.Single(Run(ledger, Codex(10, Now.AddHours(2), credits: 2), Now.AddMinutes(10)));
        Assert.Equal(PlanNoticeKind.CreditGained, notice.Kind);
        Assert.Equal(2, notice.Credits);

        // A credit spent is not news; the next one gained is.
        Assert.Empty(Run(ledger, Codex(10, Now.AddHours(2), credits: 1), Now.AddMinutes(15)));
        Assert.Single(Run(ledger, Codex(10, Now.AddHours(2), credits: 2), Now.AddMinutes(20)));
    }

    [Fact]
    public void ResetCredits_AreNotAnnouncedAgainAfterARestart()
    {
        var ledger = new NoticeLedger();
        Run(ledger, Codex(10, Now.AddHours(2), credits: 1), Now);
        Run(ledger, Codex(10, Now.AddHours(2), credits: 2), Now.AddMinutes(1));
        var path = Path.Combine(_folder, "notices.json");
        ledger.Save(path);

        Assert.Empty(Run(NoticeLedger.Load(path), Codex(10, Now.AddHours(2), credits: 2), Now.AddHours(1)));
    }

    [Fact]
    public void ResetCredits_UnknownKeepsTheLastFigure()
    {
        var ledger = new NoticeLedger();
        Run(ledger, Codex(10, Now.AddHours(2), credits: 3), Now);

        Assert.Empty(Run(ledger, Codex(10, Now.AddHours(2)), Now.AddMinutes(5)));
        Assert.Empty(Run(ledger, Codex(10, Now.AddHours(2), credits: 3), Now.AddMinutes(10)));
    }

    [Fact]
    public void Ledger_ReportsChangesAndSurvivesADamagedFile()
    {
        var ledger = new NoticeLedger();
        Assert.False(ledger.Changed);
        Run(ledger, Codex(100, Now.AddHours(2)), Now);
        Assert.True(ledger.Changed);

        var path = Path.Combine(_folder, "notices.json");
        ledger.Save(path);
        Assert.False(ledger.Changed);

        File.WriteAllText(path, "{ not json");
        Assert.Empty(NoticeLedger.Load(path).Windows);
        Assert.Empty(NoticeLedger.Load(Path.Combine(_folder, "missing.json")).Windows);
    }

    [Fact]
    public void Texts_SayWhichLimitAndWhenItResets()
    {
        var english = CultureInfo.InvariantCulture;
        var window = new LimitWindow(95, FiveHours, Now.AddHours(2).AddMinutes(10));

        var low = new PlanNotice(PlanNoticeKind.Low, "Codex", window);
        Assert.Equal("Codex is running low", low.Title);
        Assert.StartsWith("5 h limit: 5 % left. It resets in 2 h 10 min", low.Message(Now, english), StringComparison.Ordinal);

        var reached = new PlanNotice(PlanNoticeKind.Reached, "Claude", window with { UsedPercent = 100 });
        Assert.Equal("Claude limit reached", reached.Title);
        Assert.StartsWith("The 5 h limit is used up. It resets in 2 h 10 min", reached.Message(Now, english), StringComparison.Ordinal);

        Assert.Equal("The 5 h limit has reset: your usage is back.", new PlanNotice(PlanNoticeKind.Reset, "Codex", window).Message(Now, english));
        Assert.Equal("You now have 1 limit reset in reserve.", new PlanNotice(PlanNoticeKind.CreditGained, "Codex", null, 1).Message(Now, english));
        Assert.Equal("You now have 3 limit resets in reserve.", new PlanNotice(PlanNoticeKind.CreditGained, "Codex", null, 3).Message(Now, english));
    }
}
