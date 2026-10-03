using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using WinModes.App.Pages;
using WinModes.App.Services;
using WinModes.Core.Tuning;
using Wpf.Ui.Controls;

namespace WinModes.App;

/// <summary>Edits portable choices. Importing and authoring never execute Windows changes.</summary>
public partial class ConfigurationWindow : FluentWindow
{
    private readonly ObservableCollection<ChoiceRow> _rows = [];
    private OptimizePage? _page;
    private bool _loading;
    private bool _closed;

    public ConfigurationWindow()
    {
        InitializeComponent();
        Choices.ItemsSource = _rows;
        Closed += (_, _) => _closed = true;
        Loaded += async (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            MinWidth = Math.Min(MinWidth, area.Width);
            MinHeight = Math.Min(MinHeight, area.Height);
            Width = Math.Min(Width, area.Width);
            Height = Math.Min(Height, area.Height);
            await LoadCurrentAsync();
        };
    }

    private async Task LoadCurrentAsync()
    {
        if (_loading || Owner is not MainWindow shell) return;
        SetLoading(true);
        StatusText.Text = Loc.T("Reading current settings…");
        try
        {
            _page = shell.OpenOptimize() ?? throw new InvalidOperationException("Optimize could not be opened.");
            await _page.PrepareConfigurationAsync();
            if (_closed) return;
            var choices = _page.GetConfigurationChoices().Select(choice => new SettingChoice
                { Id = choice.Id, PartIndex = choice.PartIndex, Desired = choice.Desired }).ToList();
            Show(choices);
            StatusText.Text = Loc.T("Only readable Optimize settings are included. This file is a configuration, not a backup of previous registry values.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (!_closed) StatusText.Text = Loc.T("Current settings could not be read. Refresh Optimize and try again.");
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void Show(IReadOnlyList<SettingChoice> choices)
    {
        _rows.Clear();
        foreach (var choice in choices)
        {
            var tweak = ServiceTuning.Catalog.Find(choice.Id);
            var part = tweak?.Parts.FirstOrDefault(part => part.Index == choice.PartIndex);
            var supported = tweak is not null && part is not null && TweakGuard.Validate(tweak).Count == 0;
            _rows.Add(new ChoiceRow(choice, tweak is null ? choice.Id : Loc.T(tweak.Title),
                part is null ? Loc.F("Part {0}", choice.PartIndex) : Loc.T(part.Label), supported));
        }
        StageButton.IsEnabled = !_loading && _page is not null && _rows.Any(row => row.Supported);
    }

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
            { Title = Loc.T("Import configuration"), Filter = "WinModes configuration (*.winmodes.json)|*.winmodes.json|JSON (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        SetLoading(true);
        try
        {
            var configuration = await Task.Run(() => SettingsConfigurationFile.Read(dialog.FileName));
            if (_closed) return;
            Show(configuration.Settings);
            StatusText.Text = Loc.F("{0} choices loaded; {1} unsupported. Review each choice before staging.", _rows.Count, _rows.Count(row => !row.Supported));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            if (!_closed) StatusText.Text = Loc.T("The configuration is invalid, unreadable, too large, or uses an unsupported version. Existing choices were kept.");
        }
        finally { SetLoading(false); }
    }

    private async void OnExport(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var choices = IncludedChoices();
        if (choices.Count == 0) { StatusText.Text = Loc.T("Select at least one supported setting."); return; }
        if (HasConflicts(choices)) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
            { Title = Loc.T("Export configuration"), Filter = "WinModes configuration (*.winmodes.json)|*.winmodes.json", FileName = "Windows-settings.winmodes.json" };
        if (dialog.ShowDialog(this) != true) return;
        SetLoading(true);
        try
        {
            await Task.Run(() => SettingsConfigurationFile.Write(dialog.FileName, choices));
            if (_closed) return;
            StatusText.Text = Loc.T("Configuration saved. No Windows settings were changed.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            if (!_closed) StatusText.Text = Loc.T("The configuration could not be saved.");
        }
        finally { SetLoading(false); }
    }

    private List<SettingChoice> IncludedChoices() => _rows.Where(row => row.Included && row.Supported)
        .Select(row => new SettingChoice { Id = row.Id, PartIndex = row.PartIndex, Desired = row.Desired }).ToList();

    private bool HasConflicts(IReadOnlyList<SettingChoice> choices)
    {
        var writes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var choice in choices.Where(choice => choice.Desired))
        {
            var part = ServiceTuning.Catalog.Find(choice.Id)?.Parts.FirstOrDefault(part => part.Index == choice.PartIndex);
            if (part is null) continue;
            var value = part.Setting ?? "Disabled";
            if (writes.TryGetValue(part.Target, out var prior) && prior != value)
            {
                StatusText.Text = Loc.T("Some selected settings request different values for the same target. Skip one before continuing.");
                return true;
            }
            writes[part.Target] = value;
        }
        return false;
    }

    private void OnStage(object sender, RoutedEventArgs e)
    {
        if (_loading || _page is null) return;
        var choices = IncludedChoices();
        if (choices.Count == 0) { StatusText.Text = Loc.T("Select at least one supported setting."); return; }
        if (HasConflicts(choices)) return;
        var rejected = _page.StageConfigurationChoices(choices.Select(choice => (choice.Id, choice.PartIndex, choice.Desired)).ToList());
        if (rejected.Count > 0)
        {
            StatusText.Text = Loc.F("{0} choices could not be staged. Review Optimize for unavailable settings or missing undo records.", rejected.Count)
                + Environment.NewLine + string.Join(Environment.NewLine, rejected.Take(12));
            return;
        }
        Close();
    }

    private async void OnReset(object sender, RoutedEventArgs e) => await LoadCurrentAsync();
    private void OnSelect(object sender, RoutedEventArgs e) { if (!_loading) foreach (var row in _rows) row.Included = row.Supported; }
    private void OnSkip(object sender, RoutedEventArgs e) { if (!_loading) foreach (var row in _rows) row.Included = false; }
    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void SetLoading(bool loading)
    {
        _loading = loading;
        if (_closed) return;
        ConfigurationActions.IsEnabled = !loading;
        Choices.IsEnabled = !loading;
        StageButton.IsEnabled = !loading && _page is not null && _rows.Any(row => row.Supported);
    }

    private sealed class ChoiceRow : INotifyPropertyChanged
    {
        public ChoiceRow(SettingChoice choice, string title, string detail, bool supported)
        {
            Id = choice.Id;
            PartIndex = choice.PartIndex;
            Title = title;
            Detail = detail;
            Supported = supported;
            Included = supported;
            Desired = choice.Desired;
        }
        public string Id { get; }
        public int PartIndex { get; }
        public string Title { get; }
        public string Detail { get; }
        public bool Supported { get; }
        public string IncludeName => Loc.F("Include: {0}", Title);
        public string DesiredName => Loc.F("Desired state: {0}", Title);
        public string Reason => Supported ? Loc.T("Apply or undo is checked against this PC when staged.") : Loc.T("Unknown or unsupported setting; skipped.");
        public bool Included { get; set { if (field == value) return; field = value; PropertyChanged?.Invoke(this, new(nameof(Included))); } }
        public bool Desired { get; set; }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
