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
        ["services"] = typeof(ServicesPage),
        ["history"] = typeof(HistoryPage),
        ["settings"] = typeof(SettingsPage),
        ["protection"] = typeof(ProtectionPage),
        ["about"] = typeof(AboutPage),
    };

    /// <param name="startPage">Page name from the <c>--page</c> argument; unknown or null opens the dashboard.</param>
    public MainWindow(string? startPage = null)
    {
        InitializeComponent();

        var page = startPage is not null && PagesByName.TryGetValue(startPage, out var requested) ? requested : typeof(DashboardPage);
        Loaded += (_, _) => RootNavigation.Navigate(page);
    }

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
