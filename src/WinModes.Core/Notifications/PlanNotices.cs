using System.Globalization;
using WinModes.Core.Localization;
using WinModes.Core.Usage;

namespace WinModes.Core.Notifications;

public enum PlanNoticeKind
{
    /// <summary>A window is under the chosen share.</summary>
    Low,

    /// <summary>A window is used up.</summary>
    Reached,

    /// <summary>A window that was used up started over.</summary>
    Reset,

    /// <summary>The account holds more limit resets in reserve than before.</summary>
    CreditGained,
}

/// <summary>Which notices the user wants for one tool.</summary>
public sealed record PlanNoticeChoice(bool Low, bool Reached, bool Reset, bool CreditGained);

/// <summary>One thing to tell the user about a plan.</summary>
/// <param name="Window">The window concerned; null for a credit notice.</param>
/// <param name="Credits">Resets in reserve, for a credit notice.</param>
/// <param name="Also">Other windows of the same tool that reached the same state at the same time: one notice tells them all.</param>
public sealed record PlanNotice(PlanNoticeKind Kind, string Tool, LimitWindow? Window, int Credits = 0, IReadOnlyList<LimitWindow>? Also = null)
{
    public string Title => Kind switch
    {
        PlanNoticeKind.Low => Loc.F("{0} is running low", Tool),
        PlanNoticeKind.Reached => Loc.F("{0} limit reached", Tool),
        PlanNoticeKind.Reset => Loc.F("{0} is available again", Tool),
        _ => Loc.F("{0}: a limit reset was added", Tool),
    };

    public string Message(DateTimeOffset now, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        // One sentence per window, in the order they were found (the short window first).
        return Also is { Count: > 0 }
            ? string.Join(" ", new[] { Window! }.Concat(Also).Select(window => (this with { Window = window, Also = null }).Message(now, culture)))
            : SingleMessage(now, culture);
    }

    private string SingleMessage(DateTimeOffset now, CultureInfo culture)
    {
        var window = Window;
        var name = window?.WindowName ?? "";
        var resets = window?.ResetsAt is { } at && at > now ? (Subscriptions.Span(at - now), Subscriptions.LocalTime(at, now, culture)) : default((string, string)?);

        return Kind switch
        {
            PlanNoticeKind.Low when resets is { } soon => Subscriptions.Capitalize(Loc.In(culture, "{0} limit: {1:0} % left. It resets in {2} ({3}).", name, window!.RemainingPercent, soon.Item1, soon.Item2)),
            PlanNoticeKind.Low => Subscriptions.Capitalize(Loc.In(culture, "{0} limit: {1:0} % left.", name, window!.RemainingPercent)),
            PlanNoticeKind.Reached when resets is { } soon => Loc.In(culture, "The {0} limit is used up. It resets in {1} ({2}).", name, soon.Item1, soon.Item2),
            PlanNoticeKind.Reached => Loc.In(culture, "The {0} limit is used up.", name),
            PlanNoticeKind.Reset => Loc.In(culture, "The {0} limit has reset: your usage is back.", name),
            _ => Credits == 1 ? Loc.T("You now have 1 limit reset in reserve.") : Loc.In(culture, "You now have {0} limit resets in reserve.", Credits),
        };
    }
}

/// <summary>
/// Decides which plan notices to send. Each one is sent once per limit cycle: the <see cref="NoticeLedger"/> remembers what was
/// announced, so the same low or empty limit is not announced again at every start of the app or every read.
/// </summary>
public static class PlanNoticeEngine
{
    /// <summary>Under this share left, a window counts as used up.</summary>
    private const double UsedUpPercent = 0.5;

    /// <summary>The reset time of a window moves a little between two reads; a start of the next cycle moves it by hours.</summary>
    private static readonly TimeSpan CycleTolerance = TimeSpan.FromMinutes(10);

    /// <summary>A reset older than this is not announced (the app was closed when it happened).</summary>
    private static readonly TimeSpan ResetFreshness = TimeSpan.FromHours(2);

