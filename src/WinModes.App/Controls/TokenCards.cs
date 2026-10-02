using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WinModes.App.Services;
using WinModes.Core.Usage.Tokens;

namespace WinModes.App.Controls;

/// <summary>
/// The cards of the token statistics, one per tool: the total, how it splits (new input, output, cache read, cache write), the
/// tokens of each day and the models and projects that used the most. Built in code from the summaries of <see cref="TokenIndex"/>.
/// </summary>
internal static class TokenCards
{
    private const int ListedRows = 5;
    private const double ChartHeight = 56;
    private const double RowBarHeight = 4;

    private static readonly Brush InputBrush = Palette.Start;
    private static readonly Brush OutputBrush = Palette.Power;
    private static readonly Brush CacheReadBrush = Palette.Container;
    private static readonly Brush CacheWriteBrush = Palette.Apps;

    public static UIElement Build(IReadOnlyList<TokenSummary> summaries)
    {
        ArgumentNullException.ThrowIfNull(summaries);

        var panel = new StackPanel();
        foreach (var summary in summaries)
        {
            panel.Children.Add(Card(summary));
        }

        return panel;
    }

    /// <summary>1 234 → "1 k", 12 300 000 → "12.3 M": tokens come in billions, so exact figures say nothing.</summary>
    public static string Format(long tokens, CultureInfo culture) => tokens switch
    {
        >= 1_000_000_000 => Loc.In(culture, "{0:0.0} B", tokens / 1e9),
        >= 1_000_000 => Loc.In(culture, "{0:0.0} M", tokens / 1e6),
        >= 1_000 => Loc.In(culture, "{0:0} k", tokens / 1e3),
        _ => tokens.ToString("0", culture),
    };

    private static Border Card(TokenSummary summary)
    {
        var culture = CultureInfo.CurrentCulture;
        var accent = summary.Tool == "Claude" ? Palette.Power : Palette.Container;
        var body = new StackPanel();
        body.Children.Add(Header(summary, accent, culture));
        body.Children.Add(Split(summary.Total, culture));
        if (summary.Days.Count > 1)
        {
            body.Children.Add(Chart(summary.Days, accent, culture));
        }

        body.Children.Add(List(Loc.T("Models"), summary.Models, Palette.Apps, culture, hideNames: false));
        body.Children.Add(List(Loc.T("Projects"), summary.Projects, Palette.Container, culture, hideNames: true));

        var card = new Border { Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(20, 18, 20, 18), Child = body };
        card.SetResourceReference(FrameworkElement.StyleProperty, "CardBorderStyle");
        return card;
    }

    private static Grid Header(TokenSummary summary, Brush accent, CultureInfo culture)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var logo = ToolIcons.For(summary.Tool);
        grid.Children.Add(new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(9),
            Background = Palette.Tint(accent),
            Child = logo is not null
                ? new Image { Source = logo, Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                : null,
        });

        var name = new TextBlock { Text = summary.Tool, FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);

