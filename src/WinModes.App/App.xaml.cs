using System.IO;
using System.Windows;
using WinModes.Core;
using WinModes.Core.Planning;
using WinModes.Core.Profiles;
using WinModes.Core.Protection;
using Forms = System.Windows.Forms;

namespace WinModes.App;

public partial class App : Application, IDisposable
{
    private const string SingleInstanceMutexName = @"Local\WinModes.App";
    private const string OnboardingArgument = "--onboarding";
    private const string LanguageArgument = "--language";
    private TaskbarWidgetWindow? _taskbarWidget;

    private Services.ErrorGuard? _errorGuard;
    private Mutex? _singleInstance;
    private Forms.NotifyIcon? _trayIcon;
    private MainWindow? _window;
    private readonly Services.LiveStats _liveStats = new();
    private Services.TrayMeter? _trayMeter;

    /// <summary>Shared sampler; pages may listen to it while they are shown.</summary>
    internal Services.LiveStats Stats => _liveStats;

    private WidgetWindow? _widget;
    private bool _listening;
    private Services.HotkeyService? _hotkeys;
    private bool _alertRaised;
    private Services.AutoSwitcher? _autoSwitcher;
    private static readonly TimeSpan UsageFlushInterval = TimeSpan.FromMinutes(1);
    private readonly HashSet<string> _toolAlertsRaised = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastUsageFlush = DateTime.Now;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        _errorGuard = Services.ErrorGuard.Register(this, new ErrorLog(ErrorLog.DefaultPath), () =>
            _trayIcon?.ShowBalloonTip(5000, "WinModes",
                Loc.T("WinModes hit an unexpected error and kept running. The details are in errors.log in %LocalAppData%\\WinModes."), Forms.ToolTipIcon.Warning));

        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        // The argument sets the language for this run only, without changing the saved setting.
        var languageIndex = Array.IndexOf(e.Args, LanguageArgument);
        UseLanguage(languageIndex >= 0 && languageIndex + 1 < e.Args.Length ? e.Args[languageIndex + 1] : Services.AppSettings.Load().Language);

