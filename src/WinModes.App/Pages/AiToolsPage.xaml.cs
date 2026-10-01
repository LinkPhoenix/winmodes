using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinModes.App.Services;
using WinModes.Core.Planning;

namespace WinModes.App.Pages;

/// <summary>
/// Running AI and coding tools, one card per session with the folder it works in and every process it started.
/// Refreshes by itself, except while a menu is open.
/// </summary>
public partial class AiToolsPage : Page
{
    private const string FolderGlyph = "";
    private const string AppGlyph = "";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(3);

    private readonly DispatcherTimer _timer = new() { Interval = RefreshInterval };
    private readonly HashSet<int> _expanded = [];
    private bool _refreshing;
    private bool _menuOpen;

    public AiToolsPage()
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
        // Rebuilding the cards would close an open menu under the pointer.
        if (_refreshing || _menuOpen || !IsVisible)
        {
            return;
        }

        _refreshing = true;
        try
        {
            var sessions = await Task.Run(() =>
            {
                var found = AiToolCatalog.FindSessions(ProcessActions.Sample());
                // Extract icons off the UI thread; the cache keeps later refreshes cheap.
                foreach (var session in found)
                {
                    IconCache.Get(session.Root.ExecutablePath);
                    foreach (var node in session.Descendants)
                    {
                        IconCache.Get(node.ExecutablePath);
                    }
                }

                return found;
            });
            Show(sessions);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void Show(IReadOnlyList<AiSession> sessions)
    {
        var culture = CultureInfo.CurrentCulture;
        var tools = sessions
            .GroupBy(session => session.Tool)
            .OrderBy(group => AiToolCatalog.Tools.ToList().IndexOf(group.Key))
            .Select(group =>
            {
                var rows = group.OrderByDescending(session => session.TotalMemoryMb).Select(session => ToRow(session, culture)).ToList();
                var icon = IconCache.Get(group.First().Root.ExecutablePath);
                return new ToolRow(
                    group.Key.Name,
                    $"{Pluralize(rows.Count, "session")}, {DashboardPage.FormatMemory(group.Sum(session => session.TotalMemoryMb), culture)}",
                    icon,
                    icon is null ? Visibility.Visible : Visibility.Collapsed,
                    rows);
            })
            .ToList();

        Tools.ItemsSource = tools;
        EmptyState.Visibility = tools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var processCount = sessions.Sum(session => session.Descendants.Count + 1);
        Summary.Text = tools.Count == 0
            ? "No AI or coding tool is running."
            : string.Create(culture,
                $"{Pluralize(sessions.Count, "session")} across {Pluralize(tools.Count, "tool")}: {processCount} processes using {DashboardPage.FormatMemory(sessions.Sum(session => session.TotalMemoryMb), culture)}. Right-click a session for actions.");
    }

    private SessionRow ToRow(AiSession session, CultureInfo culture)
    {
        var folder = session.Root.WorkingDirectory;
        // A desktop app runs from its own install folder or from System32: that is not a project folder.
        var isInstallFolder = folder is not null && session.Root.ExecutablePath is { } executable
            && executable.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
        var isSystemFolder = folder is not null
            && folder.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase);
        var hasFolder = !string.IsNullOrEmpty(folder) && !isInstallFolder && !isSystemFolder && Directory.Exists(folder);
        var started = session.Root.StartTime is { } start ? string.Create(culture, $"started {start:g}") : "start time unknown";
        var nodes = new[] { session.Root }.Concat(session.Descendants.OrderByDescending(node => node.PrivateMemoryMb));

        return new SessionRow(
            session.Root.Pid,
            session.Tool.Name,
            hasFolder ? Path.GetFileName(folder) is { Length: > 0 } name ? name : folder! : $"{session.Tool.Name} app",
            hasFolder ? $"{folder}  -  PID {session.Root.Pid}, {started}" : $"PID {session.Root.Pid}, {started}",
            hasFolder ? FolderGlyph : AppGlyph,
            hasFolder ? folder : null,
            Pluralize(session.Descendants.Count + 1, "process", "processes"),
            string.Create(culture, $"{session.TotalCpuPercent:0.0} %"),
            DashboardPage.FormatMemory(session.TotalMemoryMb, culture),
            _expanded.Contains(session.Root.Pid),
            [.. nodes.Select(node => ToProcess(node, culture))]);
    }

    private static ProcessRow ToProcess(ProcessNode node, CultureInfo culture)
    {
        var icon = IconCache.Get(node.ExecutablePath);
        return new ProcessRow(
            node.Name,
            node.Pid,
            string.Create(culture, $"{node.CpuPercent:0.0} %"),
            DashboardPage.FormatMemory(node.PrivateMemoryMb, culture),
            node.CommandLine ?? node.ExecutablePath ?? "",
            icon,
            icon is null ? Visibility.Visible : Visibility.Collapsed);
    }

    private static string Pluralize(int count, string singular, string? plural = null) =>
        count == 1 ? $"1 {singular}" : $"{count} {plural ?? singular + "s"}";

    private static SessionRow? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as SessionRow;

    private void OnExpanded(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row)
        {
            _expanded.Add(row.Pid);
        }
    }

    private void OnCollapsed(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row)
        {
            _expanded.Remove(row.Pid);
        }
    }

    private void OnMenuOpening(object sender, ContextMenuEventArgs e) => _menuOpen = true;

    private void OnMenuClosing(object sender, ContextMenuEventArgs e) => _menuOpen = false;

    private void OnOpenFolder(object sender, RoutedEventArgs e) => ProcessActions.OpenLocation(RowOf(sender)?.Folder);

    private void OnCopyFolder(object sender, RoutedEventArgs e) => ProcessActions.Copy(RowOf(sender)?.Folder);

    private void OnCopyPid(object sender, RoutedEventArgs e) =>
        ProcessActions.Copy(RowOf(sender)?.Pid.ToString(CultureInfo.InvariantCulture));

    private async void OnEndSession(object sender, RoutedEventArgs e)
    {
        _menuOpen = false;
        if (RowOf(sender) is not { } row)
        {
            return;
        }

        var problem = await ProcessActions.EndAsync(row.Pid, $"{row.ToolName} session '{row.Title}'", wholeTree: true, isProtected: true);
        await RefreshAsync();
        if (problem is not null)
        {
            Summary.Text = problem;
        }
    }

    private sealed record ToolRow(string Name, string Summary, ImageSource? Icon, Visibility GlyphVisibility, IReadOnlyList<SessionRow> Sessions);

    private sealed record SessionRow(
        int Pid, string ToolName, string Title, string Subtitle, string Glyph, string? Folder, string ProcessText, string CpuText,
        string MemoryText, bool IsExpanded, IReadOnlyList<ProcessRow> Processes)
    {
        public bool HasFolder => Folder is not null;
    }

    private sealed record ProcessRow(
        string Name, int Pid, string CpuText, string MemoryText, string Command, ImageSource? Icon, Visibility GlyphVisibility);
}
