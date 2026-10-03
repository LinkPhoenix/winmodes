using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinModes.App.Services;
using WinModes.Core;
using WinModes.Core.Apps;

namespace WinModes.App.Pages;

/// <summary>What starts with Windows. Items of this user are turned on and off the way Task Manager does, and can be reset.</summary>
public partial class StartupPage : Page
{
    private List<StartupRow> _rows = [];

    public StartupPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ReloadAsync();
    }

    private static bool IsProtectedId(string name) => AppServices.Policy.IsProtectedApp(name);

    private async Task ReloadAsync()
    {
        var items = await Task.Run(StartupService.List);
        var journal = StartupService.Journal.Load();
        _rows = [.. items
            .OrderByDescending(item => item.Enabled)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => new StartupRow(item, journal.Any(change => change.Source == item.Source && change.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase))))];

        var enabled = _rows.Count(row => row.IsOn);
        Summary.Text = _rows.Count == 0
            ? Loc.T("Nothing starts with Windows.")
            : Loc.F("{0} of {1} items start with Windows.", enabled, _rows.Count);
        ShowRows();
    }

    private void ShowRows()
    {
        var terms = SearchMatcher.Terms(SearchBox.Text);
        var shown = _rows.Where(row => SearchMatcher.MatchesTerms(terms, row.Name, row.Detail, row.SourceText)).ToList();
        Rows.ItemsSource = shown;
        EmptyText.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Title = Loc.T(terms.Length == 0 ? "Nothing starts with Windows." : "Nothing matches.");
        EmptyText.Hint = terms.Length == 0 ? "" : Loc.T("Try another search or clear filters.");
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e) => ShowRows();

    private async void OnToggleClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not StartupRow row || sender is not Wpf.Ui.Controls.ToggleSwitch toggle)
        {
            return;
        }

        var wanted = toggle.IsChecked == true;
        var error = await Task.Run(() => StartupService.SetEnabled(row.Item, wanted, IsProtectedId));
        ResultCard.Visibility = Visibility.Visible;
        ResultText.Text = error ?? Loc.F(wanted ? "{0} will start with Windows." : "{0} will no longer start with Windows.", row.Name);
        await ReloadAsync();
    }

    private async void OnResetClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not StartupRow row)
        {
            return;
        }

        var error = await Task.Run(() => StartupService.Reset(row.Item));
        ResultCard.Visibility = Visibility.Visible;
        ResultText.Text = error ?? Loc.F("{0} is back to what it was.", row.Name);
        await ReloadAsync();
    }

    private sealed class StartupRow(StartupItem item, bool changed)
    {
        private readonly bool _protected = StartupGuard.IsProtected(item.Name, IsProtectedId);

        public StartupItem Item => item;
        public string Name => item.Name;
        public string DisplayName => item.Source is StartupSource.UserFolder or StartupSource.CommonFolder ? Path.GetFileNameWithoutExtension(item.Name) : item.Name;
        public bool IsOn => item.Enabled;
        public bool CanToggle => item.IsUserLevel && !_protected && StartupApproval.CanChange(item.Raw);

        public string Detail => Privacy.Enabled
            ? Path.GetFileName(item.Program ?? item.Command)
            : item.Command;

        public Visibility ToggleVisibility => CanToggle ? Visibility.Visible : Visibility.Collapsed;
        public Visibility StateVisibility => CanToggle ? Visibility.Collapsed : Visibility.Visible;
        public string StateText => Loc.T(IsOn ? "Starts" : "Does not start");
        public Brush StateTint => Palette.Tint(IsOn ? Palette.Start : Palette.Neutral);

        public ImageSource? Icon => item.Program is { } program && File.Exists(program) ? IconCache.Get(program) : null;
        public Visibility GlyphVisibility => Icon is null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ProtectedVisibility => _protected ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ResetVisibility => changed ? Visibility.Visible : Visibility.Collapsed;

        public string SourceText => item.Source switch
        {
            StartupSource.UserRun => Loc.T("This user"),
            StartupSource.UserFolder => Loc.T("Startup folder"),
            StartupSource.MachineRun or StartupSource.MachineRun32 => Loc.T("All users"),
            _ => Loc.T("All users, folder"),
        };

        public string? ToggleTip => item.IsUserLevel
            ? null
            : Loc.T("This item starts for every user: change it in Task Manager, with administrator rights.");
    }
}
