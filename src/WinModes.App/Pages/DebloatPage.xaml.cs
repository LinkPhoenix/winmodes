using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinModes.App.Controls;
using WinModes.App.Services;
using WinModes.Core;
using WinModes.Core.Apps;

namespace WinModes.App.Pages;

/// <summary>
/// Removes preinstalled apps for the current user, one by one or in a group, after a confirmation that lists what each one is for
/// and what stops working without it. Everything removed is listed below and can be restored.
/// </summary>
public partial class DebloatPage : Page
{
    private const string AllGlyph = "";

    /// <summary>The category of the apps that are on the PC but not in the list of WinModes: shown, never removed.</summary>
    private const string OthersCategory = "Your other apps";

    /// <summary>Glyph and colour of each category of data/apps.json, in the order of the category bar.</summary>
    private static readonly (string Category, string Glyph, Brush Color)[] Categories =
    [
        ("Search and news", "", Palette.Container),
        ("Microsoft 365", "", Palette.Apps),
        ("Communication", "", Palette.Start),
        ("Utilities", "", Palette.Neutral),
        ("Media", "", Palette.Stop),
        ("Games", "", Palette.Power),
        ("AI", "", Palette.Container),
        ("Developer", "", Palette.Start),
        ("Other", "", Palette.Neutral),
        (OthersCategory, "", Palette.Neutral),
    ];

    private List<AppRow> _rows = [];
    private List<AppRow> _others = [];
    private List<CategoryChip> _chips = [];
    private OneDriveState? _oneDrive;
    private string? _selectedCategory;
    private bool _busy;
    private bool _reading;
    private bool _inventoryFresh;
    private List<AppRow> _visibleRows = [];

    public DebloatPage()
    {
        InitializeComponent();
        ViewMode.Mode = PageViewModeStore.Load("debloat", PageViewMode.Cards);
        Loaded += async (_, _) => await ReloadAsync();
    }


    private static (string Glyph, Brush Color) LookUp(string category) =>
        Categories.FirstOrDefault(item => item.Category.Equals(category, StringComparison.OrdinalIgnoreCase)) is { Glyph: not null } found
            ? (found.Glyph, found.Color)
            : ("", Palette.Neutral);

    /// <summary>The reading the list on screen was built from.</summary>
    private DebloatSnapshot? _shown;

    /// <summary>False while <see cref="_oneDrive"/> is a reading kept from an earlier visit, which may be out of date.</summary>
    private bool _oneDriveFresh;

    /// <summary>
    /// Shows the apps of this PC. The page is kept between visits, so the last reading is shown at once and a new one replaces it only if
    /// something changed. Selection is preserved by package identity when the inventory is rebuilt.
    /// </summary>
    private async Task ReloadAsync(bool force = false)
    {
        if (_reading || _busy)
        {
            return;
        }

        _reading = true;
        _inventoryFresh = false;
        RefreshButton.IsEnabled = false;
        UpdateRemoveButton();
        try
        {

            if (_shown is null)
            {
                Headline.Text = Loc.T("Reading the apps of this PC…");
                SubHeadline.Text = "";
                if (DebloatSnapshot.Last is { } cached)
                {
                    Show(cached);
                }

                if (OneDriveService.Last is { } cachedDrive)
                {
                    _oneDrive = cachedDrive;
                    ShowOneDrive();
                }
            }

            var snapshot = await DebloatSnapshot.TakeAsync();
            _inventoryFresh = snapshot.InventorySucceeded;
            if (force || _shown is null || !snapshot.SameAs(_shown) || !snapshot.InventorySucceeded || !_shown.InventorySucceeded)
            {
                Show(snapshot);
            }

            _shown = snapshot;
            ReadStatus.Text = snapshot.InventorySucceeded
                ? Loc.F("Checked at {0} · Windows AppX · current account", snapshot.CheckedUtc!.Value.ToLocalTime().ToString("t", System.Globalization.CultureInfo.CurrentCulture))
                : snapshot.CheckedUtc is { } last
                    ? Loc.F("Refresh failed · showing the reading from {0:g}", last.ToLocalTime())
                    : Loc.T("Installation status unavailable · retry the reading");
            ReadWarning.IsOpen = !snapshot.InventorySucceeded || !snapshot.NamesSucceeded;
            ReadWarning.Message = Loc.T(!snapshot.InventorySucceeded
                ? "Windows could not provide a complete app inventory. Removal is paused. Refresh to try again."
                : "The package inventory is current, but some Start menu names could not be refreshed.");
            UpdateRemoveButton();

            // Counting the files that exist only online can take seconds: it comes after the list, which does not wait for it.
            _oneDriveFresh = false;
            _oneDrive = await OneDriveService.InspectAsync();
            _oneDriveFresh = true;
            ShowOneDrive();
        }
        finally
        {
            _reading = false;
            RefreshButton.IsEnabled = true;
            UpdateRemoveButton();
        }
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await ReloadAsync(force: true);

    private async void OnHelp(object sender, RoutedEventArgs e)
    {
        var help = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("Reading app badges"),
            Content = Loc.T("Installed describes the last complete Windows AppX inventory for your account. Safe to remove and Check first describe consequences, not whether you use an app. Protected apps are never removed by WinModes.\n\nRecovery badges show whether a separate Store or winget route is recorded. Local restore depends on the original files remaining on this PC. Recovery unverified does not mean permanent removal.\n\nSelect visible and Select safe results affect only the current results. Selections remain when you change filters; the review includes every selected package. Nothing is removed before you confirm."),
            CloseButtonText = Loc.T("Close"),
        };
        await help.ShowDialogAsync();
    }

