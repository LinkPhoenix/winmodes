using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinModes.App.Controls;
using WinModes.App.Services;
using WinModes.Core.Planning;
using WinModes.Core.Profiles;

namespace WinModes.App.Pages;

/// <summary>Live vitals, 60-second history, top memory consumers and shortcuts to the modes. Refreshes by itself.</summary>
public partial class DashboardPage : Page
{
    private const int TopProcessCount = 8;
    private const double MbPerGb = 1024;
    // CPU and memory are one kernel call each, so they are sampled every second for the charts.
    private static readonly TimeSpan FastInterval = TimeSpan.FromSeconds(1);
    // Enumerating every process and service is heavier; do it every few fast ticks.
    private const int SlowEveryTicks = 3;
    private const int ModesEveryTicks = 15;
    private static readonly Brush[] BarColors = [Palette.Apps, Palette.Container, Palette.Start, Palette.Power, Palette.Stop];
    private static readonly Brush InactiveDot = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));

    private readonly SystemMonitor _monitor = new();
    private readonly DispatcherTimer _timer = new() { Interval = FastInterval };
    private readonly List<QuickMode> _modes;
    private int _tick;
    private bool _slowRefreshRunning;

    public DashboardPage()
    {
        InitializeComponent();

        _modes = [.. ModeCatalog.Load().Select(mode => new QuickMode(mode))];
        QuickModes.ItemsSource = _modes;
        CpuDetail.Text = $"{Environment.ProcessorCount} logical processors";
        CpuChartDetail.Text = CpuDetail.Text;

        _timer.Tick += async (_, _) => await OnTickAsync();
        Loaded += async (_, _) =>
        {
            _tick = 0;
            _timer.Start();
            await OnTickAsync();
        };
        // Stop sampling when the page is not shown, so a hidden window costs nothing.
        Unloaded += (_, _) => _timer.Stop();
    }

    private async Task OnTickAsync()
    {
        if (!IsVisible)
        {
            return;
        }

        RefreshVitals();

        var tick = _tick++;
        if (tick % SlowEveryTicks == 0 && !_slowRefreshRunning)
        {
            _slowRefreshRunning = true;
            try
            {
                await RefreshProcessesAsync();
                if (tick % ModesEveryTicks == 0)
                {
                    await RefreshModesAsync();
                }
            }
            finally
            {
                _slowRefreshRunning = false;
            }
        }
    }

    private void RefreshVitals()
    {
        try
        {
            var cpu = _monitor.SampleCpuPercent();
            var memory = SystemMonitor.SampleMemory();
            var culture = CultureInfo.CurrentCulture;

            Smooth.To(CpuGauge, RingGauge.ValueProperty, cpu);
            CpuValue.Text = string.Create(culture, $"{cpu:0} %");
            Smooth.To(CpuBar, SegmentBar.ValueProperty, cpu);
            CpuChart.Push(cpu);
            CpuChartValue.Text = string.Create(culture, $"{cpu:0.0} %");

            Smooth.To(MemoryGauge, RingGauge.ValueProperty, memory.UsedPercent);
            MemoryValue.Text = string.Create(culture, $"{memory.UsedPercent:0} %");
            MemoryDetail.Text = string.Create(culture, $"{memory.UsedGb:0.0} / {memory.TotalGb:0.0} GB");
            Smooth.To(MemoryBar, SegmentBar.ValueProperty, memory.UsedPercent);
            MemoryChart.Push(memory.UsedPercent);
            MemoryChartValue.Text = string.Create(culture, $"{memory.UsedPercent:0.0} %");
            MemoryInUse.Text = string.Create(culture, $"{memory.UsedGb:0.0} GB");
            MemoryAvailable.Text = string.Create(culture, $"{memory.AvailableGb:0.0} GB");
            MemoryCommitted.Text = string.Create(culture, $"{memory.CommittedGb:0.0} / {memory.CommitLimitGb:0.0} GB");
            MemoryCached.Text = string.Create(culture, $"{memory.CachedGb:0.0} GB");
        }
        catch (Win32Exception)
        {
            MemoryDetail.Text = "System figures are unavailable.";
        }
    }

    private async Task RefreshProcessesAsync()
    {
        try
        {
            var (snapshot, rows) = await Task.Run(() =>
            {
                var groups = SystemMonitor.GetProcessGroups().Take(TopProcessCount).ToList();
                var largest = groups.Count > 0 ? Math.Max(groups[0].PrivateMemoryMb, 1) : 1;
                var culture = CultureInfo.CurrentCulture;
                var list = groups.Select((group, index) => new ProcessRow(
                    group.Name,
                    group.Count > 1 ? $"×{group.Count}" : "",
                    group.PrivateMemoryMb / largest * 100,
                    FormatMemory(group.PrivateMemoryMb, culture),
                    BarColors[index % BarColors.Length],
                    IconCache.Get(group.ExecutablePath))).ToList();
                return (SystemSnapshot.Capture(), list);
            });

            ProcessValue.Text = snapshot.ProcessCount.ToString(CultureInfo.CurrentCulture);
            ServiceValue.Text = snapshot.RunningServiceCount.ToString(CultureInfo.CurrentCulture);
            TopProcesses.ItemsSource = rows;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            ProcessValue.Text = "?";
        }
    }

    private async Task RefreshModesAsync()
    {
        var active = ModeSwitcher.ActiveMode;
        var activeMode = _modes.FirstOrDefault(mode => mode.Profile.Mode.Equals(active, StringComparison.OrdinalIgnoreCase));
        ActiveModeText.Text = activeMode is null ? "Active mode: none" : $"Active mode: {activeMode.Label}";
        ActiveModeDot.Fill = activeMode?.Accent ?? InactiveDot;

        foreach (var mode in _modes)
        {
            try
            {
                var plan = await Task.Run(() => AppServices.Planner.Plan(mode.Profile));
                mode.Summary = ReferenceEquals(mode, activeMode) ? "Active"
                    : plan.Changes.Count == 1 ? "1 change" : $"{plan.Changes.Count} changes";
            }
            catch (ProfileException)
            {
                mode.Summary = "Blocked by protection";
            }
        }
    }

    private void OnModeClick(object sender, RoutedEventArgs e) =>
        (Application.Current.MainWindow as MainWindow)?.NavigateTo(typeof(ModesPage));

    internal static string FormatMemory(double megabytes, CultureInfo culture) =>
        megabytes >= MbPerGb
            ? string.Create(culture, $"{megabytes / MbPerGb:0.0} GB")
            : string.Create(culture, $"{megabytes:0} MB");

    private sealed class QuickMode(ModeCatalog.Entry entry) : INotifyPropertyChanged
    {
        private string _summary = "Checking…";

        public event PropertyChangedEventHandler? PropertyChanged;

        public ModeProfile Profile => entry.Profile;
        public string Label => entry.Profile.Label;
        public string Glyph => entry.Glyph;
        public Brush Accent => entry.Accent;

        public string Summary
        {
            get => _summary;
            set
            {
                _summary = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
            }
        }
    }

    private sealed record ProcessRow(string Name, string CountText, double Share, string MemoryText, Brush Color, ImageSource? Icon)
    {
        public Visibility GlyphVisibility => Icon is null ? Visibility.Visible : Visibility.Collapsed;
    }
}
