using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinModes.App.Controls;
using WinModes.App.Pages;
using WinModes.App.Services;
using Wpf.Ui.Controls;

namespace WinModes.App;

/// <summary>Application shell: title bar and navigation. Pages hold the content.</summary>
public partial class MainWindow : FluentWindow
{
    private const int MinimumZoom = 80;
    private const int MaximumZoom = 140;
    private const int ZoomStep = 10;
    private readonly DispatcherTimer _placementSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private WindowPreferences? _normalPlacement;
    private bool _placementReady;
    private int _zoomPercent;
    private Guid? _dismissedOperation;
    private static readonly Dictionary<string, Type> PagesByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dashboard"] = typeof(DashboardPage),
        ["modes"] = typeof(ModesPage),
        ["processes"] = typeof(ProcessesPage),
        ["ai"] = typeof(AiToolsPage),
        ["usage"] = typeof(UsagePage),
        ["services"] = typeof(ServicesPage),
        ["optimize"] = typeof(OptimizePage),
        ["debloat"] = typeof(DebloatPage),
        ["startup"] = typeof(StartupPage),
        ["software"] = typeof(SoftwarePage),
        ["automation"] = typeof(AutomationPage),
        ["widget"] = typeof(WidgetPage),
        ["notifications"] = typeof(NotificationsPage),
        ["history"] = typeof(HistoryPage),
        ["settings"] = typeof(SettingsPage),
        ["protection"] = typeof(ProtectionPage),
        ["about"] = typeof(AboutPage),
    };

    /// <param name="startPage">Page name from the <c>--page</c> argument; unknown or null opens the dashboard.</param>
    /// <param name="showOnboarding">Show the first-run guide whatever the saved setting says.</param>
    public MainWindow(string? startPage = null, bool showOnboarding = false)
    {
        InitializeComponent();
        var settings = AppSettings.Load();
        _normalPlacement = settings.MainWindowPlacement;
        SetZoom(settings.UiZoomPercent, save: false);
        _placementSaveTimer.Tick += (_, _) => SavePlacement();
        LocationChanged += (_, _) => QueuePlacementSave();
        SizeChanged += (_, _) => QueuePlacementSave();
        StateChanged += (_, _) => QueuePlacementSave();
        OperationStatus.Changed += OnOperationChanged;
        Closed += (_, _) =>
        {
            _placementSaveTimer.Stop();
            OperationStatus.Changed -= OnOperationChanged;
        };
        UpdateOperationStatus();
        // Keep the initial window inside the working area at the current display scale.
        var workArea = System.Windows.SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, workArea.Width);
        MinHeight = Math.Min(MinHeight, workArea.Height);
        Width = Math.Min(Width, workArea.Width);
        Height = Math.Min(Height, workArea.Height);
        VersionText.Text = $"v{AppInfo.Version}";
        BetaBadges.Show(BetaBadge, BetaBadgeText);

        var page = startPage is not null && PagesByName.TryGetValue(startPage, out var requested) ? requested : typeof(DashboardPage);
        Loaded += (_, _) =>
        {
            WindowPlacement.Restore(this, settings.MainWindowPlacement);
            _placementReady = true;
            _normalPlacement = WindowPlacement.Capture(this, _normalPlacement);
            RootNavigation.Navigate(page);
            if (showOnboarding || !Services.AppSettings.Load().OnboardingDone)
            {
                ShowOnboarding();
            }
        };
    }

    public void ShowOnboarding()
    {
        var guide = new Controls.OnboardingView();
        guide.Completed += (_, openModes) =>
        {
            OnboardingHost.Visibility = System.Windows.Visibility.Collapsed;
            OnboardingHost.Content = null;
            PaneToggle.IsEnabled = true;
            // Reload the current page so it reflects the options just chosen.
            RootNavigation.Navigate(openModes ? typeof(ModesPage) : typeof(DashboardPage));
        };
        OnboardingHost.Content = guide;
        OnboardingHost.Visibility = System.Windows.Visibility.Visible;
        PaneToggle.IsEnabled = false;
    }

    private void OnTogglePane(object sender, System.Windows.RoutedEventArgs e) => RootNavigation.IsPaneOpen = !RootNavigation.IsPaneOpen;

    private void OnSupport(object sender, System.Windows.RoutedEventArgs e) => new SupportWindow { Owner = this }.ShowDialog();

    private void OnConfiguration(object sender, RoutedEventArgs e) => new ConfigurationWindow { Owner = this }.ShowDialog();

    private void OnZoomOut(object sender, RoutedEventArgs e) => SetZoom(_zoomPercent - ZoomStep);
    private void OnZoomIn(object sender, RoutedEventArgs e) => SetZoom(_zoomPercent + ZoomStep);
    private void OnZoomReset(object sender, RoutedEventArgs e) => SetZoom(100);

    private void OnOperationHistory(object sender, RoutedEventArgs e) => RootNavigation.Navigate(typeof(HistoryPage));

    private void OnDismissOperation(object sender, RoutedEventArgs e)
    {
        _dismissedOperation = OperationStatus.Current?.Id;
        UpdateOperationStatus();
    }

    private void OnOperationChanged()
    {
        if (!Dispatcher.HasShutdownStarted)
        {
            _ = Dispatcher.BeginInvoke(UpdateOperationStatus);
        }
    }

    private void UpdateOperationStatus()
    {
        var current = OperationStatus.Current;
        OperationStrip.Visibility = current is null || current.Id == _dismissedOperation ? Visibility.Collapsed : Visibility.Visible;
        if (current is null)
        {
            return;
        }

        var state = Loc.T(current.IsRunning ? "In progress" : current.Failed ? "Completed with errors" : "Completed");
        OperationTitle.Text = $"{current.Title} · {state}";
        OperationDetail.Text = current.Total > 0
            ? Loc.F("{0} of {1} items completed · {2}", current.Completed, current.Total, current.Detail)
            : current.Detail;
        OperationProgress.IsIndeterminate = current.IsRunning && current.Total == 0;
        OperationProgress.Maximum = Math.Max(1, current.Total);
        OperationProgress.Value = current.Completed;
        OperationProgress.Visibility = current.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        DismissOperationButton.IsEnabled = !current.IsRunning;
    }

    private void SetZoom(int percent, bool save = true)
    {
        _zoomPercent = Math.Clamp(percent, MinimumZoom, MaximumZoom);
        // One finite navigation viewport remains finite at every scale; the lists retain their virtualizing panels.
        ContentZoom.ScaleX = ContentZoom.ScaleY = _zoomPercent / 100d;
        ZoomResetButton.Content = $"{_zoomPercent}%";
        ZoomOutButton.IsEnabled = _zoomPercent > MinimumZoom;
        ZoomInButton.IsEnabled = _zoomPercent < MaximumZoom;
        if (save)
        {
            SaveUiSettings(AppSettings.Load() with { UiZoomPercent = _zoomPercent });
        }
    }

    private void QueuePlacementSave()
    {
        if (!_placementReady)
        {
            return;
        }

        _normalPlacement = WindowPlacement.Capture(this, _normalPlacement);
        _placementSaveTimer.Stop();
        _placementSaveTimer.Start();
    }

    private void SavePlacement()
    {
        _placementSaveTimer.Stop();
        if (_placementReady && _normalPlacement is not null)
        {
            SaveUiSettings(AppSettings.Load() with { MainWindowPlacement = _normalPlacement });
        }
    }

    private static void SaveUiSettings(AppSettings settings)
    {
        try
        {
            settings.SaveUiPreference();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Window movement must remain usable even when preferences cannot be written.
            System.Diagnostics.Trace.TraceWarning("Could not save window preferences: {0}", ex.GetType().Name);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0
            && (Keyboard.Modifiers & ~(ModifierKeys.Control | ModifierKeys.Shift)) == 0)
        {
            var zoom = e.Key switch
            {
                Key.Add or Key.OemPlus => _zoomPercent + ZoomStep,
                Key.Subtract or Key.OemMinus => _zoomPercent - ZoomStep,
                Key.D0 or Key.NumPad0 => 100,
                _ => (int?)null,
            };
            if (zoom is { } value)
            {
                SetZoom(value);
                e.Handled = true;
                return;
            }
        }

        if (OnboardingHost.Visibility == System.Windows.Visibility.Visible)
        {
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key >= Key.D1 && e.Key <= Key.D9)
        {
            Type[] destinations = [typeof(DashboardPage), typeof(ModesPage), typeof(ProcessesPage), typeof(AiToolsPage), typeof(UsagePage), typeof(ServicesPage), typeof(OptimizePage), typeof(StartupPage), typeof(DebloatPage)];
            RootNavigation.Navigate(destinations[(int)e.Key - (int)Key.D1]);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && SearchField.FocusIn(RootNavigation))
        {
            e.Handled = true;
        }
    }

    public void NavigateTo(Type pageType) => RootNavigation.Navigate(pageType);

    internal OptimizePage? OpenOptimize()
    {
        RootNavigation.Navigate(typeof(OptimizePage));
        return FindOptimizePage(RootNavigation);
    }

    private static OptimizePage? FindOptimizePage(DependencyObject parent)
    {
        if (parent is OptimizePage page)
        {
            return page;
        }

        if (parent is System.Windows.Controls.ContentControl { Content: OptimizePage contentPage })
        {
            return contentPage;
        }

        if (parent is System.Windows.Controls.ContentPresenter { Content: OptimizePage presentedPage })
        {
            return presentedPage;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            if (FindOptimizePage(VisualTreeHelper.GetChild(parent, index)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        SavePlacement();

        if (!AppSettings.Load().CloseToTray && OperationStatus.Current is { IsRunning: true })
        {
            e.Cancel = true;
            System.Windows.MessageBox.Show(this,
                Loc.T("An operation is still running. Wait for it to finish before quitting WinModes."),
                Loc.T("Operation in progress"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            base.OnClosing(e);
            return;
        }

        if (Services.AppSettings.Load().CloseToTray)
        {
            // The app stays in the notification area; "Quit" in the tray menu exits.
            e.Cancel = true;
            Hide();
        }
        else
        {
            System.Windows.Application.Current.Shutdown();
        }

        base.OnClosing(e);
    }
}
