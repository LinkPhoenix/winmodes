namespace WinModes.Core.Tests;

public sealed class TaskbarVisibilityTests
{
    // A 1280 px high screen with a 48 px taskbar along the bottom, or along the top.
    [Theory]
    [InlineData(1232, 1280, false)] // shown at the bottom
    [InlineData(1280, 1328, true)] // slid right off the bottom edge
    [InlineData(1277, 1325, true)] // only the line that reveals it is left
    [InlineData(1250, 1298, false)] // half way in: sliding, still worth placing the widget
    [InlineData(0, 48, false)] // shown at the top
    [InlineData(-48, 0, true)] // slid off the top edge
    [InlineData(-45, 3, true)] // only the line at the top is left
    public void SlidOut_FollowsHowMuchOfTheBarIsOnTheScreen(int barTop, int barBottom, bool expected) =>
        Assert.Equal(expected, TaskbarVisibility.IsSlidOut(barTop, barBottom, 0, 1280));

    [Fact]
    public void SecondScreen_UsesItsOwnEdges()
    {
        // A screen placed above the primary one: its bottom edge is at 0.
        Assert.False(TaskbarVisibility.IsSlidOut(-48, 0, -1080, 0));
        Assert.True(TaskbarVisibility.IsSlidOut(0, 48, -1080, 0));
    }
}
