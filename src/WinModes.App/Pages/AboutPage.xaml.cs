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

        VersionText.Text = Loc.F("Version {0}", AppInfo.Version);
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
            new(Loc.T("Modes available"), string.Join(", ", ModeCatalog.Load().Select(entry => entry.Profile.Label))),
            new(Loc.T("Switch history"), Services.Privacy.Path(AppPaths.JournalDirectory)),
            new(Loc.T("Profiles"), Services.Privacy.Path(AppServices.ProfilesDirectory)),
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
        UpdateText.Text = Loc.T("Checking...");
        ShowUpdate(await Services.UpdateChecker.CheckAsync());
        CheckButton.IsEnabled = true;
    }

    private void ShowUpdate(Services.UpdateStatus status)
    {
        UpdateText.Text = status switch
        {
            { Error: { } error } => Loc.F("{0} Try again later.", error),
            { IsNewer: true } => Loc.F("Version {0} is available. You have v{1}.", status.LatestTag, AppInfo.Version),
            _ => Loc.F("You have the latest version (v{0}).", AppInfo.Version),
        };
        _latestTag = status.IsNewer ? status.LatestTag : null;
        ReleasePageButton.Visibility = status.IsNewer ? Visibility.Visible : Visibility.Collapsed;
        // A portable copy cannot be replaced by the installer: it gets the new zip in the Downloads folder instead.
        InstallButton.Visibility = status.IsNewer ? Visibility.Visible : Visibility.Collapsed;
        InstallButton.Content = Loc.T(Services.UpdateInstaller.IsInstalledBuild ? "Download and install" : "Download");
    }

    private async Task DownloadPortableAsync(string tag)
    {
        InstallButton.IsEnabled = false;
        CheckButton.IsEnabled = false;
        try
        {
            var progress = new Progress<double>(percent => UpdateText.Text = Loc.F("Downloading {0}: {1:0} %", tag, percent));
            var zip = await Services.UpdateInstaller.DownloadPortableAsync(tag, progress, CancellationToken.None);
            UpdateText.Text = Loc.F("{0} is in your Downloads folder, checked against the release checksum. Close WinModes and extract it over this copy.", System.IO.Path.GetFileName(zip));
            Services.UpdateInstaller.Reveal(zip);
        }
        catch (Services.UpdateException ex)
        {
            UpdateText.Text = ex.Message;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The file is downloaded; only the folder could not be opened.
        }
        finally
        {
            InstallButton.IsEnabled = true;
            CheckButton.IsEnabled = true;
        }
    }

    private string? _latestTag;

    private async void OnInstallUpdate(object sender, RoutedEventArgs e)
    {
        if (_latestTag is not { } tag)
        {
            return;
        }

        if (!Services.UpdateInstaller.IsInstalledBuild)
        {
            await DownloadPortableAsync(tag);
            return;
        }

        var confirm = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.F("Install WinModes {0}?", tag),
            Content = Loc.T("The installer is downloaded from the GitHub release and checked against its published checksum. WinModes then closes and the installer starts; Windows asks for permission."),
            PrimaryButtonText = Loc.T("Download and install"),
            CloseButtonText = Loc.T("Cancel"),
        };
        if (await confirm.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            return;
        }

        InstallButton.IsEnabled = false;
        CheckButton.IsEnabled = false;
        try
        {
            var progress = new Progress<double>(percent => UpdateText.Text = Loc.F("Downloading {0}: {1:0} %", tag, percent));
            var installer = await Services.UpdateInstaller.DownloadAsync(tag, progress, CancellationToken.None);
            UpdateText.Text = Loc.T("Starting the installer...");
            Services.UpdateInstaller.Run(installer);
            Application.Current.Shutdown();
        }
        catch (Services.UpdateException ex)
        {
            UpdateText.Text = ex.Message;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            UpdateText.Text = Loc.T("The installer could not be started. Open the release page instead.");
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
            Title = Loc.T("Export a WinModes report"),
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
            ReportText.Text = Loc.T("Report saved. Read it before sharing it: it lists the programs running on this PC.");
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ReportText.Text = Loc.T("The report could not be saved there. Choose another folder.");
        }
    }

    private const int ReportProcessCount = 15;

    private async void OnCreateSupportFile(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Loc.T("Create a WinModes support file"),
            FileName = $"winmodes-support-{DateTime.Now:yyyyMMdd-HHmm}.zip",
            Filter = "Zip (*.zip)|*.zip",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await Task.Run(() =>
            {
                using var stream = System.IO.File.Create(dialog.FileName);
                WinModes.Core.Reports.SupportBundle.Write(stream, Services.SupportInfo.Collect(), Environment.UserName, Environment.MachineName);
            });
            SupportText.Text = Loc.T("Support file saved. Open it and read it before sharing it: your user name and PC name were taken out, but check that nothing else private is left.");
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.IO.InvalidDataException)
        {
            SupportText.Text = Loc.T("The support file could not be saved there. Choose another folder.");
        }
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