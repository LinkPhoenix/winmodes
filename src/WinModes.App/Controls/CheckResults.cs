using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using WinModes.Core.Usage;

namespace WinModes.App.Controls;

/// <summary>The result of a <see cref="ProviderCheck"/>: one row per check, with an icon for its state and what to do about it.</summary>
public sealed class CheckResults : StackPanel
{
    private const double IconSize = 18;

    public CheckResults() => Visibility = Visibility.Collapsed;

    public void Show(IReadOnlyList<CheckItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        Children.Clear();
        foreach (var item in items)
        {
            var (symbol, brush) = item.State switch
            {
                CheckState.Ok => (SymbolRegular.CheckmarkCircle24, Palette.Start),
                CheckState.Warning => (SymbolRegular.Warning24, Palette.Power),
                _ => (SymbolRegular.DismissCircle24, Palette.Stop),
            };

            var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
            text.Children.Add(new System.Windows.Controls.TextBlock { Text = item.Label, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = item.Detail,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Palette.Neutral,
            });

            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var icon = new SymbolIcon { Symbol = symbol, FontSize = IconSize, Foreground = brush, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
            row.Children.Add(icon);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            Children.Add(row);
        }

        Visibility = Visibility.Visible;
    }
}
