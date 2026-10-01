using WinModes.Core.Planning;
using WinModes.Core.Profiles;
using WinModes.Core.Protection;

const int ExitOk = 0;
const int ExitUsage = 2;
const int ExitInvalid = 1;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintUsage();
    return args.Length == 0 ? ExitUsage : ExitOk;
}

var root = WinModes.Core.RepositoryLocator.Find(GetOption(args, "--root") ?? Environment.CurrentDirectory);
if (root is null)
{
    Console.Error.WriteLine("Cannot find the win-modes data (data/protected.json). Use --root <path>.");
    return ExitUsage;
}

var store = new ProfileStore(Path.Combine(root, "profiles"));
var policy = ProtectionPolicy.Load(Path.Combine(root, "data", "protected.json"));

try
{
    switch (args[0])
    {
        case "modes":
            foreach (var mode in store.ListModes())
            {
                Console.WriteLine(mode);
            }
            return ExitOk;

        case "validate":
            return Validate(store, policy);

        case "plan" when args.Length >= 2:
            PrintPlan(new ModePlanner(new WindowsSystemProbe(), policy).Plan(store.Load(args[1])));
            return ExitOk;

        default:
            PrintUsage();
            return ExitUsage;
    }
}
catch (ProfileException ex)
{
    Console.Error.WriteLine(ex.Message);
    return ExitInvalid;
}

static int Validate(ProfileStore store, ProtectionPolicy policy)
{
    var failed = false;
    foreach (var mode in store.ListModes())
    {
        var violations = policy.Validate(store.Load(mode));
        Console.WriteLine($"{mode}: {(violations.Count == 0 ? "OK" : "INVALID")}");
        foreach (var violation in violations)
        {
            Console.WriteLine($"  - {violation}");
            failed = true;
        }
    }
    return failed ? ExitInvalid : ExitOk;
}

static void PrintPlan(ModePlan plan)
{
    Console.WriteLine($"Plan for mode '{plan.Mode}' (read-only, nothing is changed):");
    foreach (var group in plan.Changes.GroupBy(change => change.Kind))
    {
        Console.WriteLine();
        Console.WriteLine($"{group.Key} ({group.Count()})");
        foreach (var change in group)
        {
            Console.WriteLine($"  {change.Target,-34} {change.From} -> {change.To}");
        }
    }
    Console.WriteLine();
    Console.WriteLine($"{plan.Changes.Count} change(s), {plan.Skipped.Count} already in the target state or not applicable.");
}

static string? GetOption(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static void PrintUsage()
{
    Console.WriteLine("""
        winmodes - Windows 11 mode switcher (read-only preview build)

        Usage:
          winmodes modes                 List available modes
          winmodes validate              Check every profile against data/protected.json
          winmodes plan <mode>           Show what switching to <mode> would change
        Options:
          --root <path>                  Repository root (default: search upward from the current directory)
        """);
}
