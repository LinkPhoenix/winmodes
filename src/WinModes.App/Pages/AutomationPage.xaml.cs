using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinModes.App.Controls;
using WinModes.App.Services;
using WinModes.Core.Automation;
using WinModes.Core.Planning;

namespace WinModes.App.Pages;

/// <summary>
/// Opt-in automatic switching: the on/off switch, then, for each mode, the programs that start it, and the rules on battery and hours.
/// </summary>
public partial class AutomationPage : Page
{
    private const string DefaultGlyph = "";
    private const int RunningRefreshSeconds = 5;

    private static readonly int[] GraceChoices = [30, 60, 120, 300, 600];

    private readonly IReadOnlyList<ModeCatalog.Entry> _modes = ModeCatalog.Load();
    private readonly DispatcherTimer _countdown = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly bool _loaded;
    private HashSet<string> _openTools = [];
    private HashSet<string> _running = new(StringComparer.OrdinalIgnoreCase);
    private List<ProgramRow> _programRows = [];
    private List<CategoryChip> _chips = [];
    private string? _selectedMode;
    private string? _activeMode;
    private int _ticks;

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
        _selectedMode = AutoSwitchSetup.CodingMode(settings.Rules);
        if (_modes.All(entry => !entry.Profile.Mode.Equals(_selectedMode, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedMode = _modes.Count > 0 ? _modes[0].Profile.Mode : null;
        }

        ShowAll(settings.Rules);
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
            SilentRow.Description = Loc.T("Available when WinModes is installed with its setup program: a task that runs without a prompt must start from a folder only administrators can change.");
        }

        _countdown.Tick += async (_, _) =>
        {
            ShowState();
            if (++_ticks % RunningRefreshSeconds == 0)
            {
                await RefreshRunningAsync();
            }
        };
        Loaded += async (_, _) =>
        {
            _countdown.Start();
            AutoSwitcher.StatusChanged += OnStatusChanged;
            ModeSwitcher.Changed += OnStatusChanged;
            await Task.WhenAll(FindOpenToolsAsync(), RefreshRunningAsync());
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
            : !settings.HasActiveRules
                ? Loc.T("No rule yet: turn on a coding tool below or add a rule.")
                : AutoSwitchText.Describe(AutoSwitcher.Status, _activeMode, DateTimeOffset.Now);
    }

    /// <summary>Redraws everything that depends on the rules: the mode bar, the list of the selected mode, and the rules on battery and hours.</summary>
    private void ShowAll(IReadOnlyList<AutoSwitchRule> rules)
    {
        ShowModes(rules);
        ShowPanel(rules);
        ShowRules(rules);
    }

    private static bool IsProgram(AutoSwitchRule rule) => !AutoSwitchConditions.IsCondition(rule.Process);

    private static bool IsMode(AutoSwitchRule rule, string? mode) => rule.Mode.Equals(mode, StringComparison.OrdinalIgnoreCase);

    private static bool IsToolRule(AutoSwitchRule rule) => AutoSwitchConditions.TryParseTool(rule.Process, out _);

    /// <summary>One chip per mode, with the number of programs that start it.</summary>
    private void ShowModes(IReadOnlyList<AutoSwitchRule> rules)
    {
        var codingMode = AutoSwitchSetup.CodingMode(rules);
        List<(string? Key, string Title, string Glyph, Brush Color, int Count)> entries =
        [
            .. _modes.Select(entry => ((string?)entry.Profile.Mode, entry.Profile.Label, entry.Glyph, Palette.ModeColor(entry.Profile.Mode),
                rules.Count(rule => rule.Enabled && IsMode(rule, entry.Profile.Mode) && (IsProgram(rule) || IsToolRule(rule))
                    && (!IsToolRule(rule) || entry.Profile.Mode.Equals(codingMode, StringComparison.OrdinalIgnoreCase))))),
        ];
        _chips = CategoryChips.Sync(ModeBar, _chips, entries, _selectedMode);
    }

    private void OnModeClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CategoryChip { Key: { } mode })
        {
            return;
        }

