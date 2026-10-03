using System.Text.Json;
using WinModes.Core.Tuning;

namespace WinModes.Core.Tests;

/// <summary>Checks the real data/tweaks.json that ships with the app, not a sample.</summary>
public sealed class TweakCatalogDataTests
{
    private static readonly string[] Categories =
        ["Privacy and telemetry", "Ads and suggestions", "Search and AI", "Gaming", "System and background", "Explorer and developer"];

    private static readonly string[] Risks = ["low", "medium", "high"];

    private static string CatalogPath
    {
        get
        {
            var root = RepositoryLocator.Find(AppContext.BaseDirectory) ?? throw new InvalidOperationException("Repository root not found.");
            return Path.Combine(root, "data", "tweaks.json");
        }
    }

    private static int RawCount() => JsonDocument.Parse(File.ReadAllText(CatalogPath)).RootElement.GetArrayLength();

    [Fact]
    public void EveryTweakOfTheCatalog_PassesTheGuard()
    {
        // A tweak that breaks the guard is silently dropped when the catalog loads; this makes that visible.
        var loaded = TweakCatalog.Load(CatalogPath);

        Assert.Equal(RawCount(), loaded.Tweaks.Count);
    }

    [Fact]
    public void NoRegistryValue_IsSetByTwoTweaks()
    {
        // Two tweaks on one value would fight over the journal: undoing one would leave the other half done.
        var catalog = TweakCatalog.Load(CatalogPath);

        var duplicates = catalog.Tweaks
            .SelectMany(tweak => tweak.Values.Select(value => (Key: $"{value.Hive}|{value.Path}|{value.Name}".ToUpperInvariant(), tweak.Id)))
            .GroupBy(pair => pair.Key)
            .Where(group => group.Select(pair => pair.Id).Distinct().Count() > 1)
            .Select(group => $"{group.Key} in {string.Join(", ", group.Select(pair => pair.Id).Distinct())}")
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void NoScheduledTask_IsDisabledByTwoTweaks()
    {
        var catalog = TweakCatalog.Load(CatalogPath);

        var duplicates = catalog.Tweaks
            .SelectMany(tweak => tweak.Tasks.Select(task => (Task: task.ToUpperInvariant(), tweak.Id)))
            .GroupBy(pair => pair.Task)
            .Where(group => group.Select(pair => pair.Id).Distinct().Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void EveryTweak_HasAKnownCategory_ATitleAndADescription()
    {
        var catalog = TweakCatalog.Load(CatalogPath);

        Assert.All(catalog.Tweaks, tweak =>
        {
            Assert.Contains(tweak.Category, Categories);
            Assert.False(string.IsNullOrWhiteSpace(tweak.Title));
            Assert.False(string.IsNullOrWhiteSpace(tweak.Description));
            Assert.Contains(tweak.Risk, Risks);
        });
    }

    [Fact]
    public void ARecommendedTweak_IsLowRisk()
    {
        var catalog = TweakCatalog.Load(CatalogPath);

        Assert.All(catalog.Tweaks.Where(tweak => tweak.Recommended), tweak => Assert.Equal("low", tweak.Risk));
    }
}
