using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinModes.App.Services;
using WinModes.Core.Automation;

namespace WinModes.App.Pages;

/// <summary>Opt-in automatic switching: the on/off switch and the "program runs, activate mode" rules.</summary>
public partial class AutomationPage : Page
{
    private const string DefaultGlyph = "";

    private readonly IReadOnlyList<ModeCatalog.Entry> _modes = ModeCatalog.Load();
    private readonly bool _loaded;

    public AutomationPage()
    {
        InitializeComponent();

        var settings = AppSettings.Load().AutoSwitch;
        Enabled.IsChecked = settings.Enabled;
        RevertWhenClosed.IsChecked = settings.RevertWhenClosed;
        Mode.ItemsSource = _modes.Select(entry => new ModeChoice(entry.Profile.Mode, entry.Profile.Label)).ToList();
        Mode.SelectedIndex = _modes.Count > 0 ? 0 : -1;
        Trigger.ItemsSource = TriggerChoices;
        Trigger.SelectedIndex = 0;
        ShowRules(settings.Rules);

        // Setting the initial values raises the change events; only user changes are saved.
        _loaded = true;
    }

    private void ShowRules(IReadOnlyList<AutoSwitchRule> rules)
    {
        Rules.ItemsSource = rules.Select(rule =>
        {
            var entry = _modes.FirstOrDefault(mode => mode.Profile.Mode.Equals(rule.Mode, StringComparison.OrdinalIgnoreCase));
            return new RuleRow(
                rule,
                $"{AutoSwitchConditions.Describe(rule.Process)}  →  {entry?.Profile.Label ?? rule.Mode} mode",
                entry is null ? "This mode no longer exists: the rule is ignored." : Explain(rule.Process),
                entry?.Glyph ?? DefaultGlyph,
                entry?.Accent ?? Palette.Neutral);
        }).ToList();
        EmptyState.Visibility = rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            Save(AppSettings.Load().AutoSwitch.Rules);
        }
    }

    private void Save(IReadOnlyList<AutoSwitchRule> rules)
    {
        var current = AppSettings.Load();
        (current with
        {
            AutoSwitch = current.AutoSwitch with
            {
                Enabled = Enabled.IsChecked == true,
                RevertWhenClosed = RevertWhenClosed.IsChecked == true,
                Rules = rules,
            },
        }).Save();
        ShowRules(rules);
        (Application.Current as App)?.ApplyDisplaySettings();
    }

    private async void OnProcessListOpened(object sender, EventArgs e)
    {
        var typed = ProcessName.Text;
        var names = await Task.Run(() => AutoSwitcher.RunningProcessNames().Order(StringComparer.OrdinalIgnoreCase).ToList());
        ProcessName.ItemsSource = names;
        ProcessName.Text = typed;
    }

    private static string Explain(string key) => key switch
    {
        AutoSwitchConditions.Battery => "While this PC runs on battery",
        _ when AutoSwitchConditions.TryParseSchedule(key, out _, out _) => "Every day during these hours",
        _ => "While this program is open",
    };

    private void OnTriggerChanged(object sender, SelectionChangedEventArgs e)
    {
        var kind = (Trigger.SelectedItem as TriggerChoice)?.Kind ?? TriggerKind.Program;
        ProcessName.Visibility = kind == TriggerKind.Program ? Visibility.Visible : Visibility.Collapsed;
        TimeRange.Visibility = kind == TriggerKind.Schedule ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Builds the rule key from the form, or returns null after explaining what is missing.</summary>
    private string? ReadTrigger()
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        switch ((Trigger.SelectedItem as TriggerChoice)?.Kind)
        {
            case TriggerKind.Battery:
                return AutoSwitchConditions.Battery;
            case TriggerKind.Schedule:
                if (TimeOnly.TryParseExact(TimeFrom.Text.Trim(), ["H:mm", "HH:mm"], culture, System.Globalization.DateTimeStyles.None, out var from)
                    && TimeOnly.TryParseExact(TimeTo.Text.Trim(), ["H:mm", "HH:mm"], culture, System.Globalization.DateTimeStyles.None, out var to)
                    && from != to)
                {
                    return AutoSwitchConditions.Schedule(from, to);
                }

                Status.Text = "Type two different times as hours and minutes, for example 09:00 and 18:00.";
                return null;
            default:
                var name = AutoSwitchPlanner.Normalize(ProcessName.Text ?? "");
                if (name.Length == 0 || AutoSwitchConditions.IsCondition(name))
                {
                    Status.Text = "Type or pick a program name.";
                    return null;
                }

                return name;
        }
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        if (Mode.SelectedItem is not ModeChoice choice)
        {
            Status.Text = "Choose a mode.";
            return;
        }

        if (ReadTrigger() is not { } key)
        {
            return;
        }

        var label = AutoSwitchConditions.Describe(key);
        var rules = AppSettings.Load().AutoSwitch.Rules;
        if (rules.Any(rule => rule.Process.Equals(key, StringComparison.OrdinalIgnoreCase)))
        {
            Status.Text = $"There is already a rule for \"{label}\". Remove it first to change its mode.";
            return;
        }

        Save([.. rules, new AutoSwitchRule(key, choice.Mode)]);
        ProcessName.Text = "";
        Status.Text = Enabled.IsChecked == true ? $"Rule added: {label}." : $"Rule added: {label}. Turn automatic switching on to use it.";
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RuleRow row)
        {
            Save([.. AppSettings.Load().AutoSwitch.Rules.Where(rule => rule != row.Rule)]);
            Status.Text = $"Rule removed: {AutoSwitchConditions.Describe(row.Rule.Process)}.";
        }
    }

    private enum TriggerKind
    {
        Program,
        Battery,
        Schedule,
    }

    private static readonly TriggerChoice[] TriggerChoices =
    [
        new(TriggerKind.Program, "When a program runs"),
        new(TriggerKind.Battery, "When on battery"),
        new(TriggerKind.Schedule, "During these hours"),
    ];

    private sealed record TriggerChoice(TriggerKind Kind, string Label);

    private sealed record ModeChoice(string Mode, string Label);

    private sealed record RuleRow(AutoSwitchRule Rule, string Title, string Detail, string Glyph, Brush Accent);
}
