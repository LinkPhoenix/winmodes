using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinModes.App.Services;
using WinModes.Core.Planning;
using WinModes.Core.Profiles;

namespace WinModes.App.Pages;

/// <summary>Mode cards and the grouped preview of the selected mode. Read-only.</summary>
public partial class ModesPage : Page
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    private List<ModeCard> _cards = [];
    private readonly DispatcherTimer _timer = new() { Interval = RefreshInterval };

    // The countdown of automatic switching moves every second; the rest of the page only every few.
    private readonly DispatcherTimer _countdown = new() { Interval = TimeSpan.FromSeconds(1) };
    private string? _activeMode;
    private bool _isBusy;

    public ModesPage()
    {
        InitializeComponent();

        ReloadCards();
        AutoBadge.Background = Palette.Tint(Palette.BrandBrush);
        AutoGlyph.Foreground = Palette.BrandBrush;
        ShowAutomation(withHint: true);

        _timer.Tick += async (_, _) => await RefreshAsync();
        _countdown.Tick += (_, _) => ShowAutomation(withHint: false);
        Loaded += async (_, _) =>
        {
            _timer.Start();
            _countdown.Start();
            ModeSwitcher.Changed += OnModeChanged;
            AutoSwitcher.StatusChanged += OnAutoStatusChanged;
            await RefreshAsync();
        };
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            _countdown.Stop();
            ModeSwitcher.Changed -= OnModeChanged;
            AutoSwitcher.StatusChanged -= OnAutoStatusChanged;
        };
    }

    // A switch made by the tray, a hotkey or automatic switching shows here at once instead of at the next refresh.
    private void OnModeChanged() => Dispatcher.BeginInvoke(async () => await RefreshAsync());

    private void OnAutoStatusChanged() => Dispatcher.BeginInvoke(() => ShowAutomation(withHint: false));

    private void OnAutoSetupClick(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.NavigateTo(typeof(AutomationPage));

    private void OnAutoToggled(object sender, RoutedEventArgs e)
    {
        AutoSwitchSetup.SetEnabled(AutoToggle.IsChecked == true);
        ShowAutomation(withHint: true);
    }

    private void ShowAutomation(bool withHint)
    {
        var settings = Services.AppSettings.Load().AutoSwitch;
        AutoToggle.IsChecked = settings.Enabled;
        AutoStatus.Text = !settings.Enabled
            ? Loc.T("Off. Turned on, Code mode starts by itself when Claude Code, Codex or another coding tool opens, and ends a little after the last one closes.")
            : settings.Rules.Count == 0
                ? Loc.T("No rule yet. Open Set up to choose the tools and programs that start a mode.")
                : AutoSwitchText.Describe(AutoSwitcher.Status, _activeMode, DateTimeOffset.Now);

        if (withHint)
        {
            // Asking Windows whether the silent-switch task exists is not done every second.
            var asks = settings.Enabled && !ModeSwitcher.SwitchesSilently;
            AutoHint.Visibility = asks ? Visibility.Visible : Visibility.Collapsed;
            AutoHint.Text = ModeSwitcher.CanSwitchSilently
                ? Loc.T("Windows asks for permission at each switch. Turn on the switch without a prompt in Set up to stop that.")
                : Loc.T("Windows asks for permission at each switch. Without a prompt is possible once WinModes is installed with its setup program.");
        }
    }

    private void ReloadCards()
    {
        _cards = [.. ModeCatalog.Load().Select(entry => new ModeCard(entry))];
        ModeCards.ItemsSource = _cards;
    }

    private static ModeCard? CardOf(object sender) => (sender as FrameworkElement)?.DataContext as ModeCard;

    private async Task ChangeLibraryAsync(string done, Action change)
    {
        try
        {
            change();
            ReloadCards();
            ResultCard.Visibility = Visibility.Visible;
            ResultTitle.Text = done;
            ResultLines.ItemsSource = null;
            await RefreshAsync();
        }
        catch (Exception ex) when (ex is ProfileException or System.IO.IOException or UnauthorizedAccessException)
        {
            ResultCard.Visibility = Visibility.Visible;
            ResultTitle.Text = Loc.T("Not done");
            ResultLines.ItemsSource = new[] { ex.Message };
        }
    }

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is not { } card)
        {
            return;
        }

        if (card.IsActive)
        {
            await ChangeLibraryAsync("", () => throw new ProfileException(Loc.T("Deactivate this mode before editing it.")));
            return;
        }

        var editor = new ModeEditorWindow(card.Profile.Mode) { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() == true)
        {
            await ChangeLibraryAsync(Loc.F("{0} mode saved", card.Label), () => { });
        }
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = Loc.T("Import a mode"), Filter = Loc.T("Mode profile") + " (*.json)|*.json" };
        if (dialog.ShowDialog() == true)
        {
            await ChangeLibraryAsync(Loc.T("Mode imported"), () => AppServices.Library.Import(dialog.FileName));
        }
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is not { } card)
        {
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Loc.F("Export {0} mode", card.Label),
            Filter = Loc.T("Mode profile") + " (*.json)|*.json",
            FileName = card.Profile.Mode + ".json",
        };
        if (dialog.ShowDialog() == true)
        {
            await ChangeLibraryAsync(Loc.F("{0} mode exported", card.Label), () => AppServices.Library.Export(card.Profile.Mode, dialog.FileName));
        }
    }

    private async void OnDuplicateClick(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is not { } card)
        {
            return;
        }

        // First free name: code-2, code-3...
        var existing = AppServices.Store.ListModes();
        var index = 2;
        while (existing.Contains($"{card.Profile.Mode}-{index}", StringComparer.OrdinalIgnoreCase))
        {
            index++;
        }

        await ChangeLibraryAsync(Loc.F("{0} mode duplicated", card.Label),
            () => AppServices.Library.Duplicate(card.Profile.Mode, $"{card.Profile.Mode}-{index}", $"{card.Label} {index}"));
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is not { } card)
        {
            return;
        }

        if (card.IsActive)
        {
            await ChangeLibraryAsync("", () => throw new ProfileException(Loc.T("Deactivate this mode before deleting it.")));
            return;
        }

        var confirm = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.F("Delete {0} mode?", card.Label),
            Content = Loc.T("Its profile file is removed. Export it first if you want to keep a copy."),
            PrimaryButtonText = Loc.T("Delete"),
            CloseButtonText = Loc.T("Cancel"),
        };
        if (await confirm.ShowDialogAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            await ChangeLibraryAsync(Loc.F("{0} mode deleted", card.Label), () => AppServices.Library.Delete(card.Profile.Mode));
        }
    }

    private async Task RefreshAsync()
    {
        if (_isBusy || !IsVisible)
        {
            return;
        }

        ShowActiveMode();
        await LoadChangeCountsAsync();
    }

    private void ShowActiveMode()
    {
        var active = ModeSwitcher.ActiveMode;
        _activeMode = active;
        var automatic = active is not null && ModeSwitcher.ActiveModeIsAutomatic;
        foreach (var card in _cards)
        {
            card.IsActive = card.Profile.Mode.Equals(active, StringComparison.OrdinalIgnoreCase);
            card.IsAutomatic = card.IsActive && automatic;
        }

        var activeCard = _cards.FirstOrDefault(card => card.IsActive);
        StatusText.Text = activeCard is null
            ? Loc.T("No mode is active. Windows is in its normal state.")
            : Loc.F(automatic ? "{0} mode is active, started automatically." : "{0} mode is active.", activeCard.Label);
        UndoButton.Visibility = activeCard is null ? Visibility.Collapsed : Visibility.Visible;
        ShowAutomation(withHint: false);
    }

    private async void OnActivateClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy || sender is not FrameworkElement { Tag: ModeCard card })
        {
            return;
        }

        if (card.IsActive)
        {
            await RunSwitchAsync(Loc.F("Deactivating {0} mode…", card.Label), Loc.T("Deactivate"), AppServices.Switcher.UndoAsync);
            return;
        }

        if (!Services.AppSettings.Load().ConfirmBeforeActivate)
        {
            await RunSwitchAsync(Loc.F("Activating {0} mode…", card.Label), Loc.F("{0} mode", card.Label), () => AppServices.Switcher.ActivateAsync(card.Profile));
            return;
        }

        var confirm = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.F("Activate {0} mode?", card.Label),
            Content = $"{card.ChangeCount} {card.ChangeCaption}.\n\n"
                + Loc.T("Services are stopped and set to Manual, never Disabled. Windows will ask for administrator permission once.") + "\n"
                + (card.Profile.Wsl.Running ? "" : Loc.T("WSL and Docker Desktop will be shut down: running containers and WSL sessions will end.") + "\n")
                + "\n" + Loc.T("You can undo the switch at any time."),
            PrimaryButtonText = Loc.T("Activate"),
            CloseButtonText = Loc.T("Cancel"),
        };
        if (await confirm.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            return;
        }

        await RunSwitchAsync(Loc.F("Activating {0} mode…", card.Label), Loc.F("{0} mode", card.Label), () => AppServices.Switcher.ActivateAsync(card.Profile));
    }

    private async void OnUndoClick(object sender, RoutedEventArgs e)
    {
        if (!_isBusy)
        {
            await RunSwitchAsync(Loc.T("Restoring the previous state…"), Loc.T("Undo"), AppServices.Switcher.UndoAsync);
        }
    }

    private async Task RunSwitchAsync(string progress, string title, Func<Task<SwitchReport>> action)
    {
        _isBusy = true;
        SetCardsEnabled(false);
        ResultCard.Visibility = Visibility.Visible;
        ResultTitle.Text = progress;
        ResultLines.ItemsSource = null;

        try
        {
            var report = await action();
            ResultTitle.Text = Loc.F(report.Succeeded ? "{0}: done" : "{0}: not applied", title);
            ResultLines.ItemsSource = report.Lines;
        }
        finally
        {
            _isBusy = false;
            SetCardsEnabled(true);
            await RefreshAsync();
        }
    }

    private void SetCardsEnabled(bool enabled)
    {
        foreach (var card in _cards)
        {
            card.CanActivate = enabled;
        }
    }

    private async Task LoadChangeCountsAsync()
    {
        foreach (var card in _cards)
        {
            try
            {
                // Planning queries every service; keep it off the UI thread.
                var plan = await Task.Run(() => AppServices.Planner.Plan(card.Profile));
                card.SetChangeCount(plan.Changes.Count);
            }
            catch (ProfileException)
            {
                card.SetBlocked();
            }
        }
    }

    private async void OnPreviewClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ModeCard card })
        {
            return;
        }

        foreach (var other in _cards)
        {
            other.IsSelected = ReferenceEquals(other, card);
        }

        DetailTitle.Text = Loc.F("{0} mode", card.Label);
        DetailSummary.Text = Loc.T("Reading the current state of this PC…");
        ChangeGroups.ItemsSource = null;

        try
        {
            var plan = await Task.Run(() => AppServices.Planner.Plan(card.Profile));
            ChangeGroups.ItemsSource = BuildGroups(plan);
            DetailSummary.Text = plan.Changes.Count == 0
                ? Loc.T("This PC already matches the mode. Nothing would change.")
                : Loc.N(plan.Changes.Count, "1 change would be made.", "{0} changes would be made.") + " "
                    + Loc.N(plan.Skipped.Count, "1 item already in the target state.", "{0} items already in the target state.") + " "
                    + Loc.T("Nothing is changed by a preview.");
        }
        catch (ProfileException ex)
        {
            DetailSummary.Text = ex.Message;
        }
    }

    private static List<ChangeGroup> BuildGroups(ModePlan plan) =>
        [.. plan.Changes
            .GroupBy(change => Categorize(change.Kind))
            .OrderBy(group => group.Key.Order)
            .Select((group, index) => new ChangeGroup(
                group.Key.Title,
                group.Key.Glyph,
                group.Key.Color,
                Palette.Tint(group.Key.Color),
                Loc.N(group.Count(), "1 change", "{0} changes"),
                IsExpanded: index == 0,
                [.. group.Select(ToRow)]))];

    private static ChangeRow ToRow(PlannedChange change)
    {
        var (verb, glyph, color) = change.Kind switch
        {
            ChangeKind.StopService => (Loc.T("Stop"), "", Palette.Stop),
            ChangeKind.StartService => (Loc.T("Start"), "", Palette.Start),
            ChangeKind.CloseApp => (Loc.T("Close"), "", Palette.Stop),
            ChangeKind.LaunchApp => (Loc.T("Launch"), "", Palette.Start),
            ChangeKind.ShutdownWsl => (Loc.T("Stop"), "", Palette.Stop),
            ChangeKind.StartDocker => (Loc.T("Start"), "", Palette.Start),
            ChangeKind.SetPowerPlan => (Loc.T("Switch"), "", Palette.Power),
            _ => (change.Kind.ToString(), "", Palette.Neutral),
        };

        return new ChangeRow(glyph, color, Palette.Tint(color), verb, change.Target, change.Reason, change.From, change.To,
            Loc.F("{0} {1}, from {2} to {3}", verb, change.Target, change.From, change.To));
    }

    private static (int Order, string Title, string Glyph, Brush Color) Categorize(ChangeKind kind) => kind switch
    {
        ChangeKind.StopService or ChangeKind.StartService => (0, Loc.T("Services"), "", Palette.Start),
        ChangeKind.CloseApp or ChangeKind.LaunchApp => (1, Loc.T("Apps"), "", Palette.Apps),
        ChangeKind.ShutdownWsl or ChangeKind.StartDocker => (2, Loc.T("WSL and Docker"), "", Palette.Container),
        _ => (3, Loc.T("Power"), "", Palette.Power),
    };


    private sealed class ModeCard(ModeCatalog.Entry entry) : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isActive;
        private bool _isAutomatic;
        private bool _canActivate = true;
        private string _changeCount = "…";
        private string _changeCaption = Loc.T("checking this PC");

        public event PropertyChangedEventHandler? PropertyChanged;

        public ModeProfile Profile => entry.Profile;
        public string Glyph => entry.Glyph;
        public Brush Accent => entry.Accent;
        public string Label => Profile.Label;
        public string Intent => Loc.T(Profile.Intent);
        public string ChangeCount => _changeCount;
        public string ChangeCaption => _changeCaption;
        public string AccessibleName => Loc.F("{0} mode", Label) + $", {ChangeCount} {ChangeCaption}";

        public bool IsHighlighted => _isSelected || _isActive;
        public string PillText => Loc.T(_isActive ? (_isAutomatic ? "ACTIVE (AUTO)" : "ACTIVE") : "PREVIEWING");
        public string ActionText => Loc.T(_isActive ? "Deactivate" : "Activate");
        public bool CanDelete => !ProfileLibrary.IsBuiltIn(Profile.Mode);

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                NotifyHighlight();
            }
        }

        public bool IsActive
        {
            get => _isActive;
            set
            {
                _isActive = value;
                NotifyHighlight();
            }
        }

        /// <summary>The mode is on because automatic switching started it.</summary>
        public bool IsAutomatic
        {
            get => _isAutomatic;
            set
            {
                _isAutomatic = value;
                Notify(nameof(PillText));
            }
        }

        public bool CanActivate
        {
            get => _canActivate;
            set
            {
                _canActivate = value;
                Notify(nameof(CanActivate));
            }
        }

        private void NotifyHighlight()
        {
            Notify(nameof(IsHighlighted));
            Notify(nameof(PillText));
            Notify(nameof(ActionText));
        }

        public void SetChangeCount(int count)
        {
            _changeCount = count.ToString(CultureInfo.CurrentCulture);
            _changeCaption = Loc.T(count == 1 ? "change from the current state" : "changes from the current state");
            NotifySummary();
        }

        public void SetBlocked()
        {
            _changeCount = "!";
            _changeCaption = Loc.T("blocked by the protection policy");
            NotifySummary();
        }

        private void NotifySummary()
        {
            Notify(nameof(ChangeCount));
            Notify(nameof(ChangeCaption));
            Notify(nameof(AccessibleName));
        }

        private void Notify(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }

    private sealed record ChangeGroup(
        string Title, string Glyph, Brush Color, Brush Tint, string CountSummary, bool IsExpanded, IReadOnlyList<ChangeRow> Rows);

    private sealed record ChangeRow(
        string Glyph, Brush Color, Brush Tint, string Verb, string Target, string Reason, string From, string To, string AccessibleName);
}
