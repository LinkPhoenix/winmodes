namespace WinModes.Core.Tests;

public sealed class StartupEntryTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\WinModes\\WinModes.exe\" --minimized", "C:\\Program Files\\WinModes\\WinModes.exe")]
    [InlineData("\"C:\\Apps\\WinModes.exe\"", "C:\\Apps\\WinModes.exe")]
    [InlineData("C:\\Apps\\WinModes.exe --minimized", "C:\\Apps\\WinModes.exe")]
    [InlineData("C:\\Program Files\\WinModes\\WINMODES.EXE --minimized", "C:\\Program Files\\WinModes\\WINMODES.EXE")]
    [InlineData("  \"D:\\tools\\winmodes.exe\"  ", "D:\\tools\\winmodes.exe")]
    [InlineData("C:\\Apps\\run --flag", "C:\\Apps\\run")]
    public void ProgramOf_TakesTheProgramWhateverTheQuotes(string command, string expected) => Assert.Equal(expected, StartupEntry.ProgramOf(command));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ProgramOf_IsNullWithoutACommand(string? command) => Assert.Null(StartupEntry.ProgramOf(command));

    [Fact]
    public void ProgramOf_ExpandsEnvironmentVariables()
    {
        var program = StartupEntry.ProgramOf("\"%SystemRoot%\\System32\\cmd.exe\" /c");

        Assert.DoesNotContain('%', program!);
        Assert.EndsWith("System32\\cmd.exe", program, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Inspect_TellsAWorkingEntryFromAMissingTargetAndNoEntry()
    {
        Assert.Equal(StartupState.NotRegistered, StartupEntry.Inspect(null, _ => true));
        Assert.Equal(StartupState.Working, StartupEntry.Inspect("\"C:\\a\\WinModes.exe\" --minimized", path => path == "C:\\a\\WinModes.exe"));
        Assert.Equal(StartupState.TargetMissing, StartupEntry.Inspect("\"C:\\a\\WinModes.exe\" --minimized", _ => false));
    }
}
