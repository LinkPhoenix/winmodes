using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinModes.App.Services;
using WinModes.Core.Usage;
using Glyph = Wpf.Ui.Controls.SymbolRegular;
using GlyphIcon = Wpf.Ui.Controls.SymbolIcon;

namespace WinModes.App.Controls;

/// <summary>What the hover cards of the widgets say: the plan of Claude or Codex, and what the AI tools use.</summary>
internal static class HoverContent
{
    /// <summary>The last known status of a tool, without the limit resets in reserve when the widget is set not to show them.</summary>
    public static SubscriptionStatus? Current(string tool, WidgetSettings settings)
    {
        var status = SubscriptionMonitor.Current.FirstOrDefault(known => known.Tool == tool);
        return status is not null && !settings.ShowResetCredits ? status with { ResetCredits = null } : status;
    }

    /// <summary>The plan of a tool: the tool and its plan, each limit with a bar and its reset, and the limit resets in reserve.</summary>
    public static UIElement Plan(string tool, SubscriptionStatus? status) => PlanBody(tool, status, DateTimeOffset.Now, CultureInfo.CurrentCulture);

    /// <summary>What the AI tools use: the total and its share of the memory, then each tool with a bar of its share of the total.</summary>
    public static UIElement Ai(StatsReading reading)
    {
        const double MbPerGb = 1024;
        const int MaxRows = 8;
        const double BarHeight = 4;

        var culture = CultureInfo.CurrentCulture;
        var totalMb = reading.AiMemoryMb;
        var memoryMb = reading.Memory.TotalGb * MbPerGb;
        var panel = new StackPanel();

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.Children.Add(new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(9),
            Background = Palette.Tint(Palette.Apps),
            Child = new GlyphIcon { Symbol = Glyph.Sparkle24, Foreground = Palette.Apps, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        });
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        texts.Children.Add(new TextBlock { Text = Loc.T("AI tools"), FontSize = 15, FontWeight = FontWeights.SemiBold });
        texts.Children.Add(new TextBlock
        {
            Text = totalMb <= 0 ? Loc.T("None running") : Loc.In(culture, "{0:0.0} GB, {1:0} % of your memory", totalMb / MbPerGb, memoryMb > 0 ? totalMb / memoryMb * 100 : 0),
            FontSize = 12,
            Foreground = Palette.Neutral,
        });
        Grid.SetColumn(texts, 1);
        header.Children.Add(texts);
        panel.Children.Add(header);

        var tools = reading.AiTools.OrderByDescending(tool => tool.MemoryMb).ToList();
        foreach (var tool in tools.Take(MaxRows))
        {
            var row = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition());
            row.RowDefinitions.Add(new RowDefinition());

            var icon = IconCache.Get(tool.ExecutablePath);
            if (icon is not null)
            {
                row.Children.Add(new Image { Source = icon, Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            }

            var name = new TextBlock { Text = tool.Sessions > 1 ? $"{tool.Name} ×{tool.Sessions}" : tool.Name, FontWeight = FontWeights.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(name, 1);
            row.Children.Add(name);
            var memory = new TextBlock { Text = string.Create(culture, $"{tool.MemoryMb / MbPerGb:0.0} GB"), FontWeight = FontWeights.SemiBold, FontSize = 13, Foreground = Palette.Apps, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(memory, 2);
            row.Children.Add(memory);

            var share = totalMb > 0 ? tool.MemoryMb / totalMb * 100 : 0;
            var track = new Grid { Height = BarHeight, Margin = new Thickness(0, 6, 0, 0) };
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(share, 0.001), GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(100 - share, 0.001), GridUnitType.Star) });
            var rail = new Border { CornerRadius = new CornerRadius(BarHeight / 2), Background = new SolidColorBrush(Color.FromArgb(0x33, 0x80, 0x80, 0x80)) };
            Grid.SetColumnSpan(rail, 2);
            track.Children.Add(rail);
            track.Children.Add(new Border { CornerRadius = new CornerRadius(BarHeight / 2), Background = Palette.Apps });
            Grid.SetRow(track, 1);
            Grid.SetColumnSpan(track, 3);
            row.Children.Add(track);
            panel.Children.Add(row);
        }

        if (tools.Count > MaxRows)
        {
            panel.Children.Add(Note(Loc.N(tools.Count - MaxRows, "1 more tool", "{0} more tools"), 12, small: true));
        }

        return panel;
    }

    private static StackPanel PlanBody(string tool, SubscriptionStatus? status, DateTimeOffset now, CultureInfo culture)
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
}