    /// <summary>Notices to send now for these statuses. The ledger is updated, and flagged as changed when it needs saving.</summary>
    public static IReadOnlyList<PlanNotice> Evaluate(NoticeLedger ledger, IEnumerable<SubscriptionStatus> statuses, Func<string, PlanNoticeChoice> choiceFor, double lowPercent, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(statuses);
        ArgumentNullException.ThrowIfNull(choiceFor);

        var notices = new List<PlanNotice>();
        foreach (var status in statuses)
        {
            var choice = choiceFor(status.Tool);
            foreach (var window in new[] { status.Primary, status.Secondary }.OfType<LimitWindow>())
            {
                CheckWindow(ledger, status.Tool, window, choice, lowPercent, now, notices);
            }

            CheckCredits(ledger, status, choice, notices);
        }

        return Merge(notices);
    }

    /// <summary>
    /// A tool whose two windows reach the same state in the same read (both used up, both reset) gets one notice, not two. The
    /// reset of the short window and of the weekly one often come together on the week's first day.
    /// </summary>
    private static List<PlanNotice> Merge(List<PlanNotice> notices)
    {
        var merged = new List<PlanNotice>();
        foreach (var notice in notices)
        {
            var same = notice.Window is null ? -1 : merged.FindIndex(known => known.Kind == notice.Kind && known.Tool == notice.Tool && known.Window is not null);
            if (same < 0)
            {
                merged.Add(notice);
            }
            else
            {
                merged[same] = merged[same] with { Also = [.. merged[same].Also ?? [], notice.Window!] };
            }
        }

        return merged;
    }

    private static void CheckWindow(NoticeLedger ledger, string tool, LimitWindow window, PlanNoticeChoice choice, double lowPercent, DateTimeOffset now, List<PlanNotice> notices)
    {
        var key = $"{tool}:{window.WindowMinutes}";
        ledger.Windows.TryGetValue(key, out var mark);

        // The cycle the marks belong to ended: by the clock, or because a read shows the next one.
        if (mark?.ResetsAt is { } cycleEnd && (cycleEnd <= now || (window.ResetsAt is { } next && next > cycleEnd + CycleTolerance)))
        {
            if (mark.ReachedSent && choice.Reset && now - cycleEnd <= ResetFreshness)
            {
                notices.Add(new PlanNotice(PlanNoticeKind.Reset, tool, window));
            }

            ledger.Windows.Remove(key);
            ledger.Changed = true;
            mark = null;
        }

        // A figure from before the reset says nothing about the new cycle.
        if (window.HasReset(now))
        {
            return;
        }

        var usedUp = window.RemainingPercent < UsedUpPercent;
        var low = window.RemainingPercent < lowPercent;
        var lowSent = mark?.LowSent ?? false;
        var reachedSent = mark?.ReachedSent ?? false;
        PlanNoticeKind? kind = null;

        if (usedUp && choice.Reached)
        {
            if (!reachedSent)
            {
                (kind, reachedSent, lowSent) = (PlanNoticeKind.Reached, true, true);
            }
        }
        else if (low && choice.Low && !lowSent)
        {
            (kind, lowSent) = (PlanNoticeKind.Low, true);
        }

        if (kind is null)
        {
            return;
        }

        notices.Add(new PlanNotice(kind.Value, tool, window));
        ledger.Windows[key] = new WindowMark(mark?.ResetsAt ?? window.ResetsAt, lowSent, reachedSent);
        ledger.Changed = true;
    }

    private static void CheckCredits(NoticeLedger ledger, SubscriptionStatus status, PlanNoticeChoice choice, List<PlanNotice> notices)
    {
        if (status.ResetCredits is not { } credits)
        {
            return;
        }

        // The first figure seen is the starting point, not news.
        if (ledger.Credits.TryGetValue(status.Tool, out var known))
        {
            if (credits > known && choice.CreditGained)
            {
                notices.Add(new PlanNotice(PlanNoticeKind.CreditGained, status.Tool, null, credits));
            }

            if (credits == known)
            {
                return;
            }
        }

        ledger.Credits[status.Tool] = credits;
        ledger.Changed = true;
    }
}
