using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using WinModes.Core.Engine;

namespace WinModes.App.Pages;

/// <summary>Version, where data is stored and third-party notices.</summary>
public partial class AboutPage : Page
{
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
}
