using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using WinModes.App.Services;
using WinModes.Core;
using WinModes.Core.Planning;
using WinModes.Core.Software;

namespace WinModes.App.Pages;

public partial class SoftwarePage : Page
{
    private readonly ISystemProbe _probe = new WindowsSystemProbe();
    private readonly List<SoftwareRow> _rows = [];
    private readonly Dictionary<string, TextBlock> _categoryHeaders = [];
    private bool _ready;
    private bool _busy;
    private SoftwareAudience _audience;
    private Action? _cancelQueue;

    public SoftwarePage()
    {
        InitializeComponent();
        _rows.AddRange(SoftwareCatalog.Entries.Select(entry => new SoftwareRow(entry, UpdateSelection)));
        _ready = true;
        RefreshCategories();
    }

    private void OnAudienceChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _audience = CodingTab.IsChecked == true ? SoftwareAudience.AiCoding : SoftwareAudience.Everyday;
        RefreshCategories();
    }

    private void RefreshCategories()
    {
        CategoryBox.ItemsSource = new[] { Loc.T("All categories") }.Concat(_rows.Where(row => row.Entry.Audience == _audience).Select(row => row.Category).Distinct()).ToArray();
        CategoryBox.SelectedIndex = 0;
        ShowRows();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ShowRows();
    private void OnCategoryChanged(object sender, SelectionChangedEventArgs e) => ShowRows();

    private void OnCategoryHeaderLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBlock header || header.DataContext is not CollectionViewGroup group) return;
        _categoryHeaders[group.Name.ToString()!] = header;
        RefreshCategoryNavigation();
    }

    private void OnCategoryHeaderUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBlock header || header.DataContext is not CollectionViewGroup group) return;
        var name = group.Name.ToString()!;
        if (_categoryHeaders.TryGetValue(name, out var current) && ReferenceEquals(header, current)) _categoryHeaders.Remove(name);
        RefreshCategoryNavigation();
    }

    private void RefreshCategoryNavigation() => CategoryNav.SetAnchors(_categoryHeaders.Values
        .Where(header => header.IsLoaded && Rows.IsAncestorOf(header) && header.DataContext is CollectionViewGroup { ItemCount: > 0 })
        .OrderBy(header => ((CollectionViewGroup)header.DataContext).Items.OfType<SoftwareRow>().First().CategoryOrder));

    private void OnOpenGuide(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SoftwareRow row) return;
        try { Process.Start(new ProcessStartInfo(row.Entry.Website) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            ActionStatus.Text = Loc.T("The official guide could not be opened. Try again.");
        }
    }

    private IReadOnlyList<SoftwareRow> VisibleRows()
    {
        var terms = SearchMatcher.Terms(SearchBox.Text);
        var category = CategoryBox.SelectedIndex > 0 ? CategoryBox.SelectedItem as string : null;
        return [.. _rows.Where(row => row.Entry.Audience == _audience && (category is null || row.Category == category)
            && SearchMatcher.MatchesTerms(terms, row.Name, row.Category, row.Description, row.Entry.WingetId))];
    }

    private void ShowRows()
    {
        if (!_ready) return;
        var shown = VisibleRows();
        var view = new ListCollectionView(shown.ToArray());
        view.SortDescriptions.Add(new SortDescription(nameof(SoftwareRow.CategoryOrder), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(SoftwareRow.Name), ListSortDirection.Ascending));
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SoftwareRow.Category)));
        Rows.ItemsSource = view;
        Empty.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSummary(shown.Count);
        UpdateSelection();
    }

    private void UpdateSummary(int shownCount) => Summary.Text = Loc.F("{0} shown · {1} detected installed · {2} apps in the catalog",
        shownCount, _rows.Count(row => row.Installed), _rows.Count);

    private void UpdateSelection()
    {
        if (!_ready) return;
        var count = _rows.Count(row => row.IsSelected);
        InstallSelected.Content = Loc.F("Install selection ({0})", count);
        InstallSelected.IsEnabled = count > 0 && !_busy;
        SelectionText.Text = Loc.F("{0} selected across both categories", count);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Filters.IsEnabled = CatalogHost.IsEnabled = DetectButton.IsEnabled = SelectVisibleButton.IsEnabled = ClearButton.IsEnabled = !busy;
        UpdateSelection();
    }

    private async Task<bool> DetectAsync()
    {
        ActionStatus.Text = Loc.T("Checking installed apps with WinGet…");
        var observation = await Task.Run(_probe.GetSoftwareInventory);
        if (!observation.Available)
        {
            ActionStatus.Text = Loc.T(observation.Error ?? "WinGet detection is unavailable.");
            return false;
        }
        foreach (var row in _rows) row.Observe(observation);
        var checkedTime = observation.CheckedUtc.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture);
        ObservationText.Text = Loc.F("Checked {0}", checkedTime);
        ActionStatus.Text = Loc.T("Detection complete. Apps not matched by WinGet or Windows remain marked Not detected; this does not prove they are absent.");
        UpdateSummary(VisibleRows().Count);
        return true;
    }

    private async void OnDetect(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true);
        try { await DetectAsync(); }
        finally { SetBusy(false); }
    }

    private void OnSelectVisible(object sender, RoutedEventArgs e)
    {
        foreach (var row in VisibleRows().Where(row => row.CanSelect)) row.IsSelected = true;
    }

    private void OnClearSelection(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows) row.IsSelected = false;
    }

    private async void OnInstallSelection(object sender, RoutedEventArgs e) => await InstallAsync(_rows.Where(row => row.IsSelected).ToArray());
    private async void OnInstallOne(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SoftwareRow row) await InstallAsync([row]);
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        _cancelQueue?.Invoke();
        CancelButton.IsEnabled = false;
        ActionStatus.Text = Loc.T("Stopping after the current installer. Finish or cancel its dialog to continue.");
    }

    private async Task InstallAsync(IReadOnlyList<SoftwareRow> requested)
    {
        if (_busy || requested.Count == 0) return;
        SetBusy(true);
        Guid operation = Guid.Empty;
        try
        {
            if (!await DetectAsync()) return;
            var chosen = requested.Where(row => row.CanSelect).ToArray();
            if (chosen.Length == 0) { ActionStatus.Text = Loc.T("The selected apps are already installed or unavailable in WinGet."); return; }
            var review = new StackPanel();
            review.Children.Add(new TextBlock
            {
                Text = Loc.T("WinGet will install these apps one at a time. Its window will ask for agreements and administrator approval when needed. Existing apps are skipped. Dependencies are not installed automatically. Uninstall through Windows Settings if needed; this is not a mode change with Undo."),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
            });
            foreach (var row in chosen)
            {
                review.Children.Add(new TextBlock { Text = $"{row.Name} · {row.Entry.WingetId} · {row.Entry.Source}", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
                if (row.Entry.Note is not null) review.Children.Add(new TextBlock { Text = row.Note, TextWrapping = TextWrapping.Wrap });
            }
            var confirm = new Wpf.Ui.Controls.MessageBox
            {
                Owner = Window.GetWindow(this),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Title = Loc.F("Install {0} apps with WinGet?", chosen.Length),
                Content = new ScrollViewer { Content = review, MaxHeight = 440, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                PrimaryButtonText = Loc.T("Install"), CloseButtonText = Loc.T("Cancel"),
            };
            if (await confirm.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
            if (!OperationStatus.TryBegin(Loc.T("Install software"), chosen.Length, out operation))
            {
                ActionStatus.Text = Loc.T("An operation is already running. Wait for it to finish.");
                return;
            }
            using var cancelSource = new CancellationTokenSource();
            _cancelQueue = cancelSource.Cancel;
            CancelButton.Visibility = Visibility.Visible;
            CancelButton.IsEnabled = true;
            InstallProgress.Visibility = Visibility.Visible;
            InstallProgress.Maximum = chosen.Length;
            InstallProgress.Value = 0;
            var journal = new SoftwareInstallJournal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinModes", "software-installs"));
            var installer = new SoftwareInstaller(_probe, new WindowsWinget(), journal);
            var completed = 0;
            ActionStatus.Text = Loc.T("Follow the WinGet window and installer dialogs. You can stop the queue after the current app.");
            var results = await installer.InstallAsync([.. chosen.Select(row => row.Entry.Id)], (entry, result) => Dispatcher.Invoke(() =>
            {
                var row = _rows.Single(item => item.Entry.Id == entry.Id);
                row.SetResult(result);
                if (result.Outcome is SoftwareInstallOutcome.Installed or SoftwareInstallOutcome.AlreadyInstalled or SoftwareInstallOutcome.NeedsRestart) row.IsSelected = false;
                InstallProgress.Value = ++completed;
                OperationStatus.Progress(operation, completed, $"{entry.Name} · {row.ResultText}");
            }), cancelSource.Token);
            ResultsText.Text = string.Join(Environment.NewLine, results.Select(result =>
            {
                var row = _rows.Single(item => item.Entry.Id == result.EntryId);
                return $"{row.Name} · {row.ResultText}";
            }));
            ResultsPanel.Visibility = Visibility.Visible;
            var failed = results.Any(result => result.Outcome is SoftwareInstallOutcome.Failed or SoftwareInstallOutcome.Unverified);
            var summary = Loc.F("{0} of {1} apps processed. See the results for each app.", completed, chosen.Length);
            OperationStatus.Complete(operation, summary, failed);
            await DetectAsync();
            ActionStatus.Text = summary;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            ActionStatus.Text = Loc.T("Installation could not continue. No automatic retry was started. Check WinGet and the installation journal.");
            if (operation != Guid.Empty) OperationStatus.Complete(operation, ActionStatus.Text, failed: true);
        }
        finally
        {
            _cancelQueue = null;
            CancelButton.Visibility = InstallProgress.Visibility = Visibility.Collapsed;
            SetBusy(false);
        }
    }

    private sealed class SoftwareRow(SoftwareEntry entry, Action selectionChanged) : INotifyPropertyChanged
    {
        private readonly int _categoryOrder = SoftwareCatalog.Entries.Where(item => item.Audience == entry.Audience)
            .Select(item => item.Category).Distinct().ToList().IndexOf(entry.Category);
        private InstalledSoftware? _installed;
        private bool _checked;
        private bool _selected;
        private SoftwareInstallResult? _result;
        public event PropertyChangedEventHandler? PropertyChanged;
        public SoftwareEntry Entry => entry;
        public string Name => entry.Name;
        public string Category => Loc.T(entry.Category);
        public int CategoryOrder => _categoryOrder;
        public string Description => Loc.T(entry.Description);
        public string Note => entry.Note is null ? "" : Loc.T(entry.Note);
        public ImageSource Logo => SoftwareLogos.Get(entry.Id);
        public string KindText => Loc.T(entry.Kind switch { SoftwareKind.Cli => "CLI tool", SoftwareKind.EditorBridge => "Editor integration", _ => "Desktop app" });
        public Visibility InstallVisibility => entry.WingetId is null ? Visibility.Collapsed : Visibility.Visible;
        public Visibility GuideVisibility => entry.WingetId is null ? Visibility.Visible : Visibility.Collapsed;
        public string PackageId => entry.WingetId ?? Loc.T("Unavailable in WinGet");
        public string SourceText => entry.Source == "msstore" ? Loc.T("WinGet · Microsoft Store") : "WinGet";
        public bool Installed => _installed is not null;
        public bool CanSelect => entry.WingetId is not null && !Installed && _result?.Outcome != SoftwareInstallOutcome.Unverified;
        public bool IsSelected
        {
            get => _selected;
            set { if (_selected == value) return; _selected = value && CanSelect; Raise(nameof(IsSelected)); selectionChanged(); }
        }
        public string InstallationText => Loc.T(Installed ? "Installed" : entry.WingetId is null ? "Unavailable in WinGet" : _checked ? "Not detected" : "Not checked");
        public string InstallationGlyph => Installed ? "\uE73E" : "\uE946";
        public Brush InstallationTone => Installed ? Palette.Start : Palette.Neutral;
        public string VersionText => _installed?.Version is { Length: > 0 } version ? $"v{version}" : "";
        public Visibility NoteVisibility => entry.Note is null ? Visibility.Collapsed : Visibility.Visible;
        public string ResultText => _result is null ? "" : Loc.T(_result.Outcome switch
        {
            SoftwareInstallOutcome.Installed => "Installed",
            SoftwareInstallOutcome.AlreadyInstalled => "Already installed · Skipped",
            SoftwareInstallOutcome.NeedsRestart => "Installed · Restart needed",
            SoftwareInstallOutcome.Unverified => "Installation unverified · Check WinGet",
            _ => "Installation failed",
        }) + (_result.ExitCode is { } code ? $" ({code})" : "");
        public void Observe(SoftwareInventory inventory)
        {
            _checked = true; _installed = inventory.Find(entry);
            if (Installed) IsSelected = false;
            foreach (var property in new[] { nameof(Installed), nameof(CanSelect), nameof(InstallationText), nameof(InstallationGlyph), nameof(InstallationTone), nameof(VersionText) }) Raise(property);
        }
        public void SetResult(SoftwareInstallResult result)
        {
            _result = result;
            if (!CanSelect) IsSelected = false;
            Raise(nameof(ResultText));
            Raise(nameof(CanSelect));
        }
        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
