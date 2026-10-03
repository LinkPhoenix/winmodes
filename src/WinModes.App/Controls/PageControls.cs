using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Wpf.Ui.Controls;
using WinModes.Core;

namespace WinModes.App.Controls;

/// <summary>
/// The top of a page: a coloured icon tile, the title, a line of description (the element written inside the header, so
/// a page can name it and fill it from its code) and, on the right, the buttons or fields of the page.
/// </summary>
[ContentProperty(nameof(Content))]
public sealed class PageHeader : ContentControl
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(SymbolRegular), typeof(PageHeader), new PropertyMetadata(SymbolRegular.Empty));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(PageHeader), new PropertyMetadata(Palette.BrandBrush, (d, _) => ((PageHeader)d).Refresh()));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(PageHeader), new PropertyMetadata(""));

    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
        nameof(Actions), typeof(object), typeof(PageHeader), new PropertyMetadata(null, (d, _) => ((PageHeader)d).Refresh()));

    private static readonly DependencyPropertyKey TileBrushKey = DependencyProperty.RegisterReadOnly(
        nameof(TileBrush), typeof(Brush), typeof(PageHeader), new PropertyMetadata(Brushes.Transparent));

    private static readonly DependencyPropertyKey ActionsVisibilityKey = DependencyProperty.RegisterReadOnly(
        nameof(ActionsVisibility), typeof(Visibility), typeof(PageHeader), new PropertyMetadata(Visibility.Collapsed));

    public static readonly DependencyProperty TileBrushProperty = TileBrushKey.DependencyProperty;
    public static readonly DependencyProperty ActionsVisibilityProperty = ActionsVisibilityKey.DependencyProperty;

    static PageHeader() => DefaultStyleKeyProperty.OverrideMetadata(typeof(PageHeader), new FrameworkPropertyMetadata(typeof(PageHeader)));

    public PageHeader()
    {
        Focusable = false;
        IsTabStop = false;
        Refresh();
    }

    public SymbolRegular Icon
    {
        get => (SymbolRegular)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>Colour of the icon; its tint fills the tile behind it.</summary>
    public Brush Accent
    {
        get => (Brush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>What sits on the right of the title: a search box, buttons, a status chip.</summary>
    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    public Brush TileBrush => (Brush)GetValue(TileBrushKey.DependencyProperty);

    public Visibility ActionsVisibility => (Visibility)GetValue(ActionsVisibilityKey.DependencyProperty);

    private void Refresh()
    {
        SetValue(TileBrushKey, Accent is SolidColorBrush ? Palette.Tint(Accent) : Brushes.Transparent);
        SetValue(ActionsVisibilityKey, Actions is null ? Visibility.Collapsed : Visibility.Visible);
    }
}

/// <summary>What a list shows when nothing is left to show: a large glyph, a title and a hint of what to try.</summary>
public sealed class EmptyState : Control
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(EmptyState), new PropertyMetadata(""));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(EmptyState), new PropertyMetadata(""));

    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(
        nameof(Hint), typeof(string), typeof(EmptyState), new PropertyMetadata(""));

    static EmptyState() => DefaultStyleKeyProperty.OverrideMetadata(typeof(EmptyState), new FrameworkPropertyMetadata(typeof(EmptyState)));

    public EmptyState()
    {
        Focusable = false;
        IsTabStop = false;
    }

    /// <summary>A character of the Segoe Fluent Icons font; the magnifier by default.</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Hint
    {
        get => (string)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }
}

/// <summary>
/// The search box of the list pages: the same look as everywhere, a clear button, Escape to empty it. Ctrl+F, from anywhere
/// in the window, brings the focus to the one that is on screen (see <see cref="FocusIn"/>).
/// </summary>
public sealed class SearchField : Wpf.Ui.Controls.TextBox
{
    static SearchField() => DefaultStyleKeyProperty.OverrideMetadata(typeof(SearchField), new FrameworkPropertyMetadata(typeof(Wpf.Ui.Controls.TextBox)));

    public SearchField()
    {
        Icon = new SymbolIcon(SymbolRegular.Search24);
        ClearButtonEnabled = true;
        SetResourceReference(Control.BackgroundProperty, "AppCardBrush");
        SetResourceReference(Control.BorderBrushProperty, "AppCardStrokeBrush");
        SetResourceReference(Control.ForegroundProperty, "TextFillColorPrimaryBrush");
        BorderThickness = new Thickness(1);
        MinHeight = 36;
    }

    /// <summary>Focuses the first search box that is on screen under <paramref name="root"/>; false when there is none.</summary>
    public static bool FocusIn(DependencyObject root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var field = root.Descendants().OfType<SearchField>().FirstOrDefault(candidate => candidate.IsVisible && candidate.IsEnabled);
        if (field is null)
        {
            return false;
        }

        field.Focus();
        field.SelectAll();
        return true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.Key == Key.Escape && Text.Length > 0)
        {
            Clear();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}

/// <summary>
/// Lays its children out in as many columns as the width allows (each at least <see cref="MinItemWidth"/>, up to
/// <see cref="MaxColumns"/>), the cards sharing the width, so a page reflows instead of squeezing when the window narrows.
/// </summary>
public sealed class AdaptiveCardGrid : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(AdaptiveCardGrid),
        new FrameworkPropertyMetadata(300.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns), typeof(int), typeof(AdaptiveCardGrid),
        new FrameworkPropertyMetadata(int.MaxValue, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(AdaptiveCardGrid),
        new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MinItemHeightProperty = DependencyProperty.Register(
        nameof(MinItemHeight), typeof(double), typeof(AdaptiveCardGrid),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    /// <summary>Gap between two cards, sideways and downwards.</summary>
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public double MinItemHeight
    {
        get => (double)GetValue(MinItemHeightProperty);
        set => SetValue(MinItemHeightProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? MinItemWidth : availableSize.Width;
        var visible = VisibleChildren();
        if (visible.Count == 0)
        {
            return new Size(double.IsInfinity(availableSize.Width) ? 0 : width, 0);
        }

        var columns = Math.Min(CardGridMath.ColumnCount(width, MinItemWidth, Spacing, MaxColumns), visible.Count);
        var itemWidth = CardGridMath.ItemWidth(width, columns, Spacing);
        var height = 0.0;
        for (var start = 0; start < visible.Count; start += columns)
        {
            var rowHeight = RowHeight(visible, start, columns, itemWidth);
            height += rowHeight + (start > 0 ? Spacing : 0);
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var visible = VisibleChildren();
        var columns = Math.Min(CardGridMath.ColumnCount(finalSize.Width, MinItemWidth, Spacing, MaxColumns), visible.Count);
        var itemWidth = CardGridMath.ItemWidth(finalSize.Width, columns, Spacing);
        var y = 0.0;
        for (var start = 0; start < visible.Count; start += columns)
        {
            var rowHeight = RowHeight(visible, start, columns, itemWidth);
            for (var column = 0; column < columns && start + column < visible.Count; column++)
            {
                visible[start + column].Arrange(new Rect(column * (itemWidth + Spacing), y, itemWidth, rowHeight));
            }

            y += rowHeight + Spacing;
        }

        return finalSize;
    }

    private double RowHeight(List<UIElement> visible, int start, int columns, double itemWidth)
    {
        var rowHeight = MinItemHeight;
        for (var column = 0; column < columns && start + column < visible.Count; column++)
        {
            var child = visible[start + column];
            child.Measure(new Size(itemWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
        }

        return rowHeight;
    }

    private List<UIElement> VisibleChildren() =>
        [.. InternalChildren.Cast<UIElement>().Where(child => child.Visibility != Visibility.Collapsed)];
}
