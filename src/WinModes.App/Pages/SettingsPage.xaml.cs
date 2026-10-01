using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using WinModes.App.Services;
using WinModes.Core.Engine;

namespace WinModes.App.Pages;

/// <summary>Preferences: startup, window behaviour and opt-in automation. Saved on every change.</summary>
public partial class SettingsPage : Page
{
    private readonly bool _loaded;

    public SettingsPage()
    {
        InitializeComponent();

        var settings = AppSettings.Load();
        StartWithWindows.IsChecked = AppSettings.StartsWithWindows;
        StartMinimized.IsChecked = settings.StartMinimized;
        CloseToTray.IsChecked = settings.CloseToTray;
        ConfirmBeforeActivate.IsChecked = settings.ConfirmBeforeActivate;
        JournalPath.Text = AppPaths.JournalDirectory;

        var choices = new List<Choice> { new(null, "None") };
        choices.AddRange(ModeCatalog.Load().Select(entry => new Choice(entry.Profile.Mode, entry.Profile.Label)));
        AutoMode.ItemsSource = choices;
        AutoMode.SelectedItem = choices.FirstOrDefault(choice => choice.Mode == settings.AutoActivateMode) ?? choices[0];

        // Setting the initial values raises the change events; only user changes are saved.
        _loaded = true;
    }

    private void OnChanged(object sender, RoutedEventArgs e) => Save();

    private void OnAutoModeChanged(object sender, SelectionChangedEventArgs e) => Save();

    private void Save()
    {
        if (!_loaded)
        {
            return;
        }

        var minimized = StartMinimized.IsChecked == true;
        AppSettings.SetStartWithWindows(StartWithWindows.IsChecked == true, minimized);
        new AppSettings
        {
            StartMinimized = minimized,
            CloseToTray = CloseToTray.IsChecked == true,
            ConfirmBeforeActivate = ConfirmBeforeActivate.IsChecked == true,
            AutoActivateMode = (AutoMode.SelectedItem as Choice)?.Mode,
        }.Save();
    }

    private void OnOpenJournal(object sender, RoutedEventArgs e)
    {
        // The folder only exists after the first switch.
        var folder = Directory.Exists(AppPaths.JournalDirectory) ? AppPaths.JournalDirectory : AppPaths.DataDirectory;
        if (Directory.Exists(folder))
        {
            using var process = Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder } });
        }
        else
        {
            JournalPath.Text = $"{AppPaths.JournalDirectory} (created at the first mode switch)";
        }
    }

    private sealed record Choice(string? Mode, string Label);
}
