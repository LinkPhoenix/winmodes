using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinModes.Core.Engine;

namespace WinModes.App.Pages;

/// <summary>Mode switches read from the journal, newest first. Refreshes by itself.</summary>
public partial class HistoryPage : Page
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);
    private const string DefaultGlyph = "";

    private readonly DispatcherTimer _timer = new() { Interval = RefreshInterval };
    private string _lastSignature = "";

    public HistoryPage()
    {
        InitializeComponent();
        _timer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) =>
        {
            _timer.Start();
            await RefreshAsync();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    private async Task RefreshAsync()
    {
        if (!IsVisible)
        {
            return;
        }

        var sessions = await Task.Run(AppServices.Switcher.Journal.LoadAll);

        // Rebuilding the list collapses the expanders, so only do it when something changed.
        var signature = string.Join('|', sessions.Select(session => $"{session.Id}:{session.Reverted}:{session.DoneCount}"));
        if (signature == _lastSignature)
        {
            return;
        }

        _lastSignature = signature;
        var catalog = ModeCatalog.Load();
        Sessions.ItemsSource = sessions.Select(session =>
        {
            var entry = catalog.FirstOrDefault(mode => mode.Profile.Mode.Equals(session.Mode, StringComparison.OrdinalIgnoreCase));
            var status = session.Reverted ? (Loc.T("Undone"), Palette.Neutral) : session.DoneCount > 0 ? (Loc.T("Active"), Palette.Start) : (Loc.T("No change"), Palette.Neutral);
            return new SessionRow(
                Loc.F("{0} mode", entry?.Profile.Label ?? session.Mode),
                Loc.F("{0:g} - {1} services", session.StartedUtc.ToLocalTime(), session.Entries.Count),
                entry?.Glyph ?? DefaultGlyph,
                entry?.Accent ?? Palette.Neutral,
                status.Item1,
                Palette.Tint(status.Item2),
                [.. session.Entries.Select(ToRow)]);
        }).ToList();

        EmptyState.Visibility = sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Summary.Text = sessions.Count == 0
            ? Loc.T("Every mode switch is recorded here with the state it replaced.")
            : Loc.F("{0} switch(es) recorded. Undo the active one from the Modes page.", sessions.Count);
    }

    private static EntryRow ToRow(JournalEntry entry)
    {
        var verb = Loc.T(entry.Kind == EntryKind.StopService ? "Stop" : "Start");
        var before = Loc.F("Before: {0}, {1}", entry.BeforeStartMode, Loc.T(entry.BeforeRunning ? "running" : "stopped"));
        var color = entry.Outcome switch
        {
            EntryOutcome.Done or EntryOutcome.Reverted => Palette.Start,
            EntryOutcome.Failed => Palette.Stop,
            _ => Palette.Neutral,
        };
        return new EntryRow($"{verb} {entry.Target}", entry.Detail is null ? before : $"{before}. {entry.Detail}", Loc.T(entry.Outcome.ToString()), color);
    }

    private sealed record SessionRow(
        string Title, string Subtitle, string Glyph, Brush Accent, string Status, Brush StatusTint, IReadOnlyList<EntryRow> Entries);

    private sealed record EntryRow(string Title, string Detail, string Outcome, Brush OutcomeColor);
}
