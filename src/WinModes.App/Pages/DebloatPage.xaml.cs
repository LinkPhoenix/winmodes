using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinModes.App.Services;
using WinModes.Core.Apps;

namespace WinModes.App.Pages;

/// <summary>
/// Removes preinstalled apps for the current user, one by one or in a group, after a confirmation that lists what each one is for
/// and what stops working without it. Everything removed is listed below and can be restored.
/// </summary>
public partial class DebloatPage : Page
{
    private List<AppRow> _rows = [];
    private OneDriveState? _oneDrive;
    private bool _busy;

    public DebloatPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        Headline.Text = Loc.T("Reading the apps of this PC…");
        SubHeadline.Text = "";
        var installed = await AppxService.ListAsync();
        _rows = [.. installed
            .Select(package => (Package: package, Entry: AppxService.Catalog.Find(package)))
            .Where(pair => pair.Entry is not null)
            .GroupBy(pair => pair.Entry!.Id)
            .Select(group => new AppRow(group.Select(pair => pair.Package).ToList(), group.First().Entry!))
            .OrderBy(row => row.Entry.Tier)
            .ThenBy(row => row.Entry.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.Title, StringComparer.CurrentCultureIgnoreCase)];

        var safe = _rows.Count(row => row.Entry.Tier == AppTier.Safe);
        Headline.Text = _rows.Count == 0 ? Loc.T("Nothing to remove") : Loc.N(_rows.Count, "1 app can be removed", "{0} apps can be removed");
        SubHeadline.Text = _rows.Count == 0
            ? ""
            : Loc.N(safe, "1 is safe for nearly everyone; the others are useful to some people, and what they do is written next to each one.",
                "{0} are safe for nearly everyone; the others are useful to some people, and what they do is written next to each one.");
        ShowRows();
        ShowRemoved();
        _oneDrive = await OneDriveService.InspectAsync();
        ShowOneDrive();
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
        if (_busy || _oneDrive is not { CanUninstall: true } state)
        {
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

        _busy = true;
        ShowOneDrive();
        ResultCard.Visibility = Visibility.Visible;
        ResultText.Text = Loc.T("Uninstalling OneDrive…");
        var reason = await OneDriveService.UninstallAsync();
        _busy = false;
        ResultText.Text = reason ?? Loc.T("OneDrive was uninstalled.");
        _oneDrive = await OneDriveService.InspectAsync();
        ShowOneDrive();
    }

    private async void OnOneDriveRestore(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        ShowOneDrive();
        ResultCard.Visibility = Visibility.Visible;
        ResultText.Text = Loc.T("Installing OneDrive again…");
        var reason = await OneDriveService.RestoreAsync();
        _busy = false;
        ResultText.Text = reason ?? Loc.T("OneDrive is installed again. Sign in to use it.");
        _oneDrive = await OneDriveService.InspectAsync();
        ShowOneDrive();
    }

    private void ShowRows()
    {
        var text = SearchBox.Text.Trim();
        var shown = _rows.Where(row => text.Length == 0 || row.Title.Contains(text, StringComparison.CurrentCultureIgnoreCase)
            || row.Entry.Category.Contains(text, StringComparison.CurrentCultureIgnoreCase)).ToList();
        Rows.ItemsSource = shown;
        EmptyText.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SelectSafe.IsEnabled = !_busy && _rows.Any(row => row.Entry.Tier == AppTier.Safe);
        UpdateRemoveButton();
    }

    private void ShowRemoved()
    {
        var removed = AppxService.Journal.Load().OrderByDescending(app => app.RemovedUtc).ToList();
        Removed.ItemsSource = removed.Select(app => new RemovedRow(app)).ToList();
        var visible = removed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RemovedTitle.Visibility = visible;
        RemovedCard.Visibility = visible;
    }

    private void UpdateRemoveButton()
    {
        var selected = _rows.Count(row => row.IsSelected);
        RemoveButton.IsEnabled = !_busy && selected > 0;
        RemoveButton.Content = selected > 0 ? Loc.F("Remove selected ({0})", selected) : Loc.T("Remove selected");
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e) => ShowRows();

