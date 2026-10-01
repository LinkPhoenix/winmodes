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
                $"{rule.Process}  →  {entry?.Profile.Label ?? rule.Mode} mode",
                entry is null ? "This mode no longer exists: the rule is ignored." : $"When {rule.Process} is running",
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

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        var name = AutoSwitchPlanner.Normalize(ProcessName.Text ?? "");
        if (name.Length == 0 || Mode.SelectedItem is not ModeChoice choice)
        {
            Status.Text = "Type or pick a program name and choose a mode.";
            return;
        }

        var rules = AppSettings.Load().AutoSwitch.Rules;
        if (rules.Any(rule => rule.Process.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            Status.Text = $"There is already a rule for {name}. Remove it first to change its mode.";
            return;
        }

        Save([.. rules, new AutoSwitchRule(name, choice.Mode)]);
        ProcessName.Text = "";
        Status.Text = Enabled.IsChecked == true ? $"Rule added for {name}." : $"Rule added for {name}. Turn automatic switching on to use it.";
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RuleRow row)
        {
            Save([.. AppSettings.Load().AutoSwitch.Rules.Where(rule => rule != row.Rule)]);
            Status.Text = $"Rule removed for {row.Rule.Process}.";
        }
    }

    private sealed record ModeChoice(string Mode, string Label);

    private sealed record RuleRow(AutoSwitchRule Rule, string Title, string Detail, string Glyph, Brush Accent);
}
