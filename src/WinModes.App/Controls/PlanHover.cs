using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using WinModes.App.Services;
using WinModes.Core.Usage;
using Forms = System.Windows.Forms;
using Glyph = Wpf.Ui.Controls.SymbolRegular;
using GlyphIcon = Wpf.Ui.Controls.SymbolIcon;

namespace WinModes.App.Controls;

/// <summary>
/// The card shown when the mouse rests on the Claude or Codex part of a widget, in place of a plain tooltip: the tool and its
/// plan, each limit with a bar and its reset, and the limit resets in reserve. It appears after a short pause, next to the part
/// pointed at, and never takes the mouse or the focus.
/// </summary>
internal static class PlanHover
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
    private static string _tool = "";
    private static Func<SubscriptionStatus?>? _status;
    private static DateTime _closedAt = DateTime.MinValue;

    static PlanHover()
    {
        Pause.Tick += (_, _) =>
        {
            Pause.Stop();
            Open();
        };
        Refresh.Tick += (_, _) => Update();
    }

    /// <summary>The last known status of a tool, without the limit resets in reserve when the widget is set not to show them.</summary>
    public static SubscriptionStatus? Current(string tool, WidgetSettings settings)
    {
        var status = SubscriptionMonitor.Current.FirstOrDefault(known => known.Tool == tool);
        return status is not null && !settings.ShowResetCredits ? status with { ResetCredits = null } : status;
    }

    /// <summary>Shows the card of <paramref name="tool"/> when the mouse rests on <paramref name="target"/>.</summary>
    public static void Attach(FrameworkElement target, string tool, Func<SubscriptionStatus?> status)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(status);

        target.MouseEnter += (_, _) => Arm(target, tool, status);
        target.MouseLeave += (_, _) => Close(target);
        // A click or a right-click is an action on the widget: the card gets out of its way.
        target.PreviewMouseDown += (_, _) => Close(target);
        target.IsVisibleChanged += (_, _) => Close(target);
        target.Unloaded += (_, _) => Close(target);
    }

    private static void Arm(FrameworkElement target, string tool, Func<SubscriptionStatus?> status)
    {
        (_target, _tool, _status) = (target, tool, status);
        Pause.Stop();
        Pause.Interval = DateTime.UtcNow - _closedAt < QuickWindow ? QuickDelay : ShowDelay;
        Pause.Start();
    }

    private static void Open()
    {
        if (_target is not { IsVisible: true, IsMouseOver: true } || _status is null)
        {
            return;
        }

        _card ??= new CardWindow();
        _card.Present(_target, _tool, _status());
        Refresh.Start();
    }

    /// <summary>Keeps the figures of an open card current, and closes it when the mouse is no longer on its target.</summary>
    private static void Update()
    {
        if (_target is not { IsVisible: true, IsMouseOver: true } || _card is null || _status is null)
        {
            Close(_target);
            return;
        }

        _card.Present(_target, _tool, _status());
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

        public void Present(FrameworkElement target, string tool, SubscriptionStatus? status)
        {
            var now = DateTimeOffset.Now;
            var culture = CultureInfo.CurrentCulture;
            var card = new Border { Width = CardWidth, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(16, 14, 16, 14), Child = Build(tool, status, now, culture) };
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

        private static StackPanel Build(string tool, SubscriptionStatus? status, DateTimeOffset now, CultureInfo culture)
        {
            var panel = new StackPanel();
            panel.Children.Add(Header(tool, status));

            if (status is null || status.Primary is null)
            {
                panel.Children.Add(Note(status is null ? Loc.T("No Claude or Codex plan found on this PC") : Loc.T("Usage is not stored on this PC"), 14));
            }

            foreach (var limit in new[] { status?.Primary, status?.Secondary })
            {
                if (limit is not null)
                {
                    panel.Children.Add(Limit(limit, now, culture));
                }
            }

            if (status?.ResetCredits is { } credits)
            {
                panel.Children.Add(Credits(credits));
            }

            if (status?.SeenAt is { } seen)
            {
                panel.Children.Add(Note(Loc.F("As of {0}", Subscriptions.LocalTime(seen, now, culture)), 12, small: true));
            }

            return panel;
        }

        private static Grid Header(string tool, SubscriptionStatus? status)
        {
            var accent = tool == "Claude" ? Palette.Power : Palette.Container;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            var logo = ToolIcons.For(tool);
            var tile = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(9),
                Background = Palette.Tint(accent),
                Child = logo is not null
                    ? new Image { Source = logo, Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                    : new GlyphIcon { Symbol = tool == "Claude" ? Glyph.Sparkle24 : Glyph.Code24, Foreground = accent, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            grid.Children.Add(tile);

            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            texts.Children.Add(new TextBlock { Text = tool, FontSize = 15, FontWeight = FontWeights.SemiBold });
            texts.Children.Add(new TextBlock { Text = status?.Plan ?? Loc.T("Plan unknown"), FontSize = 12, Foreground = Palette.Neutral });
            Grid.SetColumn(texts, 1);
            grid.Children.Add(texts);
            return grid;
        }

        /// <summary>One limit: its name and what is left, a bar in the colour of what is left, and when it starts over.</summary>
        private static StackPanel Limit(LimitWindow limit, DateTimeOffset now, CultureInfo culture)
        {
            const double BarHeight = 6;

            var ended = limit.HasReset(now);
            var left = ended ? 0 : limit.RemainingPercent;
            var brush = ended ? Palette.Neutral : Palette.RemainingBrush(left);

            var head = new Grid { Margin = new Thickness(0, 16, 0, 6) };
            head.Children.Add(new TextBlock { Text = Subscriptions.Capitalize(Loc.F("{0} limit", limit.WindowName)), FontWeight = FontWeights.SemiBold, FontSize = 13 });
            head.Children.Add(new TextBlock
            {
                Text = ended ? Loc.T("reset") : Loc.In(culture, "{0:0} % left", left),
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = brush,
                HorizontalAlignment = HorizontalAlignment.Right,
            });

            // The bar is two star columns, so it needs no width: what is left, then what is used.
            var track = new Grid { Height = BarHeight };
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(left, 0.001), GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(100 - left, 0.001), GridUnitType.Star) });
            var rail = new Border { CornerRadius = new CornerRadius(BarHeight / 2), Background = new SolidColorBrush(Color.FromArgb(0x33, 0x80, 0x80, 0x80)) };
            Grid.SetColumnSpan(rail, 2);
            track.Children.Add(rail);
            track.Children.Add(new Border { CornerRadius = new CornerRadius(BarHeight / 2), Background = brush, Visibility = left > 0 ? Visibility.Visible : Visibility.Collapsed });

            var panel = new StackPanel();
            panel.Children.Add(head);
            panel.Children.Add(track);
            var when = ended
                ? Subscriptions.Capitalize(Loc.F("{0} limit reset on {1}; no use recorded since", limit.WindowName, limit.ResetsAt is { } at ? Subscriptions.LocalTime(at, now, culture) : ""))
                : limit.ResetsAt is { } reset ? Loc.F("Resets in {0} ({1})", Subscriptions.Span(reset - now), Subscriptions.LocalTime(reset, now, culture)) : "";
            if (when.Length > 0)
            {
                panel.Children.Add(Note(when, 6, small: true));
            }

            return panel;
        }

        private static Border Credits(int credits)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new GlyphIcon { Symbol = Glyph.ArrowCounterclockwise24, Foreground = Palette.BrandBrush, FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock
            {
                Text = credits <= 0 ? Loc.T("No limit reset in reserve") : Loc.N(credits, "1 limit reset in reserve", "{0} limit resets in reserve"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            return new Border { Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(10, 7, 10, 7), CornerRadius = new CornerRadius(8), Background = Palette.Tint(Palette.BrandBrush), Child = row };
        }

        private static TextBlock Note(string text, double top, bool small = false) => new()
        {
            Text = text,
            Margin = new Thickness(0, top, 0, 0),
            FontSize = small ? 11 : 12,
            Foreground = Palette.Neutral,
            TextWrapping = TextWrapping.Wrap,
        };

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
