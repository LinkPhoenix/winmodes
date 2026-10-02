using WinModes.Core.Planning;
using WinModes.Core.Profiles;
using WinModes.Core.Protection;
using WinModes.Core.Tuning;

namespace WinModes.Core.Tests;

/// <summary>Checks the mode profiles that ship with the app (profiles/*.json), not samples.</summary>
public sealed class ShippedProfilesTests
{
    private static readonly string[] ExpectedModes = ["code", "eco", "focus", "game", "work"];
    private static readonly string[] PowerPlans = ["power-saver", "balanced", "high-performance", "ultimate-performance"];

    private static string Root => RepositoryLocator.Find(AppContext.BaseDirectory) ?? throw new InvalidOperationException("Repository root not found.");

    private static ProfileStore Store => new(Path.Combine(Root, "profiles"));

    [Fact]
    public void TheAppShipsTheFiveBuiltInModes() => Assert.Equal(ExpectedModes, Store.ListModes());

    [Fact]
    public void EveryShippedProfile_RespectsTheProtectionPolicy()
    {
        var policy = ProtectionPolicy.Load(Path.Combine(Root, "data", "protected.json"));

        Assert.All(Store.ListModes(), mode => Assert.Empty(policy.Validate(Store.Load(mode))));
    }

    [Fact]
    public void EveryShippedProfile_HasANameAnIntentAndAKnownPowerPlan()
    {
        Assert.All(Store.ListModes().Select(Store.Load), profile =>
        {
            Assert.False(string.IsNullOrWhiteSpace(profile.Label));
            Assert.False(string.IsNullOrWhiteSpace(profile.Intent));
            Assert.Contains(profile.Power.Plan, PowerPlans);
        });
    }

    [Fact]
    public void TheSettingsOfAMode_ExistInTheCatalog_AndNeedNoAdministratorNorRestart()
    {
        var catalog = TweakCatalog.Load(Path.Combine(Root, "data", "tweaks.json"));

        foreach (var profile in Store.ListModes().Select(Store.Load))
        {
            foreach (var id in profile.TweakIds)
            {
                var tweak = catalog.Find(id);
                Assert.True(tweak is not null, $"{profile.Mode}: '{id}' is not in the catalog.");
                Assert.True(ModeTweaks.CanBeApplied(new TweakInfo(tweak.Id, tweak.Title, false, tweak.NeedsElevation, tweak.Restart)),
                    $"{profile.Mode}: '{id}' needs the administrator or a restart.");
            }
        }
    }

    [Fact]
    public void ADistractionFreeMode_LeavesTheServicesAndTheDevStackAlone()
    {
        var focus = Store.Load("focus");

        Assert.Empty(focus.Services.Stop);
        Assert.True(focus.Wsl.Running);
        Assert.Null(focus.Wsl.Docker);
        Assert.Contains("toast-notifications-global-off", focus.TweakIds);
    }
}
