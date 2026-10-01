using WinModes.Core;
using WinModes.Core.Engine;
using WinModes.Core.Planning;
using WinModes.Core.Profiles;
using WinModes.Core.Protection;

// Elevated helper. It accepts only a verb and a mode name; profiles, protection policy and journal
// are read from its own location and from the admin-only journal folder, never from the caller.
const int ExitOk = 0;
const int ExitFailed = 1;
const int ExitUsage = 2;

if (args.Length == 0 || args[0] is not ("apply" or "revert") || (args[0] == "apply" && args.Length != 2))
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
    var engine = new ModeEngine(new WindowsServiceControl(), new ModePlanner(new WindowsSystemProbe(), policy), policy, journal);

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
