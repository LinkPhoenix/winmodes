namespace WinModes.Core;

/// <summary>
/// Tells whether an auto-hide taskbar is slid out of sight. Such a taskbar keeps its window and moves it off the edge of its
/// screen: nothing but a thin line is left on the screen, so a widget placed over it would float over the desktop.
/// </summary>
public static class TaskbarVisibility
{
    /// <summary>A hidden taskbar still leaves a few pixels on the screen to catch the mouse.</summary>
    public const int HiddenTolerance = 5;

    /// <summary>
    /// True when the horizontal taskbar (top and bottom, in pixels) lies off its screen, or shows only the line that reveals it.
    /// A taskbar that is not auto-hide always lies fully inside its screen, so it is never reported hidden.
    /// </summary>
    public static bool IsSlidOut(int barTop, int barBottom, int screenTop, int screenBottom, int tolerance = HiddenTolerance) =>
        barTop >= screenBottom - tolerance || barBottom <= screenTop + tolerance;
}
