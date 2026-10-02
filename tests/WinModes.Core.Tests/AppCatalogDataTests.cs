using System.Text.Json;
using WinModes.Core.Apps;

namespace WinModes.Core.Tests;

/// <summary>Checks the real data/apps.json that ships with the app.</summary>
public sealed class AppCatalogDataTests
{
    private static string CatalogPath => Path.Combine(RepositoryLocator.Find(AppContext.BaseDirectory) ?? throw new InvalidOperationException("Repository root not found."), "data", "apps.json");

    [Fact]
    public void EveryEntryOfTheCatalog_SurvivesTheGuard()
    {
        // An entry that reaches a protected package is dropped when the catalog loads; this makes that visible.
        var raw = JsonDocument.Parse(File.ReadAllText(CatalogPath)).RootElement.GetArrayLength();

        Assert.Equal(raw, AppCatalog.Load(CatalogPath).Entries.Count);
        Assert.True(raw >= 50, "The catalog should hold the usual preinstalled apps.");
    }

    [Fact]
    public void EveryEntry_HasATitle_AWhy_AndNoPatternThatStartsWithAWildcard()
    {
        Assert.All(AppCatalog.Load(CatalogPath).Entries, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Title));
            Assert.False(string.IsNullOrWhiteSpace(entry.Why));
            Assert.All(entry.Packages, pattern => Assert.False(pattern.StartsWith('*')));
        });
    }

    [Fact]
    public void ACheckFirstApp_SaysWhatStopsWorkingWithoutIt()
    {
        var withoutWarning = AppCatalog.Load(CatalogPath).Entries
            .Where(entry => entry.Tier == AppTier.Consider && string.IsNullOrWhiteSpace(entry.BreaksIfRemoved))
            .Select(entry => entry.Id)
            .ToList();

        // A few have nothing known to break; the rest must warn.
        Assert.True(withoutWarning.Count <= 8, $"Too many 'check first' apps without a warning: {string.Join(", ", withoutWarning)}");
    }

    [Theory]
    [InlineData("Microsoft.WindowsStore")]
    [InlineData("Microsoft.DesktopAppInstaller")]
    [InlineData("Microsoft.WindowsTerminal")]
    [InlineData("Microsoft.MicrosoftEdge.Stable")]
    [InlineData("Microsoft.VCLibs.140.00")]
    [InlineData("SpotifyAB.SpotifyMusic")]
    [InlineData("MicrosoftWindows.Client.WebExperience")]
    public void ThePackagesThatBreakOtherTools_AreNotInTheCatalog(string name)
    {
        var package = new InstalledPackage(name, name + "_1.0.0.0_x64__8wekyb3d8bbwe", name + "_8wekyb3d8bbwe", "1.0.0.0", @"C:\Program Files\WindowsApps\x", false, false);

        Assert.Null(AppCatalog.Load(CatalogPath).Find(package));
    }
}
