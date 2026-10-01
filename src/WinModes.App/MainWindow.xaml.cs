using System.ComponentModel;
using WinModes.App.Pages;
using Wpf.Ui.Controls;

namespace WinModes.App;

/// <summary>Application shell: title bar and navigation. Pages hold the content.</summary>
public partial class MainWindow : FluentWindow
{
    private static readonly Dictionary<string, Type> PagesByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dashboard"] = typeof(DashboardPage),
        ["modes"] = typeof(ModesPage),
        ["processes"] = typeof(ProcessesPage),
        ["ai"] = typeof(AiToolsPage),
        ["usage"] = typeof(UsagePage),
        ["services"] = typeof(ServicesPage),
        ["optimize"] = typeof(OptimizePage),
        ["automation"] = typeof(AutomationPage),
        ["widget"] = typeof(WidgetPage),
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
        VersionText.Text = $"v{AppInfo.Version}";

        var page = startPage is not null && PagesByName.TryGetValue(startPage, out var requested) ? requested : typeof(DashboardPage);
        Loaded += (_, _) =>
        {
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

    public void NavigateTo(Type pageType) => RootNavigation.Navigate(pageType);

    protected override void OnClosing(CancelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

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
