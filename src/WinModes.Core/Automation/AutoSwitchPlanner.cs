namespace WinModes.Core.Automation;

/// <summary>"When this program runs, activate that mode."</summary>
/// <param name="Process">The program, or a condition (see <see cref="AutoSwitchConditions"/>).</param>
/// <param name="Mode">The mode to activate.</param>
/// <param name="Enabled">A rule that is off is kept in the list but never acts. Settings saved before it existed have no value, which means on.</param>
/// <param name="Path">Where the program was found, kept only to show its icon; matching uses the name.</param>
/// <param name="Label">The name to show for the program, when it is not the process name.</param>
public sealed record AutoSwitchRule(string Process, string Mode, bool Enabled = true, string? Path = null, string? Label = null);

public enum AutoSwitchKind
{
    None,
    Activate,
    Revert,
}

public sealed record AutoSwitchDecision(AutoSwitchKind Kind, string? Mode = null, string? Process = null)
{
    public static AutoSwitchDecision None { get; } = new(AutoSwitchKind.None);
}

/// <summary>How long automatic switching waits before it acts.</summary>
/// <param name="StartDelay">A trigger must hold this long before its mode starts, so a program that opens and closes at once changes nothing.</param>
/// <param name="Grace">Once nothing triggers the mode any more, it stays this long before it ends or hands over, so closing one tool to open another does not make the PC go back and forth.</param>
/// <param name="FailureHold">After a switch that failed or was refused, automatic switching stays quiet this long instead of asking again.</param>
public sealed record AutoSwitchTiming(TimeSpan StartDelay, TimeSpan Grace, TimeSpan FailureHold)
{
    public static AutoSwitchTiming Default { get; } = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(10));

    public AutoSwitchTiming WithGrace(TimeSpan grace) => this with { Grace = grace };
}

/// <summary>What automatic switching is doing now, for the Modes and Automation pages.</summary>
public enum AutoSwitchState
{
    /// <summary>Nothing triggers a mode.</summary>
    Idle,

    /// <summary>A trigger holds; its mode starts when <see cref="AutoSwitchStatus.Until"/> is reached.</summary>
    Starting,

    /// <summary>A mode started automatically is on, because <see cref="AutoSwitchStatus.Trigger"/> still holds.</summary>
    Active,

    /// <summary>Nothing triggers the active mode any more; it ends (or hands over) at <see cref="AutoSwitchStatus.Until"/>.</summary>
    Ending,

    /// <summary>Nothing triggers the active mode any more, but the user chose not to return to normal automatically.</summary>
    Kept,

    /// <summary>A mode chosen by hand is on: automatic switching leaves it alone.</summary>
    Manual,

    /// <summary>The mode was undone by hand while its trigger is still there: it starts again with the next launch.</summary>
    Declined,

    /// <summary>Holding back, just after the app started or after a switch that did not work.</summary>
    Waiting,

    /// <summary>The user paused automatic switching until <see cref="AutoSwitchStatus.Until"/> (or until they resume it).</summary>
    Paused,
}

public sealed record AutoSwitchStatus(AutoSwitchState State, string? Mode = null, string? Trigger = null, DateTimeOffset? Until = null);

/// <summary>
/// Decides when automatic switching should act. It only decides: the caller performs the switch
/// through the same path as a manual one, so the protection list and the journal still apply.
/// </summary>
/// <remarks>
/// A mode stays on while any rule that names it still holds, however many tools triggered it, and ends only after
/// <see cref="AutoSwitchTiming.Grace"/> without one. A mode chosen by hand is never replaced or undone.
/// </remarks>
public sealed class AutoSwitchPlanner(AutoSwitchTiming? timing = null)
{
    private const string ExecutableExtension = ".exe";

    /// <summary>Changed while it runs when the user picks another grace period.</summary>
    public AutoSwitchTiming Timing { get; set; } = timing ?? AutoSwitchTiming.Default;

    // A program is acted on once per run. Without this, undoing a mode by hand while the
    // program is still open would switch it back on at the next check.
    private readonly HashSet<string> _handled = new(StringComparer.OrdinalIgnoreCase);

