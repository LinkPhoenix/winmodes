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
        _latestTag = status.IsNewer ? status.LatestTag : null;
        ReleasePageButton.Visibility = status.IsNewer ? Visibility.Visible : Visibility.Collapsed;
        // A portable copy cannot be replaced by the installer: its user downloads the new zip from the release page.
        InstallButton.Visibility = status.IsNewer && Services.UpdateInstaller.IsInstalledBuild ? Visibility.Visible : Visibility.Collapsed;
    }

    private string? _latestTag;

    private async void OnInstallUpdate(object sender, RoutedEventArgs e)
    {
        if (_latestTag is not { } tag)
        {
            return;
        }

        var confirm = new Wpf.Ui.Controls.MessageBox
        {
            Title = $"Install WinModes {tag}?",
            Content = "The installer is downloaded from the GitHub release and checked against its published checksum. "
                + "WinModes then closes and the installer starts; Windows asks for permission.",
            PrimaryButtonText = "Download and install",
            CloseButtonText = "Cancel",
        };
        if (await confirm.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            return;
        }

        InstallButton.IsEnabled = false;
        CheckButton.IsEnabled = false;
        try
        {
            var progress = new Progress<double>(percent => UpdateText.Text = $"Downloading {tag}: {percent:0} %");
            var installer = await Services.UpdateInstaller.DownloadAsync(tag, progress, CancellationToken.None);
            UpdateText.Text = "Starting the installer...";
            Services.UpdateInstaller.Run(installer);
            Application.Current.Shutdown();
        }
        catch (Services.UpdateException ex)
        {
            UpdateText.Text = ex.Message;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            UpdateText.Text = "The installer could not be started. Open the release page instead.";
        }
        finally
        {
            InstallButton.IsEnabled = true;
            CheckButton.IsEnabled = true;
        }
    }

    private async void OnExportReport(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export a WinModes report",
            FileName = $"winmodes-report-{DateTime.Now:yyyyMMdd-HHmm}.md",
            Filter = "Markdown (*.md)|*.md|Text (*.txt)|*.txt",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var report = await Task.Run(() =>
            {
                var snapshot = WinModes.Core.Planning.SystemSnapshot.Capture();
                return WinModes.Core.Reports.SystemReport.Build(new WinModes.Core.Reports.SystemReportData(
                    AppInfo.Version,
                    DateTime.Now,
                    RuntimeInformation.OSDescription,
                    Environment.ProcessorCount,
                    WinModes.Core.Planning.SystemMonitor.SampleMemory(),
                    Services.ModeSwitcher.ActiveMode,
                    snapshot.ProcessCount,
                    snapshot.RunningServiceCount,
                    [.. WinModes.Core.Planning.SystemMonitor.GetProcessGroups().Take(ReportProcessCount)],
                    WinModes.Core.Planning.AiToolCatalog.FindSessions(Services.ProcessActions.Sample())));
            });
            await System.IO.File.WriteAllTextAsync(dialog.FileName, report);
            ReportText.Text = "Report saved. Read it before sharing it: it lists the programs running on this PC.";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ReportText.Text = "The report could not be saved there. Choose another folder.";
        }
    }

    private const int ReportProcessCount = 15;

    private void OnOpenRelease(object sender, RoutedEventArgs e) => Open(Services.UpdateChecker.ReleasesPage);

    private void OnShowGuide(object sender, RoutedEventArgs e) => (Application.Current as App)?.ShowOnboarding();

    private void OnOpenSource(object sender, RoutedEventArgs e) => Open(SourceUrl);

    private static void Open(string url)
    {
        // Fixed https addresses only; the default browser opens them.
        using var browser = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}