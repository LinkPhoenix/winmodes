using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace WinModes.App.Controls;

/// <summary>
/// A column of shortcuts to the sections of a long settings page: it stays in view beside the page's own
/// <see cref="ScrollViewer"/>, jumps to a section when clicked and marks the section being read. The sections are the
/// <see cref="SettingSection"/>s of the page and any element marked with <see cref="IsAnchorProperty"/>; one that is hidden
/// (a tool that does not apply) is left out.
/// </summary>
public sealed class SectionNav : Border
{
    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(
        nameof(Target), typeof(ScrollViewer), typeof(SectionNav), new PropertyMetadata(null, (d, e) => ((SectionNav)d).Attach(e.OldValue as ScrollViewer, e.NewValue as ScrollViewer)));

    public static readonly DependencyProperty IsAnchorProperty = DependencyProperty.RegisterAttached(
        "IsAnchor", typeof(bool), typeof(SectionNav), new PropertyMetadata(false));

    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(SymbolRegular), typeof(SectionNav), new PropertyMetadata(SymbolRegular.Empty));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.RegisterAttached(
        "Accent", typeof(Brush), typeof(SectionNav), new PropertyMetadata(null));

    // Animated to scroll the page smoothly; it only forwards its value to the scroll viewer while a scroll is running.
    private static readonly DependencyProperty OffsetProperty = DependencyProperty.Register(
        "Offset", typeof(double), typeof(SectionNav), new PropertyMetadata(0d, (d, e) => ((SectionNav)d).OnOffset((double)e.NewValue)));

    private static readonly Duration ScrollTime = new(TimeSpan.FromMilliseconds(380));
    private static readonly Duration SlideTime = new(TimeSpan.FromMilliseconds(260));
    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    // The space kept above a section the nav scrolls to.
    private const double TopGap = 8;
    private const double MarkHeight = 18;

    private readonly StackPanel _items = new();
    private readonly TranslateTransform _slide = new();
    private readonly Border _mark = new() { Width = 3, Height = MarkHeight, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(1, 0, 0, 0), Visibility = Visibility.Collapsed };
    private bool _scrolling;
    private int _current = -1;

    // The section just clicked stays marked while the page rests where the click put it, even when it cannot reach the top.
    private FrameworkElement? _pinned;
    private double _pinnedOffset;
    private readonly List<Entry> _entries = [];
    private List<FrameworkElement> _anchors = [];
    private string _shown = "";

    public SectionNav()
    {
        Width = 235;
        _mark.RenderTransform = _slide;
        Child = new Grid { Children = { _items, _mark } };
        VerticalAlignment = VerticalAlignment.Top;
    }

    /// <summary>The scroll viewer of the page whose sections are listed.</summary>
    public ScrollViewer? Target
    {
        get => (ScrollViewer?)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    public static bool GetIsAnchor(DependencyObject element) => (bool)element.GetValue(IsAnchorProperty);

    public static void SetIsAnchor(DependencyObject element, bool value) => element.SetValue(IsAnchorProperty, value);

    public static SymbolRegular GetIcon(DependencyObject element) => (SymbolRegular)element.GetValue(IconProperty);

    public static void SetIcon(DependencyObject element, SymbolRegular value) => element.SetValue(IconProperty, value);

    public static Brush? GetAccent(DependencyObject element) => (Brush?)element.GetValue(AccentProperty);

    public static void SetAccent(DependencyObject element, Brush? value) => element.SetValue(AccentProperty, value);

    private void Attach(ScrollViewer? old, ScrollViewer? now)
    {
        if (old is not null)
        {
            old.ScrollChanged -= OnScrolled;
            old.Loaded -= OnTargetLoaded;
            old.PreviewMouseWheel -= OnWheel;
        }

        if (now is not null)
        {
            now.ScrollChanged += OnScrolled;
            now.Loaded += OnTargetLoaded;
            now.PreviewMouseWheel += OnWheel;
            if (now.IsLoaded)
            {
                OnTargetLoaded(now, new RoutedEventArgs());
            }
        }
    }

    private void OnTargetLoaded(object sender, RoutedEventArgs e)
    {
        _anchors = [];
        if (Target?.Content is DependencyObject content)
        {
            Collect(content, _anchors);
        }

        Refresh();
    }

    private static void Collect(DependencyObject parent, List<FrameworkElement> found)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is FrameworkElement element && (element is SettingSection || GetIsAnchor(element)))
            {
                found.Add(element);
                continue;
            }

            Collect(child, found);
        }
    }

    private void OnScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (_pinned is not null && !_scrolling && Math.Abs(Target!.VerticalOffset - _pinnedOffset) > 2)
        {
            _pinned = null;
        }

        Refresh();
    }

    private void Refresh()
    {
        if (Target?.Content is not FrameworkElement content)
        {
            return;
        }

        var visible = _anchors.Where(anchor => anchor.IsVisible).ToList();
        var titles = string.Join('\n', visible.Select(Title));
        if (titles != _shown)
        {
            _shown = titles;
            Rebuild(visible);
        }

        // The section being read is the last one whose top has reached the top of the view; at the very end, the last one.
        var current = -1;
        for (var index = 0; index < visible.Count; index++)
        {
            if (Top(visible[index], content) - Target.VerticalOffset <= TopGap + 1)
            {
                current = index;
            }
        }

        if (Target.ScrollableHeight > 0 && Target.VerticalOffset >= Target.ScrollableHeight - 1)
        {
            current = visible.Count - 1;
        }

        if (_pinned is not null && visible.IndexOf(_pinned) is var pinned and >= 0)
        {
            current = pinned;
        }

        current = visible.Count == 0 ? -1 : Math.Max(current, 0);
        for (var index = 0; index < _entries.Count; index++)
        {
            _entries[index].SetCurrent(index == current);
        }

        if (current != _current || titles != _marked)
        {
            _marked = titles;
            MoveMark(current);
        }
    }

    private string _marked = "";

    /// <summary>Slides the bar beside the entries to the one in view, in the colour of its section.</summary>
    private void MoveMark(int index)
    {
        var first = _current < 0;
        _current = index;
        if (index < 0 || index >= _entries.Count)
        {
            _mark.Visibility = Visibility.Collapsed;
            return;
        }

        _items.UpdateLayout();
        var button = _entries[index].Button;
        var top = button.TranslatePoint(new Point(0, 0), _items).Y + ((button.ActualHeight - MarkHeight) / 2);
        _mark.Background = _entries[index].Accent;
        _mark.Visibility = Visibility.Visible;
        if (first || !SystemParameters.ClientAreaAnimation)
        {
            _slide.BeginAnimation(TranslateTransform.YProperty, null);
            _slide.Y = top;
            return;
        }

        _slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(top, SlideTime) { EasingFunction = Ease });
    }

    private void Rebuild(List<FrameworkElement> visible)
    {
        _items.Children.Clear();
        _entries.Clear();
        foreach (var anchor in visible)
        {
            var entry = new Entry(Title(anchor), IconOf(anchor), AccentOf(anchor));
            var target = anchor;
            entry.Button.Click += (_, _) => Scroll(target);
            _entries.Add(entry);
            _items.Children.Add(entry.Button);
        }
    }

    private void Scroll(FrameworkElement anchor)
    {
        if (Target?.Content is not FrameworkElement content)
        {
            return;
        }

        var to = Math.Clamp(Top(anchor, content) - TopGap, 0, Target.ScrollableHeight);
        (_pinned, _pinnedOffset) = (anchor, to);
        Refresh();
        if (!SystemParameters.ClientAreaAnimation)
        {
            Target.ScrollToVerticalOffset(to);
            return;
        }

        // The page glides to the section, which then fades in so the eye finds where it landed.
        _scrolling = true;
        var glide = new DoubleAnimation(Target.VerticalOffset, to, ScrollTime) { EasingFunction = Ease, FillBehavior = FillBehavior.HoldEnd };
        glide.Completed += (_, _) =>
        {
            // Stopped only after the flag is down: the value then falls back to its base without moving the page.
            _scrolling = false;
            BeginAnimation(OffsetProperty, null);
        };
        BeginAnimation(OffsetProperty, glide);
        anchor.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, new Duration(TimeSpan.FromMilliseconds(550))) { EasingFunction = Ease });
    }

    private void OnOffset(double value)
    {
        if (_scrolling)
        {
            Target?.ScrollToVerticalOffset(value);
        }
    }

    // The wheel takes over from a running glide.
    private void OnWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        _pinned = null;
        if (_scrolling)
        {
            _scrolling = false;
            BeginAnimation(OffsetProperty, null);
        }
    }

    private static double Top(FrameworkElement anchor, FrameworkElement content) => anchor.TransformToAncestor(content).Transform(new Point(0, 0)).Y;

    private static string Title(FrameworkElement anchor) => anchor switch
    {
        SettingSection section => section.Header,
        TextBlock block => block.Text,
        _ => AutomationProperties.GetName(anchor),
    };

    private static SymbolRegular IconOf(FrameworkElement anchor) => GetIcon(anchor) is var icon && icon != SymbolRegular.Empty ? icon : (anchor as SettingSection)?.Icon ?? SymbolRegular.Empty;

    private static Brush AccentOf(FrameworkElement anchor) => GetAccent(anchor) ?? (anchor as SettingSection)?.Accent ?? Palette.Neutral;

    /// <summary>One shortcut: a flat button with the icon and the title of its section, filled while that section is the one in view.</summary>
    private sealed class Entry
    {
        private readonly TextBlock _text;

        public Entry(string title, SymbolRegular icon, Brush accent)
        {
            Accent = accent;
            _text = new TextBlock { Text = title, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            if (icon != SymbolRegular.Empty)
            {
                row.Children.Add(new SymbolIcon { Symbol = icon, FontSize = 16, Foreground = accent, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center });
            }

            row.Children.Add(_text);
            Button = new Wpf.Ui.Controls.Button
            {
                Appearance = ControlAppearance.Transparent,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 0, 0, 2),
                Content = row,
            };
            AutomationProperties.SetName(Button, title);
            Button.ToolTip = title;
            SetCurrent(false);
        }

        public Wpf.Ui.Controls.Button Button { get; }

        public Brush Accent { get; }

        public void SetCurrent(bool current)
        {
            _text.FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal;
            _text.SetResourceReference(TextBlock.ForegroundProperty, current ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
            Button.SetResourceReference(Control.BackgroundProperty, current ? "SubtleFillColorSecondaryBrush" : "SubtleFillColorTransparentBrush");
        }
    }
}
