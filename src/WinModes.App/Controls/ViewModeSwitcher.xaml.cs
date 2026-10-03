using System.Windows;
using System.Windows.Controls;

namespace WinModes.App.Controls;

public enum PageViewMode
{
    Cards,
    List,
    Compact,
}

public partial class ViewModeSwitcher : UserControl
{
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(PageViewMode), typeof(ViewModeSwitcher), new PropertyMetadata(PageViewMode.List, OnModeChanged));

    public PageViewMode Mode
    {
        get => (PageViewMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public event EventHandler? ModeChanged;

    public ViewModeSwitcher()
    {
        InitializeComponent();
        UpdateSelection();
    }

    private static void OnModeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((ViewModeSwitcher)sender).UpdateSelection();

    private void OnCardsChecked(object sender, RoutedEventArgs e) => ChangeMode(PageViewMode.Cards);

    private void OnListChecked(object sender, RoutedEventArgs e) => ChangeMode(PageViewMode.List);

    private void OnCompactChecked(object sender, RoutedEventArgs e) => ChangeMode(PageViewMode.Compact);

    private void ChangeMode(PageViewMode mode)
    {
        if (Mode == mode)
        {
            return;
        }

        Mode = mode;
        ModeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSelection()
    {
        if (CardsButton is null)
        {
            return;
        }

        CardsButton.IsChecked = Mode == PageViewMode.Cards;
        ListButton.IsChecked = Mode == PageViewMode.List;
        CompactButton.IsChecked = Mode == PageViewMode.Compact;
    }
}
