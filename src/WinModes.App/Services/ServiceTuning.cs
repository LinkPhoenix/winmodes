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

    public static Task<TuningReport> RunAsync(TuneAction action, params string[] targets) => RunAsync([(action, targets)]);

    public static async Task<TuningReport> RunAsync(IReadOnlyList<(TuneAction Action, IReadOnlyList<string> Targets)> groups)
    {
        var machineJournal = await Task.Run(() => new TweakJournal(AppPaths.MachineTweakJournal).Load().Select(record => record.Id).ToHashSet(StringComparer.OrdinalIgnoreCase));
        var arguments = new List<string> { ChangeVerb };
        foreach (var (action, targets) in groups)
        {
            // Only the tweaks with a machine-wide part concern the helper.
            var forHelper = action switch
            {
                TuneAction.Tweak => [.. targets.Where(id => Catalog.Find(id)?.NeedsElevation == true)],
                TuneAction.Untweak => [.. targets.Where(machineJournal.Contains)],
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

    private static List<TuneResult> ApplyUserPart(IReadOnlyList<(TuneAction Action, IReadOnlyList<string> Targets)> groups)
    {
        var results = new List<TuneResult>();
        var journaled = UserTweaks.JournaledIds().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (action, targets) in groups)
        {
            if (action == TuneAction.Tweak)
            {
                results.AddRange(targets.Select(Catalog.Find).Where(tweak => tweak is { HasUserPart: true }).Select(tweak => UserTweaks.Apply(tweak!)));
            }
            else if (action == TuneAction.Untweak)
            {
                results.AddRange(targets.Where(journaled.Contains).Select(UserTweaks.Undo));
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