        _selectedMode = mode;
        Status.Text = "";
        ShowAll(AppSettings.Load().AutoSwitch.Rules);
    }

    /// <summary>The selected mode: what it is, the coding tools when it is the mode they start, and the programs added for it.</summary>
    private void ShowPanel(IReadOnlyList<AutoSwitchRule> rules)
    {
        var entry = _modes.FirstOrDefault(mode => mode.Profile.Mode.Equals(_selectedMode, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return;
        }

        ModeTile.Background = entry.Accent;
        ModeGlyph.Text = entry.Glyph;
        ModeTitle.Text = Loc.F("{0} mode starts when one of these programs runs", entry.Profile.Label);
        ModeDetail.Text = Loc.T(entry.Profile.Intent);

        var showTools = entry.Profile.Mode.Equals(AutoSwitchSetup.CodingMode(rules), StringComparison.OrdinalIgnoreCase);
        ToolsBlock.Visibility = showTools ? Visibility.Visible : Visibility.Collapsed;
        if (showTools)
        {
            ShowTools(rules);
        }

        var mode = entry.Profile.Mode;
        var color = Palette.ModeColor(mode);
        _programRows = [.. rules.Where(rule => IsProgram(rule) && IsMode(rule, mode)).Select(rule => new ProgramRow(rule, entry.Glyph, color, _running))];
        Programs.ItemsSource = _programRows;
        ProgramsEmpty.Text = Loc.T("No program yet. Add one below: this mode starts while it runs.");
        ProgramsEmpty.Visibility = _programRows.Count == 0 && !showTools ? Visibility.Visible : Visibility.Collapsed;
        _ = LoadIconsAsync(_programRows);
    }

    private static async Task LoadIconsAsync(IReadOnlyList<ProgramRow> rows)
    {
        if (await IconCache.PreloadAsync(rows.Select(row => row.Rule.Path)))
        {
            foreach (var row in rows)
            {
                row.Icon = IconCache.Peek(row.Rule.Path);
            }
        }
    }

    /// <summary>Which programs are open now, to tell the user which ones would act.</summary>
    private async Task RefreshRunningAsync()
    {
        _running = await Task.Run(AutoSwitcher.RunningProcessNames);
        foreach (var row in _programRows)
        {
            row.IsRunning = _running.Contains(AutoSwitchPlanner.Normalize(row.Rule.Process));
        }
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
                    rules.Any(rule => rule.Enabled && rule.Process.Equals(AutoSwitchConditions.Tool(tool.Id), StringComparison.OrdinalIgnoreCase)),
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
        Status.Text = Loc.F(toggle.IsChecked == true ? "{0} will start a mode when it opens." : "{0} no longer starts a mode.", row.Name);
    }

    private void OnToolModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || ToolMode.SelectedItem is not ModeChoice mode)
        {
            return;
        }

        var rules = AppSettings.Load().AutoSwitch.Rules
            .Select(rule => IsToolRule(rule) ? rule with { Mode = mode.Mode } : rule)
            .ToList();

        // The tools are listed with the mode they start: follow them there.
        _selectedMode = mode.Mode;
        Save(rules);
    }

    private void OnProgramToggled(object sender, RoutedEventArgs e)
    {
        if (!_loaded || (sender as FrameworkElement)?.DataContext is not ProgramRow row || sender is not Wpf.Ui.Controls.ToggleSwitch toggle)
        {
            return;
        }

        var on = toggle.IsChecked == true;
        Save([.. AppSettings.Load().AutoSwitch.Rules.Select(rule => rule == row.Rule ? rule with { Enabled = on } : rule)]);
        Status.Text = Loc.F(on ? "{0} starts the mode again." : "{0} no longer starts the mode, and stays in the list.", row.Title);
    }

    private void OnProgramRemove(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ProgramRow row)
        {
            Save([.. AppSettings.Load().AutoSwitch.Rules.Where(rule => rule != row.Rule)]);
            Status.Text = Loc.F("{0} removed from the list.", row.Title);
        }
    }

    private void OnNewProgramKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnAddTyped(sender, e);
            e.Handled = true;
        }
    }

    private void OnAddTyped(object sender, RoutedEventArgs e)
    {
        var name = AutoSwitchPlanner.Normalize(NewProgram.Text ?? "");
        if (name.Length == 0 || AutoSwitchConditions.IsCondition(name))
        {
            Status.Text = Loc.T("Type or pick a program name.");
            return;
        }

        if (AddPrograms([new PickedProgram(name, null, null)]))
        {
            NewProgram.Text = "";
        }
    }

    private void OnPickRunning(object sender, RoutedEventArgs e)
    {
        var picker = new ProgramPickerWindow { Owner = Window.GetWindow(this) };
        if (picker.ShowDialog() == true)
        {
            AddPrograms(picker.Picked);
        }
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("Choose a program"),
            Filter = Loc.T("Programs (*.exe)|*.exe"),
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var name = AutoSwitchPlanner.Normalize(dialog.FileName);
        var title = ProgramPickerWindow.DescribeProgram(dialog.FileName, name);
        AddPrograms([new PickedProgram(name, dialog.FileName, title == name ? null : title)]);
    }

    /// <summary>Adds the programs to the selected mode. Returns true when something was added.</summary>
    private bool AddPrograms(IReadOnlyList<PickedProgram> programs)
    {
        var mode = _selectedMode;
        var entry = _modes.FirstOrDefault(item => item.Profile.Mode.Equals(mode, StringComparison.OrdinalIgnoreCase));
        if (mode is null || entry is null)
        {
            Status.Text = Loc.T("Choose a mode.");
            return false;
        }

        var rules = AppSettings.Load().AutoSwitch.Rules.ToList();
        var added = new List<string>();
        foreach (var program in programs)
        {
            var key = AutoSwitchPlanner.Normalize(program.Name);
            if (key.Length == 0 || AutoSwitchConditions.IsCondition(key))
            {
                continue;
            }

            if (rules.FirstOrDefault(rule => rule.Process.Equals(key, StringComparison.OrdinalIgnoreCase)) is { } existing)
            {
                var other = _modes.FirstOrDefault(item => item.Profile.Mode.Equals(existing.Mode, StringComparison.OrdinalIgnoreCase))?.Profile.Label ?? existing.Mode;
                Status.Text = Loc.F("{0} is already in the list, for {1} mode.", program.Label ?? key, other);
                continue;
            }

            rules.Add(new AutoSwitchRule(key, mode, true, program.Path, program.Label));
            added.Add(program.Label ?? key);
        }

        if (added.Count == 0)
        {
            return false;
        }

        Save(rules);
        Status.Text = Loc.F(Enabled.IsChecked == true ? "Added to {1} mode: {0}." : "Added to {1} mode: {0}. Turn automatic switching on to use it.",
            string.Join(", ", added), entry.Profile.Label);
        return true;
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

    /// <summary>The rules that are not a program of a mode: battery, hours, and a program whose mode no longer exists.</summary>
    private void ShowRules(IReadOnlyList<AutoSwitchRule> rules)
    {
        var shown = rules.Where(rule => !IsToolRule(rule) && (!IsProgram(rule) || _modes.All(mode => !IsMode(rule, mode.Profile.Mode)))).ToList();
        Rules.ItemsSource = shown.Select(rule =>
        {
            var entry = _modes.FirstOrDefault(mode => mode.Profile.Mode.Equals(rule.Mode, StringComparison.OrdinalIgnoreCase));
            return new RuleRow(
                rule,
                AutoSwitchConditions.Describe(rule.Process) + "  →  " + Loc.F("{0} mode", entry?.Profile.Label ?? rule.Mode),
                entry is null ? Loc.T("This mode no longer exists: the rule is ignored.") : Explain(rule.Process),
                entry?.Glyph ?? DefaultGlyph,
                entry?.Accent ?? Palette.Neutral);
        }).ToList();
        EmptyState.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
            ShowAll(AppSettings.Load().AutoSwitch.Rules);
            ShowState();
            return;
        }

        Save(AppSettings.Load().AutoSwitch.Rules);
    }

    private void Save(IReadOnlyList<AutoSwitchRule> rules)
    {
        // A game outranks the other programs, and a program that only says "I am working" comes last.
        var ordered = AutoSwitchRules.Prioritize(rules);
        var current = AppSettings.Load();
        (current with
        {
            AutoSwitch = current.AutoSwitch with
            {
                Enabled = Enabled.IsChecked == true,
                RevertWhenClosed = RevertWhenClosed.IsChecked == true,
                GraceSeconds = (Grace.SelectedItem as GraceChoice)?.Seconds ?? AutoSwitchSettings.DefaultGraceSeconds,
                Rules = ordered,
            },
        }).Save();
        ShowAll(ordered);
        ShowState();
        (Application.Current as App)?.ApplyDisplaySettings();
    }

    private static string Explain(string key) => key switch
    {
        AutoSwitchConditions.Battery => Loc.T("While this PC runs on battery"),
        _ when AutoSwitchConditions.TryParseSchedule(key, out _, out _) => Loc.T("Every day during these hours"),
        _ => Loc.T("While this program is open"),
    };

    private void OnTriggerChanged(object sender, SelectionChangedEventArgs e)
    {
        var kind = (Trigger.SelectedItem as TriggerChoice)?.Kind ?? TriggerKind.Battery;
        TimeRange.Visibility = kind == TriggerKind.Schedule ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Builds the rule key from the form, or returns null after explaining what is missing.</summary>
    private string? ReadTrigger()
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        if ((Trigger.SelectedItem as TriggerChoice)?.Kind != TriggerKind.Schedule)
        {
            return AutoSwitchConditions.Battery;
        }

        if (TimeOnly.TryParseExact(TimeFrom.Text.Trim(), ["H:mm", "HH:mm"], culture, System.Globalization.DateTimeStyles.None, out var from)
            && TimeOnly.TryParseExact(TimeTo.Text.Trim(), ["H:mm", "HH:mm"], culture, System.Globalization.DateTimeStyles.None, out var to)
            && from != to)
        {
            return AutoSwitchConditions.Schedule(from, to);
        }

        Status.Text = Loc.T("Type two different times as hours and minutes, for example 09:00 and 18:00.");
        return null;
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
        Battery,
        Schedule,
    }

    private static readonly TriggerChoice[] TriggerChoices =
    [
        new(TriggerKind.Battery, Loc.T("When on battery")),
        new(TriggerKind.Schedule, Loc.T("During these hours")),
    ];

    private sealed record TriggerChoice(TriggerKind Kind, string Label);

    private sealed record ModeChoice(string Mode, string Label);

    private sealed record GraceChoice(int Seconds, string Label);

    private sealed record ToolRow(
        string Id, string Name, ImageSource? Icon, bool IsOn, Visibility RunningVisibility, Visibility GlyphVisibility, Brush Color, Brush Tint);

    private sealed record RuleRow(AutoSwitchRule Rule, string Title, string Detail, string Glyph, Brush Accent);

    /// <summary>A program that starts a mode: its icon and name, whether it is open now, and the switch that turns it off without losing it.</summary>
    private sealed class ProgramRow : INotifyPropertyChanged
    {
        private ImageSource? _icon;
        private bool _isRunning;

        public ProgramRow(AutoSwitchRule rule, string glyph, Brush color, IReadOnlySet<string> running)
        {
            Rule = rule;
            Glyph = glyph;
            Color = color;
            Tint = Palette.Tint(color);
            _icon = IconCache.Peek(rule.Path);
            _isRunning = running.Contains(AutoSwitchPlanner.Normalize(rule.Process));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public AutoSwitchRule Rule { get; }
        public string Glyph { get; }
        public Brush Color { get; }
        public Brush Tint { get; }

        private string Name => AutoSwitchPlanner.Normalize(Rule.Process);

        public string Title => Rule.Label ?? Name;
        public string Subtitle => Rule.Label is null ? "" : $" ({Name})";
        public string Detail => Rule.Path is { } path ? Privacy.Path(path) : Loc.T("While this program is open");
        public bool IsOn => Rule.Enabled;

        /// <summary>A program that is switched off stays in the list, faded.</summary>
        public double Emphasis => Rule.Enabled ? 1 : 0.55;

        public ImageSource? Icon
        {
            get => _icon;
            set
            {
                _icon = value;
                foreach (var property in (string[])[nameof(Icon), nameof(GlyphVisibility)])
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
                }
            }
        }

        public Visibility GlyphVisibility => _icon is null ? Visibility.Visible : Visibility.Collapsed;

        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (_isRunning != value)
                {
                    _isRunning = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RunningVisibility)));
                }
            }
        }

        public Visibility RunningVisibility => _isRunning && Rule.Enabled ? Visibility.Visible : Visibility.Collapsed;
    }
}
