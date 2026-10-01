namespace WinModes.Core.Automation;

/// <summary>"When this program runs, activate that mode."</summary>
public sealed record AutoSwitchRule(string Process, string Mode);

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

/// <summary>
/// Decides when automatic switching should act. It only decides: the caller performs the switch
/// through the same path as a manual one, so the protection list and the journal still apply.
/// </summary>
public sealed class AutoSwitchPlanner
{
    private const string ExecutableExtension = ".exe";

    // A program is acted on once per run. Without this, undoing a mode by hand while the
    // program is still open would switch it back on at the next check.
    private readonly HashSet<string> _handled = new(StringComparer.OrdinalIgnoreCase);
    private string? _activatedMode;
    private string? _activatedBy;

    private bool IsOurMode(string? activeMode) =>
        _activatedMode is not null && string.Equals(activeMode, _activatedMode, StringComparison.OrdinalIgnoreCase);

    private int? RankOfActivator(IReadOnlyList<AutoSwitchRule> rules)
    {
        if (_activatedBy is null)
        {
            return null;
        }

        for (var rank = 0; rank < rules.Count; rank++)
        {
            if (string.Equals(Normalize(rules[rank].Process), _activatedBy, StringComparison.OrdinalIgnoreCase))
            {
                return rank;
            }
        }

        return null;
    }

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

    /// <param name="rules">In priority order: the first rule whose program runs wins.</param>
    /// <param name="running">Normalized names of the running processes, plus the conditions that hold (see <see cref="AutoSwitchConditions"/>).</param>
    /// <param name="activeMode">Mode active right now, or null.</param>
    /// <param name="revertWhenClosed">Undo a mode this planner activated once its program has exited.</param>
    public AutoSwitchDecision Evaluate(IReadOnlyList<AutoSwitchRule> rules, IReadOnlySet<string> running, string? activeMode, bool revertWhenClosed)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(running);

        _handled.RemoveWhere(name => !running.Contains(name));

        // The user changed mode by hand, or the activation was refused: the mode is no longer ours to undo.
        if (_activatedMode is not null && !string.Equals(activeMode, _activatedMode, StringComparison.OrdinalIgnoreCase))
        {
            _activatedMode = null;
            _activatedBy = null;
        }

        var activatorRank = RankOfActivator(rules);
        for (var rank = 0; rank < rules.Count; rank++)
        {
            var rule = rules[rank];
            var name = Normalize(rule.Process);
            if (name.Length == 0 || !running.Contains(name))
            {
                continue;
            }

            // The mode a higher-ranked rule set stays while its program runs. The lower rule is not marked
            // as handled, so it takes over once the higher program exits.
            if (activatorRank is { } higher && rank > higher && running.Contains(_activatedBy!) && IsOurMode(activeMode))
            {
                continue;
            }

            if (!_handled.Add(name))
            {
                continue;
            }

            if (string.Equals(activeMode, rule.Mode, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _activatedMode = rule.Mode;
            _activatedBy = name;
            return new AutoSwitchDecision(AutoSwitchKind.Activate, rule.Mode, name);
        }

        if (revertWhenClosed && _activatedMode is not null && _activatedBy is not null && !running.Contains(_activatedBy))
        {
            var decision = new AutoSwitchDecision(AutoSwitchKind.Revert, _activatedMode, _activatedBy);
            _activatedMode = null;
            _activatedBy = null;
            return decision;
        }

        return AutoSwitchDecision.None;
    }
}
