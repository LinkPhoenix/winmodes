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

    private Mutex? _singleInstance;
    private Forms.NotifyIcon? _trayIcon;
    private MainWindow? _window;

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
        var pageIndex = Array.IndexOf(e.Args, "--page");
        _window = new MainWindow(pageIndex >= 0 && pageIndex + 1 < e.Args.Length ? e.Args[pageIndex + 1] : null);
        _trayIcon = CreateTrayIcon();
        StartSession(e.Args);
    }

    private async void StartSession(string[] args)
    {
        // At sign-in the app starts hidden; the tray icon opens the window.
        if (!args.Contains(Services.AppSettings.MinimizedArgument))
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

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open WinModes", null, (_, _) => ShowWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Shutdown());

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
        _trayIcon?.Dispose();
        _singleInstance?.Dispose();
        GC.SuppressFinalize(this);
    }
}
