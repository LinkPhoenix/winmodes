using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Screen = System.Windows.Forms.Screen;

namespace WinModes.App.Services;

/// <summary>Physical monitor position and logical size, so display scaling does not shrink the restored UI.</summary>
internal sealed record WindowPreferences(string Monitor, int Left, int Top, double Width, double Height, bool Maximized);

internal static partial class WindowPlacement
{
    private const uint NoZOrder = 0x0004;
    private const uint NoActivate = 0x0010;
    private const uint NoSize = 0x0001;

    public static void Restore(Window window, WindowPreferences? saved)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var screen = Screen.AllScreens.FirstOrDefault(item => item.DeviceName == saved?.Monitor)
            ?? Screen.FromHandle(handle);
        var area = screen.WorkingArea;
        var valid = saved is not null && double.IsFinite(saved.Width) && double.IsFinite(saved.Height)
            && saved.Width > 0 && saved.Height > 0;

        // Move first: Windows supplies the target monitor's DPI before the logical size is restored.
        if (valid)
        {
            SetWindowPos(handle, IntPtr.Zero, Math.Clamp(saved!.Left, area.Left, area.Right - 1),
                Math.Clamp(saved.Top, area.Top, area.Bottom - 1), 0, 0, NoSize | NoZOrder | NoActivate);
        }

        var scale = Math.Max(96, GetDpiForWindow(handle)) / 96d;
        window.MinWidth = Math.Min(980, area.Width / scale);
        window.MinHeight = Math.Min(560, area.Height / scale);
        var width = Math.Clamp(valid ? saved!.Width : window.Width, window.MinWidth, area.Width / scale);
        var height = Math.Clamp(valid ? saved!.Height : window.Height, window.MinHeight, area.Height / scale);
        var pixelsWide = Math.Min(area.Width, (int)Math.Round(width * scale));
        var pixelsHigh = Math.Min(area.Height, (int)Math.Round(height * scale));
        var left = valid ? saved!.Left : area.Left + (area.Width - pixelsWide) / 2;
        var top = valid ? saved!.Top : area.Top + (area.Height - pixelsHigh) / 2;
        SetWindowPos(handle, IntPtr.Zero, Math.Clamp(left, area.Left, area.Right - pixelsWide),
            Math.Clamp(top, area.Top, area.Bottom - pixelsHigh), pixelsWide, pixelsHigh, NoZOrder | NoActivate);

        if (valid && saved!.Maximized)
        {
            window.WindowState = WindowState.Maximized;
        }
    }

    public static WindowPreferences? Capture(Window window, WindowPreferences? previous)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            return previous;
        }

        if (window.WindowState == WindowState.Maximized)
        {
            return previous is null ? null : previous with { Maximized = true };
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (!GetWindowRect(handle, out var bounds))
        {
            return previous;
        }

        var scale = Math.Max(96, GetDpiForWindow(handle)) / 96d;
        return new(Screen.FromHandle(handle).DeviceName, bounds.Left, bounds.Top,
            (bounds.Right - bounds.Left) / scale, (bounds.Bottom - bounds.Top) / scale, false);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr window, out NativeRect bounds);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(IntPtr window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
