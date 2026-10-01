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
    private static readonly int[] AlertLimitsGb = [2, 4, 6, 8, 12, 16, 24];
    private static readonly int[] WslLimitsGb = [4, 6, 8, 12, 16];

    private readonly bool _loaded;

    public SettingsPage()
    {
        InitializeComponent();

        var settings = AppSettings.Load();
        StartWithWindows.IsChecked = AppSettings.StartsWithWindows;
        StartMinimized.IsChecked = settings.StartMinimized;
        CloseToTray.IsChecked = settings.CloseToTray;
        ConfirmBeforeActivate.IsChecked = settings.ConfirmBeforeActivate;
        ShowAiMemoryInTray.IsChecked = settings.ShowAiMemoryInTray;
        EnableHotkeys.IsChecked = settings.EnableHotkeys;

        var limits = new List<Limit> { new(0, "Off") };
        limits.AddRange(AlertLimitsGb.Select(gb => new Limit(gb, $"{gb} GB")));
        AiAlert.ItemsSource = limits;
        AiAlert.SelectedItem = limits.FirstOrDefault(limit => limit.Gb == settings.AiMemoryAlertGb) ?? limits[0];
        JournalPath.Text = Privacy.Path(AppPaths.JournalDirectory);
        PrivacyMode.IsChecked = settings.PrivacyMode;

        var choices = new List<Choice> { new(null, "None") };
        choices.AddRange(ModeCatalog.Load().Select(entry => new Choice(entry.Profile.Mode, entry.Profile.Label)));
        AutoMode.ItemsSource = choices;
        AutoMode.SelectedItem = choices.FirstOrDefault(choice => choice.Mode == settings.AutoActivateMode) ?? choices[0];

        var currentWsl = File.Exists(WslConfig.DefaultPath) ? WslConfig.ReadMemoryGb(File.ReadAllText(WslConfig.DefaultPath)) : null;
        var wslChoices = new List<Limit> { new(0, "No limit set") };
        wslChoices.AddRange(WslLimitsGb.Union(currentWsl is { } gb ? [gb] : []).Order().Select(value => new Limit(value, $"{value} GB")));
        WslMemory.ItemsSource = wslChoices;
        WslMemory.SelectedItem = wslChoices.FirstOrDefault(choice => choice.Gb == (currentWsl ?? 0)) ?? wslChoices[0];

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
        (AppSettings.Load() with
        {
            StartMinimized = minimized,
            CloseToTray = CloseToTray.IsChecked == true,
            ConfirmBeforeActivate = ConfirmBeforeActivate.IsChecked == true,
            ShowAiMemoryInTray = ShowAiMemoryInTray.IsChecked == true,
            EnableHotkeys = EnableHotkeys.IsChecked == true,
            PrivacyMode = PrivacyMode.IsChecked == true,
            AiMemoryAlertGb = (AiAlert.SelectedItem as Limit)?.Gb ?? 0,
            AutoActivateMode = (AutoMode.SelectedItem as Choice)?.Mode,
        }).Save();
        Privacy.Set(PrivacyMode.IsChecked == true);
        JournalPath.Text = Privacy.Path(AppPaths.JournalDirectory);
        (Application.Current as App)?.ApplyDisplaySettings();
    }

    private void OnWslMemoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || WslMemory.SelectedItem is not Limit limit)
        {
            return;
        }

        try
        {
            var path = WslConfig.DefaultPath;
            var content = File.Exists(path) ? File.ReadAllText(path) : "";
            var updated = WslConfig.WithMemoryGb(content, limit.Gb == 0 ? null : limit.Gb);
            if (updated == content)
            {
                return;
            }

            // Keep the file as it was before WinModes first touched it.
            var backup = path + ".winmodes.bak";
            if (File.Exists(path) && !File.Exists(backup))
            {
                File.Copy(path, backup);
            }

            File.WriteAllText(path, updated);
            WslNote.Text = "Saved. It applies the next time WSL starts (after 'wsl --shutdown' or a restart of Docker Desktop).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            WslNote.Text = $"The .wslconfig file could not be written: {ex.Message}";
        }
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
            JournalPath.Text = $"{Privacy.Path(AppPaths.JournalDirectory)} (created at the first mode switch)";
        }
    }

    private sealed record Choice(string? Mode, string Label);

    private sealed record Limit(int Gb, string Label);
}
