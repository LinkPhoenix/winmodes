using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace WinModes.App.Controls;

/// <summary>
/// A titled group of <see cref="SettingRow"/>s in one card. The rows go in a panel written inside the section;
/// the last one loses its divider. Its look is the template in SettingControls.xaml.
/// </summary>
[ContentProperty(nameof(Rows))]
public sealed class SettingSection : System.Windows.Controls.Control
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(SymbolRegular), typeof(SettingSection), new PropertyMetadata(SymbolRegular.Empty));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(SettingSection), new PropertyMetadata(Palette.Neutral));

    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(SettingSection), new PropertyMetadata(""));

    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows), typeof(Panel), typeof(SettingSection), new PropertyMetadata(null));

    static SettingSection() => DefaultStyleKeyProperty.OverrideMetadata(typeof(SettingSection), new FrameworkPropertyMetadata(typeof(SettingSection)));

    public SettingSection() => Loaded += (_, _) => HideLastDivider();

    public SymbolRegular Icon
    {
        get => (SymbolRegular)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public Brush Accent
    {
        get => (Brush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public Panel? Rows
    {
        get => (Panel?)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    private void HideLastDivider()
    {
        if (Rows?.Children.OfType<SettingRow>().LastOrDefault() is { } last)
        {
            last.ShowDivider = false;
        }
    }
}
