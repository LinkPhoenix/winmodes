using System.Windows;

namespace WinModes.App.Services;

/// <summary>
/// Whether a page can be seen. A minimized window keeps its pages "visible" for WPF, so timers that only checked
/// <see cref="UIElement.IsVisible"/> went on reading the system for a window nobody could see.
/// </summary>
internal static class WindowActivity
{
    public static bool IsShown(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.IsVisible && Window.GetWindow(element) is { WindowState: not WindowState.Minimized };
    }
}
