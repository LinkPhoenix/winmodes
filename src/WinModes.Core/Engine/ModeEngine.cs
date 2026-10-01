using System.ComponentModel;
using System.Globalization;
using WinModes.Core.Planning;
using WinModes.Core.Profiles;
using WinModes.Core.Protection;

namespace WinModes.Core.Engine;

/// <summary>
/// Applies and reverts the service part of a mode. Every change is journaled before it is made,
/// protected services are refused at the last moment, and a revert only restores values the engine itself set.
/// </summary>
public sealed class ModeEngine(IServiceControl services, ModePlanner planner, ProtectionPolicy policy, JournalStore journal)
{
    public JournalSession Apply(ModeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // Planning validates the whole profile against the protection policy and throws if it breaks it.
        var plan = planner.Plan(profile);
        var stopTargets = plan.Changes.Where(change => change.Kind == ChangeKind.StopService).Select(change => change.Target)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var session = new JournalSession
        {
            Id = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + profile.Mode,
            Mode = profile.Mode,
            StartedUtc = DateTimeOffset.UtcNow,
        };

        foreach (var change in plan.Changes.Where(change => change.Kind is ChangeKind.StopService or ChangeKind.StartService))
        {
            var entry = Record(change, session);
            if (entry is null)
            {
                continue;
            }

            journal.Save(session);
            Execute(entry, stopTargets);
            journal.Save(session);
        }

        session.Completed = true;
        journal.Save(session);
        return session;
    }

    public JournalSession Revert(JournalSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        foreach (var entry in Enumerable.Reverse(session.Entries).Where(entry => entry.NeedsRevert))
        {
            RevertEntry(entry);
            journal.Save(session);
        }

        // An entry that could not be restored stays revertible, so the session stays active and a later revert retries it.
        session.Reverted = session.RevertibleCount == 0;
        journal.Save(session);
        return session;
    }

    private JournalEntry? Record(PlannedChange change, JournalSession session)
    {
        var state = services.GetState(change.Target);
        if (state is null)
        {
            return null;
        }

        var entry = new JournalEntry
        {
            Target = change.Target,
            Kind = change.Kind == ChangeKind.StopService ? EntryKind.StopService : EntryKind.StartService,
            BeforeStartMode = state.StartMode,
            BeforeDelayedAutoStart = services.IsDelayedAutoStart(change.Target),
            BeforeRunning = state.IsRunning,
        };
        session.Entries.Add(entry);
        return entry;
    }

    private void Execute(JournalEntry entry, HashSet<string> stopTargets)
    {
        // Fail closed: re-check at the moment of the write, whatever the plan said.
        if (policy.IsProtectedService(entry.Target))
        {
            Mark(entry, EntryOutcome.Skipped, "Protected service.");
            return;
        }

        try
        {
            if (entry.Kind == EntryKind.StartService)
            {
                services.StartService(entry.Target);
                Mark(entry, EntryOutcome.Done, null);
                return;
            }

            var blocking = services.GetRunningDependents(entry.Target).Where(name => !stopTargets.Contains(name)).ToList();
            if (blocking.Count > 0)
            {
                Mark(entry, EntryOutcome.Skipped, $"Still needed by: {string.Join(", ", blocking)}.");
                return;
            }

            services.SetStartMode(entry.Target, ServiceStartMode.Manual, delayedAutoStart: false);
            // From here the start type has changed, so the entry must be revertible even if the stop fails.
            entry.Outcome = EntryOutcome.Done;
            services.StopService(entry.Target);
        }
        catch (Exception ex) when (IsServiceFailure(ex))
        {
            if (entry.Outcome == EntryOutcome.Done)
            {
                entry.Detail = $"Set to Manual, but it could not be stopped: {ex.Message}";
            }
            else
            {
                Mark(entry, EntryOutcome.Failed, ex.Message);
            }
        }
    }

    private void RevertEntry(JournalEntry entry)
    {
        try
        {
            var current = services.GetState(entry.Target);
            if (current is null)
            {
                Mark(entry, EntryOutcome.RevertSkipped, "Service no longer installed.");
                return;
            }

            if (entry.Kind == EntryKind.StartService)
            {
                if (!entry.BeforeRunning && current.IsRunning)
                {
                    services.StopService(entry.Target);
                }

                Mark(entry, EntryOutcome.Reverted, null);
                return;
            }

            // Someone else changed the start type since the switch: their choice wins.
            if (current.StartMode != ServiceStartMode.Manual)
            {
                Mark(entry, EntryOutcome.RevertSkipped, $"Start type is now {current.StartMode}; left as is.");
                return;
            }

            if (entry.BeforeStartMode is ServiceStartMode.Automatic or ServiceStartMode.Manual)
            {
                services.SetStartMode(entry.Target, entry.BeforeStartMode, entry.BeforeDelayedAutoStart);
            }

            if (entry.BeforeRunning && !current.IsRunning)
            {
                services.StartService(entry.Target);
            }

            Mark(entry, EntryOutcome.Reverted, null);
        }
        catch (Exception ex) when (IsServiceFailure(ex))
        {
            entry.Detail = $"Revert failed: {ex.Message}";
        }
    }

    private static void Mark(JournalEntry entry, EntryOutcome outcome, string? detail)
    {
        entry.Outcome = outcome;
        entry.Detail = detail;
    }

    private static bool IsServiceFailure(Exception ex) =>
        ex is InvalidOperationException or Win32Exception or System.ServiceProcess.TimeoutException;
}
