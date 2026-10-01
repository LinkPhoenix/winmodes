using System.Windows;
using System.Windows.Controls;
using WinModes.App.Services;

namespace WinModes.App.Controls;

/// <summary>
/// First-run guide shown over the pages: what the app does, what it never touches,
/// the optional features, and how to start. It changes nothing on the PC besides the user's own preferences.
/// </summary>
public partial class OnboardingView : UserControl
{
    private readonly StackPanel[] _steps;
    private int _index;

    public OnboardingView()
    {
        InitializeComponent();
        _steps = [StepWelcome, StepProtection, StepOptions, StepReady];

        var settings = AppSettings.Load();
        StartWithWindows.IsChecked = AppSettings.StartsWithWindows;
        TrayMeter.IsChecked = settings.ShowAiMemoryInTray;
        Widget.IsChecked = settings.ShowDesktopWidget;
        PrivacyMode.IsChecked = settings.PrivacyMode;
        ShowStep();
    }

    /// <summary>Raised when the guide is finished or skipped. True asks to open the Modes page.</summary>
    public event EventHandler<bool>? Completed;

    private void ShowStep()
    {
        for (var i = 0; i < _steps.Length; i++)
        {
            _steps[i].Visibility = i == _index ? Visibility.Visible : Visibility.Collapsed;
        }

        var last = _index == _steps.Length - 1;
        BackButton.Visibility = _index == 0 ? Visibility.Collapsed : Visibility.Visible;
        SkipButton.Visibility = last ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = last ? "Open Modes" : "Next";
        StepText.Text = $"Step {_index + 1} of {_steps.Length}";
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        _index = Math.Max(_index - 1, 0);
        ShowStep();
    }

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (_index < _steps.Length - 1)
        {
            _index++;
            ShowStep();
            return;
        }

        Finish(saveOptions: true, openModes: true);
    }

    // Skipping before the options step must not apply toggles the user never saw.
    private void OnSkip(object sender, RoutedEventArgs e) => Finish(saveOptions: _index >= Array.IndexOf(_steps, StepOptions), openModes: false);

    private void Finish(bool saveOptions, bool openModes)
    {
        var settings = AppSettings.Load() with { OnboardingDone = true };
        if (saveOptions)
        {
            settings = settings with
            {
                ShowAiMemoryInTray = TrayMeter.IsChecked == true,
                ShowDesktopWidget = Widget.IsChecked == true,
                PrivacyMode = PrivacyMode.IsChecked == true,
            };
            AppSettings.SetStartWithWindows(StartWithWindows.IsChecked == true, settings.StartMinimized);
            Privacy.Set(settings.PrivacyMode);
        }

        settings.Save();
        (Application.Current as App)?.ApplyDisplaySettings();
        Completed?.Invoke(this, openModes);
    }
}
