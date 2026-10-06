using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinModes.App.Controls;
using WinModes.App.Services;
using WinModes.Core;
using WinModes.Core.Planning;
using WinModes.Core.Tuning;

namespace WinModes.App.Pages;

/// <summary>Installed Windows services with their state and protection. A right-click changes one through the elevated helper.</summary>
public partial class ServicesPage : Page
{
    private IReadOnlyList<ServiceInfo> _services = [];
    private Dictionary<string, ServiceTweak> _tweaks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<RowHolder<Row>> _rows = [];
    private bool _menuOpen;
    private bool _changing;

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

    private readonly DispatcherTimer _timer = new() { Interval = RefreshInterval };
    private bool _refreshing;

    public ServicesPage()
    {
        InitializeComponent();
        Rows.ItemsSource = _rows;
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
        // Rebuilding the rows would close the menu the user is reading.
        if (_refreshing || _menuOpen || !WinModes.App.Services.WindowActivity.IsShown(this))
        {
            return;
        }

        _refreshing = true;
        try
        {
            (_services, _tweaks) = await Task.Run(() => (
                SystemMonitor.GetServices(),
                ServiceTuning.Store.Load().ToDictionary(tweak => tweak.Service, StringComparer.OrdinalIgnoreCase)));
            ApplyFilter();

            // Rows first, icons when they are read: reading them is most of the time the page took to appear.
            if (await IconCache.PreloadAsync(_services.Select(service => service.ExecutablePath)))
            {
                ApplyFilter();
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnFilterChanged(object sender, RoutedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        // Filter events fire while the page is still being built.
        if (Rows is null || Summary is null)
        {
            return;
        }

        var search = SearchMatcher.Terms(SearchBox.Text);
        var visible = _services
            .Where(service => RunningOnly.IsChecked != true || service.IsRunning)
            .Where(service => ProtectedOnly.IsChecked != true || AppServices.Policy.IsProtectedService(service.Name))
            .Where(service => SearchMatcher.MatchesTerms(search, service.Name, service.DisplayName))
            .Select(ToRow)
            .ToList();

        _rows.Reconcile(visible, row => row.Name);
        EmptyState.Visibility = _services.Count > 0 && visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Hint = Loc.T("Try another search or clear filters.");
        Summary.Text =
            Loc.F("{0} of {1} services are running. Showing {2}. Right-click a service to start it, stop it or change its start type.",
                _services.Count(service => service.IsRunning), _services.Count, visible.Count);
    }

    private void OnMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // The menu belongs to the list: point it at the row under the pointer, or show nothing between rows.
        var row = (e.OriginalSource as DependencyObject)?.FindAncestor<ContentPresenter>()?.Content as RowHolder<Row>;
        if (row is null)
        {
            e.Handled = true;
            return;
        }

        Rows.ContextMenu!.DataContext = row.Data;
        _menuOpen = true;
    }

    private void OnMenuClosing(object sender, ContextMenuEventArgs e) => _menuOpen = false;

    private void OnCopyName(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Row row)
        {
            Clipboard.SetText(row.Name);
        }
    }

    private async void OnMenuAction(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: Row row, Tag: string tag } || !Enum.TryParse<TuneAction>(tag, out var action) || _changing)
        {
            return;
        }

        if (action == TuneAction.Disabled && !await ConfirmDisableAsync(row))
        {
            return;
        }

        if (!OperationStatus.TryBegin(Loc.F("Service change: {0}", row.Name), 1, out var operation))
        {
            ResultText.Text = Loc.T("Another operation is already running.");
            ResultCard.Visibility = Visibility.Visible;
            return;
        }
        _changing = true;
        try
        {
            var report = await ServiceTuning.RunAsync(action, row.Name);
            OperationStatus.Progress(operation, report.Results.Count, report.Summary);
            OperationStatus.Complete(operation, report.Summary, failed: !report.Succeeded);
            ResultText.Text = report.Summary;
            ResultCard.Visibility = Visibility.Visible;
            // The menu is closed by now, whatever the closing event said.
            _menuOpen = false;
            await RefreshAsync();
        }
        finally
        {
            if (OperationStatus.Current is { IsRunning: true } current && current.Id == operation)
            {
                OperationStatus.Complete(operation, Loc.T("The operation did not finish."), failed: true);
            }
            _changing = false;
        }
    }

    private static async Task<bool> ConfirmDisableAsync(Row row)
    {
        var confirm = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.F("Disable {0}?", row.DisplayName),
            Content = Loc.T("A disabled service cannot start, even when Windows or an app needs it. Prefer Manual unless you are sure. You can restore the original start type from this menu or from the Optimize page."),
            PrimaryButtonText = Loc.T("Disable"),
            CloseButtonText = Loc.T("Cancel"),
        };
        return await confirm.ShowDialogAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary;
    }

    private Row ToRow(ServiceInfo service)
    {
        var color = service.IsRunning ? Palette.Start : Palette.Neutral;
        return new Row(
            service.DisplayName,
            service.Name,
            service.StartMode,
            service.IsRunning,
            color,
            Palette.Tint(color),
            AppServices.Policy.IsProtectedService(service.Name),
            _tweaks.GetValueOrDefault(service.Name)?.OriginalStartMode,
            ServiceTuning.Knowledge.Find(service.Name)?.Description is { Length: > 0 } description ? description : null,
            IconCache.Peek(service.ExecutablePath));
    }

    private sealed record Row(
        string DisplayName, string Name, ServiceStartMode Mode, bool IsRunning, Brush StatusColor, Brush StatusTint, bool IsProtected,
        ServiceStartMode? Original, string? Description, ImageSource? Icon)
    {
        public string StartMode => Mode.ToString();
        public string Status => Loc.T(IsRunning ? "Running" : "Stopped");
        public Visibility ProtectedVisibility => IsProtected ? Visibility.Visible : Visibility.Collapsed;
        public Visibility GlyphVisibility => Icon is null ? Visibility.Visible : Visibility.Collapsed;

        public bool CanStart => !IsRunning && Mode != ServiceStartMode.Disabled;
        public bool CanStop => IsRunning && !IsProtected;
        public bool CanChange => !IsProtected && Mode != ServiceStartMode.Unknown;
        public bool CanRestore => Original is not null && !IsProtected;
        public bool IsAutomatic => Mode == ServiceStartMode.Automatic;
        public bool IsManual => Mode == ServiceStartMode.Manual;
        public bool IsDisabled => Mode == ServiceStartMode.Disabled;
        public string RestoreHeader => Original is { } original ? Loc.F("Restore original start type ({0})", original) : Loc.T("Restore original start type");
    }
}
