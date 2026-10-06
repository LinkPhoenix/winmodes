using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinModes.App.Services;
using WinModes.Core;
using WinModes.Core.Engine;
using WinModes.Core.Tuning;

namespace WinModes.App.Pages;

/// <summary>Service journals and recovery snapshots, without inferring unrecorded outcomes.</summary>
public partial class HistoryPage : Page
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly ObservableCollection<SessionRow> _visible = [];
    private List<SessionRow> _rows = [];
    private bool _reading;
    private string _signature = "";

    public HistoryPage()
    {
        InitializeComponent();
        Sessions.ItemsSource = _visible;
        _timer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) =>
        {
            OperationStatus.Changed += OnOperationChanged;
            ShowOperation();
            _timer.Start();
            await RefreshAsync();
        };
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            OperationStatus.Changed -= OnOperationChanged;
        };
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_reading || !WinModes.App.Services.WindowActivity.IsShown(this)) { return; }
        _reading = true;
        RefreshButton.IsEnabled = false;
        try
        {
            var read = await Task.Run(HistoryReader.Read);
            var signature = JsonSerializer.Serialize(read);
            if (signature != _signature && (read.UnreadableSources.Count == 0 || _rows.Count == 0))
            {
                var previous = _rows.ToDictionary(row => row.Key, StringComparer.Ordinal);
                _rows = BuildRows(read).OrderByDescending(row => row.Timestamp).GroupBy(row => row.Key, StringComparer.Ordinal).Select(group => group.First()).Select(row =>
                {
                    if (!previous.TryGetValue(row.Key, out var existing)) { return row; }
                    if (existing.Title == row.Title && existing.Subtitle == row.Subtitle && existing.Status == row.Status
                        && existing.Entries.SequenceEqual(row.Entries)) { return existing; }
                    row.IsExpanded = existing.IsExpanded;
                    return row;
                }).ToList();
                Filter();
                _signature = signature;
            }
            ReadStatus.Text = Loc.F("Last checked: {0:g}", DateTimeOffset.Now);
            ReadError.IsOpen = read.UnreadableSources.Count > 0;
            ReadError.Message = Loc.F("Unreadable sources: {0}. Other available records remain visible.", string.Join(", ", read.UnreadableSources.Distinct()));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            ReadError.IsOpen = true;
            ReadError.Message = Loc.T("History could not be refreshed. The last reading remains visible.");
        }
        finally
        {
            _reading = false;
            RefreshButton.IsEnabled = true;
        }
    }

    private static IEnumerable<SessionRow> BuildRows(HistoryRead read)
    {
        var catalog = ModeCatalog.Load();
        foreach (var session in read.Modes)
        {
            var mode = catalog.FirstOrDefault(item => item.Profile.Mode.Equals(session.Mode, StringComparison.OrdinalIgnoreCase));
            var failed = session.Entries.Any(entry => entry.Outcome == EntryOutcome.Failed);
            var state = session.Reverted ? "Undone" : !session.Completed ? "Incomplete" : failed ? "Contains failures" : "Completed";
            yield return new("mode:" + session.Id, 1, session.StartedUtc, Loc.F("{0} mode", mode?.Profile.Label ?? session.Mode),
                Loc.F("{0} · {1} service record(s)", DateLabel(session.StartedUtc), session.Entries.Count),
                mode?.Glyph ?? "", mode?.Accent ?? Palette.Neutral, Loc.T(state), Palette.Tint(failed ? Palette.Stop : Palette.Neutral),
                [.. session.Entries.Select(entry => new EntryRow(
                    $"{Loc.T(entry.Kind == EntryKind.StopService ? "Stop" : "Start")} {entry.Target}",
                    Loc.F("Before: {0}, {1}", entry.BeforeStartMode, Loc.T(entry.BeforeRunning ? "running" : "stopped")) + (entry.Detail is null ? "" : $". {entry.Detail}"),
                    Loc.T(entry.Outcome.ToString()), entry.Outcome == EntryOutcome.Failed ? Palette.Stop : Palette.Neutral))]);
        }
        foreach (var (records, scope) in new[] { (read.UserTweaks, "Current user"), (read.MachineTweaks, "This PC") })
        {
            foreach (var record in records)
            {
                var tweak = ServiceTuning.Catalog.Find(record.Id);
                var entries = record.Values.Select(value => new EntryRow($"{value.Hive}\\{value.Path} · {value.Name}",
                    Loc.F("Saved: {0} → Requested: {1}", value.Existed ? value.Previous ?? Loc.T("Unknown") : Loc.T("Not set"), value.Written),
                    Loc.T("Recovery record"), Palette.Neutral)).Concat(record.DisabledTasks.Select(task =>
                        new EntryRow(task, Loc.T("Saved: enabled → Requested: disabled"), Loc.T("Recovery record"), Palette.Neutral))).ToList();
                yield return new($"tweak:{scope}:{record.Id}", 2, record.AppliedUtc, tweak is null ? record.Id : Loc.T(tweak.Title),
                    $"{DateLabel(record.AppliedUtc)} · {Loc.T(scope)} · {Loc.T("Optimize")}", "", Palette.ModeColor("code"),
                    Loc.T("Recorded"), Palette.Tint(Palette.Neutral), entries);
            }
        }
        foreach (var record in read.Services)
        {
            yield return new("service:" + record.Service, 2, record.ChangedUtc, record.Service,
                $"{DateLabel(record.ChangedUtc)} · {Loc.T("Services")}", "", Palette.ModeColor("code"), Loc.T("Recorded"), Palette.Tint(Palette.Neutral),
                [new(record.Service, Loc.F("Saved: {0} → Requested: {1}", record.OriginalStartMode, record.SetTo), Loc.T("Recovery record"), Palette.Neutral)]);
        }
        foreach (var app in read.Apps)
        {
            yield return new("app:" + app.Name, 3, app.RemovedUtc, Loc.T(app.Title),
                $"{DateLabel(app.RemovedUtc)} · {Loc.T("Debloat")}", "", Palette.Apps, Loc.T("Recorded"), Palette.Tint(Palette.Neutral),
                [new(app.Name, app.FullName, Loc.T("Removal recovery record. Open Debloat to check installation and restore options."), Palette.Neutral)]);
        }
    }

    private static string DateLabel(DateTimeOffset timestamp) => timestamp == default ? Loc.T("Date not recorded") : timestamp.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (IsInitialized) { Filter(); }
    }

    private void OnKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsInitialized) { Filter(); }
    }

    private void Filter()
    {
        if (KindFilter is null || SearchBox is null) { return; }
        var matches = _rows.Where(row => (KindFilter.SelectedIndex <= 0 || row.Kind == KindFilter.SelectedIndex)
            && SearchMatcher.Matches(SearchBox.Text, row.Title, row.Subtitle, row.Status,
                string.Join(' ', row.Entries.Select(entry => $"{entry.Title} {entry.Detail} {entry.Outcome}")))).ToList();
        // Keep unchanged row containers and their expansion state across periodic reads.
        for (var index = 0; index < matches.Count; index++)
        {
            if (index < _visible.Count && ReferenceEquals(_visible[index], matches[index])) { continue; }
            var existingIndex = _visible.IndexOf(matches[index]);
            if (existingIndex >= 0) { _visible.Move(existingIndex, index); }
            else { _visible.Insert(index, matches[index]); }
        }
        while (_visible.Count > matches.Count) { _visible.RemoveAt(_visible.Count - 1); }
        EmptyState.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Sessions.Visibility = matches.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyTitle.Text = Loc.T(_rows.Count == 0 ? "No records yet" : "No matching records");
        Summary.Text = Loc.F("{0} of {1} record(s)", matches.Count, _rows.Count);
    }

    private void OnOperationChanged() => Dispatcher.BeginInvoke(ShowOperation);

    private void ShowOperation()
    {
        var operation = OperationStatus.Current;
        OperationPanel.Visibility = operation is null ? Visibility.Collapsed : Visibility.Visible;
        if (operation is null) { return; }
        OperationTitle.Text = Loc.F("{0} · {1}", operation.Title, Loc.T(operation.IsRunning ? "In progress" : operation.Failed ? "Contains failures" : "Completed"));
        OperationDetail.Text = operation.Total > 0
            ? Loc.F("This app session · {0}/{1} result(s) · {2}", operation.Completed, operation.Total, operation.Detail)
            : Loc.F("This app session · {0}", operation.Detail);
        OperationProgress.IsIndeterminate = operation.IsRunning && operation.Completed == 0;
        OperationProgress.Maximum = Math.Max(1, operation.Total);
        OperationProgress.Value = operation.Completed;
        OperationProgress.Visibility = operation.IsRunning ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnModes(object sender, RoutedEventArgs e) => Navigate(typeof(ModesPage));
    private void OnOptimize(object sender, RoutedEventArgs e) => Navigate(typeof(OptimizePage));
    private void OnDebloat(object sender, RoutedEventArgs e) => Navigate(typeof(DebloatPage));
    private void Navigate(Type type) => (Window.GetWindow(this) as MainWindow)?.NavigateTo(type);

    private sealed class SessionRow(string key, int kind, DateTimeOffset timestamp, string title, string subtitle,
        string glyph, Brush accent, string status, Brush statusTint, IReadOnlyList<EntryRow> entries)
    {
        public string Key { get; } = key;
        public int Kind { get; } = kind;
        public DateTimeOffset Timestamp { get; } = timestamp;
        public string Title { get; } = title;
        public string Subtitle { get; } = subtitle;
        public string Glyph { get; } = glyph;
        public Brush Accent { get; } = accent;
        public string Status { get; } = status;
        public Brush StatusTint { get; } = statusTint;
        public IReadOnlyList<EntryRow> Entries { get; } = entries;
        public bool IsExpanded { get; set; }
    }

    private sealed record EntryRow(string Title, string Detail, string Outcome, Brush OutcomeColor);
}