    private void Show(DebloatSnapshot snapshot)
    {
        _shown = snapshot;
        var installed = snapshot.Installed;
        var startNames = snapshot.StartNames;
        var open = _rows.Where(row => row.IsExpanded).Select(row => row.Entry.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = _rows.SelectMany(row => row.SelectedPackages).Select(package => package.Package.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var byEntry = installed
            .Select(package => (Package: package, Entry: AppxService.Catalog.Find(package)))
            .Where(pair => pair.Entry is not null)
            .GroupBy(pair => pair.Entry!.Id)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.Package).ToList(), StringComparer.OrdinalIgnoreCase);

        // What can be removed first, then what the list covers but this PC does not have.
        _rows = [.. AppxService.Catalog.Entries
            .Select(entry => new AppRow(entry, byEntry.GetValueOrDefault(entry.Id) ?? [], UpdateRemoveButton,
                inventoryKnown: snapshot.CheckedUtc is not null, localRestorePackages: snapshot.LocalRestorePackages))
            .OrderByDescending(row => row.IsInstalled)
            .ThenBy(row => row.Entry.Tier)
            .ThenBy(row => row.Entry.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase)];

        // The apps of the Start menu that the list does not cover, so everything on the PC can be seen (and is left alone).
        var listed = byEntry.Values.SelectMany(packages => packages.Select(package => package.FullName)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _others = [.. installed
            .Where(package => !package.IsFramework && !listed.Contains(package.FullName) && startNames.ContainsKey(package.Family))
            .GroupBy(package => package.Family, StringComparer.OrdinalIgnoreCase)
            .Select(group => new AppRow(
                new AppEntry { Id = $"other:{group.Key}", Title = startNames[group.Key], Packages = [group.First().Name], Category = OthersCategory },
                [.. group], UpdateRemoveButton, isOther: true, isProtected: group.Any(AppGuard.IsProtected)))
            .OrderBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase)];
        foreach (var row in _rows.Concat(_others).Where(row => open.Contains(row.Entry.Id)))
        {
            row.IsExpanded = true;
        }

        foreach (var package in _rows.SelectMany(row => row.Packages).Where(package => selected.Contains(package.Package.FullName)))
        {
            package.IsSelected = true;
        }

        var present = _rows.Count(row => row.IsInstalled);
        var safe = _rows.Count(row => row is { IsInstalled: true, Entry.Tier: AppTier.Safe });
        Headline.Text = snapshot.CheckedUtc is null ? Loc.T("Installation status unavailable")
            : present == 0 ? Loc.T("Nothing to remove") : Loc.N(present, "1 app can be removed", "{0} apps can be removed");
        SubHeadline.Text = snapshot.CheckedUtc is null ? Loc.T("Refresh to read the apps installed for your account.") : present == 0
            ? Loc.T("None of the apps in the list of WinModes is installed for your account.")
            : Loc.F("{0} low-risk · {1} need review · protected apps stay untouched", safe, present - safe);
        ShowRows();
        ShowRemoved();
        _ = LoadIconsAsync([.. _rows, .. _others]);
    }

    /// <summary>
    /// Reads the logos off the UI thread and shows them as they come; until then a glyph stands in. An app takes a logo only when it stands
    /// for a single package: a group such as the promoted games would otherwise wear the logo of one of them.
    /// </summary>
    private static async Task LoadIconsAsync(IReadOnlyList<AppRow> rows)
    {
        var logos = await Task.Run(() => rows
            .SelectMany(row => row.Packages.Select(package => (Row: row, Package: package)))
            .AsParallel()
            .Select(item => (item.Row, item.Package, Path: PackageIcons.FindLogo(item.Package.Package.InstallLocation)))
            .Where(item => item.Path is not null)
            .ToList());
        await IconCache.PreloadAsync(logos.Select(item => item.Path));
        foreach (var (row, package, path) in logos)
        {
            package.Icon = IconCache.Peek(path);
            if (row.Entry.Packages.Count == 1 && row.Packages.Count == 1)
            {
                row.Icon = package.Icon;
            }
        }
    }

