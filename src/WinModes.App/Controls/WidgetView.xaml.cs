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

    public WidgetView() => InitializeComponent();

    /// <param name="scaled">False keeps the natural size whatever the size option says.</param>
    internal void Apply(WidgetSettings settings, bool scaled = true)
    {
        _settings = settings;
        var scale = scaled ? Math.Clamp(settings.ScalePercent, 80, 150) / 100d : 1;
        Root.LayoutTransform = new ScaleTransform(scale, scale);

        ModeText.Visibility = Visible(settings.ShowMode);
        CpuRow.Visibility = Visible(settings.ShowCpu);
        MemoryRow.Visibility = Visible(settings.ShowMemory);
        NetworkRow.Visibility = Visible(settings.ShowNetwork);
        AiSection.Visibility = Visible(settings.ShowAiTools);
        AiTools.Visibility = Visible(settings.ShowToolDetail);
        // No line above the AI block when it is the only thing shown.
        AiSeparator.Visibility = Visible(settings.ShowCpu || settings.ShowMemory || settings.ShowNetwork);
    }

    internal void Show(StatsReading reading)
    {
        var culture = CultureInfo.CurrentCulture;
        Smooth.To(CpuBar, SegmentBar.ValueProperty, reading.CpuPercent);
        CpuText.Text = string.Create(culture, $"{reading.CpuPercent:0} %");
        Smooth.To(MemoryBar, SegmentBar.ValueProperty, reading.Memory.UsedPercent);
        MemoryText.Text = string.Create(culture, $"{reading.Memory.UsedPercent:0} %");
        NetworkText.Text = string.Create(culture, $"↓ {reading.DownMbps:0.0}  ↑ {reading.UpMbps:0.0} Mb/s");

        AiTotal.Text = string.Create(culture, $"{reading.AiMemoryMb / MbPerGb:0.0} GB");
        AiTools.ItemsSource = reading.AiTools.Count == 0
            ? [new KeyValuePair<string, string>("None running", "")]
            : reading.AiTools.Take(Math.Max(_settings.MaxTools, 1)).Select(tool => new KeyValuePair<string, string>(
                tool.Sessions > 1 ? $"{tool.Name} ×{tool.Sessions}" : tool.Name,
                string.Create(culture, $"{tool.MemoryMb / MbPerGb:0.0} GB"))).ToList();

        var mode = ModeSwitcher.ActiveMode;
        ModeText.Text = mode is null ? "No mode" : $"{culture.TextInfo.ToTitleCase(mode)} mode";
    }

    private static Visibility Visible(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
