using WinModes.Core.Apps;

namespace WinModes.Core.Tests;

public sealed class OneDriveCheckTests
{
    private const string Root = @"C:\Users\Alex\OneDrive";

    [Fact]
    public void AFolderInsideAnyRoot_IsReported_AndOthersAreNot()
    {
        var folders = new Dictionary<string, string>
        {
            ["Desktop"] = @"C:\Users\Alex\OneDrive\Desktop",
            ["Documents"] = @"C:\Users\Alex\Documents",
            ["Pictures"] = @"D:\Work\OneDrive - Contoso\Pictures",
            ["Downloads"] = @"C:\Users\Alex\OneDrive-notes\Downloads",
        };

        var redirected = OneDriveCheck.Redirected(folders, [Root, @"D:\Work\OneDrive - Contoso"]);

        // OneDrive-notes is a sibling folder, not a OneDrive root: a name that merely starts the same is not inside.
        Assert.Equal(["Desktop", "Pictures"], redirected);
    }

    [Fact]
    public void ARootThatIsTheFolderItself_CountsAsInside() =>
        Assert.Equal(["Desktop"], OneDriveCheck.Redirected(new Dictionary<string, string> { ["Desktop"] = Root + @"\" }, [Root]));

    [Fact]
    public void NoRootsOrBlankPaths_ReportNothing()
    {
        Assert.Empty(OneDriveCheck.Redirected(new Dictionary<string, string> { ["Desktop"] = Root }, []));
        Assert.Empty(OneDriveCheck.Redirected(new Dictionary<string, string> { ["Desktop"] = "" }, [Root]));
        Assert.Empty(OneDriveCheck.Redirected(new Dictionary<string, string> { ["Desktop"] = Root }, ["  "]));
    }

    [Theory]
    [InlineData(0x00400000, true)]
    [InlineData(0x00040000, true)]
    [InlineData((int)FileAttributes.Offline, true)]
    [InlineData((int)FileAttributes.Normal, false)]
    [InlineData((int)(FileAttributes.Hidden | FileAttributes.Archive), false)]
    public void OnlyPlaceholdersOfOnlineFiles_AreCounted(int attributes, bool online) => Assert.Equal(online, OneDriveCheck.IsOnlineOnly((FileAttributes)attributes));

    [Fact]
    public void UninstallingIsRefusedWhileAKnownFolderLivesInOneDrive_AndConfirmationIsAskedForRiskyStates()
    {
        var clean = new OneDriveState(true, [Root], false, [], 0, false);
        Assert.True(clean.CanUninstall);
        Assert.False(clean.NeedsConfirmation);

        Assert.False(clean with { RedirectedFolders = ["Documents"] } is { CanUninstall: true });
        Assert.False((clean with { Installed = false }).CanUninstall);
        Assert.True((clean with { SignedIn = true }).NeedsConfirmation);
        Assert.True((clean with { OnlineOnlyFiles = 3 }).NeedsConfirmation);
    }

    [Fact]
    public void TheSetupProgram_IsOnlyEverTheOneOfWindows()
    {
        var found = OneDriveCheck.SetupProgram(path => path.EndsWith(@"System32\OneDriveSetup.exe", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(found);
        Assert.EndsWith(@"System32\OneDriveSetup.exe", found, StringComparison.OrdinalIgnoreCase);
        Assert.Null(OneDriveCheck.SetupProgram(_ => false));
    }
}
