using System.IO;
using System.Windows;
using System.Windows.Threading;
using WinModes.Core;
using WinModes.Core.Notifications;
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
    private Services.Notifier? _notifier;
    private readonly NoticeLedger _ledger = NoticeLedger.Load(NoticeLedger.DefaultPath);
    private DispatcherTimer? _planTimer;
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
            _notifier?.Show(Services.NoticeKind.Problem, "WinModes",
                Loc.T("WinModes hit an unexpected error and kept running. The details are in errors.log in %LocalAppData%\\WinModes."), Forms.ToolTipIcon.Warning, durationMs: 5000));

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
        var planner = new ModePlanner(new WindowsSystemProbe(), policy, new Services.CatalogTweakProbe());

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
        _notifier = new Services.Notifier(_trayIcon);
        Services.AccountSession.SessionEnded += OnSessionEnded;
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
        WarnAboutBrokenStartup();
        // A copy of a power plan left by a mode that never ended cleanly (a crash, a power cut) is removed once the PC has settled.
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(40));
            try
            {
                await Services.ModeSwitcher.RemoveOrphanPowerPlansAsync();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
            {
                // The plan list could not be read: try again at the next start.
            }
        });

        // Reading the state of every Optimize setting takes a moment: do it once the PC has settled, so the page opens with it already read.
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(45));
            try
            {
                await Services.OptimizeSnapshot.TakeAsync();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                // Nothing is kept: the page reads it when it is opened.
            }
        });

        // The same for the apps of the PC (the Debloat page): two PowerShell commands, so a little later, when nothing else is loading.
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(60));
            try
            {
                await Services.DebloatSnapshot.TakeAsync();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                // Nothing is kept: the page reads it when it is opened.
            }
        });

        var autoMode = Services.AppSettings.Load().AutoActivateMode;
        if (autoMode is null || Services.ModeSwitcher.ActiveMode is not null)
        {
            return;
        }

        try
        {
            var report = await AppServices.Switcher.ActivateAsync(AppServices.Store.Load(autoMode));
            _notifier?.Show(report.Succeeded ? Services.NoticeKind.Mode : Services.NoticeKind.Problem, "WinModes",
                report.Succeeded ? Loc.F("{0} mode activated automatically.", autoMode) : report.Lines[0], Forms.ToolTipIcon.Info, durationMs: 4000);
        }
        catch (ProfileException ex)
        {
            _notifier?.Show(Services.NoticeKind.Problem, "WinModes", ex.Message, Forms.ToolTipIcon.Warning, durationMs: 4000);
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        if (!Services.AppSettings.Load().CheckForUpdates)
        {
            return;
        }

        // Announced once per release: the About page keeps showing it, but the notification is not repeated at each start.
        var status = await Services.UpdateChecker.CheckAsync();
        if (status.IsNewer && _notifier is not null && _ledger.UpdateTag != status.LatestTag
            && _notifier.Show(Services.NoticeKind.Update, Loc.T("WinModes update available"),
                Loc.F("Version {0} is out (you have v{1}). Click to see it on the About page.", status.LatestTag, AppInfo.FullVersion),
                Forms.ToolTipIcon.Info, () => OpenPage(typeof(Pages.AboutPage)), durationMs: 8000))
        {
            _ledger.UpdateTag = status.LatestTag;
            _ledger.Changed = true;
            SaveLedger();
        }
    }

    /// <summary>
    /// "Start with Windows" is on but its entry points to a file that is gone: Windows would skip it silently, so the user is told
    /// once per broken entry, with the way to fix it.
    /// </summary>
    private void WarnAboutBrokenStartup()
    {
        var command = Services.AppSettings.StartupCommand;
        if (Services.AppSettings.StartupState != StartupState.TargetMissing || _ledger.StartupWarned == command || _notifier is null)
        {
            return;
        }

        if (_notifier.Show(Services.NoticeKind.Problem, Loc.T("WinModes will not start with Windows"),
            Loc.T("Its startup entry points to a file that no longer exists. Fix it on the Settings page."), Forms.ToolTipIcon.Warning, () => OpenPage(typeof(Pages.SettingsPage))))
        {
            _ledger.StartupWarned = command;
            _ledger.Changed = true;
            SaveLedger();
        }
    }

    private void OpenPage(Type page)
    {
        ShowWindow();
        _window?.NavigateTo(page);
    }

    private void SaveLedger()
    {
        if (_ledger.Changed)
        {
            _ledger.Save(NoticeLedger.DefaultPath);
        }
    }

    /// <summary>The provider ended a WinModes sign-in (the session was revoked): the usage can no longer be read until the user signs in again.</summary>
    private void OnSessionEnded(object? sender, WinModes.Core.Accounts.AccountProvider provider) =>
        _notifier?.Show(Services.NoticeKind.SignIn, Loc.F("{0} sign-in expired", provider.DisplayName),
            Loc.T("The usage can no longer be read. Sign in again on the Widget page."), Forms.ToolTipIcon.Warning, () => OpenPage(typeof(Pages.WidgetPage)));

    /// <summary>The "test notification" button of the Notifications page; shown whatever the choices, to check that Windows lets it through.</summary>
    internal void ShowTestNotice() =>
        _notifier?.Show(Services.NoticeKind.Test, "WinModes", Loc.T("This is a test notification: they work."));

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

        ApplyPlanWatch(settings);
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
        EndIdleSessions(reading, settings.AutoEndIdleMinutes);
        RecordUsage(reading, settings.RecordUsageHistory);

        if (settings.AiMemoryAlertGb <= 0)
        {
            return;
        }

        var usedGb = reading.AiMemoryMb / MbPerGb;
        if (usedGb >= settings.AiMemoryAlertGb && !_alertRaised)
        {
            var top = reading.AiTools.Count > 0 ? " " + Loc.F("Largest: {0}.", reading.AiTools[0].Name) : "";
            // Raised only once shown: held back by the quiet hours, it is shown when they end if the memory is still above.
            _alertRaised = _notifier?.Show(Services.NoticeKind.Memory, Loc.T("WinModes - AI tools memory"),
                Loc.F("AI tools use {0:0.0} GB, above your {1} GB limit.", usedGb, settings.AiMemoryAlertGb) + top,
                Forms.ToolTipIcon.Warning) == true;
        }
        else if (usedGb < settings.AiMemoryAlertGb * RearmRatio)
        {
            _alertRaised = false;
        }
    }

    private static readonly TimeSpan PlanWatchInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Watches the plans for notices while they are wanted and a plan is read at all: independent of the widget, which only shows them.
    /// </summary>
    private void ApplyPlanWatch(Services.AppSettings settings)
    {
        var wanted = settings.Notifications.Enabled && settings.Widget.ShowSubscriptions;
        if (wanted && _planTimer is null)
        {
            _planTimer = new DispatcherTimer { Interval = PlanWatchInterval };
            _planTimer.Tick += (_, _) => CheckPlans();
            _planTimer.Start();
            // Asking now starts the first read; its result is picked up at the next tick.
            CheckPlans();
        }
        else if (!wanted && _planTimer is not null)
        {
            _planTimer.Stop();
            _planTimer = null;
        }
    }

    private void CheckPlans()
    {
        var settings = Services.AppSettings.Load();
        var widget = settings.Widget;
        var statuses = Services.SubscriptionMonitor.Get(widget.ShowClaudePlan, widget.ShowCodexPlan, widget.ShowGrokPlan);
        // During the quiet hours nothing is decided: the limits are judged again when they end, so no notice is lost.
        if (!Services.SubscriptionMonitor.HasRead || _notifier is null || settings.Notifications.IsQuietNow())
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var notices = PlanNoticeEngine.Evaluate(_ledger, statuses, tool => settings.Notifications.ChoiceFor(tool, widget), settings.Notifications.LowPercent, now);
        foreach (var notice in notices)
        {
            var warning = notice.Kind is PlanNoticeKind.Low or PlanNoticeKind.Reached;
            _notifier.Show(Services.NoticeKind.Plan, notice.Title, notice.Message(now, culture), warning ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
        }

        SaveLedger();
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
            if (usedGb >= limitGb && !_toolAlertsRaised.Contains(tool.Name))
            {
                if (_notifier?.Show(Services.NoticeKind.Memory, $"WinModes - {tool.Name}",
                    Loc.F("{0} uses {1:0.0} GB, above your {2} GB limit per tool.", tool.Name, usedGb, limitGb),
                    Forms.ToolTipIcon.Warning) == true)
                {
                    _toolAlertsRaised.Add(tool.Name);
                }
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
                _notifier?.Show(Services.NoticeKind.Idle, Loc.T("WinModes - idle session ended"),
                    Loc.F("{0} in {1} used no CPU for {2} minutes and was closed.", session.Tool.Name, project, idleMinutes), Forms.ToolTipIcon.Info, durationMs: 5000);
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

        var automatic = new Forms.ToolStripMenuItem(Loc.T("Switch modes automatically")) { Checked = Services.AppSettings.Load().AutoSwitch.Enabled };
        automatic.Click += (_, _) => Services.AutoSwitchSetup.SetEnabled(!Services.AppSettings.Load().AutoSwitch.Enabled);
        menu.Items.Add(automatic);
        if (_autoSwitcher is { } switcher && Services.AppSettings.Load().AutoSwitch.Enabled)
        {
            var pause = new Forms.ToolStripMenuItem(Loc.T(switcher.IsPaused ? "Resume automatic switching" : "Pause automatic switching for 1 hour"));
            pause.Click += (_, _) =>
            {
                if (switcher.IsPaused)
                {
                    switcher.Resume();
                }
                else
                {
                    switcher.PauseFor(TimeSpan.FromHours(1));
                }
            };
            menu.Items.Add(pause);
        }

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Loc.T("Quit"), null, (_, _) =>
        {
            if (Services.OperationStatus.Current?.IsRunning == true)
            {
                _window?.Show();
                MessageBox.Show(Loc.T("An operation is still running. Wait for it to finish before quitting."), "WinModes",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Shutdown();
        });
    }

    /// <summary>Runs a switch started outside the main window and reports the outcome in a notification. Returns whether it worked.</summary>
    internal async Task<bool> SwitchFromTrayAsync(string title, Func<Task<Services.SwitchReport>> action)
    {
        try
        {
            var report = await action();
            _notifier?.Show(report.Succeeded ? Services.NoticeKind.Mode : Services.NoticeKind.Problem, $"WinModes - {title}",
                report.Succeeded ? string.Join("\n", report.Lines.Take(3)) : report.Lines[0],
                report.Succeeded ? Forms.ToolTipIcon.Info : Forms.ToolTipIcon.Warning, durationMs: 4000);
            return report.Succeeded;
        }
        catch (ProfileException ex)
        {
            _notifier?.Show(Services.NoticeKind.Problem, "WinModes", ex.Message, Forms.ToolTipIcon.Warning, durationMs: 4000);
            return false;
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
        // Only a minimized window is restored: one that was maximized before it went to the tray stays maximized.
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

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
