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
            // Turned off under Monitoring: the page says so and reads nothing, with no timer running.
            var read = WinModes.App.Services.AppSettings.Load().Monitoring.AiTools;
            OffState.Visibility = read ? Visibility.Collapsed : Visibility.Visible;
            EndIdleButton.Visibility = read ? Visibility.Visible : Visibility.Collapsed;
            if (!read)
            {
                Summary.Text = Loc.T("The reading of AI tools is turned off.");
                return;
            }

            _timer.Start();
            await RefreshAsync();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    private void OnOpenMonitoring(object sender, RoutedEventArgs e) =>
        (Application.Current.MainWindow as MainWindow)?.NavigateTo(typeof(SettingsPage));

    private async Task RefreshAsync()
    {
        // Rebuilding the cards would close an open menu under the pointer.
        if (_refreshing || _menuOpen || !WinModes.App.Services.WindowActivity.IsShown(this))
        {
            return;
        }

        _refreshing = true;
        try
        {
            var sessions = await Task.Run(() =>
            {
                McpConfig.Refresh();
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
                    $"{Loc.N(rows.Count, "1 session", "{0} sessions")}, {DashboardPage.FormatMemory(group.Sum(session => session.TotalMemoryMb), culture)}",
                    icon,
                    icon is null ? Visibility.Visible : Visibility.Collapsed,
                    rows);
            })
            .ToList();

        Tools.ItemsSource = tools;

        var duplicates = McpServers.FindDuplicates(sessions);
        DuplicateCard.Visibility = duplicates.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Duplicates.ItemsSource = duplicates.Select(duplicate => new DuplicateRow(
            duplicate.Name,
            Loc.F("in {0} sessions, {1}", duplicate.Sessions, Loc.N(duplicate.Processes, "1 process", "{0} processes")),
            DashboardPage.FormatMemory(duplicate.MemoryMb, culture))).ToList();
        DuplicateSummary.Text = duplicates.Count == 0
            ? ""
            : Loc.F("{0} in total. Each session starts its own copy of the MCP servers it is configured with; closing sessions you no longer use frees them.",
                DashboardPage.FormatMemory(duplicates.Sum(duplicate => duplicate.MemoryMb), culture));

        // Only project sessions are offered for cleanup; desktop apps are left to the user.
        _idleSessions = [.. sessions.Where(session => HasProjectFolder(session) && AiActivityTracker.IdleFor(session) >= IdleThreshold)];
        EndIdleButton.IsEnabled = _idleSessions.Count > 0;
        EndIdleButton.Content = _idleSessions.Count > 0 ? Loc.F("End idle sessions ({0})", _idleSessions.Count) : Loc.T("End idle sessions");
        EmptyState.Visibility = tools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var processCount = sessions.Sum(session => session.Descendants.Count + 1);
        Summary.Text = tools.Count == 0
            ? Loc.T("No AI or coding tool is running.")
            : Loc.F("{0} across {1}: {2} processes using {3}. Right-click a session for actions.",
                Loc.N(sessions.Count, "1 session", "{0} sessions"), Loc.N(tools.Count, "1 tool", "{0} tools"), processCount,
                DashboardPage.FormatMemory(sessions.Sum(session => session.TotalMemoryMb), culture));
    }

    private SessionRow ToRow(AiSession session, CultureInfo culture)
    {
        var folder = session.Root.WorkingDirectory;
        var hasFolder = HasProjectFolder(session);
        var idle = AiActivityTracker.IdleFor(session);
        var started = session.Root.StartTime is { } start ? Loc.F("started {0:g}", start) : Loc.T("start time unknown");
        var nodes = new[] { session.Root }.Concat(session.Descendants.OrderByDescending(node => node.PrivateMemoryMb));

        return new SessionRow(
            session.Root.Pid,
            session.Tool.Name,
            hasFolder ? Privacy.Project(Path.GetFileName(folder) is { Length: > 0 } name ? name : folder!) : Loc.F("{0} app", session.Tool.Name),
            hasFolder ? $"{(Privacy.Enabled ? Privacy.HiddenFolder : folder)}  -  PID {session.Root.Pid}, {started}" : $"PID {session.Root.Pid}, {started}",
            hasFolder ? FolderGlyph : AppGlyph,
            hasFolder ? folder : null,
            DescribeProcesses(session, culture),
            string.Create(culture, $"{session.TotalCpuPercent:0.0} %"),
            DashboardPage.FormatMemory(session.TotalMemoryMb, culture),
            _expanded.Contains(session.Root.Pid),
            idle >= IdleDisplayThreshold ? Loc.F("idle {0} min", (int)idle.TotalMinutes) : "",
            [.. nodes.Select(node => ToProcess(node, culture, node != session.Root && McpServers.IsServer(session, node)))]);
    }

    private static string DescribeProcesses(AiSession session, CultureInfo culture)
    {
        var text = Loc.N(session.Descendants.Count + 1, "1 process", "{0} processes");
        var servers = McpServers.Servers(session);
        return servers.Count == 0
            ? text
            : $"{text}, {servers.Count} MCP ({DashboardPage.FormatMemory(servers.Sum(server => server.MemoryMb), culture)})";
    }

    private static bool HasProjectFolder(AiSession session) => AiSessions.HasProjectFolder(session);

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
            Title = Loc.N(idle.Count, "End 1 idle session?", "End {0} idle sessions?"),
            Content = Loc.F("These sessions used no CPU for at least {0} minutes:", (int)IdleThreshold.TotalMinutes) + $"\n\n{names}\n\n"
                + Loc.T("Each session and every process it started will be closed. Unsaved work in them is lost."),
            PrimaryButtonText = Loc.T("End sessions"),
            CloseButtonText = Loc.T("Cancel"),
        };
        if (await confirm.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            return;
        }

        var failed = idle.Count(session => !AiSessions.End(session));

        await RefreshAsync();
        if (failed > 0)
        {
            Summary.Text = Loc.F("{0} session(s) could not be ended.", failed);
        }
    }

    private static ProcessRow ToProcess(ProcessNode node, CultureInfo culture, bool isMcpServer)
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
            isMcpServer ? Visibility.Visible : Visibility.Collapsed);
    }


    private sealed record DuplicateRow(string Name, string Detail, string Memory);

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

        var problem = await ProcessActions.EndAsync(row.Pid, Loc.F("{0} session '{1}'", row.ToolName, row.Title), wholeTree: true, isProtected: true);
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
