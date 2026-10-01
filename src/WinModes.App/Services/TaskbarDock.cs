using System.Runtime.InteropServices;
using System.Windows.Automation;
using Microsoft.Win32;

namespace WinModes.App.Services;

/// <summary>Where the taskbar, its app icons and its notification area are, in physical pixels.</summary>
internal readonly record struct TaskbarArea(IntPtr Taskbar, int Left, int Top, int Right, int Bottom, int TrayLeft, int ContentLeft, int ContentRight)
{
    public int Height => Bottom - Top;
}

/// <summary>What can be read in a few microseconds to tell that the taskbar changed: its alignment setting and its rectangle.</summary>
internal readonly record struct TaskbarSignature(int Alignment, int Left, int Top, int Right, int Bottom);

/// <summary>
/// Places a window on the free part of the Windows taskbar. Windows 11 has no toolbar API, so the window stays a normal
/// top-level window: it is owned by the taskbar (an owned window is always kept above its owner, so the Start menu and
/// flyouts never hide it) and moved over the taskbar. The room left by the app icons is read with UI Automation, which
/// sees every button of the taskbar whatever its alignment.
/// </summary>
internal static class TaskbarDock
{
    private const string TaskbarClass = "Shell_TrayWnd";
    private const string TrayClass = "TrayNotifyWnd";
    private const string SystemTrayClassPrefix = "SystemTray.";
    private const int OwnerIndex = -8;
    private const uint NoSize = 0x0001;
    private const uint NoZOrder = 0x0004;
    private const uint NoActivate = 0x0010;
    private static readonly IntPtr Topmost = new(-1);

    /// <summary>
    /// The primary taskbar when it lies along the top or bottom edge; null otherwise (not supported yet) or when its icons
    /// cannot be read. Takes some tens of milliseconds: call it from a worker thread.
    /// </summary>
    public static TaskbarArea? Find()
    {
        var taskbar = FindWindow(TaskbarClass, null);
        if (taskbar == IntPtr.Zero || !GetWindowRect(taskbar, out var bar) || bar.Right - bar.Left <= bar.Bottom - bar.Top)
        {
            return null;
        }

        var tray = FindWindowEx(taskbar, IntPtr.Zero, TrayClass, null);
        var trayLeft = tray != IntPtr.Zero && GetWindowRect(tray, out var trayRect) ? trayRect.Left : bar.Right;
        return ContentExtent(taskbar, trayLeft) is var (contentLeft, contentRight)
            ? new TaskbarArea(taskbar, bar.Left, bar.Top, bar.Right, bar.Bottom, trayLeft, contentLeft, contentRight)
            : null;
    }

    /// <summary>
    /// Cheap check to run often: Windows 11 writes the alignment (left or centred) as soon as the user changes it, and the
    /// rectangle changes when the taskbar moves, resizes or Explorer restarts. A change means the icons are about to move.
    /// </summary>
    public static TaskbarSignature? ReadSignature()
    {
        var taskbar = FindWindow(TaskbarClass, null);
        if (taskbar == IntPtr.Zero || !GetWindowRect(taskbar, out var bar))
        {
            return null;
        }

        var alignment = Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAl", 1) is int value ? value : 1;
        return new TaskbarSignature(alignment, bar.Left, bar.Top, bar.Right, bar.Bottom);
    }

    /// <summary>Calls <paramref name="changed"/> (from another thread) when icons are added to or removed from the taskbar.</summary>
    public static void Watch(IntPtr taskbar, Action changed)
    {
        try
        {
            Automation.AddStructureChangedEventHandler(AutomationElement.FromHandle(taskbar), TreeScope.Subtree, (_, _) => changed());
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            // Not watched: the regular check still notices, a little later.
        }
    }

    public static void StopWatching() => Automation.RemoveAllEventHandlers();

    /// <summary>Left and right edge of the start button, the pinned and running apps, and the Windows 11 widgets button.</summary>
    private static (int Left, int Right)? ContentExtent(IntPtr taskbar, int trayLeft)
    {
        try
        {
            var buttons = AutomationElement.FromHandle(taskbar).FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
            var (left, right) = (int.MaxValue, int.MinValue);
            foreach (AutomationElement button in buttons)
            {
                var info = button.Current;
                var bounds = info.BoundingRectangle;
                if (info.IsOffscreen || bounds.IsEmpty || bounds.Left >= trayLeft || info.ClassName.StartsWith(SystemTrayClassPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                left = Math.Min(left, (int)Math.Floor(bounds.Left));
                right = Math.Max(right, (int)Math.Ceiling(bounds.Right));
            }

            return left <= right ? (left, right) : null;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return null;
        }
    }

    /// <summary>Makes <paramref name="window"/> owned by the taskbar and puts it at <paramref name="x"/>, centred on the bar.</summary>
    public static void Place(IntPtr window, TaskbarArea area, int x, int heightPixels)
    {
        var owned = GetWindowLongPtr(window, OwnerIndex) == area.Taskbar;
        if (!owned)
        {
            SetWindowLongPtr(window, OwnerIndex, area.Taskbar);
        }

        var y = area.Top + (area.Height - heightPixels) / 2;
        // Nothing to do while it is already there: moving it again would only make it flicker.
        if (owned && GetWindowRect(window, out var current) && current.Left == x && current.Top == y)
        {
            return;
        }

        // The topmost assert happens once, when the owner is set: repeating it lifts the taskbar above open menus.
        SetWindowPos(window, owned ? IntPtr.Zero : Topmost, x, y, 0, 0, NoSize | NoActivate | (owned ? NoZOrder : 0));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr FindWindow(string className, string? title);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