        var root = RepositoryLocator.Find(AppContext.BaseDirectory) ?? RepositoryLocator.Find(Environment.CurrentDirectory);
        if (root is null)
        {
            MessageBox.Show(Loc.T("Cannot find the win-modes data (data/protected.json)."), "WinModes", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        var store = new ProfileStore(Path.Combine(root, "profiles"));
        var policy = ProtectionPolicy.Load(Path.Combine(root, "data", "protected.json"));
        var planner = new ModePlanner(new WindowsSystemProbe(), policy);

        AppServices.Initialize(store, planner, policy, Path.Combine(root, "profiles"));

        var theme = Services.AppSettings.Load().Theme == Services.AppSettings.LightTheme
            ? Wpf.Ui.Appearance.ApplicationTheme.Light
            : Wpf.Ui.Appearance.ApplicationTheme.Dark;
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(theme, Wpf.Ui.Controls.WindowBackdropType.None, updateAccent: false);
        // Brand accent instead of the Windows one, so the app looks the same on every PC.
        Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(Palette.Brand, theme);
        if (theme == Wpf.Ui.Appearance.ApplicationTheme.Light)
        {
            Palette.UseLightSurfaces(Resources);
        }
        // The argument turns privacy on for this run only, without changing the saved setting.
        Services.Privacy.Set(Services.AppSettings.Load().PrivacyMode || e.Args.Contains(Services.Privacy.CommandLineArgument));
        var pageIndex = Array.IndexOf(e.Args, "--page");
        _window = new MainWindow(pageIndex >= 0 && pageIndex + 1 < e.Args.Length ? e.Args[pageIndex + 1] : null,
            showOnboarding: e.Args.Contains(OnboardingArgument));
        _trayIcon = CreateTrayIcon();
        _trayMeter = new Services.TrayMeter(_trayIcon, _trayIcon.Icon!);
        ApplyDisplaySettings();
        StartSession(e.Args);
    }

    /// <summary>
    /// Chosen before any window exists, since every text is read when its screen is built. In English the
    /// formats of Windows are kept; another language also brings its own dates and numbers.
    /// </summary>
    private static void UseLanguage(string language)
    {
        Loc.Use(language);
        if (Loc.Current == Loc.DefaultLanguage)
        {
            return;
        }

        var culture = System.Globalization.CultureInfo.GetCultureInfo(Loc.Current);
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
        System.Globalization.CultureInfo.CurrentCulture = culture;
        System.Globalization.CultureInfo.CurrentUICulture = culture;
    }

    private async void StartSession(string[] args)
    {
        // At sign-in the app starts hidden; the tray icon opens the window. The first run always shows the guide.
        if (!args.Contains(Services.AppSettings.MinimizedArgument) || !Services.AppSettings.Load().OnboardingDone)
        {
            _window!.Show();
        }

        _ = CheckForUpdatesAsync();

        var autoMode = Services.AppSettings.Load().AutoActivateMode;
        if (autoMode is null || Services.ModeSwitcher.ActiveMode is not null)
        {
            return;
        }

        try
        {
            var report = await AppServices.Switcher.ActivateAsync(AppServices.Store.Load(autoMode));
            _trayIcon?.ShowBalloonTip(4000, "WinModes",
                report.Succeeded ? Loc.F("{0} mode activated automatically.", autoMode) : report.Lines[0], Forms.ToolTipIcon.Info);
        }
        catch (ProfileException ex)
        {
            _trayIcon?.ShowBalloonTip(4000, "WinModes", ex.Message, Forms.ToolTipIcon.Warning);
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        if (!Services.AppSettings.Load().CheckForUpdates)
        {
            return;
        }

        var status = await Services.UpdateChecker.CheckAsync();
        if (status.IsNewer && _trayIcon is not null)
        {
            _trayIcon.BalloonTipClicked += OnUpdateBalloonClicked;
            _trayIcon.ShowBalloonTip(8000, Loc.T("WinModes update available"),
                Loc.F("Version {0} is out (you have v{1}). Click to see it on the About page.", status.LatestTag, AppInfo.Version), Forms.ToolTipIcon.Info);
        }
    }

    private void OnUpdateBalloonClicked(object? sender, EventArgs e)
    {
        _trayIcon!.BalloonTipClicked -= OnUpdateBalloonClicked;
        ShowWindow();
        _window?.NavigateTo(typeof(Pages.AboutPage));
    }

    /// <summary>Turns the tray meter and the desktop widget on or off to match the settings.</summary>
    internal void ApplyDisplaySettings()
    {
        var settings = Services.AppSettings.Load();

        // The user picks the desktop, the taskbar or both; each one is created and closed on its own.
        var onTaskbar = settings.ShowDesktopWidget && settings.Widget.Placement != Services.WidgetPlacement.Desktop;
        var floating = settings.ShowDesktopWidget && settings.Widget.Placement != Services.WidgetPlacement.Taskbar;

        if (onTaskbar && _taskbarWidget is null)
        {
            _taskbarWidget = new TaskbarWidgetWindow();
            _taskbarWidget.OpenAppRequested += (_, _) => ShowWindow();
            _taskbarWidget.OpenSettingsRequested += (_, _) =>
            {
                ShowWindow();
                _window?.NavigateTo(typeof(Pages.WidgetPage));
            };
            _taskbarWidget.HideRequested += (_, _) =>
            {
                (Services.AppSettings.Load() with { ShowDesktopWidget = false }).Save();
                ApplyDisplaySettings();
            };
            _taskbarWidget.Show();
        }
        else if (!onTaskbar && _taskbarWidget is not null)
        {
            _taskbarWidget.Close();
            _taskbarWidget = null;
        }

        _taskbarWidget?.Apply(settings.Widget);

        if (floating && _widget is null)
        {
            _widget = new WidgetWindow();
            _widget.OpenAppRequested += (_, _) => ShowWindow();
            _widget.HideRequested += (_, _) =>
            {
                (Services.AppSettings.Load() with { ShowDesktopWidget = false }).Save();
                ApplyDisplaySettings();
            };
            _widget.OpenAiToolsRequested += (_, _) =>
            {
                ShowWindow();
                _window?.NavigateTo(typeof(Pages.AiToolsPage));
            };
            _widget.OpenSettingsRequested += (_, _) =>
            {
                ShowWindow();
                _window?.NavigateTo(typeof(Pages.WidgetPage));
            };
            _widget.Show();
        }
        else if (!floating && _widget is not null)
        {
            _widget.Close();
            _widget = null;
        }

        _widget?.Apply(settings.Widget);
        _liveStats.RefreshInterval = TimeSpan.FromSeconds(Math.Clamp(settings.Widget.RefreshSeconds, 1, 10));

        if (!settings.ShowAiMemoryInTray)
        {
            _trayMeter?.Reset();
        }

        ApplyHotkeys(settings.EnableHotkeys);
        _autoSwitcher ??= new Services.AutoSwitcher(SwitchFromTrayAsync);
        _autoSwitcher.Apply(settings.AutoSwitch);

        // Sample in the background only while a feature needs it.
        var needed = settings.ShowAiMemoryInTray || settings.ShowDesktopWidget || settings.AiMemoryAlertGb > 0
            || settings.AiToolAlertGb > 0 || settings.AutoEndIdleMinutes > 0 || settings.RecordUsageHistory;
        if (!settings.RecordUsageHistory)
        {
            AppServices.Usage.Flush();
        }
        if (needed && !_listening)
        {
            _liveStats.Updated += OnStats;
            _listening = true;
        }
        else if (!needed && _listening)
        {
            _liveStats.Updated -= OnStats;
            _listening = false;
        }
    }

    internal void ShowOnboarding()
    {
        ShowWindow();
        _window?.ShowOnboarding();
    }

    internal void MoveWidget(WidgetCorner corner) => _widget?.MoveTo(corner);

    private void ApplyHotkeys(bool enabled)
    {
        const uint KeyZero = 0x30;

        _hotkeys?.Clear();
        if (!enabled)
        {
            return;
        }

        _hotkeys ??= new Services.HotkeyService();
        var modes = ModeCatalog.Load();
        // Ctrl+Alt+1..9 follow the order of the mode cards.
        for (var i = 0; i < Math.Min(modes.Count, 9); i++)
        {
            var profile = modes[i].Profile;
            _hotkeys.Register(KeyZero + (uint)(i + 1), async () =>
            {
                if (!profile.Mode.Equals(Services.ModeSwitcher.ActiveMode, StringComparison.OrdinalIgnoreCase))
                {
                    await SwitchFromTrayAsync(profile.Label, () => AppServices.Switcher.ActivateAsync(profile));
                }
            });
        }

        _hotkeys.Register(KeyZero, async () =>
        {
            if (Services.ModeSwitcher.ActiveMode is not null)
            {
                await SwitchFromTrayAsync(Loc.T("Deactivate"), AppServices.Switcher.UndoAsync);
            }
        });
    }

    private void OnStats(object? sender, Services.StatsReading reading)
    {
        const double MbPerGb = 1024;
        // Re-arm the alert only after usage falls clearly below the limit, so it does not repeat on every sample.
        const double RearmRatio = 0.9;

        var settings = Services.AppSettings.Load();
        if (settings.ShowAiMemoryInTray)
        {
            _trayMeter?.Show(reading);
        }

        _taskbarWidget?.Show(reading);

        if (_widget is not null)
        {
            _widget.Show(reading);
            // Checked on each sample: cheap, and a second or two of delay is fine for hiding.
            var hide = settings.Widget.HideOnFullScreen && WidgetWindow.IsFullScreenAppActive();
            _widget.Visibility = hide ? Visibility.Hidden : Visibility.Visible;
        }

        CheckToolAlerts(reading, settings.AiToolAlertGb);
        CheckPlanAlerts(settings.Widget);
        EndIdleSessions(reading, settings.AutoEndIdleMinutes);
        RecordUsage(reading, settings.RecordUsageHistory);

        if (settings.AiMemoryAlertGb <= 0)
        {
            return;
        }

        var usedGb = reading.AiMemoryMb / MbPerGb;
        if (usedGb >= settings.AiMemoryAlertGb && !_alertRaised)
        {
            _alertRaised = true;
            var top = reading.AiTools.Count > 0 ? " " + Loc.F("Largest: {0}.", reading.AiTools[0].Name) : "";
            _trayIcon?.ShowBalloonTip(6000, Loc.T("WinModes - AI tools memory"),
                Loc.F("AI tools use {0:0.0} GB, above your {1} GB limit.", usedGb, settings.AiMemoryAlertGb) + top,
                Forms.ToolTipIcon.Warning);
        }
        else if (usedGb < settings.AiMemoryAlertGb * RearmRatio)
        {
            _alertRaised = false;
        }
    }

    private readonly HashSet<string> _planAlerted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>One notification when a plan falls under 10 % left; armed again once it is clearly above or has reset.</summary>
    private void CheckPlanAlerts(Services.WidgetSettings widget)
    {
        const double LowPercent = 10;
        const double RearmPercent = 15;

        if (!widget.ShowSubscriptions || (!widget.ClaudeLowAlert && !widget.CodexLowAlert))
        {
            _planAlerted.Clear();
            return;
        }

        var now = DateTimeOffset.Now;
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        foreach (var status in Services.SubscriptionMonitor.Current)
        {
            var wanted = status.Tool == "Claude" ? widget.ClaudeLowAlert : widget.CodexLowAlert;
            if (!wanted || status.Primary is not { } limit || limit.HasReset(now) || limit.RemainingPercent >= RearmPercent)
            {
                _planAlerted.Remove(status.Tool);
            }
            else if (limit.RemainingPercent < LowPercent && _planAlerted.Add(status.Tool))
            {
                var (value, detail, _) = WinModes.Core.Usage.Subscriptions.Describe(status, now, culture);
                _trayIcon?.ShowBalloonTip(6000, $"WinModes - {status.Tool} {status.Plan}", $"{value}. {detail.Split('\n')[0]}.", Forms.ToolTipIcon.Warning);
            }
        }
    }

    private void CheckToolAlerts(Services.StatsReading reading, int limitGb)
    {
        const double MbPerGb = 1024;
        const double RearmRatio = 0.9;

        if (limitGb <= 0)
        {
            _toolAlertsRaised.Clear();
            return;
        }

        foreach (var tool in reading.AiTools)
        {
            var usedGb = tool.MemoryMb / MbPerGb;
            if (usedGb >= limitGb && _toolAlertsRaised.Add(tool.Name))
            {
                _trayIcon?.ShowBalloonTip(6000, $"WinModes - {tool.Name}",
                    Loc.F("{0} uses {1:0.0} GB, above your {2} GB limit per tool.", tool.Name, usedGb, limitGb),
                    Forms.ToolTipIcon.Warning);
            }
            else if (usedGb < limitGb * RearmRatio)
            {
                _toolAlertsRaised.Remove(tool.Name);
            }
        }
    }

    /// <summary>Opt-in: ends project sessions that used no CPU for the chosen time. Desktop apps are never ended.</summary>
    private void EndIdleSessions(Services.StatsReading reading, int idleMinutes)
    {
        if (idleMinutes <= 0)
        {
            return;
        }

        var limit = TimeSpan.FromMinutes(idleMinutes);
        foreach (var session in reading.Sessions)
        {
            if (!Services.AiSessions.HasProjectFolder(session) || Services.AiActivityTracker.IdleFor(session) < limit)
            {
                continue;
            }

            var project = Services.Privacy.Project(Services.AiSessions.ProjectName(session));
            if (Services.AiSessions.End(session))
            {
                _trayIcon?.ShowBalloonTip(5000, Loc.T("WinModes - idle session ended"),
                    Loc.F("{0} in {1} used no CPU for {2} minutes and was closed.", session.Tool.Name, project, idleMinutes), Forms.ToolTipIcon.Info);
            }
        }
    }

    private void RecordUsage(Services.StatsReading reading, bool enabled)
    {
        if (!enabled)
        {
            return;
        }

        var now = DateTime.Now;
        AppServices.Usage.Record(now, reading.Sessions.Select(session => new WinModes.Core.Usage.UsageSample(
            session.Tool.Name, Services.AiSessions.ProjectName(session), session.TotalMemoryMb)));
        if (now - _lastUsageFlush >= UsageFlushInterval)
        {
            _lastUsageFlush = now;
            _ = Task.Run(AppServices.Usage.Flush);
        }
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        // Rebuilt each time it opens, so the active mode is always up to date.
        menu.Opening += (_, _) => FillTrayMenu(menu);
        FillTrayMenu(menu);

        var icon = new Forms.NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "WinModes",
            ContextMenuStrip = menu,
            Visible = true,
        };
        icon.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
            {
                ShowWindow();
            }
        };
        return icon;
    }

