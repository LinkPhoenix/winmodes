namespace WinModes.Core;

/// <summary>Which free part of the taskbar a widget prefers.</summary>
public enum TaskbarSide
{
    /// <summary>The side with the most room.</summary>
    Auto,
    Left,
    Right,
}

/// <summary>Where a taskbar widget goes: how much detail it can show (0 is the fullest) and its left edge in pixels.</summary>
public readonly record struct TaskbarSlot(int Level, int X);

/// <summary>
/// Chooses where a widget fits on the taskbar. The app icons sit in the middle, or at the left, so there are up to two
/// free parts: left of the icons and between the icons and the notification area. Pure arithmetic, in pixels.
/// </summary>
public static class TaskbarPlacement
{
    /// <param name="barLeft">Left edge of the taskbar.</param>
    /// <param name="contentLeft">Left edge of the first app icon (start button included).</param>
    /// <param name="contentRight">Right edge of the last app icon.</param>
    /// <param name="trayLeft">Left edge of the notification area.</param>
    /// <param name="widths">Width of the widget at each level of detail, from the fullest (index 0) to the smallest.</param>
    /// <param name="gap">Room kept between the widget and what is next to it.</param>
    /// <returns>
    /// With a chosen side: the fullest level that fits on that side, and only when none does, the other side. With automatic:
    /// the fullest level that fits anywhere, on the side with more room when both fit. Null when even the smallest does not
    /// fit. On the left part the widget touches the screen edge, on the right part the notification area.
    /// </returns>
    public static TaskbarSlot? Choose(TaskbarSide side, int barLeft, int contentLeft, int contentRight, int trayLeft, IReadOnlyList<int> widths, int gap)
    {
        ArgumentNullException.ThrowIfNull(widths);

        var leftStart = barLeft + gap;
        var leftRoom = contentLeft - gap - leftStart;
        var rightStart = contentRight + gap;
        var rightRoom = trayLeft - gap - rightStart;

        TaskbarSlot? OnLeft() => Fullest(leftRoom, widths) is { } level ? new TaskbarSlot(level, leftStart) : null;
        TaskbarSlot? OnRight() => Fullest(rightRoom, widths) is { } level ? new TaskbarSlot(level, trayLeft - gap - widths[level]) : null;

        switch (side)
        {
            case TaskbarSide.Left:
                return OnLeft() ?? OnRight();
            case TaskbarSide.Right:
                return OnRight() ?? OnLeft();
            default:
                var (left, right) = (OnLeft(), OnRight());
                if (left is null || right is null)
                {
                    return left ?? right;
                }

                // Both fit: the fuller one, and on a tie the side with more room.
                return left.Value.Level != right.Value.Level ? (left.Value.Level < right.Value.Level ? left : right) : leftRoom >= rightRoom ? left : right;
        }
    }

    /// <summary>The first (fullest) level whose width fits in <paramref name="room"/>.</summary>
    private static int? Fullest(int room, IReadOnlyList<int> widths)
    {
        for (var level = 0; level < widths.Count; level++)
        {
            if (widths[level] <= room)
            {
                return level;
            }
        }

        return null;
    }
}
