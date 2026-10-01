using WinModes.Core;
using WinModes.Core.Engine;
using WinModes.Core.Planning;
using WinModes.Core.Profiles;
using WinModes.Core.Protection;
using WinModes.Core.Tuning;

// Elevated helper. It accepts only a verb with a mode name, service names or tweak ids; profiles, protection
// policy, tweak catalog and journals are read from its own location and from the admin-only data folder,
// never from the caller. It never reads or writes the user's own registry hive or profile.
const int ExitOk = 0;
const int ExitFailed = 1;
const int ExitUsage = 2;
const string ChangeVerb = "change";
const char ActionPrefix = ':';

var isModeVerb = args.Length > 0 && (args[0] == "revert" || (args[0] == "apply" && args.Length == 2));
var isChangeVerb = args.Length > 1 && args[0] == ChangeVerb;
if (!isModeVerb && !isChangeVerb)
{
    return ExitUsage;
}

try
{
    var root = RepositoryLocator.Find(AppContext.BaseDirectory);
    if (root is null)
    {
        return ExitFailed;
    }

    AppPaths.EnsureProtectedJournalDirectory();
    var journal = new JournalStore(AppPaths.JournalDirectory);
    var policy = ProtectionPolicy.Load(Path.Combine(root, "data", "protected.json"));
    var control = new WindowsServiceControl();

    if (isChangeVerb)
    {
        return Change(args[1..], root, control, policy, journal);
    }

    var engine = new ModeEngine(control, new ModePlanner(new WindowsSystemProbe(), policy), policy, journal);

    // One mode at a time: undo the active switch before applying another, or when asked to revert.
    if (journal.FindActive() is { } active)
    {
        engine.Revert(active);
    }

    if (args[0] == "apply")
    {
        // ProfileStore only loads a name that matches an existing profile file.
        engine.Apply(new ProfileStore(Path.Combine(root, "profiles")).Load(args[1]));
    }

    return ExitOk;
}
catch (Exception ex) when (ex is ProfileException or IOException or UnauthorizedAccessException or InvalidOperationException)
{
    return ExitFailed;
}

// Arguments are groups: ":manual A B :stop A :tweak widgets". ":restore" and ":untweak" without a name
// undo every recorded change of their kind.
static int Change(string[] arguments, string root, WindowsServiceControl control, ProtectionPolicy policy, JournalStore journal)
{
    var groups = new List<(TuneAction Action, List<string> Names)>();
    foreach (var argument in arguments)
    {
        if (argument.StartsWith(ActionPrefix))
        {
            // Names only: a number would also parse as an enum value.
            if (!Enum.GetNames<TuneAction>().Contains(argument[1..], StringComparer.OrdinalIgnoreCase))
            {
                return ExitUsage;
            }

            groups.Add((Enum.Parse<TuneAction>(argument[1..], ignoreCase: true), []));
        }
        else if (groups.Count == 0)
        {
            return ExitUsage;
        }
        else
        {
            groups[^1].Names.Add(argument);
        }
    }

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
            case TuneAction.Tweak:
                results.AddRange(names.Select(id => catalog.Find(id) is { } tweak
                    ? tweaks.Apply(tweak)
                    : new TuneResult(id, action, TuneOutcome.Skipped, "Unknown tweak.")));
                break;
            case TuneAction.Untweak:
                results.AddRange((names.Count == 0 ? tweaks.JournaledIds() : names).Select(tweaks.Undo));
                break;
            default:
                results.AddRange(tuner.Apply(action, action == TuneAction.Restore && names.Count == 0 ? tuner.TweakedServices() : names));
                break;
        }
    }

    store.SaveLastResults(results);
    return ExitOk;
}
