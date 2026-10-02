using WinModes.Core.Apps;

namespace WinModes.Core.Tests;

public sealed class StartupApprovalTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"winmodes-startup-{Guid.NewGuid():N}");

    public StartupApprovalTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData(null, true)]
    [InlineData(new byte[0], true)]
    [InlineData(new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x03, 0, 0, 0, 0x4e, 0xe9, 0x28, 0x25, 0xfb, 0x3d, 0xdc, 0x01 }, false)]
    [InlineData(new byte[] { 0x07, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, false)]
    public void ASetOfBytesIsEnabledWhenItsFirstByteIsEven(byte[]? raw, bool enabled) => Assert.Equal(enabled, StartupApproval.IsEnabled(raw));

    [Fact]
    public void TurningOff_WritesTheStatusAndTheTime_ThatTaskManagerShows()
    {
        var when = new DateTimeOffset(2026, 10, 2, 9, 30, 0, TimeSpan.Zero);

        var raw = StartupApproval.Disable(when);

        Assert.Equal(StartupApproval.Length, raw.Length);
        Assert.Equal(0x03, raw[0]);
        Assert.False(StartupApproval.IsEnabled(raw));
        Assert.Equal(when, StartupApproval.DisabledAt(raw));
    }

    [Fact]
    public void TheTimeOfARealValueOfThisKind_IsRead()
    {
        // Read from a real Windows 11 registry: Proxifier, disabled.
        byte[] raw = [0x03, 0, 0, 0, 0x4e, 0xe9, 0x28, 0x25, 0xfb, 0x3d, 0xdc, 0x01];

        Assert.InRange(StartupApproval.DisabledAt(raw)!.Value.Year, 2024, 2026);
        Assert.NotNull(StartupApproval.DisabledAt(raw));
        Assert.Null(StartupApproval.DisabledAt([0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
    }

    [Fact]
    public void TurningOn_KeepsAStatusWindowsWroteItself_AndOtherwiseUsesTwo()
    {
        Assert.Equal(0x06, StartupApproval.Enable([0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0])[0]);
        Assert.Equal(0x02, StartupApproval.Enable([0x03, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8])[0]);
        Assert.Equal(0x02, StartupApproval.Enable(null)[0]);
        Assert.True(StartupApproval.HasNoTime(StartupApproval.Enable([0x03, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8])));
    }

    [Theory]
    [InlineData(new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x03, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8 }, true)]
    [InlineData(new byte[] { 0x09, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 0x02, 0, 0, 0 }, false)]
    public void AValueWinModesDoesNotUnderstand_IsLeftAlone(byte[] raw, bool canChange) => Assert.Equal(canChange, StartupApproval.CanChange(raw));

    [Theory]
    [InlineData("\"C:\\Program Files\\Steam\\steam.exe\" -silent", @"C:\Program Files\Steam\steam.exe")]
    [InlineData(@"C:\Program Files\Docker\Docker\Docker Desktop.exe", @"C:\Program Files\Docker\Docker\Docker Desktop.exe")]
    [InlineData(@"C:\Tools\tool.exe /run now", @"C:\Tools\tool.exe")]
    [InlineData("wscript.exe //B script.vbs", "wscript.exe")]
    public void TheProgramIsTheFirstQuotedPartOrTheWordEndingInExe(string command, string expected) =>
        Assert.Equal(expected, new StartupItem("x", command, StartupSource.UserRun, null).Program);

    [Fact]
    public void SecurityAudioAndWinModesItself_AreNeverOffered()
    {
        static bool NoUserId(string name) => name == "Discord";

        Assert.True(StartupGuard.IsProtected("SecurityHealth", NoUserId));
        Assert.True(StartupGuard.IsProtected("Bitdefender Agent", NoUserId));
        Assert.True(StartupGuard.IsProtected("Adguard", NoUserId));
        Assert.True(StartupGuard.IsProtected("WinModes", NoUserId));
        Assert.True(StartupGuard.IsProtected("Discord", NoUserId));
        Assert.False(StartupGuard.IsProtected("Steam", NoUserId));
    }

    [Fact]
    public void TheJournal_KeepsTheFirstValueOfAnItem_AndForgetsOnReset()
    {
        var journal = new StartupJournal(Path.Combine(_directory, "startup.json"));
        byte[] first = [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

        journal.Remember(StartupSource.UserRun, "Steam", first);
        journal.Remember(StartupSource.UserRun, "Steam", StartupApproval.Disable(DateTimeOffset.UtcNow));
        journal.Remember(StartupSource.UserFolder, "Steam", null);

        Assert.Equal(first, journal.Find(StartupSource.UserRun, "steam")?.BeforeBytes);
        Assert.Null(journal.Find(StartupSource.UserFolder, "Steam")?.BeforeBytes);
        Assert.Equal(2, journal.Load().Count);
        journal.Forget(StartupSource.UserRun, "Steam");
        Assert.Null(journal.Find(StartupSource.UserRun, "Steam"));
    }
}
