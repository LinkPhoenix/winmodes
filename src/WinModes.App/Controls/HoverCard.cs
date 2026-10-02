using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace WinModes.App.Controls;

/// <summary>
/// The card shown when the mouse rests on a part of a widget, in place of a plain tooltip. It appears after a short pause, next
/// to the part pointed at, shows what <see cref="HoverContent"/> builds for it, and never takes the mouse or the focus.
/// </summary>
internal static class HoverCard
{
    private static readonly TimeSpan ShowDelay = TimeSpan.FromMilliseconds(800);

    // Moving from one tool to the next while a card was just open shows the next one at once, as Windows does for its own flyouts.
    private static readonly TimeSpan QuickDelay = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan QuickWindow = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(1);

    private static readonly DispatcherTimer Pause = new();
    private static readonly DispatcherTimer Refresh = new() { Interval = RefreshEvery };
    private static CardWindow? _card;
    private static FrameworkElement? _target;
    private static Func<UIElement?>? _content;
    private static DateTime _closedAt = DateTime.MinValue;

    static HoverCard()
    {
        Pause.Tick += (_, _) =>
        {
            Pause.Stop();
            Open();
        };
        Refresh.Tick += (_, _) => Update();
    }

    /// <summary>
    /// Shows the card when the mouse rests on <paramref name="target"/>. <paramref name="content"/> is asked again every second
    /// while the card is open, so its figures stay current; it returns null when there is nothing to show.
    /// </summary>
    public static void Attach(FrameworkElement target, Func<UIElement?> content)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(content);

        target.MouseEnter += (_, _) => Arm(target, content);
        target.MouseLeave += (_, _) => Close(target);
        // A click or a right-click is an action on the widget: the card gets out of its way.
        target.PreviewMouseDown += (_, _) => Close(target);
        // The taskbar widget measures every level of detail on each refresh and so hides and shows its cells within one call:
        // only a cell that is still hidden once that call is over takes its card away.
        target.IsVisibleChanged += (_, _) => target.Dispatcher.BeginInvoke(() =>
        {
            if (!target.IsVisible)
            {
                Close(target);
            }
        }, DispatcherPriority.Background);
        target.Unloaded += (_, _) => Close(target);
    }

    private static void Arm(FrameworkElement target, Func<UIElement?> content)
    {
        (_target, _content) = (target, content);
        Pause.Stop();
        Pause.Interval = DateTime.UtcNow - _closedAt < QuickWindow ? QuickDelay : ShowDelay;
        Pause.Start();
    }

    private static void Open()
    {
        if (_target is not { IsVisible: true, IsMouseOver: true } || _content?.Invoke() is not { } content)
        {
            return;
        }

        _card ??= new CardWindow();
        _card.Present(_target, content);
        Refresh.Start();
    }

    /// <summary>Keeps the figures of an open card current, and closes it when the mouse is no longer on its target.</summary>
    private static void Update()
    {
        if (_target is not { IsVisible: true, IsMouseOver: true } || _card is null || _content?.Invoke() is not { } content)
        {
            Close(_target);
            return;
        }

        _card.Present(_target, content);
    }

    private static void Close(FrameworkElement? target)
    {
        if (target is null || !ReferenceEquals(target, _target))
        {
            return;
        }

        Pause.Stop();
        Refresh.Stop();
        if (_card is { IsVisible: true })
        {
            _card.Hide();
            _closedAt = DateTime.UtcNow;
        }
    }

    /// <summary>The window of the card: frameless, above everything, and invisible to the mouse so it can never cause a leave.</summary>
    private sealed class CardWindow : Window
    {
        private const double CardWidth = 292;
        private const double ShadowMargin = 12;
        private const double VisibleGap = 6;
        private const double ScreenMargin = 8;
        private const long StyleTransparent = 0x00000020;
        private const long StyleToolWindow = 0x00000080;
        private const long StyleNoActivate = 0x08000000;
        private const int ExtendedStyleIndex = -20;
        private const uint NoSize = 0x0001;
        private const uint NoActivate = 0x0010;
        private static readonly IntPtr TopmostHandle = new(-1);

        public CardWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;
            IsHitTestVisible = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            UseLayoutRounding = true;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            SourceInitialized += (_, _) =>
            {
                var handle = new WindowInteropHelper(this).Handle;
                SetWindowLongPtr(handle, ExtendedStyleIndex, new IntPtr(GetWindowLongPtr(handle, ExtendedStyleIndex).ToInt64() | StyleTransparent | StyleToolWindow | StyleNoActivate));
            };
        }

        public void Present(FrameworkElement target, UIElement body)
        {
            var card = new Border { Width = CardWidth, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(16, 14, 16, 14), Child = body };
            card.SetResourceReference(Border.BackgroundProperty, "AppCardBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "AppCardStrokeBrush");
            card.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorPrimaryBrush");
            card.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Direction = 270, Opacity = 0.35, Color = Colors.Black };
            var content = new Border { Margin = new Thickness(ShadowMargin), Child = card };

            var wasVisible = IsVisible;
            Content = content;
            // Hidden while it is moved, so it never shows at a wrong place first.
            if (!wasVisible)
            {
                Opacity = 0;
                Show();
            }

            content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Place(target, content.DesiredSize);
            Opacity = 1;
        }

        /// <summary>Puts the card above the target, or under it when there is no room above, kept inside the screen.</summary>
        private void Place(FrameworkElement target, Size desired)
        {
            var dpi = VisualTreeHelper.GetDpi(target).DpiScaleX;
            var topLeft = target.PointToScreen(new Point(0, 0));
            var (width, height) = ((int)Math.Ceiling(desired.Width * dpi), (int)Math.Ceiling(desired.Height * dpi));
            var (targetWidth, targetHeight) = (target.ActualWidth * dpi, target.ActualHeight * dpi);
            var area = Forms.Screen.FromPoint(new System.Drawing.Point((int)(topLeft.X + targetWidth / 2), (int)(topLeft.Y + targetHeight / 2))).Bounds;

            // The shadow margin is part of the window, so the visible gap is what is left after it.
            var inset = (int)Math.Round((ShadowMargin - VisibleGap) * dpi);
            var above = topLeft.Y + targetHeight / 2 > area.Top + area.Height / 2.0;
            var y = (int)(above ? topLeft.Y - height + inset : topLeft.Y + targetHeight - inset);
            var x = (int)(topLeft.X + targetWidth / 2 - width / 2.0);
            x = Math.Clamp(x, area.Left + (int)(ScreenMargin * dpi), Math.Max(area.Right - width - (int)(ScreenMargin * dpi), area.Left));
            y = Math.Clamp(y, area.Top, Math.Max(area.Bottom - height, area.Top));

            var handle = new WindowInteropHelper(this).Handle;
            SetWindowPos(handle, TopmostHandle, x, y, 0, 0, NoSize | NoActivate);
        }

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
}
