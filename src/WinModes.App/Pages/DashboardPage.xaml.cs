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
    private MonitoringSettings _monitoring = new();
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
        CpuDetail.Text = Loc.F("{0} logical processors", Environment.ProcessorCount);
        CpuChartDetail.Text = CpuDetail.Text;

        _timer.Tick += async (_, _) => await OnTickAsync();
        Loaded += async (_, _) =>
        {
            _tick = 0;
            ApplyMonitoring();
            _timer.Start();
            await OnTickAsync();
        };
        // Stop sampling when the page is not shown, so a hidden window costs nothing.
        Unloaded += (_, _) => _timer.Stop();
    }

    private async Task OnTickAsync()
    {
        if (!WinModes.App.Services.WindowActivity.IsShown(this))
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
                // Returns at once when the processes and the AI tools are both turned off.
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

    /// <summary>
    /// What the user turned off under Monitoring is not read and not shown: a card of a source that is off is hidden, so no counter is
    /// touched for it, and a note says that something is hidden on purpose.
    /// </summary>
    private void ApplyMonitoring()
    {
        var monitoring = _monitoring = WinModes.App.Services.AppSettings.Load().Monitoring;
        AiCard.Visibility = Shown(monitoring.AiTools);
        CpuCard.Visibility = CpuChartCard.Visibility = Shown(monitoring.Cpu);
        MemoryCard.Visibility = MemoryChartCard.Visibility = Shown(monitoring.Memory);
        ProcessRows.Visibility = ServiceRows.Visibility = TopCard.Visibility = Shown(monitoring.Processes);
        NetworkRows.Visibility = Shown(monitoring.Network);
        CountsCard.Visibility = Shown(monitoring.Processes || monitoring.Network);
        VitalsRow.Visibility = Shown(monitoring.Cpu || monitoring.Memory || monitoring.Processes || monitoring.Network);
        HistoryRow.Visibility = Shown(monitoring.Cpu || monitoring.Memory);
        GpuDiskRow.Visibility = Shown(monitoring.GpuDisk);

        // The modes take the whole width when the list of consumers is gone.
        Grid.SetColumn(QuickModesCard, monitoring.Processes ? 2 : 0);
        Grid.SetColumnSpan(QuickModesCard, monitoring.Processes ? 1 : 3);

        var anyOff = !(monitoring.Cpu && monitoring.Memory && monitoring.Network && monitoring.GpuDisk && monitoring.AiTools && monitoring.Processes);
        MonitoringNote.Visibility = Shown(anyOff);
    }

    private static Visibility Shown(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private void OnOpenMonitoring(object sender, RoutedEventArgs e) =>
        (Application.Current.MainWindow as MainWindow)?.NavigateTo(typeof(SettingsPage));

    private void RefreshVitals()
    {
        try
        {
            var culture = CultureInfo.CurrentCulture;

            if (_monitoring.Cpu)
            {
                var cpu = _monitor.SampleCpuPercent();
                CoreBars.Show(_monitor.SampleCoresPercent());
                Smooth.To(CpuGauge, RingGauge.ValueProperty, cpu);
                CpuValue.Text = string.Create(culture, $"{cpu:0} %");
                Smooth.To(CpuBar, SegmentBar.ValueProperty, cpu);
                CpuChart.Push(cpu);
                CpuChartValue.Text = string.Create(culture, $"{cpu:0.0} %");
            }

            if (_monitoring.Network)
            {
                var (down, up) = _monitor.SampleNetwork();
                NetworkValue.Text = string.Create(culture, $"\u2193 {down:0.0}  \u2191 {up:0.0} Mb/s");
            }

            if (_monitoring.Memory)
            {
                var memory = SystemMonitor.SampleMemory();
                Smooth.To(MemoryGauge, RingGauge.ValueProperty, memory.UsedPercent);
                MemoryValue.Text = string.Create(culture, $"{memory.UsedPercent:0} %");
                MemoryDetail.Text = string.Create(culture, $"{memory.UsedGb:0.0} / {memory.TotalGb:0.0} GB");
                MemoryFree.Text = Loc.F("{0:0.0} GB available", memory.AvailableGb);
                Smooth.To(MemoryBar, SegmentBar.ValueProperty, memory.UsedPercent);
                MemoryChart.Push(memory.UsedPercent);
                MemoryChartValue.Text = string.Create(culture, $"{memory.UsedPercent:0.0} %");
                MemoryInUse.Text = string.Create(culture, $"{memory.UsedGb:0.0} GB");
                MemoryAvailable.Text = string.Create(culture, $"{memory.AvailableGb:0.0} GB");
                MemoryCommitted.Text = string.Create(culture, $"{memory.CommittedGb:0.0} / {memory.CommitLimitGb:0.0} GB");
                MemoryCached.Text = string.Create(culture, $"{memory.CachedGb:0.0} GB");
            }
        }
        catch (Exception ex) when (ex is Win32Exception or System.Net.NetworkInformation.NetworkInformationException)
        {
            MemoryDetail.Text = Loc.T("System figures are unavailable.");
        }
    }

    private async Task RefreshGpuDiskAsync()
    {
        // Performance counters can take tens of milliseconds: read them off the UI thread, one read at a time.
        // Turned off: the counters are never opened.
        if (_gpuDiskRunning || !_monitoring.GpuDisk)
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
                DiskDetail.Text = Loc.F("All disks  -  read {0:0.0} MB/s, write {1:0.0} MB/s", reading.DiskReadMbPerSecond, reading.DiskWriteMbPerSecond);
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
        var monitoring = _monitoring;
        if (!monitoring.Processes && !monitoring.AiTools)
        {
            return;
        }

        try
        {
            var (snapshot, totalMemoryGb, rows, aiRows) = await Task.Run(() =>
            {
                var aiList = monitoring.AiTools ? BuildAiRows(AiToolCatalog.FindSessions(ProcessActions.Sample())) : [];
                var list = new List<ProcessRow>();
                if (monitoring.Processes)
                {
                    var groups = SystemMonitor.GetProcessGroups().Take(TopProcessCount).ToList();
                    var largest = groups.Count > 0 ? Math.Max(groups[0].PrivateMemoryMb, 1) : 1;
                    var culture = CultureInfo.CurrentCulture;
                    list = [.. groups.Select((group, index) => new ProcessRow(
                        group.Name,
                        group.Count > 1 ? $"×{group.Count}" : "",
                        group.PrivateMemoryMb / largest * 100,
                        FormatMemory(group.PrivateMemoryMb, culture),
                        BarColors[index % BarColors.Length],
                        IconCache.Get(group.ExecutablePath)))];
                }

                // Counting the processes and the running services is the costly part: only when that card is shown.
                var counts = monitoring.Processes ? SystemSnapshot.Capture() : null;
                return (counts, counts?.TotalMemoryGb ?? SystemMonitor.SampleMemory().TotalGb, list, aiList);
            });

            if (monitoring.Processes && snapshot is not null)
            {
                ProcessValue.Text = snapshot.ProcessCount.ToString(CultureInfo.CurrentCulture);
                ServiceValue.Text = snapshot.RunningServiceCount.ToString(CultureInfo.CurrentCulture);
                TopProcesses.ItemsSource = rows;
            }

            if (monitoring.AiTools)
            {
                ShowAiTools(aiRows, totalMemoryGb);
            }
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
            var detail = Loc.N(tool.Sessions, "1 session", "{0} sessions") + ", " + Loc.N(tool.Processes, "1 process", "{0} processes");
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

    private void ShowAiTools(List<AiToolRow> rows, double totalMemoryGb)
    {
        var culture = CultureInfo.CurrentCulture;
        AiTools.ItemsSource = rows;
        AiEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var totalMb = rows.Sum(row => row.MemoryMb);
        var share = totalMemoryGb <= 0 ? 0 : totalMb / MbPerGb / totalMemoryGb * 100;
        AiSummary.Text = rows.Count == 0
            ? ""
            : Loc.F("{0} in total, {1:0} % of this PC's memory", FormatMemory(totalMb, culture), share);
    }

    private void OnAiToolClick(object sender, RoutedEventArgs e) =>
        (Application.Current.MainWindow as MainWindow)?.NavigateTo(typeof(AiToolsPage));

    private async Task RefreshModesAsync()
    {
        var active = ModeSwitcher.ActiveMode;
        var activeMode = _modes.FirstOrDefault(mode => mode.Profile.Mode.Equals(active, StringComparison.OrdinalIgnoreCase));
        ActiveModeText.Text = activeMode is null ? Loc.T("Active mode: none") : Loc.F("Active mode: {0}", activeMode.Label);
        ActiveModeDot.Fill = activeMode?.Accent ?? InactiveDot;

        foreach (var mode in _modes)
        {
            try
            {
                var plan = await Task.Run(() => AppServices.Planner.Plan(mode.Profile));
                mode.Summary = ReferenceEquals(mode, activeMode) ? Loc.T("Active")
                    : Loc.N(plan.Changes.Count, "1 change", "{0} changes");
            }
            catch (ProfileException)
            {
                mode.Summary = Loc.T("Blocked by protection");
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
        private string _summary = Loc.T("Checking…");

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
