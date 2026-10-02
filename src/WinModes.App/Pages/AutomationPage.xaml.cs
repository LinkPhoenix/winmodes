using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinModes.App.Services;
using WinModes.Core.Automation;
using WinModes.Core.Planning;

namespace WinModes.App.Pages;

/// <summary>Opt-in automatic switching: the on/off switch and the "program runs, activate mode" rules.</summary>
public partial class AutomationPage : Page
{
    private const string DefaultGlyph = "";

    private static readonly int[] GraceChoices = [30, 60, 120, 300, 600];

    private readonly IReadOnlyList<ModeCatalog.Entry> _modes = ModeCatalog.Load();
    private readonly DispatcherTimer _countdown = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly bool _loaded;
    private HashSet<string> _openTools = [];
    private string? _activeMode;

    public AutomationPage()
    {
        InitializeComponent();

        var settings = AppSettings.Load().AutoSwitch;
        Enabled.IsChecked = settings.Enabled;
        RevertWhenClosed.IsChecked = settings.RevertWhenClosed;
        var choices = _modes.Select(entry => new ModeChoice(entry.Profile.Mode, entry.Profile.Label)).ToList();
        Mode.ItemsSource = choices;
        Mode.SelectedIndex = _modes.Count > 0 ? 0 : -1;
        ToolMode.ItemsSource = choices;
        ToolMode.SelectedItem = choices.FirstOrDefault(choice => choice.Mode.Equals(AutoSwitchSetup.CodingMode(settings.Rules), StringComparison.OrdinalIgnoreCase)) ?? choices.FirstOrDefault();
        Grace.ItemsSource = GraceChoices.Select(seconds => new GraceChoice(seconds, AutoSwitchText.Remaining(TimeSpan.FromSeconds(seconds)))).ToList();
        Grace.SelectedItem = ((IEnumerable<GraceChoice>)Grace.ItemsSource).MinBy(choice => Math.Abs(choice.Seconds - settings.GraceSeconds));
        Trigger.ItemsSource = TriggerChoices;
        Trigger.SelectedIndex = 0;
        ShowRules(settings.Rules);
        ShowTools(settings.Rules);
        ShowState();

        // Setting the initial values raises the change events; only user changes are saved.
        _loaded = true;

        if (ModeSwitcher.CanSwitchSilently)
        {
            Silent.IsChecked = ModeSwitcher.SwitchesSilently;
        }
        else
        {
            Silent.IsEnabled = false;
            SilentDetail.Text = Loc.T("Available when WinModes is installed with its setup program: a task that runs without a prompt must start from a folder only administrators can change.");
        }

        _countdown.Tick += (_, _) => ShowState();
        Loaded += async (_, _) =>
        {
            _countdown.Start();
            AutoSwitcher.StatusChanged += OnStatusChanged;
            ModeSwitcher.Changed += OnStatusChanged;
            await FindOpenToolsAsync();
        };
        Unloaded += (_, _) =>
        {
            _countdown.Stop();
            AutoSwitcher.StatusChanged -= OnStatusChanged;
            ModeSwitcher.Changed -= OnStatusChanged;
        };
    }

    private void OnStatusChanged() => Dispatcher.BeginInvoke(ShowState);

    private void ShowState()
    {
        var settings = AppSettings.Load().AutoSwitch;
        _activeMode = ModeSwitcher.ActiveMode;
        StateText.Text = !settings.Enabled
            ? Loc.T("Automatic switching is off.")
            : settings.Rules.Count == 0
                ? Loc.T("No rule yet: turn on a coding tool below or add a rule.")
                : AutoSwitchText.Describe(AutoSwitcher.Status, _activeMode, DateTimeOffset.Now);
    }

    /// <summary>Which of the coding tools have a session open now, to tell the user which ones would act.</summary>
    private async Task FindOpenToolsAsync()
    {
        _openTools = await Task.Run(() => AiToolCatalog.FindSessions(new ProcessSampler().Sample()).Select(session => session.Tool.Id).ToHashSet(StringComparer.OrdinalIgnoreCase));
        ShowTools(AppSettings.Load().AutoSwitch.Rules);
    }

    private void ShowTools(IReadOnlyList<AutoSwitchRule> rules)
    {
        Tools.ItemsSource = AutoSwitchConditions.CodingToolIds
            .Select(id => AiToolCatalog.Tools.First(tool => tool.Id == id))
            .Select(tool =>
            {
                var icon = ToolIcons.For(tool.Name);
                var accent = Palette.BrandBrush;
                return new ToolRow(
                    tool.Id,
                    tool.Name,
                    icon,
                    rules.Any(rule => rule.Process.Equals(AutoSwitchConditions.Tool(tool.Id), StringComparison.OrdinalIgnoreCase)),
                    _openTools.Contains(tool.Id) ? Visibility.Visible : Visibility.Collapsed,
                    icon is null ? Visibility.Visible : Visibility.Collapsed,
                    accent,
                    Palette.Tint(accent));
            }).ToList();
    }

    private void OnToolToggled(object sender, RoutedEventArgs e)
    {
        if (!_loaded || (sender as FrameworkElement)?.DataContext is not ToolRow row || sender is not Wpf.Ui.Controls.ToggleSwitch toggle)
        {
            return;
        }

        var key = AutoSwitchConditions.Tool(row.Id);
        var rules = AppSettings.Load().AutoSwitch.Rules.Where(rule => !rule.Process.Equals(key, StringComparison.OrdinalIgnoreCase)).ToList();
        if (toggle.IsChecked == true && ToolMode.SelectedItem is ModeChoice mode)
        {
            rules.Add(new AutoSwitchRule(key, mode.Mode));
        }

        Save(rules);
        ShowTools(rules);
        Status.Text = Loc.F(toggle.IsChecked == true ? "{0} will start a mode when it opens." : "{0} no longer starts a mode.", row.Name);
    }

