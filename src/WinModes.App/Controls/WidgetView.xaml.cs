using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinModes.App.Services;

namespace WinModes.App.Controls;

/// <summary>
/// Content of the desktop widget. The widget window and the preview on the Widget page
/// both host it, so the preview always matches the real thing.
/// </summary>
public partial class WidgetView : UserControl
{
    private const double MbPerGb = 1024;

    private WidgetSettings _settings = new();

    private const double FullWidth = 270;

    public WidgetView() => InitializeComponent();

    /// <summary>True when the point (relative to this control) is over the AI tools block.</summary>
    internal bool IsOverAiTools(Point point) =>
        AiSection.IsVisible && FullPanel.IsVisible
        && new Rect(AiSection.TranslatePoint(new Point(0, 0), this), AiSection.RenderSize).Contains(point);

    /// <param name="scaled">False keeps the natural size whatever the size option says.</param>
    internal void Apply(WidgetSettings settings, bool scaled = true)
    {
        _settings = settings;
        var scale = scaled ? Math.Clamp(settings.ScalePercent, 80, 150) / 100d : 1;
        Root.LayoutTransform = new ScaleTransform(scale, scale);

        CompactPanel.Visibility = Visible(settings.Compact);
        FullPanel.Visibility = Visible(!settings.Compact);
        Root.Width = settings.Compact ? double.NaN : FullWidth;
        GraphRow.Visibility = Visible(settings.ShowGraph && (settings.ShowCpu || settings.ShowMemory));
        CpuChart.Visibility = Visible(settings.ShowCpu);
        MemoryChart.Visibility = Visible(settings.ShowMemory);
        var interval = TimeSpan.FromSeconds(Math.Clamp(settings.RefreshSeconds, 1, 10));
        CpuChart.SampleInterval = interval;
        MemoryChart.SampleInterval = interval;

        ModeChip.Visibility = Visible(settings.ShowMode);
        CpuRow.Visibility = Visible(settings.ShowCpu);
        MemoryRow.Visibility = Visible(settings.ShowMemory);
        NetworkRow.Visibility = Visible(settings.ShowNetwork);
        AiSection.Visibility = Visible(settings.ShowAiTools);
        AiTools.Visibility = Visible(settings.ShowToolDetail);
        // No line above the AI block when it is the only thing shown.
        AiSeparator.Visibility = Visible(settings.ShowCpu || settings.ShowMemory || settings.ShowNetwork);
        PlanSection.Visibility = Visible(settings.ShowSubscriptions);
        CompactPlans.Visibility = Visible(settings.ShowSubscriptions);
        PlanSeparator.Visibility = Visible(settings.ShowCpu || settings.ShowMemory || settings.ShowNetwork || settings.ShowAiTools);
        PlanEmpty.Visibility = Visibility.Collapsed;
        // The compact dot depends on the option that may just have changed.
        _modeShown = false;
        ShowMode(ModeSwitcher.ActiveMode);
    }

    internal void Show(StatsReading reading)
    {
        var culture = CultureInfo.CurrentCulture;
        Smooth.To(CpuBar, SegmentBar.ValueProperty, reading.CpuPercent);
        CpuText.Text = string.Create(culture, $"{reading.CpuPercent:0} %");
        Smooth.To(MemoryBar, SegmentBar.ValueProperty, reading.Memory.UsedPercent);
        MemoryText.Text = string.Create(culture, $"{reading.Memory.UsedPercent:0} %");
        NetworkText.Text = string.Create(culture, $"↓ {reading.DownMbps:0.0}  ↑ {reading.UpMbps:0.0} Mb/s");

        CpuChart.Push(reading.CpuPercent);
        MemoryChart.Push(reading.Memory.UsedPercent);

        AiTotal.Text = string.Create(culture, $"{reading.AiMemoryMb / MbPerGb:0.0} GB");
        CompactCpu.Text = CpuText.Text;
        CompactMemory.Text = MemoryText.Text;
        CompactAi.Text = AiTotal.Text;
        AiTools.ItemsSource = reading.AiTools.Count == 0
            ? [new ToolRow(Loc.T("None running"), "", null)]
            : reading.AiTools.Take(Math.Max(_settings.MaxTools, 1)).Select(tool => new ToolRow(
                tool.Sessions > 1 ? $"{tool.Name} ×{tool.Sessions}" : tool.Name,
                string.Create(culture, $"{tool.MemoryMb / MbPerGb:0.0} GB"),
                IconCache.Get(tool.ExecutablePath))).ToList();

        ShowMode(ModeSwitcher.ActiveMode);
        ShowPlans(reading, culture);
    }