        var total = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        total.Children.Add(new TextBlock { Text = Format(summary.Total.Total, culture), FontSize = 22, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right });
        total.Children.Add(new TextBlock { Text = Loc.T("tokens processed"), FontSize = 12, Foreground = Palette.Neutral, HorizontalAlignment = HorizontalAlignment.Right });
        Grid.SetColumn(total, 2);
        grid.Children.Add(total);
        return grid;
    }

    /// <summary>The four kinds of token as one bar split by share, with the figure of each underneath.</summary>
    private static StackPanel Split(TokenCounts total, CultureInfo culture)
    {
        var kinds = new (string Label, long Value, Brush Brush)[]
        {
            (Loc.T("New input"), total.Input, InputBrush),
            (Loc.T("Output"), total.Output, OutputBrush),
            (Loc.T("Cache read"), total.CacheRead, CacheReadBrush),
            (Loc.T("Cache write"), total.CacheWrite, CacheWriteBrush),
        };

        var panel = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        var bar = new Grid { Height = 8 };
        foreach (var (_, value, brush) in kinds.Where(kind => kind.Value > 0))
        {
            var column = bar.ColumnDefinitions.Count;
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(value, GridUnitType.Star) });
            var segment = new Border { Background = brush, Margin = new Thickness(column == 0 ? 0 : 1, 0, 0, 0) };
            Grid.SetColumn(segment, column);
            bar.Children.Add(segment);
        }

        panel.Children.Add(new Border { CornerRadius = new CornerRadius(4), ClipToBounds = true, Child = bar });

        var legend = new UniformGrid { Columns = 4, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (label, value, brush) in kinds)
        {
            var cell = new StackPanel();
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = brush, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            title.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Palette.Neutral });
            cell.Children.Add(title);
            cell.Children.Add(new TextBlock { Text = Format(value, culture), FontWeight = FontWeights.SemiBold, Margin = new Thickness(14, 2, 0, 0) });
            legend.Children.Add(cell);
        }

        panel.Children.Add(legend);
        return panel;
    }

    /// <summary>One bar per day, as high as the day's share of the busiest day.</summary>
    private static StackPanel Chart(IReadOnlyList<TokenDay> days, Brush accent, CultureInfo culture)
    {
        var busiest = Math.Max(days.Max(day => day.Counts.Total), 1);
        var bars = new UniformGrid { Rows = 1, Height = ChartHeight };
        foreach (var day in days)
        {
            var share = (double)day.Counts.Total / busiest;
            bars.Children.Add(new Border
            {
                Height = Math.Max(share * ChartHeight, day.Counts.IsEmpty ? 1 : 3),
                Margin = new Thickness(days.Count > 14 ? 1 : 3, 0, days.Count > 14 ? 1 : 3, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
                CornerRadius = new CornerRadius(2),
                Background = day.Counts.IsEmpty ? new SolidColorBrush(Color.FromArgb(0x33, 0x80, 0x80, 0x80)) : accent,
                ToolTip = $"{day.Day.ToString("ddd d MMM", culture)}: {Format(day.Counts.Total, culture)}",
            });
        }

        var ends = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        ends.Children.Add(new TextBlock { Text = days[0].Day.ToString("d MMM", culture), FontSize = 11, Foreground = Palette.Neutral });
        ends.Children.Add(new TextBlock { Text = days[^1].Day.ToString("d MMM", culture), FontSize = 11, Foreground = Palette.Neutral, HorizontalAlignment = HorizontalAlignment.Right });

        var panel = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        panel.Children.Add(bars);
        panel.Children.Add(ends);
        return panel;
    }

    /// <summary>The models or projects that used the most, each with a bar of its share of the tool's total.</summary>
    private static StackPanel List(string title, IReadOnlyList<TokenGroup> groups, Brush accent, CultureInfo culture, bool hideNames)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Palette.Neutral });
        var largest = Math.Max(groups.Count > 0 ? groups[0].Counts.Total : 1, 1);
        foreach (var group in groups.Take(ListedRows))
        {
            var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition());
            row.RowDefinitions.Add(new RowDefinition());

            row.Children.Add(new TextBlock { Text = hideNames ? Privacy.Project(group.Name) : group.Name, TextTrimming = TextTrimming.CharacterEllipsis });
            var value = new TextBlock { Text = Format(group.Counts.Total, culture), FontWeight = FontWeights.SemiBold, Margin = new Thickness(12, 0, 0, 0) };
            Grid.SetColumn(value, 1);
            row.Children.Add(value);

            var share = (double)group.Counts.Total / largest * 100;
            var track = new Grid { Height = RowBarHeight, Margin = new Thickness(0, 4, 0, 0) };
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(share, 0.001), GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(100 - share, 0.001), GridUnitType.Star) });
            var rail = new Border { CornerRadius = new CornerRadius(RowBarHeight / 2), Background = new SolidColorBrush(Color.FromArgb(0x33, 0x80, 0x80, 0x80)) };
            Grid.SetColumnSpan(rail, 2);
            track.Children.Add(rail);
            track.Children.Add(new Border { CornerRadius = new CornerRadius(RowBarHeight / 2), Background = accent });
            Grid.SetRow(track, 1);
            Grid.SetColumnSpan(track, 2);
            row.Children.Add(track);
            panel.Children.Add(row);
        }

        return panel;
    }
}
