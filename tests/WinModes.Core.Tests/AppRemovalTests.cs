using WinModes.Core.Apps;

namespace WinModes.Core.Tests;

public sealed class AppRemovalTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"winmodes-apps-{Guid.NewGuid():N}");

    public AppRemovalTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static InstalledPackage Package(string name, bool framework = false, bool nonRemovable = false) =>
        new(name, $"{name}_1.0.0.0_x64__8wekyb3d8bbwe", $"{name}_8wekyb3d8bbwe", "1.0.0.0",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps", $"{name}_1.0.0.0_x64__8wekyb3d8bbwe"), framework, nonRemovable);

    [Theory]
    [InlineData("Microsoft.WindowsStore")]
    [InlineData("Microsoft.DesktopAppInstaller")]
    [InlineData("Microsoft.StorePurchaseApp")]
    [InlineData("Microsoft.VCLibs.140.00")]
    [InlineData("Microsoft.UI.Xaml.2.8")]
    [InlineData("Microsoft.NET.Native.Runtime.2.2")]
    [InlineData("Microsoft.WindowsAppRuntime.1.5")]
    [InlineData("Microsoft.MicrosoftEdge.Stable")]
    [InlineData("Microsoft.Win32WebViewHost")]
    [InlineData("Microsoft.SecHealthUI")]
    [InlineData("Microsoft.Windows.ShellExperienceHost")]
    [InlineData("MicrosoftWindows.Client.Core")]
    [InlineData("Microsoft.WindowsTerminal")]
    [InlineData("Microsoft.HEIFImageExtension")]
    [InlineData("Microsoft.XboxIdentityProvider")]
    [InlineData("OpenAI.Codex")]
    [InlineData("Claude")]
    public void ThePackagesThatOtherToolsBreak_AreNeverOffered(string name) => Assert.True(AppGuard.IsProtected(Package(name)));

    [Theory]
    [InlineData("Microsoft.BingNews")]
    [InlineData("Clipchamp.Clipchamp")]
    [InlineData("Microsoft.MicrosoftSolitaireCollection")]
    public void AnOrdinaryInboxApp_IsNotProtected(string name) => Assert.False(AppGuard.IsProtected(Package(name)));

    [Fact]
    public void AFrameworkAndAPackageWindowsCallsNonRemovable_AreProtectedWhateverTheirName()
    {
        Assert.True(AppGuard.IsProtected(Package("Contoso.Helper", framework: true)));
        Assert.True(AppGuard.IsProtected(Package("Contoso.Helper", nonRemovable: true)));
    }

    [Theory]
    [InlineData("Microsoft.", true)]
    [InlineData("Micro*", true)]
    [InlineData("Microsoft.WindowsStore", true)]
    [InlineData("Microsoft.Windows.ShellExperienceHost*", true)]
    [InlineData("Microsoft.Win*", true)]
    [InlineData("Microsoft.BingNews", false)]
    [InlineData("Microsoft.Bing*", false)]
    public void ACatalogPatternThatReachesTheProtectedOrIsTooBroad_IsRefused(string pattern, bool refused) => Assert.Equal(refused, AppGuard.IsProtectedPattern(pattern));

    [Theory]
    [InlineData("Microsoft.BingNews", true)]
    [InlineData("Microsoft.BingNews; Remove-Item C:\\", false)]
    [InlineData("a b", false)]
    [InlineData("", false)]
    [InlineData("..\\x", false)]
    public void OnlyPlainPackageNames_ReachWindows(string name, bool valid) => Assert.Equal(valid, AppGuard.IsValidName(name));

    [Fact]
    public void ARemovalScript_RefusesAnythingButAFullName()
    {
        Assert.Contains("Remove-AppxPackage -Package 'Microsoft.BingNews_1.0.0.0_x64__8wekyb3d8bbwe'", PackageCommands.RemoveScript("Microsoft.BingNews_1.0.0.0_x64__8wekyb3d8bbwe"), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => PackageCommands.RemoveScript("x'; Remove-Item C:\\ -Recurse; '"));
    }

    [Fact]
    public void ARestoreScript_OnlyRegistersAFolderOfWindowsApps()
    {
        var folder = Package("Microsoft.BingNews").InstallLocation;

        Assert.Contains("AppxManifest.xml", PackageCommands.RestoreScript(folder), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => PackageCommands.RestoreScript(@"C:\Users\Someone\Downloads\evil"));
        Assert.Throws<ArgumentException>(() => PackageCommands.RestoreScript(folder + @"\..\..\evil"));
        Assert.Throws<ArgumentException>(() => PackageCommands.RestoreScript(folder + "'; Remove-Item C:\\; '"));
    }

    [Fact]
    public void TheList_IsParsedWhetherPowerShellGivesOneObjectOrAnArray_AndBadNamesAreDropped()
    {
        const string One = """{"Name":"Microsoft.BingNews","FullName":"Microsoft.BingNews_1.0.0.0_x64__8wekyb3d8bbwe","Family":"F","Version":"1.0.0.0","InstallLocation":"C:\\x","IsFramework":false,"NonRemovable":false}""";
        const string Two = """[{"Name":"A.B","FullName":"A.B_1_x64__x","IsFramework":true},{"Name":"bad name","FullName":"x"},{"Name":"C.D","FullName":"C.D_1_x64__x","NonRemovable":true}]""";

        Assert.Single(PackageCommands.ParseList(One));
        var two = PackageCommands.ParseList(Two);
        Assert.Equal(["A.B", "C.D"], two.Select(package => package.Name));
        Assert.True(two[0].IsFramework);
        Assert.True(two[1].NonRemovable);
        Assert.Empty(PackageCommands.ParseList(""));
        Assert.Empty(PackageCommands.ParseList("not json"));
    }

    [Fact]
    public void AScript_IsHandedToPowerShellAsBase64OfUtf16()
    {
        var encoded = PackageCommands.Encode("Get-Date");

        Assert.Equal("Get-Date", System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(encoded)));
    }

    [Fact]
    public void TheCatalog_OffersAnInstalledPackageOnlyIfAnEntryNamesIt_AndDropsEntriesThatReachTheProtected()
    {
        var path = Path.Combine(_directory, "apps.json");
        File.WriteAllText(path, """
            [
              { "id": "news", "title": "News", "packages": ["Microsoft.BingNews"], "tier": "safe", "category": "News" },
              { "id": "store", "title": "Store", "packages": ["Microsoft.WindowsStore"], "tier": "safe" },
              { "id": "broad", "title": "Everything", "packages": ["Microsoft.*"], "tier": "safe" },
              { "id": "mixed", "title": "Mixed", "packages": ["Contoso.Tool", "Microsoft.VCLibs*"], "tier": "consider" },
              { "id": "news", "title": "Duplicate", "packages": ["Contoso.News"], "tier": "safe" }
            ]
            """);

        var catalog = AppCatalog.Load(path);

        Assert.Equal(["news"], catalog.Entries.Select(entry => entry.Id));
        Assert.Equal(AppTier.Safe, catalog.Entries[0].Tier);
        Assert.NotNull(catalog.Find(Package("Microsoft.BingNews")));
        Assert.Null(catalog.Find(Package("Microsoft.BingWeather")));
        Assert.Null(catalog.Find(Package("Microsoft.WindowsStore")));
    }

    [Fact]
    public void ACatalogThatCannotBeRead_OffersNothing()
    {
        var path = Path.Combine(_directory, "broken.json");
        File.WriteAllText(path, "{ not json");

        Assert.Empty(AppCatalog.Load(path).Entries);
        Assert.Empty(AppCatalog.Load(Path.Combine(_directory, "missing.json")).Entries);
    }

    [Fact]
    public void TheJournal_KeepsOneLinePerApp_AndForgetsOnRequest()
    {
        var journal = new RemovedAppsJournal(Path.Combine(_directory, "removed.json"));
        var app = new RemovedApp { EntryId = "news", Title = "News", Name = "Microsoft.BingNews", FullName = "Microsoft.BingNews_1_x64__x", RemovedUtc = DateTimeOffset.UtcNow };

        journal.Add(app);
        journal.Add(app with { Version = "2.0" });

        var saved = Assert.Single(journal.Load());
        Assert.Equal("2.0", saved.Version);
        journal.Forget("microsoft.bingnews");
        Assert.Empty(journal.Load());
    }
}
