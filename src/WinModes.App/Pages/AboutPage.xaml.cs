using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using WinModes.Core.Engine;

namespace WinModes.App.Pages;

/// <summary>Version, where data is stored and third-party notices.</summary>
public partial class AboutPage : Page
{
    private const string CoffeeUrl = "https://buymeacoffee.com/vckh76t96fh";
    private const string SourceUrl = "https://github.com/LinkPhoenix/winmodes";

    public AboutPage()
    {
        InitializeComponent();

        VersionText.Text = $"Version {AppInfo.Version}";
        CheckAtStartup.IsChecked = Services.AppSettings.Load().CheckForUpdates;
        _loaded = true;
        if (Services.UpdateChecker.Last is { } last)
        {
            ShowUpdate(last);
        }

        SystemRows.ItemsSource = new List<KeyValuePair<string, string>>
        {
            new("Windows", RuntimeInformation.OSDescription),
            new("Runtime", RuntimeInformation.FrameworkDescription),
            new("Modes available", string.Join(", ", ModeCatalog.Load().Select(entry => entry.Profile.Label))),
            new("Switch history", Services.Privacy.Path(AppPaths.JournalDirectory)),
            new("Profiles", Services.Privacy.Path(AppServices.ProfilesDirectory)),
        };
    }

    private void OnBuyCoffee(object sender, RoutedEventArgs e) => Open(CoffeeUrl);

    private readonly bool _loaded;

    private void OnCheckAtStartupChanged(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            (Services.AppSettings.Load() with { CheckForUpdates = CheckAtStartup.IsChecked == true }).Save();
        }
    }

    private async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        UpdateText.Text = "Checking...";
        ShowUpdate(await Services.UpdateChecker.CheckAsync());
        CheckButton.IsEnabled = true;
    }

    private void ShowUpdate(Services.UpdateStatus status)
    {
        UpdateText.Text = status switch
        {
            { Error: { } error } => $"{error} Try again later.",
            { IsNewer: true } => $"Version {status.LatestTag} is available. You have v{AppInfo.Version}.",
            _ => $"You have the latest version (v{AppInfo.Version}).",
        };
        DownloadButton.Visibility = status.IsNewer ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnOpenRelease(object sender, RoutedEventArgs e) => Open(Services.UpdateChecker.ReleasesPage);

    private void OnShowGuide(object sender, RoutedEventArgs e) => (Application.Current as App)?.ShowOnboarding();

    private void OnOpenSource(object sender, RoutedEventArgs e) => Open(SourceUrl);

    private static void Open(string url)
    {
        // Fixed https addresses only; the default browser opens them.
        using var browser = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}