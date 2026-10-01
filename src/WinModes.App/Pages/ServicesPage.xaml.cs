using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinModes.App.Services;
using WinModes.Core.Planning;
using WinModes.Core.Tuning;

namespace WinModes.App.Pages;

/// <summary>Installed Windows services with their state and protection. A right-click changes one through the elevated helper.</summary>
public partial class ServicesPage : Page
{
    private IReadOnlyList<ServiceInfo> _services = [];
    private Dictionary<string, ServiceTweak> _tweaks = new(StringComparer.OrdinalIgnoreCase);
    private bool _menuOpen;
    private bool _changing;

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

    private readonly DispatcherTimer _timer = new() { Interval = RefreshInterval };
    private bool _refreshing;

    public ServicesPage()
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
        // Rebuilding the rows would close the menu the user is reading.
        if (_refreshing || _menuOpen || !IsVisible)
        {
            return;
        }

        _refreshing = true;
        try
        {
            (_services, _tweaks) = await Task.Run(() =>
            {
                var list = SystemMonitor.GetServices();
                var tweaks = ServiceTuning.Store.Load().ToDictionary(tweak => tweak.Service, StringComparer.OrdinalIgnoreCase);
                // Extract icons off the UI thread; the cache keeps later refreshes cheap.
                foreach (var service in list)
                {
                    IconCache.Get(service.ExecutablePath);
                }

                return (list, tweaks);
            });
            ApplyFilter();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        // Filter events fire while the page is still being built.
        if (Rows is null || Summary is null)
        {
            return;
        }

        var search = SearchBox.Text.Trim();
        var visible = _services
            .Where(service => RunningOnly.IsChecked != true || service.IsRunning)
            .Where(service => ProtectedOnly.IsChecked != true || AppServices.Policy.IsProtectedService(service.Name))
            .Where(service => search.Length == 0
                || service.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || service.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Select(ToRow)
            .ToList();

        Rows.ItemsSource = visible;
        Summary.Text =
            Loc.F("{0} of {1} services are running. Showing {2}. Right-click a service to start it, stop it or change its start type.",
                _services.Count(service => service.IsRunning), _services.Count, visible.Count);
    }

    private void OnMenuOpening(object sender, ContextMenuEventArgs e) => _menuOpen = true;

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

        _changing = true;
        try
        {
            var report = await ServiceTuning.RunAsync(action, row.Name);
            ResultText.Text = report.Summary;
            ResultCard.Visibility = Visibility.Visible;
            // The menu is closed by now, whatever the closing event said.
            _menuOpen = false;
            await RefreshAsync();
        }
        finally
        {
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
            IconCache.Get(service.ExecutablePath));
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
