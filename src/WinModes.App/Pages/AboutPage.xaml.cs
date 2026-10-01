using System.Diagnostics;
using System.Reflection;
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

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Version {version?.ToString(3)}";

        SystemRows.ItemsSource = new List<KeyValuePair<string, string>>
        {
            new("Windows", RuntimeInformation.OSDescription),
            new("Runtime", RuntimeInformation.FrameworkDescription),
            new("Modes available", string.Join(", ", ModeCatalog.Load().Select(entry => entry.Profile.Label))),
            new("Switch history", AppPaths.JournalDirectory),
            new("Profiles", AppServices.ProfilesDirectory),
        };
    }

    private void OnBuyCoffee(object sender, RoutedEventArgs e) => Open(CoffeeUrl);

    private void OnOpenSource(object sender, RoutedEventArgs e) => Open(SourceUrl);

    private static void Open(string url)
    {
        // Fixed https addresses only; the default browser opens them.
        using var browser = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}