using System.Windows;
using System.Windows.Controls;
using WinModes.App.Services;

namespace WinModes.App.Pages;

/// <summary>Every notification WinModes can show, each one switched on or off here. Saved on every change.</summary>
public partial class NotificationsPage : Page
{
    private static readonly int[] LowPercentChoices = [5, 10, 20, 30];
    private static readonly int[] MemoryLimitsGb = [2, 4, 6, 8, 12, 16, 24];

    private readonly bool _loaded;

    public NotificationsPage()
    {
        InitializeComponent();

        var settings = AppSettings.Load();
        var notifications = settings.Notifications;
        Enabled.IsChecked = notifications.Enabled;
        Show(ClaudeLow, ClaudeReached, ClaudeReset, ClaudeCredit, "Claude", settings);
        Show(CodexLow, CodexReached, CodexReset, CodexCredit, "Codex", settings);
        UpdateAvailable.IsChecked = notifications.UpdateAvailable;
        SignInExpired.IsChecked = notifications.SignInExpired;
        ModeChanges.IsChecked = notifications.ModeChanges;
        IdleEnded.IsChecked = notifications.IdleSessionEnded;

        var share = LowPercentChoices.Select(percent => new Option(percent, Loc.F("Under {0} %", percent))).ToList();
        LowPercent.ItemsSource = share;
        LowPercent.SelectedItem = share.FirstOrDefault(option => option.Value == notifications.LowPercent) ?? share[1];

        var limits = new List<Option> { new(0, Loc.T("Off")) };
        limits.AddRange(MemoryLimitsGb.Select(gb => new Option(gb, $"{gb} GB")));
        AiAlert.ItemsSource = limits;
        AiAlert.SelectedItem = limits.FirstOrDefault(option => option.Value == settings.AiMemoryAlertGb) ?? limits[0];
        AiToolAlert.ItemsSource = limits;
        AiToolAlert.SelectedItem = limits.FirstOrDefault(option => option.Value == settings.AiToolAlertGb) ?? limits[0];

        var claude = ToolIcons.For("Claude");
        var codex = ToolIcons.For("Codex");
        (ClaudeSection.Picture, CodexSection.Picture) = (claude, codex);

        ShowAvailability(settings);

        // Setting the initial values raises the change events; only user changes are saved.
        _loaded = true;
    }

    private static void Show(Wpf.Ui.Controls.ToggleSwitch low, Wpf.Ui.Controls.ToggleSwitch reached, Wpf.Ui.Controls.ToggleSwitch reset, Wpf.Ui.Controls.ToggleSwitch credit, string tool, AppSettings settings)
    {
        var notices = settings.Notifications.For(tool);
        low.IsChecked = settings.Notifications.LowFor(tool, settings.Widget);
        reached.IsChecked = notices.Reached;
        reset.IsChecked = notices.Reset;
        credit.IsChecked = notices.CreditGained;
    }

    /// <summary>The options follow the master switch; the plan ones also need the plan usage, which is read only when turned on.</summary>
    private void ShowAvailability(AppSettings settings)
    {
        Options.IsEnabled = settings.Notifications.Enabled;
        PlanNoteRow.Visibility = settings.Widget.ShowSubscriptions ? Visibility.Collapsed : Visibility.Visible;
        ClaudeSection.Visibility = settings.Widget.ShowClaudePlan || !settings.Widget.ShowSubscriptions ? Visibility.Visible : Visibility.Collapsed;
        CodexSection.Visibility = settings.Widget.ShowCodexPlan || !settings.Widget.ShowSubscriptions ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnChanged(object sender, RoutedEventArgs e) => Save();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => Save();

    private void OnTest(object sender, RoutedEventArgs e) => (Application.Current as App)?.ShowTestNotice();

    private void OnOpenWidget(object sender, RoutedEventArgs e) => (Application.Current?.MainWindow as MainWindow)?.NavigateTo(typeof(WidgetPage));

    private void Save()
    {
        if (!_loaded)
        {
            return;
        }

        var current = AppSettings.Load();
        var updated = current with
        {
            AiMemoryAlertGb = (AiAlert.SelectedItem as Option)?.Value ?? 0,
            AiToolAlertGb = (AiToolAlert.SelectedItem as Option)?.Value ?? 0,
            Notifications = current.Notifications with
            {
                Enabled = Enabled.IsChecked == true,
                LowPercent = (LowPercent.SelectedItem as Option)?.Value ?? current.Notifications.LowPercent,
                Claude = new ToolNotices
                {
                    Low = ClaudeLow.IsChecked == true,
                    Reached = ClaudeReached.IsChecked == true,
                    Reset = ClaudeReset.IsChecked == true,
                    CreditGained = ClaudeCredit.IsChecked == true,
                },
                Codex = new ToolNotices
                {
                    Low = CodexLow.IsChecked == true,
                    Reached = CodexReached.IsChecked == true,
                    Reset = CodexReset.IsChecked == true,
                    CreditGained = CodexCredit.IsChecked == true,
                },
                UpdateAvailable = UpdateAvailable.IsChecked == true,
                SignInExpired = SignInExpired.IsChecked == true,
                ModeChanges = ModeChanges.IsChecked == true,
                IdleSessionEnded = IdleEnded.IsChecked == true,
            },
        };
        updated.Save();
        ShowAvailability(updated);
        (Application.Current as App)?.ApplyDisplaySettings();
    }

    private sealed record Option(int Value, string Label);
}
