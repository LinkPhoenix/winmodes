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

    private void OnStartup(object sender, StartupEventArgs e)
    {
        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        var root = RepositoryLocator.Find(AppContext.BaseDirectory) ?? RepositoryLocator.Find(Environment.CurrentDirectory);
        if (root is null)
        {
            MessageBox.Show("Cannot find the win-modes data (data/protected.json).", "WinModes", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        var store = new ProfileStore(Path.Combine(root, "profiles"));
        var policy = ProtectionPolicy.Load(Path.Combine(root, "data", "protected.json"));
        var planner = new ModePlanner(new WindowsSystemProbe(), policy);

        AppServices.Initialize(store, planner, policy, Path.Combine(root, "profiles"));

        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark, Wpf.Ui.Controls.WindowBackdropType.None, updateAccent: false);
        // Brand accent instead of the Windows one, so the app looks the same on every PC.
        Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(Palette.Brand, Wpf.Ui.Appearance.ApplicationTheme.Dark);
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

    private async void StartSession(string[] args)
    {
        // At sign-in the app starts hidden; the tray icon opens the window. The first run always shows the guide.
        if (!args.Contains(Services.AppSettings.MinimizedArgument) || !Services.AppSettings.Load().OnboardingDone)
        {
            _window!.Show();
        }

        var autoMode = Services.AppSettings.Load().AutoActivateMode;
        if (autoMode is null || Services.ModeSwitcher.ActiveMode is not null)
        {
            return;
        }

        try
        {
            var report = await AppServices.Switcher.ActivateAsync(AppServices.Store.Load(autoMode));
            _trayIcon?.ShowBalloonTip(4000, "WinModes",
                report.Succeeded ? $"{autoMode} mode activated automatically." : report.Lines[0], Forms.ToolTipIcon.Info);
        }
        catch (ProfileException ex)
        {
            _trayIcon?.ShowBalloonTip(4000, "WinModes", ex.Message, Forms.ToolTipIcon.Warning);
        }
    }

    /// <summary>Turns the tray meter and the desktop widget on or off to match the settings.</summary>
    internal void ApplyDisplaySettings()
    {
        var settings = Services.AppSettings.Load();

        if (settings.ShowDesktopWidget && _widget is null)
        {
            _widget = new WidgetWindow();
            _widget.OpenAppRequested += (_, _) => ShowWindow();
            _widget.HideRequested += (_, _) =>
            {
                (Services.AppSettings.Load() with { ShowDesktopWidget = false }).Save();
                ApplyDisplaySettings();
            };
            _widget.OpenSettingsRequested += (_, _) =>
            {
                ShowWindow();
                _window?.NavigateTo(typeof(Pages.WidgetPage));
            };
            _widget.Show();
        }
        else if (!settings.ShowDesktopWidget && _widget is not null)
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
        var needed = settings.ShowAiMemoryInTray || settings.ShowDesktopWidget || settings.AiMemoryAlertGb > 0;
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
                await SwitchFromTrayAsync("Deactivate", AppServices.Switcher.UndoAsync);
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

        _widget?.Show(reading);

        if (settings.AiMemoryAlertGb <= 0)
        {
            return;
        }

        var usedGb = reading.AiMemoryMb / MbPerGb;
        if (usedGb >= settings.AiMemoryAlertGb && !_alertRaised)
        {
            _alertRaised = true;
            var top = reading.AiTools.Count > 0 ? $" Largest: {reading.AiTools[0].Name}." : "";
            _trayIcon?.ShowBalloonTip(6000, "WinModes - AI tools memory",
                string.Create(System.Globalization.CultureInfo.CurrentCulture,
                    $"AI tools use {usedGb:0.0} GB, above your {settings.AiMemoryAlertGb} GB limit.{top}"),
                Forms.ToolTipIcon.Warning);
        }
        else if (usedGb < settings.AiMemoryAlertGb * RearmRatio)
        {
            _alertRaised = false;
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
        menu.Items.Add("Open WinModes", null, (_, _) => ShowWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());

        var active = Services.ModeSwitcher.ActiveMode;
        foreach (var entry in ModeCatalog.Load())
        {
            var profile = entry.Profile;
            var isActive = profile.Mode.Equals(active, StringComparison.OrdinalIgnoreCase);
            var item = new Forms.ToolStripMenuItem(isActive ? $"{profile.Label} mode (active)" : $"Activate {profile.Label} mode")
            {
                Checked = isActive,
                Enabled = !isActive,
            };
            item.Click += async (_, _) => await SwitchFromTrayAsync(profile.Label, () => AppServices.Switcher.ActivateAsync(profile));
            menu.Items.Add(item);
        }

        var undo = new Forms.ToolStripMenuItem("Deactivate current mode") { Enabled = active is not null };
        undo.Click += async (_, _) => await SwitchFromTrayAsync("Deactivate", AppServices.Switcher.UndoAsync);
        menu.Items.Add(undo);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Shutdown());
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
        _trayMeter?.Dispose();
        _trayIcon?.Dispose();
        _singleInstance?.Dispose();
        GC.SuppressFinalize(this);
    }
}