    private void ShowOneDrive()
    {
        var state = _oneDrive;
        if (state is null)
        {
            return;
        }

        var canRestore = !state.Installed && (OneDriveService.WasRemovedByWinModes || OneDriveService.SetupProgram is not null);
        OneDriveCard.Visibility = state.Installed || canRestore ? Visibility.Visible : Visibility.Collapsed;
        OneDriveIcon.Source = IconCache.Get(OneDriveService.IconPath);
        OneDriveIcon.Visibility = OneDriveIcon.Source is null ? Visibility.Collapsed : Visibility.Visible;
        OneDriveRemove.Visibility = state.Installed ? Visibility.Visible : Visibility.Collapsed;
        OneDriveRemove.IsEnabled = !_busy && state.CanUninstall;
        OneDriveRestore.Visibility = canRestore ? Visibility.Visible : Visibility.Collapsed;
        OneDriveRestore.IsEnabled = !_busy;

        if (!state.Installed)
        {
            OneDriveText.Text = Loc.T("OneDrive is not installed for your account.");
            return;
        }

        var lines = new List<string>
        {
            state.SignedIn ? Loc.T("An account is signed in.") : Loc.T("No account is signed in."),
        };
        if (state.RedirectedFolders.Count > 0)
        {
            lines.Add(Loc.F("Your {0} folder is inside OneDrive. Move it back out of OneDrive first: uninstalling is refused until then.", string.Join(", ", state.RedirectedFolders)));
        }

        if (state.OnlineOnlyFiles > 0)
        {
            lines.Add(Loc.F(state.CountWasCut ? "At least {0} files exist only online and would no longer open." : "{0} files exist only online and would no longer open.", state.OnlineOnlyFiles));
        }

        lines.Add(Loc.T("Uninstalling keeps every file that is on this PC. It does not touch the setup program of Windows, so you can install OneDrive again."));
        OneDriveText.Text = string.Join("\n", lines);
    }

    private async void OnOneDriveRemove(object sender, RoutedEventArgs e)
    {
        var current = _oneDrive;
        if (_busy || current is not { CanUninstall: true })
        {
            return;
        }

        // A reading kept from an earlier visit may be out of date, and the warning below depends on it.
        var state = _oneDriveFresh ? current : await OneDriveService.InspectAsync();
        if (!state.CanUninstall)
        {
            _oneDrive = state;
            ShowOneDrive();
            return;
        }

        var warning = Loc.T("OneDrive will be uninstalled for your account. The files that are on this PC stay.");
        if (state.NeedsConfirmation)
        {
            warning += "\n\n" + (state.SignedIn ? Loc.T("An account is signed in: check that the OneDrive icon says it is up to date, or files not yet uploaded stay only on this PC.") : "")
                + (state.OnlineOnlyFiles > 0 ? "\n" + Loc.F("{0} files exist only online and would no longer open.", state.OnlineOnlyFiles) : "");
        }

        var confirm = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("Uninstall OneDrive?"),
            Content = warning,
            PrimaryButtonText = Loc.T("Uninstall"),
            CloseButtonText = Loc.T("Cancel"),
        };
        if (await confirm.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            return;
        }