    // What the planner is waiting to do, and since when: a change of plan restarts the wait.
    private string? _waitingFor;
    private DateTimeOffset _waitingSince;

    private string? _lastOwner;
    private DateTimeOffset _holdUntil = DateTimeOffset.MinValue;
    private DateTimeOffset? _pausedUntil;

    public AutoSwitchStatus Status { get; private set; } = new(AutoSwitchState.Idle);

    /// <summary>Process name as Windows reports it: no folder, no ".exe".</summary>
    public static string Normalize(string process)
    {
        ArgumentNullException.ThrowIfNull(process);
        var name = process.Trim().Trim('"');
        if (AutoSwitchConditions.IsCondition(name))
        {
            return name;
        }

        name = name[(name.LastIndexOfAny(['\\', '/']) + 1)..];
        return name.EndsWith(ExecutableExtension, StringComparison.OrdinalIgnoreCase) ? name[..^ExecutableExtension.Length] : name;
    }

    /// <summary>Stays quiet until the given time: just after the app started, or after a switch that did not work.</summary>
    public void HoldUntil(DateTimeOffset until)
    {
        if (until > _holdUntil)
        {
            _holdUntil = until;
        }

        _waitingFor = null;
    }

    /// <summary>
    /// The user stops automatic switching for a while. Nothing is started or ended meanwhile, and the mode that is on stays on.
    /// <see cref="DateTimeOffset.MaxValue"/> means until the user resumes; null resumes now.
    /// </summary>
    public void PauseUntil(DateTimeOffset? until)
    {
        _pausedUntil = until;
        _waitingFor = null;
    }

    /// <summary>The switch asked for did not work (permission refused, helper failed): wait before anything else is tried.</summary>
    public void SwitchFailed(DateTimeOffset now) => HoldUntil(now + Timing.FailureHold);

    /// <param name="rules">In priority order: the first rule whose program runs wins.</param>
    /// <param name="running">Normalized names of the running processes, plus the conditions that hold (see <see cref="AutoSwitchConditions"/>).</param>
    /// <param name="activeMode">Mode active right now, or null.</param>
    /// <param name="activeByAuto">The active mode was started by automatic switching (kept by the app across restarts); a mode chosen by hand is never touched.</param>
    /// <param name="revertWhenClosed">Undo a mode started automatically once nothing triggers it any more.</param>
    /// <param name="now">The time of this check.</param>
    public AutoSwitchDecision Evaluate(
        IReadOnlyList<AutoSwitchRule> rules, IReadOnlySet<string> running, string? activeMode, bool activeByAuto, bool revertWhenClosed, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(running);

        _handled.RemoveWhere(name => !running.Contains(name));
        var matches = rules
            .Where(rule => rule.Enabled)
            .Select(rule => (Name: Normalize(rule.Process), rule.Mode))
            .Where(match => match.Name.Length > 0 && running.Contains(match.Name))
            .ToList();

        if (_pausedUntil is { } paused && now < paused)
        {
            _waitingFor = null;
            Status = new AutoSwitchStatus(AutoSwitchState.Paused, activeMode, Until: paused == DateTimeOffset.MaxValue ? null : paused);
            return AutoSwitchDecision.None;
        }

        if (activeMode is not null && !activeByAuto)
        {
            _waitingFor = null;
            _lastOwner = null;
            Status = new AutoSwitchStatus(AutoSwitchState.Manual, activeMode);
            return AutoSwitchDecision.None;
        }

        if (now < _holdUntil)
        {
            _waitingFor = null;
            Status = new AutoSwitchStatus(AutoSwitchState.Waiting, activeMode, Until: _holdUntil);
            return AutoSwitchDecision.None;
        }

        return activeMode is null ? EvaluateWithoutMode(matches, now) : EvaluateWithAutoMode(matches, activeMode, revertWhenClosed, now);
    }

