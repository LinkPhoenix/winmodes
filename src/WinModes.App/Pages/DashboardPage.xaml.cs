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
    private bool _gpuDiskRunning;
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
        _ = RefreshGpuDiskAsync();

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

            CoreBars.Show(_monitor.SampleCoresPercent());
            var (down, up) = _monitor.SampleNetwork();
            NetworkValue.Text = string.Create(culture, $"\u2193 {down:0.0}  \u2191 {up:0.0} Mb/s");

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
        catch (Exception ex) when (ex is Win32Exception or System.Net.NetworkInformation.NetworkInformationException)
        {
            MemoryDetail.Text = "System figures are unavailable.";
        }
    }

    private async Task RefreshGpuDiskAsync()
    {
        // Performance counters can take tens of milliseconds: read them off the UI thread, one read at a time.
        if (_gpuDiskRunning)
        {
            return;
        }

        _gpuDiskRunning = true;
        try
        {
            var reading = await Task.Run(GpuDiskMonitor.Shared.Sample);
            var culture = CultureInfo.CurrentCulture;

            GpuCard.Visibility = reading.GpuPercent is null ? Visibility.Collapsed : Visibility.Visible;
            if (reading.GpuPercent is { } gpu)
            {
                GpuChart.Push(gpu);
                GpuChartValue.Text = string.Create(culture, $"{gpu:0.0} %");
            }

            DiskCard.Visibility = reading.DiskActivePercent is null ? Visibility.Collapsed : Visibility.Visible;
            if (reading.DiskActivePercent is { } disk)
            {
                DiskChart.Push(disk);
                DiskChartValue.Text = string.Create(culture, $"{disk:0.0} %");
                DiskDetail.Text = string.Create(culture,
                    $"All disks  -  read {reading.DiskReadMbPerSecond:0.0} MB/s, write {reading.DiskWriteMbPerSecond:0.0} MB/s");
            }

            GpuDiskRow.Visibility = reading.GpuPercent is null && reading.DiskActivePercent is null ? Visibility.Collapsed : Visibility.Visible;
        }
        finally
        {
            _gpuDiskRunning = false;
        }
    }

    private async Task RefreshProcessesAsync()
    {
        try
        {
            var (snapshot, rows, aiRows) = await Task.Run(() =>
            {
                var sessions = AiToolCatalog.FindSessions(ProcessActions.Sample());
                var aiList = BuildAiRows(sessions);
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
                return (SystemSnapshot.Capture(), list, aiList);
            });

            ProcessValue.Text = snapshot.ProcessCount.ToString(CultureInfo.CurrentCulture);
            ServiceValue.Text = snapshot.RunningServiceCount.ToString(CultureInfo.CurrentCulture);
            TopProcesses.ItemsSource = rows;
            ShowAiTools(aiRows, snapshot);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            ProcessValue.Text = "?";
        }
    }

    private static List<AiToolRow> BuildAiRows(IReadOnlyList<AiSession> sessions)
    {
        var culture = CultureInfo.CurrentCulture;
        var tools = sessions.GroupBy(session => session.Tool)
            .Select(group => (
                Tool: group.Key,
                Sessions: group.Count(),
                Processes: group.Sum(session => session.Descendants.Count + 1),
                MemoryMb: group.Sum(session => session.TotalMemoryMb),
                Cpu: group.Sum(session => session.TotalCpuPercent),
                Path: group.First().Root.ExecutablePath))
            .OrderByDescending(tool => tool.MemoryMb)
            .ToList();
        var largest = tools.Count > 0 ? Math.Max(tools[0].MemoryMb, 1) : 1;

        return [.. tools.Select((tool, index) =>
        {
            var detail = (tool.Sessions == 1 ? "1 session" : $"{tool.Sessions} sessions") + $", {tool.Processes} processes";
            var memory = FormatMemory(tool.MemoryMb, culture);
            return new AiToolRow(
                tool.Tool.Name,
                detail,
                memory,
                string.Create(culture, $"{tool.Cpu:0.0} % CPU"),
                tool.MemoryMb / largest * 100,
                tool.MemoryMb,
                BarColors[index % BarColors.Length],
                IconCache.Get(tool.Path),
                $"{tool.Tool.Name}: {detail}, {memory}");
        })];
    }

    private void ShowAiTools(List<AiToolRow> rows, SystemSnapshot snapshot)
    {
        var culture = CultureInfo.CurrentCulture;
        AiTools.ItemsSource = rows;
        AiEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var totalMb = rows.Sum(row => row.MemoryMb);
        var share = snapshot.TotalMemoryGb <= 0 ? 0 : totalMb / MbPerGb / snapshot.TotalMemoryGb * 100;
        AiSummary.Text = rows.Count == 0
            ? ""
            : string.Create(culture, $"{FormatMemory(totalMb, culture)} in total, {share:0} % of this PC's memory");
    }

    private void OnAiToolClick(object sender, RoutedEventArgs e) =>
        (Application.Current.MainWindow as MainWindow)?.NavigateTo(typeof(AiToolsPage));

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

    private sealed record AiToolRow(
        string Name, string Detail, string MemoryText, string CpuText, double Share, double MemoryMb, Brush Color, ImageSource? Icon, string AccessibleName)
    {
        public Visibility GlyphVisibility => Icon is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private sealed record ProcessRow(string Name, string CountText, double Share, string MemoryText, Brush Color, ImageSource? Icon)
    {
        public Visibility GlyphVisibility => Icon is null ? Visibility.Visible : Visibility.Collapsed;
    }
}
