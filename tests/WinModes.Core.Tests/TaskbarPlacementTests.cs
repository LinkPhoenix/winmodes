namespace WinModes.Core.Tests;

public sealed class TaskbarPlacementTests
{
    private static readonly int[] Widths = [660, 520, 330, 260];
    private const int Gap = 8;

    // Icons centred on a 3840 px bar: free room left of them, little between them and the notification area.
    [Fact]
    public void Auto_WithCentredIcons_UsesTheLeftEdge()
    {
        var slot = TaskbarPlacement.Choose(TaskbarSide.Auto, 0, 1320, 2540, 2831, Widths, Gap);

        Assert.Equal(new TaskbarSlot(0, Gap), slot);
    }

    // Icons at the left: the room is between them and the notification area, and the widget sits against the latter.
    [Fact]
    public void Auto_WithLeftIcons_SitsAgainstTheNotificationArea()
    {
        var slot = TaskbarPlacement.Choose(TaskbarSide.Auto, 0, 0, 1211, 2831, Widths, Gap);

        Assert.Equal(new TaskbarSlot(0, 2831 - Gap - 660), slot);
    }

    [Fact]
    public void ExplicitSide_IsHonouredWhenBothFit()
    {
        Assert.Equal(Gap, TaskbarPlacement.Choose(TaskbarSide.Left, 0, 1320, 1400, 2831, Widths, Gap)!.Value.X);
        Assert.Equal(2831 - Gap - 660, TaskbarPlacement.Choose(TaskbarSide.Right, 0, 1320, 1400, 2831, Widths, Gap)!.Value.X);
    }

    [Fact]
    public void ExplicitSide_GivesWayWhenItDoesNotFit()
    {
        // The left part is empty (icons start at the edge), so "left" ends up on the right.
        var slot = TaskbarPlacement.Choose(TaskbarSide.Left, 0, 0, 1211, 2831, Widths, Gap);

        Assert.Equal(new TaskbarSlot(0, 2831 - Gap - 660), slot);
    }

    [Fact]
    public void ExplicitSide_ShrinksBeforeGivingWay()
    {
        // Centred icons: 275 px on the right, 1304 px on the left. "Right" keeps the right and drops detail.
        var slot = TaskbarPlacement.Choose(TaskbarSide.Right, 0, 1320, 2540, 2831, Widths, Gap);

        Assert.Equal(new TaskbarSlot(3, 2831 - Gap - 260), slot);
    }

    [Fact]
    public void Auto_PrefersTheFullerLevelOverTheCloserSide()
    {
        // Left holds everything, right only the smallest: the left wins whatever the room on the right.
        var slot = TaskbarPlacement.Choose(TaskbarSide.Auto, 0, 1320, 2540, 2831, Widths, Gap);

        Assert.Equal(0, slot!.Value.Level);
    }

    [Fact]
    public void ShrinksToTheFullestLevelThatFits()
    {
        // 300 px free between the icons and the notification area, nothing at the left.
        var slot = TaskbarPlacement.Choose(TaskbarSide.Auto, 0, 0, 2540, 2831, Widths, Gap);

        Assert.Equal(new TaskbarSlot(3, 2831 - Gap - 260), slot);
    }

    [Fact]
    public void ReturnsNullWhenNothingFits() =>
        Assert.Null(TaskbarPlacement.Choose(TaskbarSide.Auto, 0, 0, 2800, 2831, Widths, Gap));

    [Fact]
    public void KeepsTheGapOnBothSides()
    {
        // Exactly the width plus two gaps of room: it fits; one pixel less and it does not.
        Assert.NotNull(TaskbarPlacement.Choose(TaskbarSide.Auto, 0, 0, 1000, 1000 + Gap + 260 + Gap, [260], Gap));
        Assert.Null(TaskbarPlacement.Choose(TaskbarSide.Auto, 0, 0, 1000, 1000 + Gap + 260 + Gap - 1, [260], Gap));
    }
}
