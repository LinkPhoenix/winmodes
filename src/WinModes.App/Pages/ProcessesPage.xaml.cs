using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinModes.App.Services;
using WinModes.Core.Planning;

namespace WinModes.App.Pages;

/// <summary>
/// Process tree with search, sorting and a right-click menu. A parent row shows the total of its
/// children while collapsed. Refreshes by itself, except while a menu is open.
/// </summary>
public partial class ProcessesPage : Page
{
    private const double MbPerGb = 1024;
    private const double MediumMemoryMb = 300;
    private const double BusyCpuPercent = 5;
    private const double IndentPerLevel = 22;
    private const string Ascending = " ↑";
    private const string Descending = " ↓";
    private const string ChevronCollapsed = "";
    private const string ChevronExpanded = "";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(3);

    // Everything hangs under these shells and hosts; treating their children as roots gives a readable list.
    private static readonly HashSet<string> ContainerProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "services", "svchost", "wininit", "winlogon", "System", "smss", "csrss", "sihost", "userinit",
    };

    private readonly DispatcherTimer _timer = new() { Interval = RefreshInterval };
    private readonly HashSet<int> _expanded = [];
    private readonly ObservableCollection<RowHolder> _rows = [];
    private IReadOnlyList<ProcessNode> _nodes = [];
    private string _sortColumn = "Memory";
    private bool _sortDescending = true;
    private bool _refreshing;
    private bool _menuOpen;

    public ProcessesPage()
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
        // Rebuilding the rows would close an open menu under the pointer.
        if (_refreshing || _menuOpen || !IsVisible)
        {
            return;
        }

        _refreshing = true;
        try
        {
            _nodes = await Task.Run(() =>
            {
                var nodes = ProcessActions.Sample();
                // Extract icons off the UI thread; the cache keeps later refreshes cheap.
                foreach (var path in nodes.Select(node => node.ExecutablePath).Distinct())
                {
                    IconCache.Get(path);
                }

                return nodes;
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

    private void OnToggle(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Row row } && !_expanded.Remove(row.Pid))
        {
            _expanded.Add(row.Pid);
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
        var byPid = _nodes.ToDictionary(node => node.Pid);
        var children = ProcessSampler.BuildChildren(_nodes);
        var totals = new Dictionary<int, Totals>();
        var rows = new List<Row>();
        var search = SearchBox.Text.Trim();

        if (search.Length > 0 || ProtectedOnly.IsChecked == true)
        {
            // A filtered view is flat: a match may sit anywhere in the tree.
            var matches = _nodes
                .Where(node => search.Length == 0 || Matches(node, search, culture))
                .Where(node => ProtectedOnly.IsChecked != true || AppServices.Policy.IsProtectedProcess(node.Name));
            rows.AddRange(Sort(matches, node => new Totals(node.PrivateMemoryMb, node.CpuPercent, node.Threads, 0))
                .Select(node => ToRow(node, level: 0, new Totals(node.PrivateMemoryMb, node.CpuPercent, node.Threads, 0), hasChildren: false, culture)));
        }
        else
        {
            var roots = _nodes.Where(node => node.Pid != 0
                && (!ProcessSampler.HasLiveParent(node, byPid) || ContainerProcesses.Contains(byPid[node.ParentPid].Name)));
            foreach (var root in Sort(roots, node => Subtree(node, children, totals)))
            {
                AddTree(root, level: 0, children, totals, rows, culture);
            }
        }

        Reconcile(rows);
        SortName.Content = Loc.T("Name") + Arrow("Name");
        SortPid.Content = "PID" + Arrow("Pid");
        SortCpu.Content = "CPU" + Arrow("Cpu");
        SortMemory.Content = Loc.T("Memory") + Arrow("Memory");
        SortThreads.Content = Loc.T("Threads") + Arrow("Threads");

        var totalGb = _nodes.Sum(node => node.PrivateMemoryMb) / MbPerGb;
        Summary.Text = Loc.F("{0} processes use {1:0.0} GB of private memory. Showing {2}. Right-click a row for actions.", _nodes.Count, totalGb, rows.Count);
    }

    /// <summary>
    /// Updates the displayed rows in place. Replacing the whole list would rebuild every row's visuals
    /// on each refresh, which is what made the page stutter.
    /// </summary>
    private void Reconcile(List<Row> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (i < _rows.Count && _rows[i].Pid == row.Pid)
            {
                _rows[i].Data = row;
                continue;
            }

            var existing = -1;
            for (var j = i + 1; j < _rows.Count; j++)
            {
                if (_rows[j].Pid == row.Pid)
                {
                    existing = j;
                    break;
                }
            }

            if (existing >= 0)
            {
                _rows.Move(existing, i);
                _rows[i].Data = row;
            }
            else
            {
                _rows.Insert(i, new RowHolder(row));
            }
        }

        while (_rows.Count > rows.Count)
        {
            _rows.RemoveAt(_rows.Count - 1);
        }
    }

    private void AddTree(ProcessNode node, int level, ILookup<int, ProcessNode> children, Dictionary<int, Totals> totals, List<Row> rows, CultureInfo culture)
    {
        // A container's children are listed as roots, so they are not repeated under it.
        var kids = ContainerProcesses.Contains(node.Name) ? [] : children[node.Pid].ToList();
        var isExpanded = _expanded.Contains(node.Pid);
        var own = new Totals(node.PrivateMemoryMb, node.CpuPercent, node.Threads, kids.Count);
        var shown = kids.Count > 0 && !isExpanded ? Subtree(node, children, totals) : own;
        rows.Add(ToRow(node, level, shown, kids.Count > 0, culture));

        if (isExpanded)
        {
            foreach (var child in Sort(kids, kid => Subtree(kid, children, totals)))
            {
                AddTree(child, level + 1, children, totals, rows, culture);
            }
        }
    }

    private static Totals Subtree(ProcessNode node, ILookup<int, ProcessNode> children, Dictionary<int, Totals> cache)
    {
        if (cache.TryGetValue(node.Pid, out var cached))
        {
            return cached;
        }

        var total = new Totals(node.PrivateMemoryMb, node.CpuPercent, node.Threads, 0);
        // Guard against a cycle left by PID reuse.
        cache[node.Pid] = total;
        if (!ContainerProcesses.Contains(node.Name))
        {
            foreach (var child in children[node.Pid])
            {
                var sub = Subtree(child, children, cache);
                total = new Totals(total.MemoryMb + sub.MemoryMb, total.Cpu + sub.Cpu, total.Threads + sub.Threads, total.Descendants + sub.Descendants + 1);
            }
        }

        cache[node.Pid] = total;
        return total;
    }

    private IEnumerable<ProcessNode> Sort(IEnumerable<ProcessNode> nodes, Func<ProcessNode, Totals> totals) => (_sortColumn, _sortDescending) switch
    {
        ("Name", false) => nodes.OrderBy(node => node.Name, StringComparer.CurrentCultureIgnoreCase),
        ("Name", true) => nodes.OrderByDescending(node => node.Name, StringComparer.CurrentCultureIgnoreCase),
        ("Pid", false) => nodes.OrderBy(node => node.Pid),
        ("Pid", true) => nodes.OrderByDescending(node => node.Pid),
        ("Cpu", false) => nodes.OrderBy(node => totals(node).Cpu),
        ("Cpu", true) => nodes.OrderByDescending(node => totals(node).Cpu),
        ("Threads", false) => nodes.OrderBy(node => totals(node).Threads),
        ("Threads", true) => nodes.OrderByDescending(node => totals(node).Threads),
        (_, false) => nodes.OrderBy(node => totals(node).MemoryMb),
        _ => nodes.OrderByDescending(node => totals(node).MemoryMb),
    };

    private Row ToRow(ProcessNode node, int level, Totals shown, bool hasChildren, CultureInfo culture)
    {
        var isProtected = AppServices.Policy.IsProtectedProcess(node.Name);
        var icon = IconCache.Get(node.ExecutablePath);
        return new Row(
            node.Pid,
            node.Name,
            new Thickness(level * IndentPerLevel + (hasChildren ? 0 : IndentPerLevel), 0, 0, 0),
            _expanded.Contains(node.Pid) ? ChevronExpanded : ChevronCollapsed,
            hasChildren ? Visibility.Visible : Visibility.Collapsed,
            hasChildren && shown.Descendants > 0 && !_expanded.Contains(node.Pid) ? $"+{shown.Descendants}" : "",
            string.Create(culture, $"{shown.Cpu:0.0} %"),
            shown.Cpu >= BusyCpuPercent ? Palette.Power : Palette.Neutral,
            DashboardPage.FormatMemory(shown.MemoryMb, culture),
            shown.MemoryMb >= MbPerGb ? Palette.Stop : shown.MemoryMb >= MediumMemoryMb ? Palette.Power : Palette.Text,
            shown.Threads.ToString(culture),
            Privacy.CommandLine(node.CommandLine ?? node.ExecutablePath, node.Name),
            icon,
            icon is null ? Visibility.Visible : Visibility.Collapsed,
            isProtected,
            isProtected ? Visibility.Visible : Visibility.Collapsed,
            node.ExecutablePath,
            node.WorkingDirectory);
    }

    private static bool Matches(ProcessNode node, string search, CultureInfo culture) =>
        node.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
        || node.Pid.ToString(culture) == search
        || node.CommandLine?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;

    private string Arrow(string column) => column != _sortColumn ? "" : _sortDescending ? Descending : Ascending;

    private void OnMenuOpening(object sender, ContextMenuEventArgs e) => _menuOpen = true;

    private void OnMenuClosing(object sender, ContextMenuEventArgs e) => _menuOpen = false;

    private static Row? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as Row;

    private async void OnEndTask(object sender, RoutedEventArgs e) => await EndAsync(RowOf(sender), wholeTree: false);

    private async void OnEndTree(object sender, RoutedEventArgs e) => await EndAsync(RowOf(sender), wholeTree: true);

    private async Task EndAsync(Row? row, bool wholeTree)
    {
        _menuOpen = false;
        if (row is null)
        {
            return;
        }

        var problem = await ProcessActions.EndAsync(row.Pid, row.Name, wholeTree, row.IsProtected);
        await RefreshAsync();
        if (problem is not null)
        {
            Summary.Text = problem;
        }
    }

    private void OnOpenLocation(object sender, RoutedEventArgs e) => ProcessActions.OpenLocation(RowOf(sender)?.Path);

    private void OnOpenFolder(object sender, RoutedEventArgs e) => ProcessActions.OpenLocation(RowOf(sender)?.Folder);

    private void OnSearchOnline(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row)
        {
            ProcessActions.SearchOnline(row.Name);
        }
    }

    private void OnCopyName(object sender, RoutedEventArgs e) => ProcessActions.Copy(RowOf(sender)?.Name);

    private void OnCopyPid(object sender, RoutedEventArgs e) =>
        ProcessActions.Copy(RowOf(sender)?.Pid.ToString(CultureInfo.InvariantCulture));

    private void OnCopyCommand(object sender, RoutedEventArgs e) => ProcessActions.Copy(RowOf(sender)?.Command);

    /// <summary>Stable item for one process; swapping <see cref="Data"/> refreshes the row without recreating it.</summary>
    private sealed class RowHolder(Row data) : INotifyPropertyChanged
    {
        private Row _data = data;

        public event PropertyChangedEventHandler? PropertyChanged;

        public int Pid => _data.Pid;

        public Row Data
        {
            get => _data;
            set
            {
                if (_data != value)
                {
                    _data = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Data)));
                }
            }
        }
    }

    private sealed record Totals(double MemoryMb, double Cpu, int Threads, int Descendants);

    private sealed record Row(
        int Pid, string Name, Thickness Indent, string Chevron, Visibility ChevronVisibility, string ChildText,
        string CpuText, Brush CpuColor, string MemoryText, Brush MemoryColor, string Threads, string Command,
        ImageSource? Icon, Visibility GlyphVisibility, bool IsProtected, Visibility ProtectedVisibility, string? Path, string? Folder)
    {
        public bool HasPath => Path is not null;
        public bool HasFolder => Folder is not null;
        public bool HasCommand => Command.Length > 0;
    }
}
