using System.IO;
using WinModes.Core;
using WinModes.Core.Engine;
using WinModes.Core.Tuning;

namespace WinModes.App.Services;

/// <summary>Outcome of a change asked from the Services or Optimize page.</summary>
internal sealed record TuningReport(bool Succeeded, string Summary, IReadOnlyList<TuneResult> Results);

/// <summary>
/// Applies what the Optimize and Services pages ask. Machine-wide changes (services, HKLM values, scheduled
/// tasks) go to the elevated helper in one call, so one UAC prompt; the helper re-checks every name and id
/// itself. The per-user part of a tweak (HKCU) is written here, because the helper may run under another profile.
/// </summary>
internal static class ServiceTuning
{
    private const string ChangeVerb = "change";

    private static readonly string? Root = RepositoryLocator.Find(AppContext.BaseDirectory) ?? RepositoryLocator.Find(Environment.CurrentDirectory);

    private static readonly Lazy<ServiceKnowledge> LazyKnowledge = new(() => ServiceKnowledge.Load(Root is null ? "" : Path.Combine(Root, "data", "db")));

    private static readonly Lazy<TweakCatalog> LazyCatalog = new(() => TweakCatalog.Load(Root is null ? "" : Path.Combine(Root, "data", "tweaks.json")));

    private static readonly string UserJournalPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinModes", "tweaks.json");

    public static ServiceKnowledge Knowledge => LazyKnowledge.Value;

    public static TweakCatalog Catalog => LazyCatalog.Value;

    public static TweakStore Store { get; } = new(AppPaths.TweaksDirectory);

    /// <summary>User scope: reads every state, writes only HKCU.</summary>
    public static TweakEngine UserTweaks { get; } =
        new(new WindowsRegistryAccess(), new WindowsTaskControl(), new TweakJournal(UserJournalPath), machineScope: false);

    /// <summary>Ids of the tweaks that can be undone, whichever scope applied them.</summary>
    public static HashSet<string> LoadUndoableTweaks() =>
        [.. UserTweaks.JournaledIds().Concat(new TweakJournal(AppPaths.MachineTweakJournal).Load().Select(record => record.Id))];

    /// <summary>For each tweak WinModes changed: the parts it changed, which are the parts that can be put back one by one.</summary>
    public static Dictionary<string, HashSet<int>> LoadJournaledParts()
    {
        var parts = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in new TweakJournal(UserJournalPath).Load().Concat(new TweakJournal(AppPaths.MachineTweakJournal).Load()))
        {
            if (Catalog.Find(record.Id) is { } tweak)
            {
                parts.TryAdd(record.Id, []);
                parts[record.Id].UnionWith(record.PartsOf(tweak));
            }
        }

        return parts;
    }

    public static Task<TuningReport> RunAsync(TuneAction action, params string[] targets) => RunAsync([(action, targets)]);

    public static async Task<TuningReport> RunAsync(IReadOnlyList<(TuneAction Action, IReadOnlyList<string> Targets)> groups)
    {
        var machineJournal = await Task.Run(() => new TweakJournal(AppPaths.MachineTweakJournal).Load().ToDictionary(record => record.Id, StringComparer.OrdinalIgnoreCase));
        var arguments = new List<string> { ChangeVerb };
        foreach (var (action, targets) in groups)
        {
            // Only the tweaks with a machine-wide part concern the helper. A target is "id" or "id#parts" (see TweakSelection).
            var forHelper = action switch
            {
                TuneAction.Tweak => [.. targets.Where(target => TweakSelection.TryParse(target, out var selection)
                    && Catalog.Find(selection.Id)?.NeedsElevationFor(selection.Parts) == true)],
                TuneAction.Untweak => [.. targets.Where(target => TweakSelection.TryParse(target, out var selection) && ChangedByHelper(machineJournal, selection))],
                _ => targets,
            };
            if (forHelper.Count > 0 || action == TuneAction.Restore)
            {
                arguments.Add(":" + action.ToString().ToLowerInvariant());
                arguments.AddRange(forHelper);
            }
        }

        var results = new List<TuneResult>();
        if (arguments.Count > 1)
        {
            // Machine part first: when the prompt is refused, nothing at all has changed.
            var error = await ModeSwitcher.RunHelperAsync([.. arguments]);
            if (error is not null)
            {
                return new TuningReport(false, error, []);
            }

            results.AddRange(await Task.Run(Store.LoadLastResults));
        }

        results.AddRange(await Task.Run(() => ApplyUserPart(groups)));
        var merged = Merge(results);
        return new TuningReport(true, Describe(merged), merged);
    }

    /// <summary>True when the machine journal holds a change of the selection, so the helper (and its permission prompt) is worth starting.</summary>
    private static bool ChangedByHelper(Dictionary<string, TweakRecord> machineJournal, TweakSelection selection) =>
        machineJournal.TryGetValue(selection.Id, out var record)
        && (selection.Parts is null || Catalog.Find(selection.Id) is { } tweak && record.PartsOf(tweak).Overlaps(selection.Parts));

    private static List<TuneResult> ApplyUserPart(IReadOnlyList<(TuneAction Action, IReadOnlyList<string> Targets)> groups)
    {
        var results = new List<TuneResult>();
        var journaled = UserTweaks.JournaledIds().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (action, targets) in groups)
        {
            foreach (var target in targets)
            {
                if (!TweakSelection.TryParse(target, out var selection))
                {
                    continue;
                }

                if (action == TuneAction.Tweak && Catalog.Find(selection.Id) is { } tweak && tweak.HasUserPartFor(selection.Parts))
                {
                    results.Add(UserTweaks.Apply(tweak, selection.Parts));
                }
                else if (action == TuneAction.Untweak && journaled.Contains(selection.Id))
                {
                    // Undoing a whole tweak needs only the journal, even when the catalog no longer lists the tweak.
                    results.Add(selection.Parts is null ? UserTweaks.Undo(selection.Id)
                        : Catalog.Find(selection.Id) is { } known ? UserTweaks.Undo(known, selection.Parts)
                        : new TuneResult(selection.Id, action, TuneOutcome.Skipped, "Unknown tweak."));
                }
            }
        }

        return results;
    }

    /// <summary>A tweak with a user and a machine part gives two results; keep one line per target, the worst outcome first.</summary>
    private static List<TuneResult> Merge(List<TuneResult> results) =>
        [.. results
            .GroupBy(result => (result.Target.ToUpperInvariant(), result.Action))
            .Select(group => group.OrderByDescending(result => result.Outcome switch
            {
                TuneOutcome.Failed => 2,
                TuneOutcome.Done => 1,
                _ => 0,
            }).First())];

    private static string Describe(List<TuneResult> results)
    {
        string Name(TuneResult result) => Catalog.Find(result.Target) is { } tweak ? Loc.T(tweak.Title) : result.Target;

        if (results is [var only])
        {
            return only.Outcome switch
            {
                TuneOutcome.Done => Loc.F("{0}: done. {1}", Name(only), only.Detail).TrimEnd(),
                TuneOutcome.Skipped => Loc.F("{0}: not changed. {1}", Name(only), only.Detail),
                _ => Loc.F("{0}: failed. {1}", Name(only), only.Detail),
            };
        }

        var done = results.Count(result => result.Outcome == TuneOutcome.Done);
        var problems = results.Where(result => result.Outcome != TuneOutcome.Done)
            .Select(result => $"{Name(result)} ({result.Detail})").ToList();
        return problems.Count == 0
            ? Loc.F("{0} changes made.", done)
            : Loc.F("{0} changes made, {1} not made: {2}", done, problems.Count, string.Join("; ", problems));
    }
}
