using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace WinModes.App.Controls;

/// <summary>
/// One option of a settings page: a coloured icon tile, a title with an optional description, the control on
/// the right (the element written inside the row) and an optional <see cref="Footer"/> under the text.
/// Its look is the template in SettingControls.xaml.
/// </summary>
[ContentProperty(nameof(Control))]
public sealed class SettingRow : System.Windows.Controls.Control
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(SymbolRegular), typeof(SettingRow), new PropertyMetadata(SymbolRegular.Empty));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(SettingRow), new PropertyMetadata(Palette.Neutral, (d, _) => ((SettingRow)d).Refresh()));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(SettingRow), new PropertyMetadata(""));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingRow), new PropertyMetadata("", (d, _) => ((SettingRow)d).Refresh()));

    public static readonly DependencyProperty ControlProperty = DependencyProperty.Register(
        nameof(Control), typeof(object), typeof(SettingRow), new PropertyMetadata(null));

    public static readonly DependencyProperty FooterProperty = DependencyProperty.Register(
        nameof(Footer), typeof(object), typeof(SettingRow), new PropertyMetadata(null, (d, _) => ((SettingRow)d).Refresh()));

    public static readonly DependencyProperty ShowDividerProperty = DependencyProperty.Register(
        nameof(ShowDivider), typeof(bool), typeof(SettingRow), new PropertyMetadata(true, (d, _) => ((SettingRow)d).Refresh()));

    private static readonly DependencyPropertyKey TileBrushKey = DependencyProperty.RegisterReadOnly(
        nameof(TileBrush), typeof(Brush), typeof(SettingRow), new PropertyMetadata(Brushes.Transparent));

    private static readonly DependencyPropertyKey DescriptionVisibilityKey = DependencyProperty.RegisterReadOnly(
        nameof(DescriptionVisibility), typeof(Visibility), typeof(SettingRow), new PropertyMetadata(Visibility.Collapsed));

    private static readonly DependencyPropertyKey FooterVisibilityKey = DependencyProperty.RegisterReadOnly(
        nameof(FooterVisibility), typeof(Visibility), typeof(SettingRow), new PropertyMetadata(Visibility.Collapsed));

    private static readonly DependencyPropertyKey DividerThicknessKey = DependencyProperty.RegisterReadOnly(
        nameof(DividerThickness), typeof(Thickness), typeof(SettingRow), new PropertyMetadata(new Thickness(0, 0, 0, 1)));

    public static readonly DependencyProperty TileBrushProperty = TileBrushKey.DependencyProperty;
    public static readonly DependencyProperty DescriptionVisibilityProperty = DescriptionVisibilityKey.DependencyProperty;
    public static readonly DependencyProperty FooterVisibilityProperty = FooterVisibilityKey.DependencyProperty;
    public static readonly DependencyProperty DividerThicknessProperty = DividerThicknessKey.DependencyProperty;

    static SettingRow() => DefaultStyleKeyProperty.OverrideMetadata(typeof(SettingRow), new FrameworkPropertyMetadata(typeof(SettingRow)));

    public SettingRow() => Refresh();

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

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>The control on the right of the row (a switch, a list, buttons).</summary>
    public object? Control
    {
        get => GetValue(ControlProperty);
        set => SetValue(ControlProperty, value);
    }

    /// <summary>Content under the text, for example the result of a check.</summary>
    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    public bool ShowDivider
    {
        get => (bool)GetValue(ShowDividerProperty);
        set => SetValue(ShowDividerProperty, value);
    }

    public Brush TileBrush => (Brush)GetValue(TileBrushKey.DependencyProperty);

    public Visibility DescriptionVisibility => (Visibility)GetValue(DescriptionVisibilityKey.DependencyProperty);

    public Visibility FooterVisibility => (Visibility)GetValue(FooterVisibilityKey.DependencyProperty);

    public Thickness DividerThickness => (Thickness)GetValue(DividerThicknessKey.DependencyProperty);

    private void Refresh()
    {
        SetValue(TileBrushKey, Accent is SolidColorBrush ? Palette.Tint(Accent) : Brushes.Transparent);
        SetValue(DescriptionVisibilityKey, string.IsNullOrEmpty(Description) ? Visibility.Collapsed : Visibility.Visible);
        SetValue(FooterVisibilityKey, Footer is null ? Visibility.Collapsed : Visibility.Visible);
        SetValue(DividerThicknessKey, new Thickness(0, 0, 0, ShowDivider ? 1 : 0));
    }
}
