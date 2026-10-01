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
            ? [new ToolRow("None running", "", null)]
            : reading.AiTools.Take(Math.Max(_settings.MaxTools, 1)).Select(tool => new ToolRow(
                tool.Sessions > 1 ? $"{tool.Name} ×{tool.Sessions}" : tool.Name,
                string.Create(culture, $"{tool.MemoryMb / MbPerGb:0.0} GB"),
                IconCache.Get(tool.ExecutablePath))).ToList();

        ShowMode(ModeSwitcher.ActiveMode);
    }

    /// <summary>Colours the mode label, the border and the compact dot with the colour of the active mode.</summary>
    internal void ShowMode(string? mode)
    {
        if (mode == _shownMode && _modeShown)
        {
            return;
        }

        (_shownMode, _modeShown) = (mode, true);
        ModeText.Text = mode is null ? "No mode" : $"{CultureInfo.CurrentCulture.TextInfo.ToTitleCase(mode)} mode";
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

    private static Visibility Visible(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