    private void OnSelectionChanged(object sender, RoutedEventArgs e) => UpdateRemoveButton();

    private void OnSelectSafe(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
        {
            row.IsSelected = row.Entry.Tier == AppTier.Safe;
        }

        UpdateRemoveButton();
    }

    private async void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        var chosen = _rows.Where(row => row.IsSelected).ToList();
        if (_busy || chosen.Count == 0)
        {
            return;
        }

        var lines = chosen.Select(row => row.Breaks is null ? $"• {row.Title}" : $"• {row.Title}: {row.Breaks}");
        var confirm = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.N(chosen.Count, "Remove 1 app?", "Remove {0} apps?"),
            Content = string.Join("\n", lines) + "\n\n"
                + Loc.T("They are removed for your account only and listed on this page, where Restore brings them back. Nothing is uninstalled for other users."),
            PrimaryButtonText = Loc.T("Remove"),
            CloseButtonText = Loc.T("Cancel"),
        };
        if (await confirm.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            return;
        }

        _busy = true;
        UpdateRemoveButton();
        var failures = new List<string>();
        var done = 0;
        foreach (var row in chosen)
        {
            foreach (var package in row.Packages)
            {
                Headline.Text = Loc.F("Removing {0}…", row.Title);
                if (await AppxService.RemoveAsync(package) is { } reason)
                {
                    failures.Add($"{row.Title}: {reason}");
                }
                else
                {
                    done++;
                }
            }
        }

        _busy = false;
        ResultCard.Visibility = Visibility.Visible;
        ResultText.Text = (done > 0 ? Loc.N(done, "1 package removed.", "{0} packages removed.") : Loc.T("Nothing was removed."))
            + (failures.Count > 0 ? "\n" + string.Join("\n", failures) : "");
        await ReloadAsync();
    }

    private async void OnRestoreClick(object sender, RoutedEventArgs e)
    {
        if (_busy || (sender as FrameworkElement)?.DataContext is not RemovedRow row)
        {
            return;
        }

        _busy = true;
        ResultCard.Visibility = Visibility.Visible;
        ResultText.Text = Loc.F("Restoring {0}…", row.Title);
        var reason = await AppxService.RestoreAsync(row.App);
        _busy = false;
        ResultText.Text = reason ?? Loc.F("{0} is back.", row.Title);
        await ReloadAsync();
    }

    private void OnOpenStore(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RemovedRow { App.Reinstall.Store: { } store })
        {
            AppxService.OpenInStore(store);
        }
    }

    private sealed class AppRow(IReadOnlyList<InstalledPackage> packages, AppEntry entry) : INotifyPropertyChanged
    {
        private bool _isSelected;

        public event PropertyChangedEventHandler? PropertyChanged;

        public IReadOnlyList<InstalledPackage> Packages => packages;
        public AppEntry Entry => entry;
        public string Title => Loc.T(entry.Title);
        public string Why => Loc.T(entry.Why);
        public string? Breaks => string.IsNullOrWhiteSpace(entry.BreaksIfRemoved) ? null : Loc.F("If removed: {0}", Loc.T(entry.BreaksIfRemoved));
        public Visibility BreaksVisibility => Breaks is null ? Visibility.Collapsed : Visibility.Visible;
        public string TierText => Loc.T(entry.Tier == AppTier.Safe ? "Safe" : "Check first");
        public Brush TierTint => Palette.Tint(entry.Tier == AppTier.Safe ? Palette.Start : Palette.Power);

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    private sealed class RemovedRow(RemovedApp app)
    {
        public RemovedApp App => app;
        public string Title => Loc.T(app.Title);

        public string Detail => Loc.F("Removed on {0}.", Date(app.RemovedUtc))
            + (AppGuard.IsPackageFolder(app.InstallLocation) && System.IO.Directory.Exists(app.InstallLocation)
                ? " " + Loc.T("Its files are still on the PC: Restore brings it back at once.")
                : " " + Loc.T("Its files are gone: install it again from the Microsoft Store."));

        private static string Date(DateTimeOffset utc) => utc.LocalDateTime.ToString("g", System.Globalization.CultureInfo.CurrentCulture);

        public Visibility StoreVisibility => app.Reinstall.Store is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
