using WinModes.Core.Software;

namespace WinModes.Core.Tests;

public sealed class SoftwareInventoryTests
{
    [Fact]
    public void ListReadsInstalledVersionWithoutRequiringAnExportableVersion()
    {
        var inventory = SoftwareInventory.ParseList("""
            Nom                  ID                       Version       Disponible   Source
            --------------------------------------------------------------------------------
            Google Chrome        Google.Chrome            154.0.8037.98  155.0.8059.40 winget
            Cursor (User)        Anysphere.Cursor          3.23.12                     winget
            Unrelated app        Unknown.Other            1.0                         winget
            """);
        Assert.Equal("154.0.8037.98", inventory.Find(Entry("chrome"))?.Version);
        Assert.Equal("3.23.12", inventory.Find(Entry("cursor"))?.Version);
        Assert.Equal(2, inventory.Packages.Count);
    }

    [Fact]
    public void ListRejectsWrongSourcePartialIdsAndTruncatedIds()
    {
        var inventory = SoftwareInventory.ParseList("""
            Brave    Brave.Brave.Beta   1.0   winget
            Brave    Brave.Brave        1.0   other-source
            Cursor   Anysphere.Curs…    1.0   winget
            """);
        Assert.Empty(inventory.Packages);
    }

    [Fact]
    public void RegisteredBraveIsRecognizedWhenWinGetOnlyShowsAnArpIdentity()
    {
        var inventory = SoftwareInventory.ParseList("Brave   ARP\\User\\X64\\BraveSoftware Brave-Browser   154.1.96.61");
        var combined = WindowsSoftwareInventory.MergeRegistered(inventory, [new("Brave", "Brave Software Inc", "154.1.96.61")]);
        Assert.Equal("154.1.96.61", combined.Find(Entry("brave"))?.Version);
    }

    [Theory]
    [InlineData("Brave Beta", "Brave Software Inc")]
    [InlineData("Brave", "Another publisher")]
    [InlineData("Brave Browser Helper", "Brave Software Inc")]
    public void RegistryDoesNotGuessFromPartialNamesOrUnrelatedPublishers(string name, string publisher)
    {
        var inventory = WindowsSoftwareInventory.MergeRegistered(SoftwareInventory.ParseList(""), [new(name, publisher, "1.0")]);
        Assert.Null(inventory.Find(Entry("brave")));
    }

    [Fact]
    public void RegistryFallbackPreservesWinGetObservationAndDeduplicatesRegistryViews()
    {
        var inventory = SoftwareInventory.ParseList("Brave   Brave.Brave   154.1.96.61   winget");
        var combined = WindowsSoftwareInventory.MergeRegistered(inventory,
            [new("Brave", "Brave Software Inc", "older"), new("Brave", "Brave Software Inc", "older")]);
        Assert.Single(combined.Packages);
        Assert.Equal("154.1.96.61", combined.Find(Entry("brave"))?.Version);
    }

    [Fact]
    public void PackagedChatGptMatchesOfficialPublisherFamilyAndNeverCodexCli()
    {
        var inventory = SoftwareInventory.ParseList("ChatGPT   MSIX\\OpenAI.Codex_26.930.7945.0_x64__2p2nqsd0c76g0   26.930.7945.0");
        Assert.Equal("26.930.7945.0", inventory.Find(Entry("chatgpt"))?.Version);
        Assert.Null(inventory.Find(Entry("codex")));
        Assert.Empty(SoftwareInventory.ParseList("ChatGPT   MSIX\\OpenAI.Codex_1.0_x64__otherpublisher   1.0").Packages);
    }

    private static SoftwareEntry Entry(string id) => SoftwareCatalog.Entries.Single(entry => entry.Id == id);
}