    private void FillTrayMenu(Forms.ContextMenuStrip menu)
    {
        menu.Items.Clear();
        menu.Items.Add(Loc.T("Open WinModes"), null, (_, _) => ShowWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());

        var active = Services.ModeSwitcher.ActiveMode;
        foreach (var entry in ModeCatalog.Load())
        {
            var profile = entry.Profile;
            var isActive = profile.Mode.Equals(active, StringComparison.OrdinalIgnoreCase);
            var item = new Forms.ToolStripMenuItem(Loc.F(isActive ? "{0} mode (active)" : "Activate {0} mode", profile.Label))
            {
                Checked = isActive,
                Enabled = !isActive,
            };
            item.Click += async (_, _) => await SwitchFromTrayAsync(profile.Label, () => AppServices.Switcher.ActivateAsync(profile));
            menu.Items.Add(item);
        }

        var undo = new Forms.ToolStripMenuItem(Loc.T("Deactivate current mode")) { Enabled = active is not null };
        undo.Click += async (_, _) => await SwitchFromTrayAsync(Loc.T("Deactivate"), AppServices.Switcher.UndoAsync);
        menu.Items.Add(undo);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Loc.T("Quit"), null, (_, _) => Shutdown());
    }

    /// <summary>Runs a switch started outside the main window and reports the outcome in a notification.</summary>
    internal async Task SwitchFromTrayAsync(string title, Func<Task<Services.SwitchReport>> action)
    {
        try
        {
            var report = await action();
            _trayIcon?.ShowBalloonTip(4000, $"WinModes - {title}",
                report.Succeeded ? string.Join("\n", report.Lines.Take(3)) : report.Lines[0],
                report.Succeeded ? Forms.ToolTipIcon.Info : Forms.ToolTipIcon.Warning);
        }
        catch (ProfileException ex)
        {
            _trayIcon?.ShowBalloonTip(4000, "WinModes", ex.Message, Forms.ToolTipIcon.Warning);
        }
    }

    private static System.Drawing.Icon LoadAppIcon() =>
        (Environment.ProcessPath is { } path ? System.Drawing.Icon.ExtractAssociatedIcon(path) : null)
            ?? System.Drawing.SystemIcons.Application;

    private void ShowWindow()
    {
        if (_window is null)
        {
            return;
        }

        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        AppServices.Usage.Flush();
        if (_trayIcon is not null)
        {
            // Hide first so Windows does not leave a ghost icon behind.
            _trayIcon.Visible = false;
        }

        Dispose();
    }

    public void Dispose()
    {
        _hotkeys?.Dispose();
        _taskbarWidget?.Close();
        _trayMeter?.Dispose();
        _trayIcon?.Dispose();
        _singleInstance?.Dispose();
        GC.SuppressFinalize(this);
    }
}
