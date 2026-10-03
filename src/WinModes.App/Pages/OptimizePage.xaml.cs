using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Automation.Peers;
using System.Windows.Automation;
using WinModes.App.Controls;
using WinModes.App.Services;
using WinModes.Core;
using WinModes.Core.Planning;
using WinModes.Core.Tuning;

namespace WinModes.App.Pages;

/// <summary>
/// Windows settings worth changing, by category: catalog tweaks (registry values, scheduled tasks) and
/// services the knowledge base advises to start on demand. Each row shows the live state and a switch;
/// switches only prepare changes, which the review bar applies in one step. Every change is journaled
/// first, so switching a setting off puts the recorded value back.
/// </summary>
public partial class OptimizePage : Page
{
    private static string ServicesTitle => Loc.T("Services");
    private const string AdminGlyph = "";
    private const string AccountGlyph = "";

    /// <summary>Icon, colour and one-line purpose of each catalog category.</summary>
    private static readonly Dictionary<string, (string Glyph, Brush Color, string Subtitle)> Categories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Privacy and telemetry"] = ("", Palette.Apps, "What Windows records about you and sends to Microsoft."),
        ["Ads and suggestions"] = ("", Palette.Stop, "Promoted apps, tips and ads in Start, Settings, the lock screen and File Explorer."),
        ["Search and AI"] = ("", Palette.Container, "Web results in Start search, Widgets, Recall and Windows Copilot."),
        ["Gaming"] = ("", Palette.Power, "Game Mode, background recording and pointer behaviour."),
        ["System and background"] = ("", Palette.Start, "What keeps running or loading when you are not using it."),
        ["Explorer and developer"] = ("", Palette.Neutral, "Small conveniences for File Explorer, the taskbar and development."),
    };

    private const string AllGlyph = "";

    private List<Group> _groups = [];
    private List<CategoryChip> _chips = [];

    // The list is flat (intro, score, then a title and the rows of each category, footer) so that only the rows in view are built.
    private readonly ObservableCollection<object> _items = [];
    private readonly IntroItem _intro = new();
    private readonly ScoreCard _score = new();
    private readonly ResultCard _result = new();
    private readonly EmptyItem _empty = new();
    private readonly FooterItem _footer = new();

    /// <summary>The category the list is limited to (its title), or null for all of them.</summary>
    private string? _selectedCategory;
    private HashSet<string> _undoable = new(StringComparer.OrdinalIgnoreCase);
    private bool _hasChangedServices;
    private bool _busy;
    private bool _reviewing;
    private bool _refreshing;
    private Task? _refreshTask;
    private bool _lastRefreshFailed;

    public OptimizePage()
    {
        InitializeComponent();
        Items.ItemTemplateSelector = new ItemTemplates(
            (typeof(IntroItem), (DataTemplate)Resources["IntroTemplate"]),
            (typeof(ScoreCard), (DataTemplate)Resources["ScoreTemplate"]),
            (typeof(ResultCard), (DataTemplate)Resources["ResultTemplate"]),
            (typeof(EmptyItem), (DataTemplate)Resources["EmptyTemplate"]),
            (typeof(Group), (DataTemplate)Resources["GroupTemplate"]),
            (typeof(Row), (DataTemplate)Resources["RowTemplate"]),
            (typeof(FooterItem), (DataTemplate)Resources["FooterTemplate"]));
        Items.ItemsSource = _items;
        Loaded += async (_, _) => await RefreshAsync();
    }


    private IEnumerable<Row> Rows => _groups.SelectMany(group => group.All);

    /// <summary>The reading the list on screen was built from.</summary>
    private OptimizeSnapshot? _shown;

    /// <summary>
    /// Shows what this PC looks like now. The page is kept between visits, so the last reading is shown at once and a new one replaces it only
    /// if something changed. A switch the user has set and not applied is their work: it is left alone unless <paramref name="force"/> says the list was just changed.
    /// </summary>
    private Task RefreshAsync(bool force = false) => _refreshTask is { IsCompleted: false } ? _refreshTask : _refreshTask = RefreshCoreAsync(force);

    private async Task RefreshCoreAsync(bool force)
    {
        if (_refreshing) return;
        _refreshing = true;
        RefreshButton.IsEnabled = false;
        ObservationStatus.Text = Loc.T("Checking settings…");
        try
        {
            if (_shown is null && OptimizeSnapshot.Last is { } cached) Show(cached);
            var snapshot = await OptimizeSnapshot.TakeAsync();
            _lastRefreshFailed = false;
            if (force || _shown is null || !snapshot.SameAs(_shown)) Show(snapshot, preservePending: !force);
            else _shown = snapshot;
            var errors = snapshot.Observations.Values.SelectMany(parts => parts).Count(part => part.Error is not null);
            ObservationStatus.Text = errors == 0
                ? Loc.F("Checked {0:g}", snapshot.CheckedUtc.ToLocalTime())
                : Loc.F("Checked {0:g}; {1} unreadable parts", snapshot.CheckedUtc.ToLocalTime(), errors);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            _lastRefreshFailed = true;
            ObservationStatus.Text = _shown is null
                ? Loc.F("Refresh failed: {0}", ex.Message)
                : Loc.F("Cached check {0:g}. Refresh failed: {1}", _shown.CheckedUtc.ToLocalTime(), ex.Message);
        }
        finally
        {
            _refreshing = false;
            RefreshButton.IsEnabled = !_busy;
            UpdateReview();
        }
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            e.Handled = true;
            if (!_busy) await RefreshAsync();
        }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            SearchBox.Focus();
        }
        else if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control
            && Keyboard.FocusedElement is DependencyObject focused && focused.FindAncestor<FrameworkElement>() is { } element)
        {
            var current = element;
            while (current is not null && current.DataContext is not Row) current = VisualTreeHelper.GetParent(current) as FrameworkElement;
            if (current?.DataContext is Row row && row.Parts.Count > 0)
            {
                row.IsExpanded = !row.IsExpanded;
                e.Handled = true;
                var peer = UIElementAutomationPeer.FromElement(element) ?? UIElementAutomationPeer.CreatePeerForElement(element);
                peer?.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.MostRecent,
                    Loc.T(row.IsExpanded ? "Details expanded" : "Details collapsed"), "OptimizeDetails");
            }
        }
    }

    internal async Task PrepareConfigurationAsync()
    {
        await RefreshAsync();
        if (_shown is null || _lastRefreshFailed) throw new InvalidOperationException(Loc.T("A successful settings check is required before preparing a configuration."));
    }

    internal IReadOnlyList<(string Id, int PartIndex, bool Desired)> GetConfigurationChoices() =>
        [.. Rows.Where(row => row.Tweak is not null).SelectMany(row => row.Parts.Where(part => part.IsReadable).Select(part => (row.Id, part.Index, part.IsOn)))];

    internal IReadOnlyList<string> StageConfigurationChoices(IReadOnlyList<(string Id, int PartIndex, bool Desired)> choices)
    {
        if (_busy || _reviewing || _refreshing || _lastRefreshFailed || _shown is null)
            return [Loc.T("A successful settings check is required before preparing a configuration.")];
        var rejected = new List<string>();
        var valid = new List<(PartRow Part, bool Desired)>();
        foreach (var choice in choices)
        {
            var row = Rows.FirstOrDefault(row => row.Tweak is not null && row.Id.Equals(choice.Id, StringComparison.OrdinalIgnoreCase));
            var part = row?.Parts.FirstOrDefault(part => part.Index == choice.PartIndex);
            if (row?.Tweak is null || TweakGuard.Validate(row.Tweak).Count > 0 || part is null || !part.IsReadable
                || (!part.CanToggle && part.IsOn != choice.Desired))
                rejected.Add($"{choice.Id}#{choice.PartIndex}: " + Loc.T("Unavailable, unreadable or no recorded undo value."));
            else valid.Add((part, choice.Desired));
        }
        if (rejected.Count > 0) return rejected;
        foreach (var (part, desired) in valid) part.IsOn = desired;
        ApplyFilter();
        UpdateReview();
        return rejected;
    }

    private void Show(OptimizeSnapshot snapshot, bool preservePending = true)
    {
        var pendingParts = preservePending ? Rows.Where(row => row.Tweak is not null).SelectMany(row => row.Parts.Where(part => part.IsPending).Select(part => (row.Id, part.Index, part.IsOn, part.IsApplied))).ToList() : [];
        var pendingServices = preservePending ? Rows.Where(row => row.IsService && row.IsPending).ToDictionary(row => row.Id, row => row.IsOn, StringComparer.OrdinalIgnoreCase) : [];
        _shown = snapshot;
        var services = snapshot.Services;
        var changed = snapshot.ChangedServices;
        var journaledParts = snapshot.JournaledParts;
        var undoable = snapshot.Undoable;
        var expanded = Rows.Where(row => row.IsExpanded).Select(row => row.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _undoable = undoable;
        _hasChangedServices = changed.Count > 0;

        // Catalog order is kept: categories appear as the catalog lists them.
        _groups = [.. ServiceTuning.Catalog.Tweaks
            // Missing tasks and unreadable parts remain visible so their compatibility is explained.
            .GroupBy(tweak => tweak.Category)
            .Select(group =>
            {
                var (glyph, color, subtitle) = Categories.GetValueOrDefault(group.Key, ("", Palette.Neutral, ""));
                return new Group(Loc.T(group.Key), Loc.T(subtitle), glyph, color, [.. group.Select(tweak => Row.For(tweak, snapshot.Observations[tweak.Id], journaledParts.GetValueOrDefault(tweak.Id) ?? [], OnPartChanged,
                    snapshot.Records.Where(record => record.Id.Equals(tweak.Id, StringComparison.OrdinalIgnoreCase)).SelectMany(record => record.Values.Where(value => value.WrittenAbsent).Select(tweak.PartOf)).OfType<int>().ToHashSet()))]);
            })];

        // Services: what the knowledge base advises, then what was already changed (kept so it can be switched back).
        var byName = services.ToDictionary(service => service.Name, StringComparer.OrdinalIgnoreCase);
        var serviceRows = ServiceTuning.Knowledge.Recommend(services, AppServices.Policy).Select(Row.For)
            .Concat(changed.OrderBy(tweak => tweak.Service, StringComparer.OrdinalIgnoreCase)
                .Select(tweak => Row.For(tweak, byName.GetValueOrDefault(tweak.Service), ServiceTuning.Knowledge.Find(tweak.Service))))
            .ToList();
        if (serviceRows.Count > 0)
        {
            _groups.Add(new Group(ServicesTitle, Loc.T("Services that start with Windows although they are only needed now and then."), "", Palette.Container, serviceRows));
        }

        foreach (var row in Rows.Where(row => expanded.Contains(row.Id)))
        {
            row.IsExpanded = true;
        }

        foreach (var row in Rows)
        {
            foreach (var part in row.Parts)
                if (pendingParts.FirstOrDefault(choice => choice.Id == row.Id && choice.Index == part.Index) is var choice && choice.Id is not null)
                {
                    if (!part.IsReadable) part.IsApplied = choice.IsApplied;
                    part.IsOn = choice.IsOn;
                }
            if (row.IsService && pendingServices.TryGetValue(row.Id, out var desired)) row.IsOn = desired;
        }

        var recommended = Rows.Where(row => row.IsRecommended).ToList();
        var applied = recommended.Count(row => row.IsApplied);
        _score.Value = Loc.F("{0} of {1}", applied, recommended.Count);
        _score.Summary = Rows.Any()
            ? Loc.T("Recommendations are a starting point, not a performance score. Keep the Windows features you use.")
            : Loc.T("The tweak catalog (data/tweaks.json) and the knowledge base (data/db) are missing from this copy of WinModes.");
        _score.CanSelectRecommended = Rows.Any(row => row.CanPrepareRecommended);
        _score.CanUndoAll = _hasChangedServices || undoable.Count > 0;

        ApplyFilter();
        UpdateReview();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnFilterChanged(object sender, RoutedEventArgs e) => ApplyFilter();

    private void OnSettingFilterChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    private void OnClearFilters(object sender, RoutedEventArgs e)
    {
        _selectedCategory = null;
        SearchBox.Text = "";
        HideApplied.IsChecked = false;
        SettingFilter.SelectedIndex = 0;
        ApplyFilter();
    }

    private bool MatchesSettingFilter(Row row) => SettingFilter.SelectedIndex switch
    {
        1 => row.IsRecommended,
        2 => row.NeedsCare,
        3 => row.IsApplied,
        4 => row.NeedsElevation,
        5 => row.IsPending,
        6 => row.Parts.Any(part => !part.IsReadable) || !row.ServiceReadable,
        7 => row.PolicyPresent,
        8 => !row.CanToggle || row.Parts.Any(part => !part.CanToggle),
        _ => true,
    };

    private void ApplyFilter()
    {
        // Filter events fire while the page is still being built.
        if (Items is null || SearchBox is null || HideApplied is null || SettingFilter is null || VisibleCount is null)
        {
            return;
        }

        var search = SearchMatcher.Terms(SearchBox.Text);
        var hideApplied = HideApplied.IsChecked == true;
        foreach (var group in _groups)
        {
            // A row with a pending change stays visible, whatever the filter says.
            group.Show(row => (row.IsPending || MatchesSettingFilter(row)) && (!hideApplied || !row.IsApplied || row.IsPending)
                && SearchMatcher.MatchesTerms(search, row.Title, row.Description, row.Subtitle,
                    string.Join(" ", row.Parts.Select(part => part.Label))));
        }

        // The selected category can vanish after a change (the Services group when nothing is left to advise).
        if (_selectedCategory is not null && _groups.All(group => group.Title != _selectedCategory))
        {
            _selectedCategory = null;
        }

        var visible = _groups.Where(group => group.Rows.Count > 0 && (_selectedCategory is null || group.Title == _selectedCategory)).ToList();
        VisibleCount.Text = Loc.F("{0} shown", visible.Sum(group => group.Rows.Count));
        List<object> shown = [_intro, _score];
        if (_result.Text.Length > 0)
        {
            shown.Add(_result);
        }

        if (visible.Count == 0 && _groups.Count > 0)
        {
            shown.Add(_empty);
        }

        foreach (var group in visible)
        {
            shown.Add(group);
            shown.AddRange(group.Rows);
            for (var i = 0; i < group.Rows.Count; i++)
            {
                group.Rows[i].IsLast = i == group.Rows.Count - 1;
            }
        }

        shown.Add(_footer);
        _items.ReconcileItems(shown);
        ShowCategories();
    }

    /// <summary>One chip per category plus "All"; the numbers follow the search, so they tell where the matches are.</summary>
    private void ShowCategories()
    {
        List<(string? Key, string Title, string Glyph, Brush Color, int Count)> entries =
            [(null, Loc.T("All"), AllGlyph, Palette.BrandBrush, _groups.Sum(group => group.Rows.Count)), .. _groups.Select(group => ((string?)group.Title, group.Title, group.Glyph, group.Color, group.Rows.Count))];
        _chips = CategoryChips.Sync(CategoryBar, _chips, entries, _selectedCategory);
    }

    private void OnCategoryClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CategoryChip chip)
        {
            return;
        }

        _selectedCategory = chip.Key;
        ApplyFilter();
        (Items.Template.FindName("ListScroll", Items) as ScrollViewer)?.ScrollToTop();
    }

    private void OnRowToggled(object sender, RoutedEventArgs e) => UpdateReview();

    /// <summary>A part of a tweak was ticked or unticked: the review bar counts it.</summary>
    private void OnPartChanged() => UpdateReview();

    private void OnToggleDetails(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Row row)
        {
            row.IsExpanded = !row.IsExpanded;
        }
    }

    private void UpdateReview()
    {
        var pending = Rows.Where(row => row.IsPending).ToList();
        ApplyButton.IsEnabled = !_busy && !_reviewing && !_refreshing && !_lastRefreshFailed && pending.All(row => row.Parts.Where(part => part.IsPending).All(part => part.IsReadable));
        ReviewBar.Visibility = pending.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (pending.Count == 0)
        {
            return;
        }

        var toApply = pending.Count(row => row.WillApply);
        var toUndo = pending.Count(row => row.WillUndo);
        ReviewTitle.Text = (toApply, toUndo) switch
        {
            (_, 0) => Loc.F("{0} to apply", toApply),
            (0, _) => Loc.F("{0} to undo", toUndo),
            _ => Loc.F("{0} to apply, {1} to undo", toApply, toUndo),
        };
        ReviewDetail.Text = pending.Any(row => row.NeedsElevationForChange)
            ? Loc.T("Nothing has changed yet. Windows will ask for administrator permission once.")
            : Loc.T("Nothing has changed yet. These settings belong to your account: no administrator permission is needed.");
        StopNow.Visibility = pending.Any(row => row is { IsService: true, IsOn: true, IsRunning: true }) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSelectRecommended(object sender, RoutedEventArgs e)
    {
        // Services and settings with consequences need an individual choice, even when recommended.
        foreach (var row in Rows.Where(row => row.CanPrepareRecommended))
        {
            row.IsOn = true;
        }

        UpdateReview();
    }

    private void OnDiscard(object sender, RoutedEventArgs e)
    {
        foreach (var row in Rows.Where(row => row.IsPending))
        {
            row.Discard();
        }

        ApplyFilter();
        UpdateReview();
    }

    private async void OnApply(object sender, RoutedEventArgs e) => await ReviewAsync(ApplyReviewedAsync);

    private async Task ReviewAsync(Func<Task> action)
    {
        if (_busy || _reviewing) return;
        _reviewing = true;
        UpdateReview();
        try { await action(); }
        finally { _reviewing = false; UpdateReview(); }
    }

    private async Task ApplyReviewedAsync()
    {
        if (_busy) return;
        await RefreshAsync();
        var pending = Rows.Where(row => row.IsPending).ToList();
        if (_lastRefreshFailed || pending.Count == 0 || pending.Any(row => row.Parts.Any(part => part.IsPending && !part.IsReadable))) return;
        var review = BuildReview(pending);
        var dialog = new OptimizeReviewWindow(review, Loc.T("Review the observed and requested values. Nothing changes until you confirm. Undo restores the journaled value only when it still matches what WinModes wrote.")) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;
        var apply = pending.Where(row => row.WillApply).ToList();
        var undo = pending.Where(row => row.WillUndo).ToList();
        var groups = new List<(TuneAction, IReadOnlyList<string>)>
        {
            (TuneAction.Untweak, [.. undo.Where(row => !row.IsService).Select(row => row.Selection(apply: false))]),
            (TuneAction.Restore, [.. undo.Where(row => row.IsService).Select(row => row.Id)]),
            (TuneAction.Tweak, [.. apply.Where(row => !row.IsService).Select(row => row.Selection(apply: true))]),
            (TuneAction.Manual, [.. apply.Where(row => row.ServiceTarget == ServiceStartMode.Manual).Select(row => row.Id)]),
            (TuneAction.Disabled, [.. apply.Where(row => row.ServiceTarget == ServiceStartMode.Disabled).Select(row => row.Id)]),
        };
        if (StopNow.IsVisible && StopNow.IsChecked == true)
        {
            groups.Add((TuneAction.Stop, [.. apply.Where(row => row is { IsService: true, IsRunning: true }).Select(row => row.Id)]));
        }

        await RunAsync([.. groups.Where(group => group.Item2.Count > 0)]);
    }

    private static string RestartRequirement(string requirement) => requirement switch
    {
        "restart" => Loc.T("Some saved settings take effect after Windows restarts."),
        "sign-out" => Loc.T("Some saved settings take effect after you sign out and back in."),
        "explorer" => Loc.T("Some saved settings appear in new File Explorer windows."),
        _ => Loc.T("Settings without a restart requirement were saved; their live effects depend on Windows."),
    };

    private List<OptimizeReviewItem> BuildReview(IReadOnlyList<Row> rows)
    {
        var review = new List<OptimizeReviewItem>();
        foreach (var row in rows)
        {
            if (row.IsService)
            {
                var desired = row.IsOn ? row.ServiceTarget?.ToString() ?? Loc.T("Unknown") : row.ServiceUndo;
                var stop = row.IsOn && row.IsRunning && StopNow.IsChecked == true;
                review.Add(new(row.Title, Loc.T(row.IsOn ? "Change start type" : "Restore recorded start type"), row.Id,
                    $"{row.ServiceObserved} → {desired}" + (stop ? "; " + Loc.T("Stop now") : ""), row.ScopeTip,
                    Loc.T("Previous start type is recorded before changing it. Other changes made since may prevent undo."), row.Warning ?? ""));
                continue;
            }
            foreach (var part in row.Parts.Where(part => part.IsPending))
            {
                var observed = _shown!.Observations[row.Id][part.Index];
                var desired = part.IsOn ? observed.Desired : UndoValue(row.Tweak!, part.Index);
                review.Add(new(row.Title, part.Label, row.Tweak!.Parts[part.Index].Target,
                    Loc.F("Observed: {0} → Requested: {1}", Loc.T(observed.Actual), Loc.T(desired)),
                    Loc.T(part.MachineWide ? "Whole PC; administrator permission needed." : "Your account; no administrator permission needed."),
                    Loc.T(part.IsOn ? "Current value is recorded before the write. Undo is conditional on the value remaining unchanged afterwards." : "Restore the recorded value only if it still matches what WinModes wrote."),
                    string.Join(" ", new[] { row.Warning, row.RestartTip }.Where(text => !string.IsNullOrEmpty(text)))));
            }
        }
        return review;
    }

    private string UndoValue(Tweak tweak, int part)
    {
        foreach (var record in _shown!.Records.Where(record => record.Id.Equals(tweak.Id, StringComparison.OrdinalIgnoreCase)))
        {
            if (tweak.Parts[part].Kind == TweakPartKind.Task && record.DisabledTasks.Any(task => tweak.PartOfTask(task) == part)) return "Enabled";
            if (record.Values.FirstOrDefault(value => tweak.PartOf(value) == part) is { } value)
                return !value.Existed ? "Value absent" : $"{value.Previous} ({(value.PreviousKind == TweakValueKind.Number ? "REG_DWORD" : "REG_SZ")})";
        }
        return Loc.T("No recorded value; undo unavailable");
    }

    private async void OnUndoAll(object sender, RoutedEventArgs e) => await ReviewAsync(UndoReviewedAsync);

    private async void OnPolicyRecovery(object sender, RoutedEventArgs e) => await ReviewAsync(async () =>
    {
        await RefreshAsync();
        if (_lastRefreshFailed || _shown is null) return;
        var window = new PolicyRecoveryWindow(_shown) { Owner = Window.GetWindow(this) };
        if (window.ShowDialog() != true || window.Request is not { } request
            || !TweakSelection.TryParse(request.Target, out var selection) || selection.Parts is null
            || ServiceTuning.Catalog.Find(selection.Id) is not { } tweak) return;
        var index = selection.Parts.Single();
        var observation = _shown.Observations[tweak.Id][index];
        var release = request.Action == TuneAction.ReleasePolicy;
        var review = new OptimizeReviewWindow([new(Loc.T(tweak.Title), Loc.T(release ? "Remove policy constraint" : "Restore recorded policy"),
            tweak.Parts[index].Target, Loc.F("Observed: {0} → Requested: {1}", Loc.T(observation.Actual),
                release ? Loc.T("Value absent") : UndoValue(tweak, index)),
            Loc.T(tweak.Parts[index].MachineWide ? "Whole PC; administrator permission needed." : "Your account; no administrator permission needed."),
            Loc.T(release ? "The current value is recorded first. Undo restores it only while the value remains absent."
                : "Restore the recorded value only if it still matches what WinModes wrote."),
            Loc.T("Other policies, edition limits or removed components may still restrict the feature. This does not recover the original pre-script configuration."))],
            Loc.T("Review one policy change")) { Owner = Window.GetWindow(this) };
        if (review.ShowDialog() == true) await RunAsync([(request.Action, new[] { request.Target })]);
    });

    private async Task UndoReviewedAsync()
    {
        if (_busy) return;
        await RefreshAsync();
        if (_lastRefreshFailed || _shown is null) return;
        var changes = new List<OptimizeReviewItem>();
        foreach (var record in _shown.Records)
        {
            var tweak = ServiceTuning.Catalog.Find(record.Id);
            var title = tweak is null ? record.Id : Loc.T(tweak.Title);
            foreach (var value in record.Values)
            {
                var index = tweak?.PartOf(value);
                var observed = index is { } part ? _shown.Observations[record.Id][part].Actual : Loc.T("Not in the current catalog; checked by the undo engine");
                var desired = !value.Existed ? "Value absent" : $"{value.Previous} ({(value.PreviousKind == TweakValueKind.Number ? "REG_DWORD" : "REG_SZ")})";
                changes.Add(new(title, value.Name, $"{value.Hive}\\{value.Path}\\{value.Name}", Loc.F("Observed: {0} → Requested: {1}", Loc.T(observed), Loc.T(desired)),
                    Loc.T(value.Hive == TweakHive.Machine ? "Whole PC; administrator permission needed." : "Your account; no administrator permission needed."),
                    Loc.T("Restore the recorded value only if it still matches what WinModes wrote."), tweak?.Warning is null ? "" : Loc.T(tweak.Warning)));
            }
            foreach (var task in record.DisabledTasks)
                changes.Add(new(title, task, task, Loc.F("Observed: {0} → Requested: {1}",
                    tweak?.PartOfTask(task) is { } part ? Loc.T(_shown.Observations[record.Id][part].Actual) : Loc.T("Not in the current catalog; checked by the undo engine"), Loc.T("Enabled")),
                    Loc.T("Whole PC; administrator permission needed."), Loc.T("Restore the recorded value only if it still matches what WinModes wrote."), ""));
        }
        foreach (var service in _shown.ChangedServices)
        {
            var row = Rows.First(row => row.IsService && row.Id.Equals(service.Service, StringComparison.OrdinalIgnoreCase));
            changes.Add(new(row.Title, Loc.T("Restore recorded start type"), row.Id,
                $"{row.ServiceObserved} → {service.OriginalStartMode}", row.ScopeTip, Loc.T("Restore the recorded value only if it still matches what WinModes wrote."), ""));
        }
        if (changes.Count == 0) return;
        var dialog = new OptimizeReviewWindow(changes, Loc.T("Every setting and service changed from this page or from the Services page goes back to the value recorded before the change. A value that something else has changed since is left as it is.")) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;

        var groups = new List<(TuneAction, IReadOnlyList<string>)>();
        if (_undoable.Count > 0)
        {
            groups.Add((TuneAction.Untweak, [.. _undoable]));
        }

        if (_hasChangedServices)
        {
            // No name: the helper restores every service it has on record.
            groups.Add((TuneAction.Restore, []));
        }

        await RunAsync(groups);
    }

    private async Task RunAsync(List<(TuneAction Action, IReadOnlyList<string> Targets)> groups)
    {
        if (_busy || groups.Count == 0)
        {
            return;
        }

        if (!OperationStatus.TryBegin(Loc.T("Optimize changes"), groups.Sum(group => group.Targets.Count), out var operation))
        {
            _result.Text = Loc.T("Another operation is already running.");
            ApplyFilter();
            return;
        }
        _busy = true;
        IsEnabled = false;
        try
        {
            var report = await ServiceTuning.RunAsync(groups);
            _result.Text = report.Summary;
            _result.Results = [.. report.Results.Select(result => Loc.F("{0}: {1} — {2}", ServiceTuning.Catalog.Find(result.Target) is { } tweak ? Loc.T(tweak.Title) : result.Target,
                Loc.T(result.Outcome switch { TuneOutcome.Done => "Done", TuneOutcome.Skipped => "Not changed", _ => "Failed" }), result.Detail ?? ""))];
            var changed = report.Results.Where(result => result.Outcome == TuneOutcome.Done).Select(result => result.Target).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var restarts = report.Results.Where(result => result.Outcome == TuneOutcome.Done && result.Action != TuneAction.Stop)
                .Select(result => ServiceTuning.Catalog.Find(result.Target)?.Restart ?? "restart").Distinct().ToList();
            _result.Requirements = string.Join(" ", restarts.Select(RestartRequirement));
            OperationStatus.Progress(operation, report.Results.Count, report.Summary);
            OperationStatus.Complete(operation, report.Summary, !report.Succeeded || report.Results.Any(result => result.Outcome == TuneOutcome.Failed));
            ApplyFilter();
            // A refused permission prompt changes nothing: keep the switches so the user can try again.
            if (report.Succeeded)
            {
                await RefreshAsync();
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _result.Text = Loc.F("Operation failed: {0}", ex.Message);
            OperationStatus.Complete(operation, _result.Text, failed: true);
            ApplyFilter();
        }
        finally
        {
            _busy = false;
            IsEnabled = true;
            RefreshButton.IsEnabled = true;
            UpdateReview();
        }
    }

    /// <summary>Picks the template of an item of the flat list by its type.</summary>
    private sealed class ItemTemplates(params (Type Type, DataTemplate Template)[] templates) : DataTemplateSelector
    {
        public override DataTemplate? SelectTemplate(object? item, DependencyObject container) =>
            item is null ? null : templates.FirstOrDefault(entry => entry.Type == item.GetType()).Template;
    }

    private sealed class IntroItem;

    private sealed class EmptyItem;

    private sealed class FooterItem;

    private sealed class ResultCard : INotifyPropertyChanged
    {
        private string _text = "";
        private string _requirements = "";
        private IReadOnlyList<string> _results = [];
        public event PropertyChangedEventHandler? PropertyChanged;
        public string Text { get => _text; set { _text = value; PropertyChanged?.Invoke(this, new(nameof(Text))); } }
        public string Requirements { get => _requirements; set { _requirements = value; PropertyChanged?.Invoke(this, new(nameof(Requirements))); } }
        public IReadOnlyList<string> Results { get => _results; set { _results = value; PropertyChanged?.Invoke(this, new(nameof(Results))); } }
    }

    /// <summary>Where this PC stands: the figures of the card at the top of the list.</summary>
    private sealed class ScoreCard : INotifyPropertyChanged
    {
        private string _value = "…";
        private string _summary = "";
        private bool _canSelectRecommended;
        private bool _canUndoAll;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Value { get => _value; set => Set(ref _value, value); }
        public string Summary { get => _summary; set => Set(ref _summary, value); }
        public bool CanSelectRecommended { get => _canSelectRecommended; set => Set(ref _canSelectRecommended, value); }
        public bool CanUndoAll { get => _canUndoAll; set => Set(ref _canUndoAll, value); }

        private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }
    }

    private sealed class Group(string title, string subtitle, string glyph, Brush color, List<Row> all) : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public string Title { get; } = title;
        public string Subtitle { get; } = subtitle;
        public string Glyph { get; } = glyph;
        public Brush Color { get; } = color;
        public Brush Tint { get; } = Palette.Tint(color);

        private readonly List<Row> _all = all;

        /// <summary>Every row, whatever the filter.</summary>
        public IReadOnlyList<Row> All => _all;

        /// <summary>Rows that pass the current filter.</summary>
        public List<Row> Rows { get; private set; } = [];

        public string Progress => Loc.F("{0} of {1} applied", Rows.Count(row => row.IsApplied), Rows.Count);
        public double Percent => Rows.Count == 0 ? 0 : Rows.Count(row => row.IsApplied) * 100d / Rows.Count;

        public void Show(Func<Row, bool> filter)
        {
            Rows = [.. _all.Where(filter)];
            PropertyChanged?.Invoke(this, new(nameof(Progress)));
            PropertyChanged?.Invoke(this, new(nameof(Percent)));
        }
    }

    /// <summary>One change inside a tweak, with its own tick box: the user can take some of a tweak and leave the rest.</summary>
    private sealed class PartRow : INotifyPropertyChanged
    {
        private readonly Action _changed;
        private bool _isOn;

        public PartRow(TweakPart part, TweakPartObservation observation, bool canUndo, bool hasChoice, Action changed, bool recoveryRecorded)
        {
            Index = part.Index;
            Label = Loc.T(part.Label);
            Detail = part.Setting is null ? Loc.F("Disables the scheduled task {0}", part.Target) : $"{part.Target} = {part.Setting}";
            MachineWide = part.MachineWide;
            IsReadable = observation.CanChange;
            IsAvailable = observation.IsAvailable;
            Observation = Loc.F("Observed: {0}; apply value: {1}", Loc.T(observation.Actual), Loc.T(observation.Desired)) + (observation.Error is null ? "" : "\n" + observation.Error);
            IsApplied = observation.Applied == true;
            _isOn = IsApplied;
            // Applied before WinModes touched it: there is no earlier value on record to put back.
            CanToggle = IsReadable && !recoveryRecorded && (!IsApplied || canUndo);
            ToggleTip = recoveryRecorded ? Loc.T("Use Policies and recovery to undo this recorded removal.")
                : !IsReadable ? observation.Error ?? Loc.T("This task is absent on this PC.") : IsApplied && !canUndo
                ? Loc.T("Already set before WinModes. Open Policies and recovery to check whether a documented reset is available.") : null;
            HasChoice = hasChoice;
            _changed = changed;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public int Index { get; }
        public string Label { get; }
        public string Detail { get; }
        public string Observation { get; }
        public bool IsReadable { get; }
        public bool IsAvailable { get; }
        public bool MachineWide { get; }
        public bool IsApplied { get; set; }
        public bool CanToggle { get; }
        public string? ToggleTip { get; }
        public Visibility BlockedVisibility => CanToggle ? Visibility.Collapsed : Visibility.Visible;
        public bool HasChoice { get; }

        /// <summary>Only a tweak with several changes lets you pick; a single change is just described.</summary>
        public Visibility ChoiceVisibility => HasChoice ? Visibility.Visible : Visibility.Collapsed;

        public bool IsOn
        {
            get => _isOn;
            set
            {
                if (_isOn == value)
                {
                    return;
                }

                _isOn = value;
                foreach (var name in (string[])[nameof(IsOn), nameof(Status), nameof(StatusBrush), nameof(StatusGlyph)])
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
                }

                _changed();
            }
        }

        public bool IsPending => IsOn != IsApplied;

        public string Status => Loc.T(!IsReadable ? IsAvailable ? "Unreadable" : "Unavailable" : IsPending ? IsOn ? "Will be applied" : "Will be undone" : IsApplied ? "Applied" : "Not applied");

        public Brush StatusBrush => IsPending
            ? Palette.Apps
            : !IsReadable && IsAvailable ? Palette.Stop
            : IsApplied ? Palette.Start
            : Application.Current.TryFindResource("TextFillColorSecondaryBrush") as Brush ?? Palette.Neutral;

        public string StatusGlyph => !IsReadable && IsAvailable ? "\uE7BA" : IsPending ? "\uE8FD" : IsApplied ? "\uE73E" : "\uE946";
    }

    private sealed class Row : INotifyPropertyChanged
    {
        private const string ChevronDown = "";
        private const string ChevronUp = "";

        private bool _isOn;
        private bool _isExpanded;
        private bool _isLast;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>The last row of its category closes the card, so it gets the bottom border and the rounded corners.</summary>
        public bool IsLast
        {
            get => _isLast;
            set
            {
                if (_isLast == value)
                {
                    return;
                }

                _isLast = value;
                foreach (var name in (string[])[nameof(Frame), nameof(Corners)])
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
                }
            }
        }

        public Thickness Frame => _isLast ? new Thickness(1, 0, 1, 1) : new Thickness(1, 0, 1, 0);
        public CornerRadius Corners => _isLast ? new CornerRadius(0, 0, 10, 10) : new CornerRadius(0);

        public required string Id { get; init; }
        public required string Title { get; init; }
        public string Subtitle { get; init; } = "";
        public string Description { get; init; } = "";
        public string? Warning { get; init; }
        public bool IsRecommended { get; init; }
        public bool NeedsCare { get; init; }
        public bool CanPrepareRecommended => Tweak is not null && IsRecommended && !NeedsCare && !IsApplied
            && Parts.All(part => part.IsReadable) && CanToggle;
        public bool IsApplied { get; init; }
        public bool IsPartial { get; init; }
        public bool NeedsElevation { get; init; }
        public bool IsService { get; init; }
        public bool IsRunning { get; init; }
        public string? ToggleTip { get; init; }

        /// <summary>The tweak a row stands for; null for a service.</summary>
        public Tweak? Tweak { get; init; }

        /// <summary>The changes of a tweak that this PC can show (a task it does not have is left out). Empty for a service.</summary>
        public List<PartRow> Parts { get; init; } = [];

        /// <summary>The start type to set when a service row is switched on.</summary>
        public ServiceStartMode? ServiceTarget { get; init; }

        public required string Note { get; init; }
        public required Brush NoteTone { get; init; }
        public string NoteGlyph => IsRecommended ? "\uE73E" : IsService && IsApplied ? "\uE8FD" : NeedsCare ? "\uE7BA" : "\uE946";
        public required string NoteTip { get; init; }
        public string Tools { get; init; } = "";
        public string ToolsTip { get; init; } = "";
        public string Change { get; init; } = "";
        public string Restart { get; init; } = "";
        public string RestartTip { get; init; } = "";

        /// <summary>The switch can be used when at least one change can still be put back (or has not been made).</summary>
        public bool CanToggle => Parts.Count == 0 ? ServiceReadable : Parts.Any(part => part.CanToggle);
        public bool PolicyPresent { get; init; }
        public Visibility PolicyVisibility => PolicyPresent ? Visibility.Visible : Visibility.Collapsed;
        public bool ServiceReadable { get; init; } = true;
        public string ServiceObserved { get; init; } = "";
        public string ServiceUndo { get; init; } = "";

        /// <summary>What the user wants; differs from <see cref="IsApplied"/> while a change is pending. For a tweak, the switch is on when every change is.</summary>
        public bool IsOn
        {
            get => Parts.Count == 0 ? _isOn : Parts.Any(part => part.IsAvailable) && Parts.Where(part => part.IsAvailable).All(part => part.IsOn);
            set
            {
                if (Parts.Count > 0)
                {
                    // Each part tells the row through the callback it was built with.
                    foreach (var part in Parts.Where(part => part.CanToggle))
                    {
                        part.IsOn = value;
                    }

                    return;
                }

                if (_isOn == value)
                {
                    return;
                }

                _isOn = value;
                Notify();
            }
        }

        public bool IsPending => Parts.Count == 0 ? IsOn != IsApplied : Parts.Any(part => part.IsPending);

        public bool WillApply => Parts.Count == 0 ? IsOn && !IsApplied : Parts.Any(part => part.IsOn && !part.IsApplied);

        public bool WillUndo => Parts.Count == 0 ? !IsOn && IsApplied : Parts.Any(part => !part.IsOn && part.IsApplied);

        /// <summary>True when what is pending reaches a machine-wide setting, so Windows asks for administrator permission.</summary>
        public bool NeedsElevationForChange => Parts.Count == 0 ? NeedsElevation : Parts.Any(part => part.IsPending && part.MachineWide);

        /// <summary>"id" for the whole tweak, or "id#parts" for the parts to apply (or to undo): what the engines are given.</summary>
        public string Selection(bool apply) => Tweak is null
            ? Id
            : TweakSelection.Format(Tweak, Parts.Where(part => apply ? part.IsOn && !part.IsApplied : !part.IsOn && part.IsApplied).Select(part => part.Index));

        public void Discard()
        {
            if (Parts.Count == 0)
            {
                IsOn = IsApplied;
                return;
            }

            foreach (var part in Parts)
            {
                part.IsOn = part.IsApplied;
            }
        }

        public string Status => Loc.T(
            IsPending
                ? WillApply && WillUndo ? "Will be changed" : WillApply ? "Will be applied" : "Will be undone"
                : !ServiceReadable ? "Unavailable or unreadable" : Parts.Count > 0 && Parts.All(part => !part.IsAvailable) ? "Unavailable" : Parts.Any(part => part.IsAvailable && !part.IsReadable) ? "Unreadable" : IsApplied ? "Applied" : IsPartial ? "Partly applied" : "Not applied");

        public Brush StatusBrush => IsPending
            ? Palette.Apps
            : !ServiceReadable || Parts.Any(part => part.IsAvailable && !part.IsReadable) ? Palette.Stop
            : IsApplied ? Palette.Start
            : IsPartial ? Palette.Power
            : Application.Current.TryFindResource("TextFillColorSecondaryBrush") as Brush ?? Palette.Neutral;

        public string StatusGlyph => IsPending ? "\uE8FD" : !ServiceReadable || Parts.Any(part => part.IsAvailable && !part.IsReadable) ? "\uE7BA"
            : IsApplied ? "\uE73E" : IsPartial ? "\uE7BA" : "\uE946";

        public string SelectionHelp => ToggleTip ?? Loc.T("Prepares a change only. Review and confirm before Windows is changed.");

        public string ScopeGlyph => NeedsElevation ? AdminGlyph : AccountGlyph;
        public string ScopeLabel => Loc.T(NeedsElevation ? "Administrator required" : "Your account");

        public string ScopeTip => Loc.T(NeedsElevation
            ? "Applies to the whole PC. Windows asks for administrator permission."
            : "Applies to your account only. No administrator permission needed.");

        public Visibility WarningVisibility => Visible(!string.IsNullOrEmpty(Warning));
        public Visibility CautionVisibility => Visible(NeedsCare && IsRecommended);
        public Visibility ToolsVisibility => Visible(Tools.Length > 0);
        public Visibility ChangeVisibility => Visible(Change.Length > 0);
        public Visibility RestartVisibility => Visible(Restart.Length > 0);

        /// <summary>A tweak can be opened to see, and choose among, the changes it makes.</summary>
        public Visibility ExpanderVisibility => Visible(Parts.Count > 0);

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value)
                {
                    return;
                }

                _isExpanded = value;
                foreach (var name in (string[])[nameof(IsExpanded), nameof(Details), nameof(DetailsVisibility), nameof(ChevronGlyph), nameof(ExpanderTip)])
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
                }
            }
        }

        /// <summary>The row itself while it is open, null while it is closed, so the list of changes is only built when someone looks at it.</summary>
        public Row? Details => _isExpanded ? this : null;

        /// <summary>A template with no content is still drawn, so the closed state is hidden as well.</summary>
        public Visibility DetailsVisibility => Visible(_isExpanded);

        public string ChevronGlyph => _isExpanded ? ChevronUp : ChevronDown;

        public string ExpanderTip => Loc.T(_isExpanded ? "Hide the details" : "Show what it changes");

        public string ChoiceHint => Parts.Count > 1
            ? Loc.T("Choose which of these changes to make. Unticking one that is applied puts its earlier value back.")
            : "";

        public Visibility ChoiceHintVisibility => Visible(Parts.Count > 1);

        /// <summary>Tells the page that the switch, the status or the pending state of the row changed.</summary>
        public void Notify()
        {
            foreach (var name in (string[])[nameof(IsOn), nameof(Status), nameof(StatusBrush), nameof(StatusGlyph)])
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }

        public static Row For(Tweak tweak, IReadOnlyList<TweakPartObservation> observations, HashSet<int> journaledParts, Action changed, HashSet<int> recoveries)
        {
            var (restart, restartTip) = tweak.Restart switch
            {
                "sign-out" => (Loc.T("Shows after sign-out"), Loc.T("Visible after you sign out and back in.")),
                "explorer" => (Loc.T("Shows in new Explorer windows"), Loc.T("Visible in File Explorer windows opened afterwards.")),
                "restart" => (Loc.T("Shows after a restart"), Loc.T("Visible after Windows restarts.")),
                _ => ("", ""),
            };
            var medium = tweak.Risk != "low";
            var shown = tweak.Parts.ToList();
            Row? row = null;
            var parts = shown.Select(part => new PartRow(part, observations[part.Index], journaledParts.Contains(part.Index), shown.Count > 1, () =>
            {
                row?.Notify();
                changed();
            }, recoveries.Contains(part.Index))).ToList();
            var applicable = parts.Where(part => part.IsAvailable).ToList();
            var applied = applicable.Count > 0 && applicable.All(part => part.IsApplied);

            row = new Row
            {
                Id = tweak.Id,
                Title = Loc.T(tweak.Title),
                Description = Loc.T(tweak.Description),
                Warning = string.IsNullOrEmpty(tweak.Warning) ? tweak.Warning : Loc.T(tweak.Warning),
                IsRecommended = tweak.Recommended,
                IsApplied = applied,
                IsPartial = !applied && parts.Any(part => part.IsApplied),
                NeedsElevation = tweak.NeedsElevation,
                Tweak = tweak,
                PolicyPresent = tweak.Values.Select((value, index) => PolicyRecovery.IsPolicy(value) && observations[index].RegistryValuePresent).Any(present => present),
                Parts = parts,
                // Every change was made before WinModes touched it: there is no earlier value to put back.
                ToggleTip = parts.All(part => !part.CanToggle) ? parts[0].ToggleTip : null,
                NeedsCare = medium || !string.IsNullOrEmpty(tweak.Warning),
                Note = Loc.T(tweak.Recommended ? "Recommended" : medium || !string.IsNullOrEmpty(tweak.Warning) ? "Check first" : "Optional"),
                NoteTone = tweak.Recommended ? Palette.Start : medium || !string.IsNullOrEmpty(tweak.Warning) ? Palette.Power : Palette.Container,
                NoteTip = Loc.T(tweak.Recommended
                    ? "Low risk and shipped by several open-source optimizers whose code was read."
                    : tweak.Tools.Count > 0
                        ? "A matter of preference or with a side effect: read the note before applying it."
                        : "Well-known setting, but not part of the code-verified survey: optional."),
                Tools = tweak.Tools.Count > 0 ? Loc.N(tweak.Tools.Count, "1 optimizer", "{0} optimizers") : "",
                ToolsTip = Loc.F("Also set by: {0}.", string.Join(", ", tweak.Tools)),
                Restart = restart,
                RestartTip = restartTip,
            };
            return row;
        }

        public static Row For(ServiceRecommendation recommendation)
        {
            var target = recommendation.Advice.Recommended ?? ServiceStartMode.Manual;
            return new Row
            {
                Id = recommendation.Service.Name,
                Title = recommendation.Service.DisplayName,
                Subtitle = recommendation.Service.Name,
                Description = recommendation.Advice.Description,
                IsRecommended = recommendation.IsConfident,
                NeedsCare = true,
                NeedsElevation = true,
                IsService = true,
                IsRunning = recommendation.Service.IsRunning,
                ServiceTarget = target,
                ServiceObserved = $"{recommendation.Service.StartMode}, " + Loc.T(recommendation.Service.IsRunning ? "Running" : "Stopped"),
                Note = Loc.T(recommendation.IsConfident ? "Recommended" : "Check first"),
                NoteTone = recommendation.IsConfident ? Palette.Start : Palette.Power,
                NoteTip = Loc.T(recommendation.IsConfident
                    ? "Backed by a published source and rated low risk."
                    : recommendation.Advice.IsSourced
                        ? "Rated medium risk: switch it on only if you do not use this feature."
                        : "Identified on a PC but not backed by a published source yet: switch it on only if you know you do not need it."),
                Change = $"{recommendation.Service.StartMode} → {target}",
                Restart = Loc.T(recommendation.Service.IsRunning ? "Running now" : "Not running"),
                RestartTip = Loc.T("Changing the start type does not stop the service: it applies the next time Windows starts."),
            };
        }

        /// <summary>A service whose start type was already changed: switching it off restores the original.</summary>
        public static Row For(ServiceTweak tweak, ServiceInfo? service, ServiceAdvice? advice) => new()
        {
            Id = tweak.Service,
            Title = service?.DisplayName ?? tweak.Service,
            Subtitle = tweak.Service,
            Description = advice?.Description ?? "",
            IsApplied = true,
            _isOn = true,
            NeedsElevation = true,
            IsService = true,
            NeedsCare = true,
            IsRunning = service?.IsRunning ?? false,
            ServiceReadable = service is not null,
            ServiceObserved = service is null ? Loc.T("Unavailable or unreadable") : $"{service.StartMode}, " + Loc.T(service.IsRunning ? "Running" : "Stopped"),
            ServiceUndo = tweak.OriginalStartMode.ToString(),
            Note = Loc.T("Changed by you"),
            NoteTone = Palette.Neutral,
            NoteTip = Loc.F("Changed {0:g}. Switch it off to restore {1}.", tweak.ChangedUtc.ToLocalTime(), tweak.OriginalStartMode),
            Change = $"{tweak.OriginalStartMode} → {service?.StartMode ?? tweak.SetTo}",
        };

        private static Visibility Visible(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
    }
}
