using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using WinModes.App.Services;

namespace WinModes.App;

/// <summary>Corner of the work area where the widget can be parked.</summary>
internal enum WidgetCorner { TopLeft, TopRight, BottomLeft, BottomRight }

/// <summary>
/// Small desktop widget: CPU, memory, network and what the AI tools use.
/// Its look and content come from <see cref="WidgetSettings"/>, edited on the Widget page.
/// </summary>
public partial class WidgetWindow : Window
{
    private const double ScreenMargin = 16;
    private const int ExtendedStyleIndex = -20;
    private const long StyleTransparent = 0x00000020;
    private const long StyleToolWindow = 0x00000080;

    private WidgetSettings _settings = new();

    public WidgetWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyClickThrough();
        Loaded += (_, _) =>
        {
            var settings = AppSettings.Load();
            var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);

            // Reuse the saved position only if it is still on a connected screen.
            if (settings.WidgetLeft is { } left && settings.WidgetTop is { } top
                && screen.Contains(new Rect(left, top, ActualWidth, ActualHeight)))
            {
                Left = left;
                Top = top;
            }
            else
            {
                MoveTo(WidgetCorner.BottomRight);
            }
        };
    }

    public event EventHandler? OpenAppRequested;
    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? HideRequested;

    internal void Apply(WidgetSettings settings)
    {
        _settings = settings;
        Topmost = settings.AlwaysOnTop;
        Opacity = Math.Clamp(settings.OpacityPercent, 40, 100) / 100d;
        View.Apply(settings);

        ApplyClickThrough();
    }

    internal void Show(StatsReading reading) => View.Show(reading);

    /// <summary>Parks the widget in a corner of the work area and remembers it.</summary>
    internal void MoveTo(WidgetCorner corner)
    {
        var area = SystemParameters.WorkArea;
        Left = corner is WidgetCorner.TopLeft or WidgetCorner.BottomLeft ? area.Left + ScreenMargin : area.Right - ActualWidth - ScreenMargin;
        Top = corner is WidgetCorner.TopLeft or WidgetCorner.TopRight ? area.Top + ScreenMargin : area.Bottom - ActualHeight - ScreenMargin;
        (AppSettings.Load() with { WidgetLeft = Left, WidgetTop = Top }).Save();
    }

    private void ApplyClickThrough()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        // Tool window: the widget never appears in Alt+Tab. Transparent: clicks reach the window underneath.
        var style = GetWindowLongPtr(handle, ExtendedStyleIndex).ToInt64() | StyleToolWindow;
        style = _settings.ClickThrough ? style | StyleTransparent : style & ~StyleTransparent;
        SetWindowLongPtr(handle, ExtendedStyleIndex, new IntPtr(style));
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && !_settings.LockPosition)
        {
            // DragMove returns when the button is released: that is the new resting place.
            DragMove();
            (AppSettings.Load() with { WidgetLeft = Left, WidgetTop = Top }).Save();
        }
    }

    private void OnOpenApp(object sender, RoutedEventArgs e) => OpenAppRequested?.Invoke(this, EventArgs.Empty);

    private void OnOpenSettings(object sender, RoutedEventArgs e) => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnHide(object sender, RoutedEventArgs e) => HideRequested?.Invoke(this, EventArgs.Empty);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