    private void OnToolModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || ToolMode.SelectedItem is not ModeChoice mode)
        {
            return;
        }

        var rules = AppSettings.Load().AutoSwitch.Rules
            .Select(rule => AutoSwitchConditions.TryParseTool(rule.Process, out _) ? rule with { Mode = mode.Mode } : rule)
            .ToList();
        Save(rules);
    }

    private void OnGraceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loaded)
        {
            Save(AppSettings.Load().AutoSwitch.Rules);
        }
    }

    private async void OnSilentClicked(object sender, RoutedEventArgs e)
    {
        var wanted = Silent.IsChecked == true;
        Silent.IsEnabled = false;
        var error = await ModeSwitcher.SetSilentSwitchAsync(wanted);
        // Show what Windows really has, whatever was clicked.
        var installed = await Task.Run(() => ModeSwitcher.SwitchesSilently);
        Silent.IsChecked = installed;
        Silent.IsEnabled = true;
        Status.Text = error ?? Loc.T(installed == wanted
            ? (installed ? "Modes now switch without a permission prompt." : "Windows asks for permission at each switch again.")
            : "The scheduled task could not be changed.");
    }

    private void ShowRules(IReadOnlyList<AutoSwitchRule> rules)
    {
        Rules.ItemsSource = rules.Where(rule => !AutoSwitchConditions.TryParseTool(rule.Process, out _)).Select(rule =>
        {
            var entry = _modes.FirstOrDefault(mode => mode.Profile.Mode.Equals(rule.Mode, StringComparison.OrdinalIgnoreCase));
            return new RuleRow(
                rule,
                AutoSwitchConditions.Describe(rule.Process) + "  →  " + Loc.F("{0} mode", entry?.Profile.Label ?? rule.Mode),
                entry is null ? Loc.T("This mode no longer exists: the rule is ignored.") : Explain(rule.Process),
                entry?.Glyph ?? DefaultGlyph,
                entry?.Accent ?? Palette.Neutral);
        }).ToList();
        EmptyState.Visibility = rules.All(rule => AutoSwitchConditions.TryParseTool(rule.Process, out _)) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        // Turned on with nothing to act on: the usual coding tools are proposed, as on the Modes page.
        if (ReferenceEquals(sender, Enabled) && Enabled.IsChecked == true && AppSettings.Load().AutoSwitch.Rules.Count == 0)
        {
            AutoSwitchSetup.SetEnabled(true);
            var seeded = AppSettings.Load().AutoSwitch.Rules;
            ShowRules(seeded);
            ShowTools(seeded);
            ShowState();
            return;
        }

        Save(AppSettings.Load().AutoSwitch.Rules);
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
                GraceSeconds = (Grace.SelectedItem as GraceChoice)?.Seconds ?? AutoSwitchSettings.DefaultGraceSeconds,
                Rules = rules,
            },
        }).Save();
        ShowRules(rules);
        ShowState();
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
        AutoSwitchConditions.Battery => Loc.T("While this PC runs on battery"),
        _ when AutoSwitchConditions.TryParseSchedule(key, out _, out _) => Loc.T("Every day during these hours"),
        _ => Loc.T("While this program is open"),
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

                Status.Text = Loc.T("Type two different times as hours and minutes, for example 09:00 and 18:00.");
                return null;
            default:
                var name = AutoSwitchPlanner.Normalize(ProcessName.Text ?? "");
                if (name.Length == 0 || AutoSwitchConditions.IsCondition(name))
                {
                    Status.Text = Loc.T("Type or pick a program name.");
                    return null;
                }

                return name;
        }
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        if (Mode.SelectedItem is not ModeChoice choice)
        {
            Status.Text = Loc.T("Choose a mode.");
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
            Status.Text = Loc.F("There is already a rule for \"{0}\". Remove it first to change its mode.", label);
            return;
        }

        Save([.. rules, new AutoSwitchRule(key, choice.Mode)]);
        ProcessName.Text = "";
        Status.Text = Loc.F(Enabled.IsChecked == true ? "Rule added: {0}." : "Rule added: {0}. Turn automatic switching on to use it.", label);
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is RuleRow row)
        {
            Save([.. AppSettings.Load().AutoSwitch.Rules.Where(rule => rule != row.Rule)]);
            Status.Text = Loc.F("Rule removed: {0}.", AutoSwitchConditions.Describe(row.Rule.Process));
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
        new(TriggerKind.Program, Loc.T("When a program runs")),
        new(TriggerKind.Battery, Loc.T("When on battery")),
        new(TriggerKind.Schedule, Loc.T("During these hours")),
    ];

    private sealed record TriggerChoice(TriggerKind Kind, string Label);

    private sealed record ModeChoice(string Mode, string Label);

    private sealed record GraceChoice(int Seconds, string Label);

    private sealed record ToolRow(
        string Id, string Name, ImageSource? Icon, bool IsOn, Visibility RunningVisibility, Visibility GlyphVisibility, Brush Color, Brush Tint);

    private sealed record RuleRow(AutoSwitchRule Rule, string Title, string Detail, string Glyph, Brush Accent);
}
