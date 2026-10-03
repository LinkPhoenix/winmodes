using WinModes.Core;
using WinModes.Core.Engine;
using WinModes.Core.Planning;
using WinModes.Core.Profiles;
using WinModes.Core.Protection;
using WinModes.Core.Tuning;

// Elevated helper. It accepts only a verb with a mode name, service names or tweak ids ("task install|remove"
// manages the opt-in task that starts it without a prompt); profiles, protection
// policy, tweak catalog and journals are read from its own location and from the admin-only data folder,
// never from the caller. It never reads or writes the user's own registry hive or profile.
const int ExitOk = 0;
const int ExitFailed = 1;
const int ExitUsage = 2;

if (HelperArguments.Parse(args) is not { } command)
{
    return ExitUsage;
}

// One helper at a time, whether it was started by a prompt or by the silent task. A mode switch can take
// a while (services wait for their stop), so a second helper waits for it rather than failing at once.
using var oneAtATime = HelperLock.TryAcquire(TimeSpan.FromSeconds(90));
if (oneAtATime is null)
{
    return ExitFailed;
}

try
{
    if (command.Kind is HelperCommandKind.TaskInstall or HelperCommandKind.TaskRemove)
    {
        if (command.Kind == HelperCommandKind.TaskRemove)
        {
            SilentSwitchTask.Remove();
        }
        else if (Environment.ProcessPath is { } self)
        {
            // Refused outside Program Files: the task must not start a file a non-administrator can replace.
            SilentSwitchTask.Install(self);
        }

        return ExitOk;
    }

    var root = RepositoryLocator.Find(AppContext.BaseDirectory);
    if (root is null)
    {
        return ExitFailed;
    }

    AppPaths.EnsureProtectedJournalDirectory();
    var journal = new JournalStore(AppPaths.JournalDirectory);
    var policy = ProtectionPolicy.Load(Path.Combine(root, "data", "protected.json"));
    var control = new WindowsServiceControl();

    if (command.Kind == HelperCommandKind.Change)
    {
        return Change(command.Groups!, root, control, policy, journal);
    }

    var engine = new ModeEngine(control, new ModePlanner(new WindowsSystemProbe(), policy), policy, journal);

    // One mode at a time: undo the active switch before applying another, or when asked to revert.
    if (journal.FindActive() is { } active)
    {
        engine.Revert(active);
    }

    if (command.Kind == HelperCommandKind.Apply)
    {
        // ProfileStore only loads a name that matches an existing profile file.
        engine.Apply(new ProfileStore(Path.Combine(root, "profiles")).Load(command.Mode!));
    }

    return ExitOk;
}
catch (Exception ex) when (ex is not OutOfMemoryException)
{
    // Nobody watches this process, so the reason goes to a log the user can read; the exit code stays generic.
    LogFailure(ex);
    return ExitFailed;
}

static void LogFailure(Exception exception)
{
    try
    {
        AppPaths.EnsureProtectedDirectory(AppPaths.LogDirectory);
        new ErrorLog(AppPaths.HelperErrorLog).Append("helper", exception);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
    {
        // Without a safe place to write, the failure is only reported through the exit code.
    }
}

// Runs each ":action names" group in order; ":restore" and ":untweak" without a name undo every recorded change of their kind.
static int Change(IReadOnlyList<ChangeGroup> groups, string root, WindowsServiceControl control, ProtectionPolicy policy, JournalStore journal)
{
    AppPaths.EnsureProtectedDirectory(AppPaths.TweaksDirectory);
    var store = new TweakStore(AppPaths.TweaksDirectory);
    var changedByMode = (journal.FindActive()?.Entries ?? [])
        .Where(entry => entry.Outcome == EntryOutcome.Done && entry.Kind == EntryKind.StopService)
        .Select(entry => entry.Target)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var tuner = new ServiceTuner(control, policy, store, changedByMode);

    // A tweak is looked up by id in the catalog next to the helper; an unknown id does nothing.
    var catalog = TweakCatalog.Load(Path.Combine(root, "data", "tweaks.json"));
    var tweaks = new TweakEngine(new WindowsRegistryAccess(), new WindowsTaskControl(), new TweakJournal(AppPaths.MachineTweakJournal), machineScope: true);

    var results = new List<TuneResult>();
    foreach (var (action, names) in groups)
    {
        switch (action)
        {
            case TuneAction.ReleasePolicy:
                results.AddRange(names.Select(name => TweakSelection.TryParse(name, out var selection) && catalog.Find(selection.Id) is { } tweak
                    ? tweaks.ReleasePolicy(tweak, selection.Parts, new WindowsSystemProbe())
                    : new TuneResult(name, action, TuneOutcome.Skipped, "Unknown tweak.")));
                break;
            case TuneAction.Tweak:
                // "id" or "id#0,2": a tweak, or some parts of it. Anything else, or an id the catalog does not list, does nothing.
                results.AddRange(names.Select(name => TweakSelection.TryParse(name, out var selection) && catalog.Find(selection.Id) is { } tweak
                    ? tweaks.Apply(tweak, selection.Parts)
                    : new TuneResult(name, action, TuneOutcome.Skipped, "Unknown tweak.")));
                break;
            case TuneAction.Untweak:
                results.AddRange(names.Count == 0 ? tweaks.JournaledIds().Select(tweaks.Undo) : names.Select(name => UndoTweak(name, catalog, tweaks)));
                break;
            default:
                results.AddRange(tuner.Apply(action, action == TuneAction.Restore && names.Count == 0 ? tuner.TweakedServices() : names));
                break;
        }
    }

    store.SaveLastResults(results);
    return ExitOk;
}

// An undo of a whole tweak needs only the journal; an undo of some parts needs the catalog to know which journaled change is which.
static TuneResult UndoTweak(string name, TweakCatalog catalog, TweakEngine tweaks)
{
    if (!TweakSelection.TryParse(name, out var selection))
    {
        return new TuneResult(name, TuneAction.Untweak, TuneOutcome.Skipped, "Unknown tweak.");
    }

    if (selection.Parts is null)
    {
        return tweaks.Undo(selection.Id);
    }

    return catalog.Find(selection.Id) is { } tweak
        ? tweaks.Undo(tweak, selection.Parts)
        : new TuneResult(name, TuneAction.Untweak, TuneOutcome.Skipped, "Unknown tweak.");
}