    private AutoSwitchDecision EvaluateWithoutMode(List<(string Name, string Mode)> matches, DateTimeOffset now)
    {
        _lastOwner = null;
        var wanted = matches.FirstOrDefault(match => !_handled.Contains(match.Name));
        if (wanted.Name is null)
        {
            _waitingFor = null;
            Status = matches.Count > 0 ? new AutoSwitchStatus(AutoSwitchState.Declined, Trigger: matches[0].Name) : new AutoSwitchStatus(AutoSwitchState.Idle);
            return AutoSwitchDecision.None;
        }

        var until = WaitFor($"start|{wanted.Mode}|{wanted.Name}", Timing.StartDelay, now);
        if (now < until)
        {
            Status = new AutoSwitchStatus(AutoSwitchState.Starting, wanted.Mode, wanted.Name, until);
            return AutoSwitchDecision.None;
        }

        return Activate(wanted, matches);
    }

    private AutoSwitchDecision EvaluateWithAutoMode(List<(string Name, string Mode)> matches, string activeMode, bool revertWhenClosed, DateTimeOffset now)
    {
        var owner = matches.FirstOrDefault(match => SameMode(match.Mode, activeMode));
        if (owner.Name is not null)
        {
            _lastOwner = owner.Name;
        }

        // The first rule that holds wins, except a rule whose program was already acted on and left no mode on (it was undone by hand).
        var wanted = matches.FirstOrDefault(match => SameMode(match.Mode, activeMode) || !_handled.Contains(match.Name));
        if (wanted.Name is not null && SameMode(wanted.Mode, activeMode))
        {
            _waitingFor = null;
            Status = new AutoSwitchStatus(AutoSwitchState.Active, activeMode, wanted.Name);
            return AutoSwitchDecision.None;
        }

        if (wanted.Name is not null)
        {
            // A program of another mode: it takes over at once if it outranks the mode on, or after the grace period if the mode on lost its last trigger.
            var delay = owner.Name is not null ? Timing.StartDelay : Timing.Grace;
            var until = WaitFor($"handover|{wanted.Mode}|{wanted.Name}|{owner.Name is not null}", delay, now);
            if (now < until)
            {
                Status = new AutoSwitchStatus(owner.Name is not null ? AutoSwitchState.Starting : AutoSwitchState.Ending, wanted.Mode, wanted.Name, until);
                return AutoSwitchDecision.None;
            }

            return Activate(wanted, matches);
        }

        if (!revertWhenClosed)
        {
            _waitingFor = null;
            Status = new AutoSwitchStatus(AutoSwitchState.Kept, activeMode, _lastOwner);
            return AutoSwitchDecision.None;
        }

        var revertAt = WaitFor("revert", Timing.Grace, now);
        if (now < revertAt)
        {
            Status = new AutoSwitchStatus(AutoSwitchState.Ending, activeMode, _lastOwner, revertAt);
            return AutoSwitchDecision.None;
        }

        var decision = new AutoSwitchDecision(AutoSwitchKind.Revert, activeMode, _lastOwner);
        _waitingFor = null;
        _lastOwner = null;
        Status = new AutoSwitchStatus(AutoSwitchState.Idle);
        return decision;
    }

    private AutoSwitchDecision Activate((string Name, string Mode) wanted, List<(string Name, string Mode)> matches)
    {
        // Every program of this mode that runs now is counted as acted on: undoing the mode by hand is respected for all of them.
        foreach (var match in matches.Where(match => SameMode(match.Mode, wanted.Mode)))
        {
            _handled.Add(match.Name);
        }

        _waitingFor = null;
        _lastOwner = wanted.Name;
        Status = new AutoSwitchStatus(AutoSwitchState.Active, wanted.Mode, wanted.Name);
        return new AutoSwitchDecision(AutoSwitchKind.Activate, wanted.Mode, wanted.Name);
    }

    /// <summary>When the wait for this plan ends; a different plan than at the previous check starts the wait over.</summary>
    private DateTimeOffset WaitFor(string plan, TimeSpan delay, DateTimeOffset now)
    {
        if (_waitingFor != plan)
        {
            _waitingFor = plan;
            _waitingSince = now;
        }

        return _waitingSince + delay;
    }

    private static bool SameMode(string left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
