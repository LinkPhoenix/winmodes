using WinModes.Core.Planning;

namespace WinModes.Core.Tests;

public sealed class ProcessFactsTests
{
    [Theory]
    [InlineData(0, ProcessPriority.Unknown)]
    [InlineData(-1, ProcessPriority.Unknown)]
    [InlineData(4, ProcessPriority.Idle)]
    [InlineData(6, ProcessPriority.BelowNormal)]
    [InlineData(8, ProcessPriority.Normal)]
    [InlineData(10, ProcessPriority.AboveNormal)]
    [InlineData(13, ProcessPriority.High)]
    [InlineData(24, ProcessPriority.Realtime)]
    public void PriorityOf_NamesTheClassesOfWindows(int basePriority, ProcessPriority expected) =>
        Assert.Equal(expected, ProcessFacts.PriorityOf(basePriority));

    [Theory]
    [InlineData(5, ProcessPriority.BelowNormal)]
    [InlineData(9, ProcessPriority.Normal)]
    [InlineData(12, ProcessPriority.AboveNormal)]
    [InlineData(23, ProcessPriority.High)]
    public void PriorityOf_PutsAValueBetweenTwoClassesInTheLowerOne(int basePriority, ProcessPriority expected) =>
        Assert.Equal(expected, ProcessFacts.PriorityOf(basePriority));

    [Fact]
    public void RunningFor_IsTheTimeSinceTheStart()
    {
        var now = new DateTime(2026, 10, 7, 12, 0, 0);

        Assert.Equal(TimeSpan.FromMinutes(90), ProcessFacts.RunningFor(now.AddMinutes(-90), now));
        Assert.Equal(TimeSpan.Zero, ProcessFacts.RunningFor(now, now));
    }

    [Fact]
    public void RunningFor_IsUnknownWithoutAStartTimeOrWhenTheClockWentBack()
    {
        var now = new DateTime(2026, 10, 7, 12, 0, 0);

        Assert.Null(ProcessFacts.RunningFor(null, now));
        Assert.Null(ProcessFacts.RunningFor(now.AddMinutes(5), now));
    }
}