    private void ShowPlans(StatsReading reading, CultureInfo culture)
    {
        if (!_settings.ShowSubscriptions)
        {
            return;
        }

        // The icon comes from the tool's own program, seen while it runs and remembered afterwards.
        // Claude Code comes last so its icon wins over the desktop app's when both run.
        foreach (var tool in reading.AiTools.Where(tool => tool.ExecutablePath is not null).OrderBy(tool => tool.Name == "Claude Code"))
        {
            PlanIconPaths[tool.Name.Split(' ')[0]] = tool.ExecutablePath!;
        }

        var now = DateTimeOffset.Now;
        var statuses = SubscriptionMonitor.Get(_settings.ClaudeOnline, _settings.CodexOnline, _settings.ShowClaudePlan, _settings.ShowCodexPlan);
        var rows = statuses.Select(known =>
        {
            var status = _settings.ShowResetCredits ? known : known with { ResetCredits = null };
            var (value, detail, remaining) = WinModes.Core.Usage.Subscriptions.Describe(status, now, culture);
            var icon = PlanIconPaths.TryGetValue(status.Tool, out var path) ? IconCache.Get(path) : null;
            var shortValue = remaining is { } left ? string.Create(culture, $"{left:0} %") : value.Length > 0 ? value : "–";
            // The second limit gets its own bar unless it has started over since the figure was recorded.
            var second = remaining is not null && status.Secondary is { } secondary && !secondary.HasReset(now) ? secondary : null;
            return new PlanRow(status.Tool, status.Plan, value, detail, remaining ?? 0, Visible(remaining is not null), icon, shortValue, status.ResetCredits is { } credits ? $"↻ {credits}" : "")
            {
                WindowName = Capitalize(status.Primary?.WindowName),
                SecondWindowName = Capitalize(second?.WindowName),
                SecondRemaining = second?.RemainingPercent ?? 0,
                SecondBarVisibility = Visible(second is not null),
            };
        }).ToList();
        if (_settings.Compact)
        {
            CompactPlans.ItemsSource = rows;
        }
        else
        {
            Plans.ItemsSource = rows;
            PlanEmpty.Visibility = Visible(statuses.Count == 0 && SubscriptionMonitor.HasRead);
        }
    }

    /// <summary>First word of the tool name ("Claude", "Codex") to the program its icon is taken from.</summary>
    private static readonly Dictionary<string, string> PlanIconPaths = new(StringComparer.OrdinalIgnoreCase);

    private sealed record PlanRow(
        string Tool, string Plan, string Value, string Detail, double Remaining, Visibility BarVisibility, ImageSource? Icon, string Short, string Resets)
    {
        public string WindowName { get; init; } = "";

        public string SecondWindowName { get; init; } = "";

        public double SecondRemaining { get; init; }

        public Visibility SecondBarVisibility { get; init; } = Visibility.Collapsed;

        public Visibility IconVisibility => Icon is null ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>The name is shown only while the icon is not known yet.</summary>
        public Visibility NameVisibility => Icon is null ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Limit resets in reserve, shown in the compact line; empty when not known or switched off.</summary>
        public Visibility ResetsVisibility => Resets.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        public string ToolTip => $"{Tool} {Plan}\n{Detail}";
    }

    /// <summary>Colours the mode label, the border and the compact dot with the colour of the active mode.</summary>
    internal void ShowMode(string? mode)
    {
        if (mode == _shownMode && _modeShown)
        {
            return;
        }

        (_shownMode, _modeShown) = (mode, true);
        ModeText.Text = mode is null ? Loc.T("No mode") : Loc.F("{0} mode", CultureInfo.CurrentCulture.TextInfo.ToTitleCase(mode));
        if (mode is null)
        {
            ModeChip.Background = Brushes.Transparent;
            ModeText.ClearValue(TextBlock.ForegroundProperty);
            Root.SetResourceReference(Border.BorderBrushProperty, "AppCardStrokeBrush");
            CompactModeDot.Visibility = Visibility.Collapsed;
            return;
        }

        var accent = Palette.ModeGradient(mode);
        ModeChip.Background = accent;
        ModeText.Foreground = Brushes.White;
        Root.BorderBrush = accent;
        CompactModeDot.Fill = accent;
        CompactModeDot.ToolTip = ModeText.Text;
        CompactModeDot.Visibility = Visible(_settings.ShowMode);
    }

    private string? _shownMode;
    private bool _modeShown;

    private sealed record ToolRow(string Name, string Memory, ImageSource? Icon)
    {
        public Visibility IconVisibility => Icon is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string Capitalize(string? text) => string.IsNullOrEmpty(text) ? "" : char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..];

    private static Visibility Visible(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
