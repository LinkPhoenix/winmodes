using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinModes.App.Services;

namespace WinModes.App.Pages;

/// <summary>Opt-in history of the memory used by AI tools, per project, over the last days.</summary>
public partial class UsagePage : Page
{
    private static string DesktopApp => Loc.T("Desktop app");
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);
    private static readonly PeriodChoice[] Periods = [new(1, Loc.T("Today")), new(7, Loc.T("Last 7 days")), new(30, Loc.T("Last 30 days"))];
    private static readonly Brush[] BarColors = [Palette.Apps, Palette.Container, Palette.Start, Palette.Power, Palette.Stop];

    private readonly DispatcherTimer _timer = new() { Interval = RefreshInterval };
    private readonly bool _loaded;

    public UsagePage()
    {
        InitializeComponent();
        Period.ItemsSource = Periods;
        Period.SelectedIndex = 1;
        Enabled.IsChecked = AppSettings.Load().RecordUsageHistory;
        TokensEnabled.IsChecked = AppSettings.Load().ReadTokenLogs;
        _loaded = true;

        _timer.Tick += async (_, _) =>
        {
            if (WinModes.App.Services.WindowActivity.IsShown(this))
            {
                await RefreshAsync();
            }
        };
        Loaded += async (_, _) =>
        {
            MonitoringGate.ApplyAiTools(Enabled);
            _timer.Start();
            await RefreshAsync();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    private async Task RefreshAsync()
    {
        var days = (Period.SelectedItem as PeriodChoice)?.Days ?? 7;
        await ShowTokensAsync(days);
        var summaries = await Task.Run(() => AppServices.Usage.Summarize(DateOnly.FromDateTime(DateTime.Now), days));
        var culture = CultureInfo.CurrentCulture;
        var largest = summaries.Count > 0 ? Math.Max(summaries[0].GbHours, double.Epsilon) : 1;

        Rows.ItemsSource = summaries.Select((summary, index) => new Row(
            summary.Project.Length == 0 ? DesktopApp : Privacy.Project(summary.Project),
            summary.Tool,
            summary.GbHours / largest * 100,
            FormatDuration(summary.Duration),
            DashboardPage.FormatMemory(summary.AverageMemoryMb, culture),
            DashboardPage.FormatMemory(summary.PeakMemoryMb, culture),
            BarColors[index % BarColors.Length])).ToList();

        var recording = Enabled.IsChecked == true;
        ListCard.Visibility = summaries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = summaries.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyTitle.Text = Loc.T(recording ? "Nothing recorded yet" : "Usage history is off");
        EmptyText.Text = Loc.T(recording
            ? "Figures appear after an AI tool has run for a minute."
            : "Turn on recording above to see which projects use the most memory.");
        Summary.Text = summaries.Count == 0
            ? Loc.T("How much memory your AI tools used, per project.")
            : Loc.F("{0} project(s), ranked by memory held over time.", summaries.Count) + " " + Loc.T(recording ? "Recording is on." : "Recording is off.");
    }

    private static readonly TimeSpan TokenScanEvery = TimeSpan.FromSeconds(30);

    /// <summary>The token cards for the chosen period; a scan of the logs is started in the background when the figures are getting old.</summary>
    private async Task ShowTokensAsync(int days)
    {
        if (TokensEnabled.IsChecked != true)
        {
            TokenHost.Content = null;
            TokenStatus.Text = "";
            TokenEmpty.Visibility = Visibility.Collapsed;
            return;
        }

        if (!TokenStats.IsScanning && (TokenStats.UpdatedAt is not { } updated || DateTimeOffset.Now - updated > TokenScanEvery))
        {
            _ = TokenStats.RefreshAsync();
        }

        var summaries = await Task.Run(() => TokenStats.Summarize(days));
        TokenHost.Content = summaries.Count > 0 ? Controls.TokenCards.Build(summaries) : null;
        var scanning = TokenStats.Progress;
        var updatedAt = TokenStats.UpdatedAt?.ToString("t", CultureInfo.CurrentCulture) ?? "";
        TokenEmpty.Visibility = summaries.Count == 0 && !TokenStats.IsScanning ? Visibility.Visible : Visibility.Collapsed;
        TokenStatus.Text = TokenStats.IsScanning
            ? scanning is { FilesTotal: > 0 } progress ? Loc.F("Reading the logs… {0} of {1} files", progress.FilesDone, progress.FilesTotal) : Loc.T("Reading the logs…")
            : updatedAt.Length > 0 ? Loc.F("Updated {0}", updatedAt) : "";
    }

    private async void OnTokensChanged(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        var on = TokensEnabled.IsChecked == true;
        (AppSettings.Load() with { ReadTokenLogs = on }).Save();
        if (!on)
        {
            // Turned off: the figures kept for it go too.
            TokenStats.Clear();
        }

        await RefreshAsync();
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1 ? $"{(int)duration.TotalHours} h {duration.Minutes:00}" : $"{Math.Max((int)duration.TotalMinutes, 1)} min";

    private async void OnPeriodChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loaded)
        {
            await RefreshAsync();
        }
    }

    private async void OnEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        (AppSettings.Load() with { RecordUsageHistory = Enabled.IsChecked == true }).Save();
        (Application.Current as App)?.ApplyDisplaySettings();
        await RefreshAsync();
    }

    private async void OnClear(object sender, RoutedEventArgs e)
    {
        var confirm = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("Clear the usage history?"),
            Content = Loc.T("Every recorded day is deleted. This cannot be undone."),
            PrimaryButtonText = Loc.T("Clear history"),
            CloseButtonText = Loc.T("Cancel"),
        };
        if (await confirm.ShowDialogAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            await Task.Run(AppServices.Usage.Clear);
            await RefreshAsync();
        }
    }

    private sealed record PeriodChoice(int Days, string Label);

    private sealed record Row(string Project, string Tool, double Share, string Duration, string Average, string Peak, Brush Color);
}