        await RunTrackedAsync(Loc.T("Uninstalling OneDrive…"), 1, async operation =>
        {
            var reason = await OneDriveService.UninstallAsync();
            OperationStatus.Progress(operation, 1, reason ?? Loc.T("OneDrive was uninstalled."));
            return (reason ?? Loc.T("OneDrive was uninstalled."), reason is not null);
        });
        _oneDrive = await OneDriveService.InspectAsync();
        ShowOneDrive();
    }

    private async void OnOneDriveRestore(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        await RunTrackedAsync(Loc.T("Installing OneDrive again…"), 1, async operation =>
        {
            var reason = await OneDriveService.RestoreAsync();
            OperationStatus.Progress(operation, 1, reason ?? Loc.T("OneDrive is installed again. Sign in to use it."));
            return (reason ?? Loc.T("OneDrive is installed again. Sign in to use it."), reason is not null);
        });
        _oneDrive = await OneDriveService.InspectAsync();
        ShowOneDrive();
    }

    private void ShowRows()
    {
        // Filter events fire while the page is still being built.
        if (RowsList is null || SearchBox is null || ShowAbsent is null || StatusFilter is null || SortOrder is null || VisibleCount is null)
        {
            return;
        }

        var terms = SearchMatcher.Terms(SearchBox.Text);
        var text = SearchBox.Text.Trim();
        var showAbsent = ShowAbsent.IsChecked == true || StatusFilter.SelectedIndex == 2;
        var matching = _rows.Where(row => (row.IsInstalled || !row.InventoryKnown || showAbsent) && row.Matches(terms) && MatchesStatus(row)).ToList();
        var matchingOthers = _others.Where(row => row.Matches(terms) && MatchesStatus(row)).ToList();

        // The selected category can vanish after a removal (nothing of it is left on this PC).
        if (_selectedCategory is not null && matching.All(row => row.Entry.Category != _selectedCategory) && _rows.All(row => row.Entry.Category != _selectedCategory)
            && !(_selectedCategory == OthersCategory && _others.Count > 0))
        {
            _selectedCategory = null;
        }

        // The other apps are listed under their own chip, and under "All" only when something is searched for.
        IEnumerable<AppRow> shown = _selectedCategory switch
        {
            null => text.Length > 0 || StatusFilter.SelectedIndex == 5 ? [.. matching, .. matchingOthers] : matching,
            OthersCategory => matchingOthers,
            _ => matching.Where(row => row.Entry.Category == _selectedCategory),
        };
        var shownRows = (SortOrder.SelectedIndex switch
        {
            1 => shown.OrderBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase),
            2 => shown.OrderByDescending(row => row.Title, StringComparer.CurrentCultureIgnoreCase),
            3 => shown.OrderBy(row => Loc.T(row.Entry.Category), StringComparer.CurrentCultureIgnoreCase).ThenBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase),
            _ => shown.OrderByDescending(row => row.IsInstalled).ThenBy(row => row.Entry.Tier).ThenBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase),
        }).ToList();
        VisibleCount.Text = Loc.F("{0} shown", shownRows.Count);
        _visibleRows = shownRows;
        RowsList.ItemsSource = shownRows;
        RowsCards.ItemsSource = shownRows;
        RowsCompact.ItemsSource = shownRows;
        RowsList.Visibility = ViewMode.Mode == PageViewMode.List ? Visibility.Visible : Visibility.Collapsed;
        RowsCards.Visibility = ViewMode.Mode == PageViewMode.Cards ? Visibility.Visible : Visibility.Collapsed;
        RowsCompact.Visibility = ViewMode.Mode == PageViewMode.Compact ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = shownRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Hint = terms.Length > 0 || showAbsent
            ? Loc.T("Try another search or clear filters.")
            : Loc.T("Also show what is not installed") + ".";
        SelectSafe.IsEnabled = !_busy && _inventoryFresh && shownRows.Any(row => row is { IsInstalled: true, IsOther: false, Entry.Tier: AppTier.Safe });
        SelectVisible.IsEnabled = !_busy && _inventoryFresh && shownRows.Any(row => row is { IsInstalled: true, IsOther: false });
        ShowCategories(matching, matchingOthers);
        UpdateRemoveButton();
    }

    /// <summary>One chip per category of the list plus "All"; the numbers follow the search, so they tell where the matches are.</summary>
    private void ShowCategories(List<AppRow> matching, List<AppRow> matchingOthers)
    {
        var known = Categories.Select(item => item.Category).ToList();

        // A category with nothing to show on this PC gets no chip, whatever the search says.
        var showAbsent = ShowAbsent.IsChecked == true || StatusFilter.SelectedIndex == 2;
        var present = _rows.Where(row => row.IsInstalled || !row.InventoryKnown || showAbsent).Select(row => row.Entry.Category).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => known.FindIndex(name => name.Equals(category, StringComparison.OrdinalIgnoreCase)) is var index and >= 0 ? index : known.Count)
            .ToList();
        List<(string? Key, string Title, string Glyph, Brush Color, int Count)> entries =
            [(null, Loc.T("All"), AllGlyph, Palette.BrandBrush,
                matching.Count + (SearchBox.Text.Trim().Length > 0 || StatusFilter.SelectedIndex == 5 ? matchingOthers.Count : 0))];
        foreach (var category in present)
        {
            var (glyph, color) = LookUp(category);
            entries.Add((category, Loc.T(category), glyph, color, matching.Count(row => row.Entry.Category == category)));
        }

        if (_others.Count > 0)
        {
            var (glyph, color) = LookUp(OthersCategory);
            entries.Add((OthersCategory, Loc.T("Your other apps"), glyph, color, matchingOthers.Count));
        }

        _chips = CategoryChips.Sync(CategoryBar, _chips, entries, _selectedCategory);
    }

    private void OnCategoryClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CategoryChip chip)
        {
            return;
        }

        _selectedCategory = chip.Key;
        ShowRows();
        ListScroll.ScrollToTop();
    }

    private void OnViewModeChanged(object? sender, EventArgs e)
    {
        PageViewModeStore.Save("debloat", ViewMode.Mode);
        ShowRows();
        ListScroll.ScrollToTop();
    }

    private void ShowRemoved()
    {
        var removed = AppxService.Journal.Load().OrderByDescending(app => app.RemovedUtc).ToList();
        var rows = removed.Select(app => new RemovedRow(app)).ToList();
        Removed.ItemsSource = rows;
        var visible = removed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RemovedTitle.Visibility = visible;
        RemovedCard.Visibility = visible;
        _ = LoadRemovedIconsAsync(rows);
    }

    private static async Task LoadRemovedIconsAsync(IReadOnlyList<RemovedRow> rows)
    {
        var logos = await Task.Run(() => rows.Select(row => (Row: row, Path: PackageIcons.FindLogo(row.App.InstallLocation))).Where(pair => pair.Path is not null).ToList());
        await IconCache.PreloadAsync(logos.Select(pair => pair.Path));
        foreach (var (row, path) in logos)
        {
            row.Icon = IconCache.Peek(path);
        }
    }

    private void UpdateRemoveButton()
    {
        // Rows are built before the page has its controls.
        if (ActionBar is null)
        {
            return;
        }

        var selected = _rows.Count(row => row.HasSelection);
        var packages = _rows.Sum(row => row.SelectedPackages.Count);
        var hidden = _rows.Count(row => row.HasSelection && !_visibleRows.Contains(row));
        var warnings = _rows.Count(row => row.HasSelection && (row.Entry.Tier != AppTier.Safe || !row.HasReinstallRoute));
        ActionBar.Visibility = selected > 0 ? Visibility.Visible : Visibility.Collapsed;
        RemoveButton.IsEnabled = !_busy && !_reading && _inventoryFresh && selected > 0;
        RemoveButton.Content = selected > 0 ? Loc.F("Review removal ({0})", selected) : Loc.T("Review removal");
        ActionTitle.Text = Loc.N(selected, "1 app selected", "{0} apps selected");
        ActionSummary.Text = Loc.F("{0} packages · {1} need attention · {2} hidden by filters", packages, warnings, hidden);
        ShowSelection.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var row in _rows) row.SelectionAllowed = !_busy && !_reading && _inventoryFresh;
        SelectSafe.IsEnabled = !_busy && !_reading && _inventoryFresh && _visibleRows.Any(row => row is { IsInstalled: true, IsOther: false, Entry.Tier: AppTier.Safe });
        SelectVisible.IsEnabled = !_busy && !_reading && _inventoryFresh && _visibleRows.Any(row => row is { IsInstalled: true, IsOther: false });
    }

    private bool MatchesStatus(AppRow row) => StatusFilter.SelectedIndex switch
    {
        1 => row.IsInstalled,
        2 => row.InventoryKnown && !row.IsInstalled,
        3 => row.IsInstalled && !row.IsOther && row.Entry.Tier == AppTier.Safe,
        4 => !row.IsOther && row.Entry.Tier != AppTier.Safe,
        5 => row.IsProtected,
        6 => row.HasSelection,
        _ => true,
    };

    private void OnCatalogFilterChanged(object sender, SelectionChangedEventArgs e) => ShowRows();

    private void OnFilterChanged(object sender, TextChangedEventArgs e) => ShowRows();

    private void OnFilterToggled(object sender, RoutedEventArgs e) => ShowRows();

    private void OnSelectSafe(object sender, RoutedEventArgs e)
    {
        if (_busy || _reading || !_inventoryFresh) return;
        foreach (var row in _visibleRows.Where(row => row is { IsInstalled: true, IsOther: false, Entry.Tier: AppTier.Safe }))
        {
            row.IsSelected = true;
        }

        UpdateRemoveButton();
    }

    private void OnSelectVisible(object sender, RoutedEventArgs e)
    {
        if (_busy || _reading || !_inventoryFresh) return;
        foreach (var row in _visibleRows.Where(row => row is { IsInstalled: true, IsOther: false })) row.IsSelected = true;
        UpdateRemoveButton();
    }

    private void OnShowSelection(object sender, RoutedEventArgs e)
    {
        _selectedCategory = null;
        SearchBox.Clear();
        StatusFilter.SelectedIndex = 6;
        ShowRows();
        ListScroll.ScrollToTop();
    }

    private void OnClearSelection(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        foreach (var row in _rows)
        {
            row.IsSelected = false;
        }

        UpdateRemoveButton();
    }

    private async void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        var chosen = _rows.Where(row => row.HasSelection).ToList();
        if (_busy || _reading || !_inventoryFresh || chosen.Count == 0)
        {
            return;
        }

        var confirm = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.N(chosen.Count, "Remove 1 app?", "Remove {0} apps?"),
            Content = BuildRemovalReview(chosen),
            PrimaryButtonText = Loc.T("Remove"),
            CloseButtonText = Loc.T("Cancel"),
        };
        if (await confirm.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            return;
        }

        var total = chosen.Sum(row => row.SelectedPackages.Count);
        await RunTrackedAsync(Loc.T("Remove apps"), total, async operation =>
        {
            var results = new List<string>();
            var done = 0;
            var completed = 0;
            foreach (var row in chosen)
            {
                foreach (var package in row.SelectedPackages.Select(item => item.Package).ToList())
                {
                    Headline.Text = Loc.F("Removing {0}…", row.Title);
                    if (await AppxService.RemoveAsync(package) is { } reason)
                    {
                        results.Add(Loc.F("Failed: {0} · {1}", package.Name, reason));
                    }
                    else
                    {
                        done++;
                        results.Add(Loc.F("Removed: {0}", package.Name));
                    }
                    OperationStatus.Progress(operation, ++completed, results[^1]);
                }
            }
            var summary = (done > 0 ? Loc.N(done, "1 package removed.", "{0} packages removed.") : Loc.T("Nothing was removed."))
                + "\n" + string.Join("\n", results);
            return (summary, done != total);
        });
        await ReloadAsync(force: true);
    }

    private static ScrollViewer BuildRemovalReview(IReadOnlyList<AppRow> chosen)
    {
        var content = new StackPanel { MaxWidth = 640 };
        content.Children.Add(new TextBlock
        {
            Text = Loc.T("Removal affects your account only. WinModes records each package before removal. Local restore needs the package files to remain on this PC; reinstall routes are shown above. Other users are not changed."),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });
        foreach (var row in chosen)
        {
            var details = new StackPanel();
            var card = new Border { Child = details, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(12), CornerRadius = new CornerRadius(8) };
            card.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
            content.Children.Add(card);
            details.Children.Add(new TextBlock { Text = row.Title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            details.Children.Add(new TextBlock { Text = string.Join(", ", row.SelectedPackages.Select(package => package.Name)),
                FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) });
            var badges = new WrapPanel();
            details.Children.Add(badges);
            badges.Children.Add(new StatusBadge { Text = row.TierText, Glyph = row.TierGlyph, Tone = row.TierTone });
            badges.Children.Add(new StatusBadge { Text = row.RecoveryText, Glyph = row.RecoveryGlyph, Tone = row.RecoveryTone });
            details.Children.Add(new TextBlock { Text = row.Breaks ?? row.Why, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) });
            details.Children.Add(new TextBlock { Text = row.RecoveryTip, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        }

        return new ScrollViewer { Content = content, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    private async void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        if (_busy || (sender as FrameworkElement)?.DataContext is not RemovedRow row)
        {
            return;
        }

        await RunTrackedAsync(Loc.F("Restoring {0}…", row.Title), 1, async operation =>
        {
            var reason = await AppxService.RestoreAsync(row.App);
            OperationStatus.Progress(operation, 1, reason ?? Loc.F("{0} is back.", row.Title));
            return (reason ?? Loc.F("{0} is back.", row.Title), reason is not null);
        });
        await ReloadAsync(force: true);
    }

    private async Task RunTrackedAsync(string title, int total, Func<Guid, Task<(string Summary, bool Failed)>> run)
    {
        ResultCard.Visibility = Visibility.Visible;
        if (!OperationStatus.TryBegin(title, total, out var operation))
        {
            ResultText.Text = Loc.T("Another operation is already running.");
            return;
        }
        _busy = true;
        UpdateRemoveButton();
        ShowOneDrive();
        ResultText.Text = title;
        try
        {
            var result = await run(operation);
            ResultText.Text = result.Summary;
            OperationStatus.Complete(operation, result.Summary, result.Failed);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ResultText.Text = Loc.T("The operation stopped unexpectedly. Refresh the inventory before trying again.");
            OperationStatus.Complete(operation, ResultText.Text, failed: true);
        }
        finally
        {
            if (OperationStatus.Current is { IsRunning: true } current && current.Id == operation)
            {
                OperationStatus.Complete(operation, Loc.T("The operation did not finish."), failed: true);
            }
            _busy = false;
            UpdateRemoveButton();
            ShowOneDrive();
        }
    }

    private void OnToggleDetails(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AppRow row)
        {
            row.IsExpanded = !row.IsExpanded;
        }
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        // The page of installed apps of Windows Settings, where an app is uninstalled the usual way.
        using var started = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true });
    }

    private void OnOpenAppStore(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AppRow { Entry.Reinstall.Store: { } store })
        {
            AppxService.OpenInStore(store);
        }
    }

    private void OnOpenStore(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RemovedRow { App.Reinstall.Store: { } store })
        {
            AppxService.OpenInStore(store);
        }
    }

    private sealed class PackageRow(InstalledPackage package, bool hasChoice, Action changed) : INotifyPropertyChanged
    {
        private bool _isSelected;
        private ImageSource? _icon;

        public event PropertyChangedEventHandler? PropertyChanged;

        public InstalledPackage Package => package;
        public string Name => package.Name;
        public string VersionText => $"v{package.Version}";
        public bool CanSelect
        {
            get;
            set
            {
                if (field == value) return;
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSelect)));
            }
        }

        /// <summary>Only an app made of several packages lets you pick; a single one follows the app.</summary>
        public Visibility ChoiceVisibility => hasChoice ? Visibility.Visible : Visibility.Collapsed;

        public ImageSource? Icon
        {
            get => _icon;
            set
            {
                _icon = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                changed();
            }
        }
    }

    private sealed class AppRow : INotifyPropertyChanged
    {
        private const string ChevronDown = "";
        private const string ChevronUp = "";

        private ImageSource? _icon;
        private bool _isExpanded;

        public AppRow(AppEntry entry, IReadOnlyList<InstalledPackage> packages, Action changed, bool isOther = false, bool isProtected = false,
            bool inventoryKnown = true, IReadOnlySet<string>? localRestorePackages = null)
        {
            Entry = entry;
            IsOther = isOther;
            IsProtected = isProtected;
            InventoryKnown = inventoryKnown;
            HasLocalRestore = packages.Count > 0 && packages.All(package => localRestorePackages?.Contains(package.FullName) == true);
            Packages = [.. packages.Select(package => new PackageRow(package, packages.Count > 1, () =>
            {
                foreach (var name in (string[])[nameof(IsSelected), nameof(SelectionState), nameof(PendingVisibility)])
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
                }

                changed();
            }))];
            (Glyph, Color) = LookUp(entry.Category);
            Tint = Palette.Tint(Color);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public AppEntry Entry { get; }
        public List<PackageRow> Packages { get; }
        public string Glyph { get; }
        public Brush Color { get; }
        public Brush Tint { get; }

        /// <summary>An app of the PC that the list does not cover: shown so that everything can be seen, never offered for removal.</summary>
        public bool IsOther { get; }

        /// <summary>One of the apps WinModes never removes (the Store, winget, the system).</summary>
        public bool IsProtected { get; }

        public bool IsInstalled => Packages.Count > 0;
        public bool InventoryKnown { get; }
        public bool HasLocalRestore { get; }
        public bool HasReinstallRoute => Entry.Reinstall.Store is not null || Entry.Reinstall.Winget is not null;
        public bool SelectionAllowed
        {
            get;
            set
            {
                if (field == value) return;
                field = value;
                foreach (var package in Packages) package.CanSelect = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionAllowed)));
            }
        }
        public string Title => Loc.T(Entry.Title);
        public string Why => Loc.T(Entry.Why);
        public Visibility WhyVisibility => Entry.Why.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        public string? Breaks => string.IsNullOrWhiteSpace(Entry.BreaksIfRemoved) ? null : Loc.F("If removed: {0}", Loc.T(Entry.BreaksIfRemoved));
        public Visibility BreaksVisibility => Breaks is null ? Visibility.Collapsed : Visibility.Visible;
        public string InstallationText => Loc.T(!InventoryKnown ? "Unknown" : IsInstalled ? "Installed" : "Not installed");
        public string InstallationGlyph => !InventoryKnown ? "" : IsInstalled ? "" : "";
        public Brush InstallationTone => !InventoryKnown ? Palette.Power : IsInstalled ? Palette.Start : Palette.Neutral;
        public string InstallationTip => Loc.T(!InventoryKnown ? "Windows has not returned a complete inventory. Refresh to check."
            : IsInstalled ? "Found in the Windows AppX inventory for your account." : "Not found in the last complete Windows AppX inventory for your account.");
        public string TierText => Loc.T(IsOther ? IsProtected ? "Protected" : "Managed in Windows" : Entry.Tier == AppTier.Safe ? "Safe to remove" : "Check first");
        public string TierGlyph => IsProtected ? "" : IsOther ? "" : Entry.Tier == AppTier.Safe ? "" : "";
        public Brush TierTone => IsProtected ? Palette.Apps : IsOther ? Palette.Neutral : Entry.Tier == AppTier.Safe ? Palette.Start : Palette.Power;
        public string RecoveryText => Loc.T(HasReinstallRoute ? HasLocalRestore ? "Restore + reinstall" : "Reinstall available"
            : HasLocalRestore ? "Local restore only" : "Recovery unverified");
        public string RecoveryGlyph => HasReinstallRoute ? "" : "";
        public Brush RecoveryTone => HasReinstallRoute ? Palette.Container : Palette.Power;
        public string RecoveryTip => HasReinstallRoute ? ReinstallText + " " + Loc.T("Local restore also needs the original package files to remain on this PC.")
            : Loc.T(HasLocalRestore ? "The package manifest is on this PC. Restore can use it while those files remain. No separate reinstall route is recorded."
                : "No local package manifest or separate reinstall route has been verified. Recovery is not guaranteed.");
        public Visibility RecoveryVisibility => IsOther ? Visibility.Collapsed : Visibility.Visible;
        public Visibility PendingVisibility => HasSelection ? Visibility.Visible : Visibility.Collapsed;

        public Visibility SelectionVisibility => IsInstalled && !IsOther ? Visibility.Visible : Visibility.Hidden;

        /// <summary>An app this PC does not have is shown faded: it is there to show what the list covers.</summary>
        public double Emphasis => !InventoryKnown || IsInstalled ? 1 : 0.7;

        public ImageSource? Icon
        {
            get => _icon;
            set
            {
                _icon = value;
                foreach (var name in (string[])[nameof(Icon), nameof(GlyphVisibility)])
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
                }
            }
        }

        public Visibility GlyphVisibility => _icon is null ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>True when every package of the app is ticked; ticking the app ticks them all.</summary>
        public bool IsSelected
        {
            get => IsInstalled && Packages.All(package => package.IsSelected);
            set
            {
                foreach (var package in Packages)
                {
                    package.IsSelected = value;
                }
            }
        }

        /// <summary>True when at least one package of the app is ticked, so a removal has something to do for it.</summary>
        public bool HasSelection => Packages.Any(package => package.IsSelected);

        /// <summary>All, some or none of the packages are ticked. A click never lands on "some": it ticks all or none.</summary>
        public bool? SelectionState
        {
            get => IsSelected ? true : HasSelection ? null : false;
            set => IsSelected = value == true;
        }

        public IReadOnlyList<PackageRow> SelectedPackages => [.. Packages.Where(package => package.IsSelected)];

        public IEnumerable<string> PackagePatterns => Entry.Packages;
        public Visibility InstalledVisibility => IsInstalled ? Visibility.Visible : Visibility.Collapsed;
        public Visibility AbsentVisibility => InventoryKnown && !IsInstalled ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ChoiceHintVisibility => Packages.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        public string TierExplanation => Loc.T(IsOther
            ? IsProtected
                ? "Protected: WinModes never removes it, so the apps and tools that depend on it keep working."
                : "WinModes only removes the apps of its own list, so it leaves this one alone. To uninstall it, use Windows Settings."
            : Entry.Tier == AppTier.Safe
                ? "Safe for nearly everyone: nothing else depends on it."
                : "Useful to some people: read what stops working before you remove it.");

        public Visibility SettingsVisibility => IsOther && !IsProtected ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ReinstallVisibility => Entry.Reinstall.Store is null && Entry.Reinstall.Winget is null ? Visibility.Collapsed : Visibility.Visible;
        public Visibility StoreVisibility => Entry.Reinstall.Store is null ? Visibility.Collapsed : Visibility.Visible;

        public string ReinstallText => (Entry.Reinstall.Store, Entry.Reinstall.Winget) switch
        {
            ({ } store, { } winget) => Loc.F("Install it again from the Microsoft Store (product {0}) or with winget ({1}).", store, winget),
            ({ } store, null) => Loc.F("Install it again from the Microsoft Store (product {0}).", store),
            (null, { } winget) => Loc.F("Install it again with winget ({0}).", winget),
            _ => "",
        };

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

        /// <summary>The row itself while it is open, null while it is closed, so the details are only built when someone looks at them.</summary>
        public AppRow? Details => _isExpanded ? this : null;

        /// <summary>A template with no content is still drawn, so the closed state is hidden as well.</summary>
        public Visibility DetailsVisibility => _isExpanded ? Visibility.Visible : Visibility.Collapsed;

        public string ChevronGlyph => _isExpanded ? ChevronUp : ChevronDown;

        public string ExpanderTip => Loc.T(_isExpanded ? "Hide the details" : "Show the details");

        public bool Matches(IReadOnlyList<string> terms) => SearchMatcher.MatchesTerms(
            terms,
            Title,
            Why,
            Loc.T(Entry.Category),
            string.Join(" ", Entry.Packages),
            string.Join(" ", Packages.Select(package => package.Name)));
    }

    private sealed class RemovedRow(RemovedApp app) : INotifyPropertyChanged
    {
        private ImageSource? _icon;

        public event PropertyChangedEventHandler? PropertyChanged;

        public RemovedApp App => app;
        public string Title => Loc.T(app.Title);

        public string Detail => Loc.F("Removed on {0}.", Date(app.RemovedUtc))
            + (CanRestore
                ? " " + Loc.T("Its package manifest is still on this PC. Local restore is available.")
                : " " + Loc.T("Local restore is unavailable. Check the recorded reinstall options."));
        public bool CanRestore { get; } = AppGuard.IsPackageFolder(app.InstallLocation)
            && System.IO.File.Exists(System.IO.Path.Combine(app.InstallLocation, "AppxManifest.xml"));
        public string RestoreTip => Loc.T(CanRestore ? "Register this app again from its local package files." : "Local restore is unavailable because the package manifest could not be found.");

        private static string Date(DateTimeOffset utc) => utc.LocalDateTime.ToString("g", System.Globalization.CultureInfo.CurrentCulture);

        public Visibility StoreVisibility => app.Reinstall.Store is null ? Visibility.Collapsed : Visibility.Visible;

        public ImageSource? Icon
        {
            get => _icon;
            set
            {
                _icon = value;
                foreach (var name in (string[])[nameof(Icon), nameof(GlyphVisibility)])
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
                }
            }
        }

        public Visibility GlyphVisibility => _icon is null ? Visibility.Visible : Visibility.Collapsed;
    }
}
