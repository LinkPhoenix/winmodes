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

    private readonly List<ModeCard> _cards;
    private readonly DispatcherTimer _timer = new() { Interval = RefreshInterval };
    private bool _isBusy;

    public ModesPage()
    {
        InitializeComponent();

        _cards = [.. ModeCatalog.Load().Select(entry => new ModeCard(entry))];
        ModeCards.ItemsSource = _cards;

        _timer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) =>
        {
            _timer.Start();
            await RefreshAsync();
        };
        Unloaded += (_, _) => _timer.Stop();
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
        foreach (var card in _cards)
        {
            card.IsActive = card.Profile.Mode.Equals(active, StringComparison.OrdinalIgnoreCase);
        }

        var activeCard = _cards.FirstOrDefault(card => card.IsActive);
        StatusText.Text = activeCard is null
            ? "No mode is active. Windows is in its normal state."
            : $"{activeCard.Label} mode is active.";
        UndoButton.Visibility = activeCard is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnActivateClick(object sender, RoutedEventArgs e)
    {
        if (_isBusy || sender is not FrameworkElement { Tag: ModeCard card })
        {
            return;
        }

        if (card.IsActive)
        {
            await RunSwitchAsync($"Deactivating {card.Label} mode…", "Deactivate", AppServices.Switcher.UndoAsync);
            return;
        }

        if (!Services.AppSettings.Load().ConfirmBeforeActivate)
        {
            await RunSwitchAsync($"Activating {card.Label} mode…", $"{card.Label} mode", () => AppServices.Switcher.ActivateAsync(card.Profile));
            return;
        }

        var confirm = new Wpf.Ui.Controls.MessageBox
        {
            Title = $"Activate {card.Label} mode?",
            Content = $"{card.ChangeCount} {card.ChangeCaption}.\n\n"
                + "Services are stopped and set to Manual, never Disabled. Windows will ask for administrator permission once.\n"
                + (card.Profile.Wsl.Running ? "" : "WSL and Docker Desktop will be shut down: running containers and WSL sessions will end.\n")
                + "\nYou can undo the switch at any time.",
            PrimaryButtonText = "Activate",
            CloseButtonText = "Cancel",
        };
        if (await confirm.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            return;
        }

        await RunSwitchAsync($"Activating {card.Label} mode…", $"{card.Label} mode", () => AppServices.Switcher.ActivateAsync(card.Profile));
    }

    private async void OnUndoClick(object sender, RoutedEventArgs e)
    {
        if (!_isBusy)
        {
            await RunSwitchAsync("Restoring the previous state…", "Undo", AppServices.Switcher.UndoAsync);
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
            ResultTitle.Text = report.Succeeded ? $"{title}: done" : $"{title}: not applied";
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

        DetailTitle.Text = $"{card.Label} mode";
        DetailSummary.Text = "Reading the current state of this PC…";
        ChangeGroups.ItemsSource = null;

        try
        {
            var plan = await Task.Run(() => AppServices.Planner.Plan(card.Profile));
            ChangeGroups.ItemsSource = BuildGroups(plan);
            DetailSummary.Text = plan.Changes.Count == 0
                ? "This PC already matches the mode. Nothing would change."
                : $"{Pluralize(plan.Changes.Count, "change")} would be made. {Pluralize(plan.Skipped.Count, "item")} already in the target state. Nothing is changed by a preview.";
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
                Pluralize(group.Count(), "change"),
                IsExpanded: index == 0,
                [.. group.Select(ToRow)]))];

    private static ChangeRow ToRow(PlannedChange change)
    {
        var (verb, glyph, color) = change.Kind switch
        {
            ChangeKind.StopService => ("Stop", "", Palette.Stop),
            ChangeKind.StartService => ("Start", "", Palette.Start),
            ChangeKind.CloseApp => ("Close", "", Palette.Stop),
            ChangeKind.LaunchApp => ("Launch", "", Palette.Start),
            ChangeKind.ShutdownWsl => ("Stop", "", Palette.Stop),
            ChangeKind.StartDocker => ("Start", "", Palette.Start),
            ChangeKind.SetPowerPlan => ("Switch", "", Palette.Power),
            _ => (change.Kind.ToString(), "", Palette.Neutral),
        };

        return new ChangeRow(glyph, color, Palette.Tint(color), verb, change.Target, change.Reason, change.From, change.To,
            $"{verb} {change.Target}, from {change.From} to {change.To}");
    }

    private static (int Order, string Title, string Glyph, Brush Color) Categorize(ChangeKind kind) => kind switch
    {
        ChangeKind.StopService or ChangeKind.StartService => (0, "Services", "", Palette.Start),
        ChangeKind.CloseApp or ChangeKind.LaunchApp => (1, "Apps", "", Palette.Apps),
        ChangeKind.ShutdownWsl or ChangeKind.StartDocker => (2, "WSL and Docker", "", Palette.Container),
        _ => (3, "Power", "", Palette.Power),
    };

    private static string Pluralize(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private sealed class ModeCard(ModeCatalog.Entry entry) : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isActive;
        private bool _canActivate = true;
        private string _changeCount = "…";
        private string _changeCaption = "checking this PC";

        public event PropertyChangedEventHandler? PropertyChanged;

        public ModeProfile Profile => entry.Profile;
        public string Glyph => entry.Glyph;
        public Brush Accent => entry.Accent;
        public string Label => Profile.Label;
        public string Intent => Profile.Intent;
        public string ChangeCount => _changeCount;
        public string ChangeCaption => _changeCaption;
        public string AccessibleName => $"{Label} mode, {ChangeCount} {ChangeCaption}";

        public bool IsHighlighted => _isSelected || _isActive;
        public string PillText => _isActive ? "ACTIVE" : "PREVIEWING";
        public string ActionText => _isActive ? "Deactivate" : "Activate";

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
            _changeCaption = count == 1 ? "change from the current state" : "changes from the current state";
            NotifySummary();
        }

        public void SetBlocked()
        {
            _changeCount = "!";
            _changeCaption = "blocked by the protection policy";
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
