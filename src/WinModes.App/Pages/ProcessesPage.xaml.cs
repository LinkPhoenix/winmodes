using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinModes.App.Services;
using WinModes.Core.Planning;

namespace WinModes.App.Pages;

/// <summary>Apps grouped by executable name, with search and sorting. Refreshes by itself. Read-only.</summary>
public partial class ProcessesPage : Page
{
    private const int MaxRows = 60;
    private const double MbPerGb = 1024;
    private const double MediumMemoryMb = 300;
    private const string Ascending = " ↑";
    private const string Descending = " ↓";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(3);

    private readonly DispatcherTimer _timer = new() { Interval = RefreshInterval };
    private IReadOnlyList<ProcessGroup> _groups = [];
    private string _sortColumn = "Memory";
    private bool _sortDescending = true;
    private bool _refreshing;

    public ProcessesPage()
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
            _groups = await Task.Run(() =>
            {
                var list = SystemMonitor.GetProcessGroups();
                // Extract icons off the UI thread; the cache keeps later refreshes cheap.
                foreach (var group in list)
                {
                    IconCache.Get(group.ExecutablePath);
                }

                return list;
            });
            ShowRows();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e) => ShowRows();

    private void OnSortClick(object sender, RoutedEventArgs e)
    {
        var column = (string)((Button)sender).Tag;
        if (column == _sortColumn)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortColumn = column;
            // Names read best A to Z; figures are most useful largest first.
            _sortDescending = column != "Name";
        }

        ShowRows();
    }

    private void ShowRows()
    {
        // Filter events fire while the page is still being built.
        if (Rows is null || Summary is null)
        {
            return;
        }

        var culture = CultureInfo.CurrentCulture;
        var search = SearchBox.Text.Trim();
        var filtered = _groups
            .Where(group => search.Length == 0 || group.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Where(group => ProtectedOnly.IsChecked != true || AppServices.Policy.IsProtectedProcess(group.Name));

        var sorted = (_sortColumn, _sortDescending) switch
        {
            ("Name", false) => filtered.OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase),
            ("Name", true) => filtered.OrderByDescending(group => group.Name, StringComparer.CurrentCultureIgnoreCase),
            ("Count", false) => filtered.OrderBy(group => group.Count),
            ("Count", true) => filtered.OrderByDescending(group => group.Count),
            (_, false) => filtered.OrderBy(group => group.PrivateMemoryMb),
            _ => filtered.OrderByDescending(group => group.PrivateMemoryMb),
        };

        var largest = _groups.Count > 0 ? Math.Max(_groups.Max(group => group.PrivateMemoryMb), 1) : 1;
        var visible = sorted.Take(MaxRows).Select(group => new Row(
            group.Name,
            group.Count.ToString(culture),
            group.PrivateMemoryMb / largest * 100,
            DashboardPage.FormatMemory(group.PrivateMemoryMb, culture),
            MemoryColor(group.PrivateMemoryMb),
            AppServices.Policy.IsProtectedProcess(group.Name) ? Visibility.Visible : Visibility.Collapsed,
            IconCache.Get(group.ExecutablePath))).ToList();

        Rows.ItemsSource = visible;
        SortName.Content = "Name" + Arrow("Name");
        SortCount.Content = "Instances" + Arrow("Count");
        SortMemory.Content = "Memory" + Arrow("Memory");

        var totalGb = _groups.Sum(group => group.PrivateMemoryMb) / MbPerGb;
        Summary.Text = string.Create(culture,
            $"{_groups.Sum(group => group.Count)} processes in {_groups.Count} apps use {totalGb:0.0} GB of private memory. Showing {visible.Count}.");
    }

    private string Arrow(string column) => column != _sortColumn ? "" : _sortDescending ? Descending : Ascending;

    private static Brush MemoryColor(double megabytes) => megabytes switch
    {
        >= MbPerGb => Palette.Stop,
        >= MediumMemoryMb => Palette.Power,
        _ => Palette.Container,
    };

    private sealed record Row(
        string Name, string CountText, double Share, string MemoryText, Brush Color, Visibility ProtectedVisibility, ImageSource? Icon)
    {
        public Visibility GlyphVisibility => Icon is null ? Visibility.Visible : Visibility.Collapsed;
    }
}
