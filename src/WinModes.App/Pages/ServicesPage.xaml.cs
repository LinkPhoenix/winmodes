using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinModes.App.Services;
using WinModes.Core.Planning;

namespace WinModes.App.Pages;

/// <summary>Installed Windows services with their state and protection. Read-only.</summary>
public partial class ServicesPage : Page
{
    private IReadOnlyList<ServiceInfo> _services = [];

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
        if (_refreshing || !IsVisible)
        {
            return;
        }

        _refreshing = true;
        try
        {
            _services = await Task.Run(() =>
            {
                var list = SystemMonitor.GetServices();
                // Extract icons off the UI thread; the cache keeps later refreshes cheap.
                foreach (var service in list)
                {
                    IconCache.Get(service.ExecutablePath);
                }

                return list;
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
            $"{_services.Count(service => service.IsRunning)} of {_services.Count} services are running. Showing {visible.Count}.";
    }

    private static Row ToRow(ServiceInfo service)
    {
        var color = service.IsRunning ? Palette.Start : Palette.Neutral;
        return new Row(
            service.DisplayName,
            service.Name,
            service.StartMode.ToString(),
            service.IsRunning ? "Running" : "Stopped",
            color,
            Palette.Tint(color),
            AppServices.Policy.IsProtectedService(service.Name) ? Visibility.Visible : Visibility.Collapsed,
            IconCache.Get(service.ExecutablePath));
    }

    private sealed record Row(
        string DisplayName, string Name, string StartMode, string Status, Brush StatusColor, Brush StatusTint, Visibility ProtectedVisibility, ImageSource? Icon)
    {
        public Visibility GlyphVisibility => Icon is null ? Visibility.Visible : Visibility.Collapsed;
    }
}
