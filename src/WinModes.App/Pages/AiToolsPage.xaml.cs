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
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan IdleDisplayThreshold = TimeSpan.FromMinutes(5);

    private readonly DispatcherTimer _timer = new() { Interval = RefreshInterval };
    private readonly HashSet<int> _expanded = [];
    private bool _refreshing;
    private bool _menuOpen;
    private IReadOnlyList<AiSession> _idleSessions = [];

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
                AiActivityTracker.Observe(found);
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

        // Only project sessions are offered for cleanup; desktop apps are left to the user.
        _idleSessions = [.. sessions.Where(session => HasProjectFolder(session) && AiActivityTracker.IdleFor(session) >= IdleThreshold)];
        EndIdleButton.IsEnabled = _idleSessions.Count > 0;
        EndIdleButton.Content = _idleSessions.Count > 0 ? $"End idle sessions ({_idleSessions.Count})" : "End idle sessions";
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
        var hasFolder = HasProjectFolder(session);
        var idle = AiActivityTracker.IdleFor(session);
        var started = session.Root.StartTime is { } start ? string.Create(culture, $"started {start:g}") : "start time unknown";
        var nodes = new[] { session.Root }.Concat(session.Descendants.OrderByDescending(node => node.PrivateMemoryMb));

        return new SessionRow(
            session.Root.Pid,
            session.Tool.Name,
            hasFolder ? Privacy.Project(Path.GetFileName(folder) is { Length: > 0 } name ? name : folder!) : $"{session.Tool.Name} app",
            hasFolder ? $"{(Privacy.Enabled ? Privacy.HiddenFolder : folder)}  -  PID {session.Root.Pid}, {started}" : $"PID {session.Root.Pid}, {started}",
            hasFolder ? FolderGlyph : AppGlyph,
            hasFolder ? folder : null,
            DescribeProcesses(session, culture),
            string.Create(culture, $"{session.TotalCpuPercent:0.0} %"),
            DashboardPage.FormatMemory(session.TotalMemoryMb, culture),
            _expanded.Contains(session.Root.Pid),
            idle >= IdleDisplayThreshold ? $"idle {(int)idle.TotalMinutes} min" : "",
            [.. nodes.Select(node => ToProcess(node, culture))]);
    }

    private static bool IsMcpServer(ProcessNode node) =>
        node.CommandLine?.Contains("mcp", StringComparison.OrdinalIgnoreCase) == true;

    private static string DescribeProcesses(AiSession session, CultureInfo culture)
    {
        var text = Pluralize(session.Descendants.Count + 1, "process", "processes");
        var servers = session.Descendants.Where(IsMcpServer).ToList();
        return servers.Count == 0
            ? text
            : $"{text}, {servers.Count} MCP ({DashboardPage.FormatMemory(servers.Sum(node => node.PrivateMemoryMb), culture)})";
    }

    private static bool HasProjectFolder(AiSession session)
    {
        var folder = session.Root.WorkingDirectory;
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return false;
        }

        // A desktop app runs from its own install folder or from System32: that is not a project folder.
        var isInstallFolder = session.Root.ExecutablePath is { } executable && executable.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
        var isSystemFolder = folder.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase);
        return !isInstallFolder && !isSystemFolder;
    }

    private async void OnEndIdle(object sender, RoutedEventArgs e)
    {
        var idle = _idleSessions;
        if (idle.Count == 0)
        {
            return;
        }

        var names = string.Join("\n", idle.Select(session => $"- {session.Tool.Name}: {(Privacy.Enabled ? Privacy.HiddenFolder : session.Root.WorkingDirectory)}"));
        var confirm = new Wpf.Ui.Controls.MessageBox
        {
            Title = idle.Count == 1 ? "End 1 idle session?" : $"End {idle.Count} idle sessions?",
            Content = $"These sessions used no CPU for at least {(int)IdleThreshold.TotalMinutes} minutes:\n\n{names}\n\n"
                + "Each session and every process it started will be closed. Unsaved work in them is lost.",
            PrimaryButtonText = "End sessions",
            CloseButtonText = "Cancel",
        };
        if (await confirm.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            return;
        }

        var failed = 0;
        foreach (var session in idle)
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(session.Root.Pid);
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException)
            {
                failed++;
            }
        }

        await RefreshAsync();
        if (failed > 0)
        {
            Summary.Text = $"{failed} session(s) could not be ended.";
        }
    }

    private static ProcessRow ToProcess(ProcessNode node, CultureInfo culture)
    {
        var icon = IconCache.Get(node.ExecutablePath);
        return new ProcessRow(
            node.Name,
            node.Pid,
            string.Create(culture, $"{node.CpuPercent:0.0} %"),
            DashboardPage.FormatMemory(node.PrivateMemoryMb, culture),
            Privacy.CommandLine(node.CommandLine ?? node.ExecutablePath, node.Name),
            icon,
            icon is null ? Visibility.Visible : Visibility.Collapsed,
            IsMcpServer(node) ? Visibility.Visible : Visibility.Collapsed);
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
        string MemoryText, bool IsExpanded, string IdleText, IReadOnlyList<ProcessRow> Processes)
    {
        public Visibility IdleVisibility => IdleText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        public bool HasFolder => Folder is not null;
    }

    private sealed record ProcessRow(
        string Name, int Pid, string CpuText, string MemoryText, string Command, ImageSource? Icon, Visibility GlyphVisibility, Visibility McpVisibility);
}
