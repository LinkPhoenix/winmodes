using System.Globalization;
using System.Windows;
using System.Windows.Input;
using WinModes.App.Controls;
using WinModes.App.Services;

namespace WinModes.App;

/// <summary>
/// Small always-on-top desktop widget: CPU, memory and what the AI tools use.
/// Drag it anywhere; right-click to hide it or open the main window.
/// </summary>
public partial class WidgetWindow : Window
{
    private const double ScreenMargin = 16;
    private const double MbPerGb = 1024;

    public WidgetWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            // Start in the bottom-right corner of the work area, above the taskbar.
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - ScreenMargin;
            Top = area.Bottom - ActualHeight - ScreenMargin;
        };
    }

    public event EventHandler? OpenAppRequested;
    public event EventHandler? HideRequested;

    internal void Show(StatsReading reading)
    {
        var culture = CultureInfo.CurrentCulture;
        Smooth.To(CpuBar, SegmentBar.ValueProperty, reading.CpuPercent);
        CpuText.Text = string.Create(culture, $"{reading.CpuPercent:0} %");
        Smooth.To(MemoryBar, SegmentBar.ValueProperty, reading.Memory.UsedPercent);
        MemoryText.Text = string.Create(culture, $"{reading.Memory.UsedPercent:0} %");

        AiTotal.Text = string.Create(culture, $"{reading.AiMemoryMb / MbPerGb:0.0} GB");
        AiTools.ItemsSource = reading.AiTools.Count == 0
            ? [new KeyValuePair<string, string>("None running", "")]
            : reading.AiTools.Select(tool => new KeyValuePair<string, string>(
                tool.Sessions > 1 ? $"{tool.Name} ×{tool.Sessions}" : tool.Name,
                string.Create(culture, $"{tool.MemoryMb / MbPerGb:0.0} GB"))).ToList();

        var mode = ModeSwitcher.ActiveMode;
        ModeText.Text = mode is null ? "No mode" : $"{culture.TextInfo.ToTitleCase(mode)} mode";
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnOpenApp(object sender, RoutedEventArgs e) => OpenAppRequested?.Invoke(this, EventArgs.Empty);

    private void OnHide(object sender, RoutedEventArgs e) => HideRequested?.Invoke(this, EventArgs.Empty);
}
