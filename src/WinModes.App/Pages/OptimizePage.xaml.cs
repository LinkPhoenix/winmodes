using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinModes.App.Controls;
using WinModes.App.Services;
using WinModes.Core.Planning;
using WinModes.Core.Tuning;

namespace WinModes.App.Pages;

/// <summary>
/// Windows settings worth changing, by category: catalog tweaks (registry values, scheduled tasks) and
/// services the knowledge base advises to start on demand. Each row shows the live state and a switch;
/// switches only prepare changes, which the review bar applies in one step. Every change is journaled
/// first, so switching a setting off puts the recorded value back.
/// </summary>
public partial class OptimizePage : Page
{
    // Projects whose code was read for the surveys in research/oss-optimizers: the first twelve, then about thirty more.
    private const int SurveyedTools = 44;
    private static string ServicesTitle => Loc.T("Services");
    private const string AdminGlyph = "";
    private const string AccountGlyph = "";

    /// <summary>Icon, colour and one-line purpose of each catalog category.</summary>
    private static readonly Dictionary<string, (string Glyph, Brush Color, string Subtitle)> Categories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Privacy and telemetry"] = ("", Palette.Apps, "What Windows records about you and sends to Microsoft."),
        ["Ads and suggestions"] = ("", Palette.Stop, "Promoted apps, tips and ads in Start, Settings, the lock screen and File Explorer."),
        ["Search and AI"] = ("", Palette.Container, "Web results in Start search, Widgets, Recall and Windows Copilot."),
        ["Gaming"] = ("", Palette.Power, "Game Mode, background recording and pointer behaviour."),
        ["System and background"] = ("", Palette.Start, "What keeps running or loading when you are not using it."),
        ["Explorer and developer"] = ("", Palette.Neutral, "Small conveniences for File Explorer, the taskbar and development."),
    };

    private const string AllGlyph = "";

    private List<Group> _groups = [];
    private List<CategoryChip> _chips = [];

    /// <summary>The category the list is limited to (its title), or null for all of them.</summary>
    private string? _selectedCategory;
    private HashSet<string> _undoable = new(StringComparer.OrdinalIgnoreCase);
    private bool _hasChangedServices;
    private bool _busy;

    public OptimizePage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private IEnumerable<Row> Rows => _groups.SelectMany(group => group.All);

    private async Task RefreshAsync()
    {
        var (services, changed, states, journaledParts, undoable) = await Task.Run(() => (
            SystemMonitor.GetServices(),
            ServiceTuning.Store.Load(),
            ServiceTuning.Catalog.Tweaks.ToDictionary(tweak => tweak.Id, ServiceTuning.UserTweaks.GetPartStates),
            ServiceTuning.LoadJournaledParts(),
            ServiceTuning.LoadUndoableTweaks()));
        _undoable = undoable;
        _hasChangedServices = changed.Count > 0;

        // Catalog order is kept: categories appear as the catalog lists them.
        _groups = [.. ServiceTuning.Catalog.Tweaks
            // A tweak made only of tasks this PC does not have has nothing to offer.
            .Where(tweak => states[tweak.Id].Any(state => state is not null))
            .GroupBy(tweak => tweak.Category)
            .Select(group =>
            {
                var (glyph, color, subtitle) = Categories.GetValueOrDefault(group.Key, ("", Palette.Neutral, ""));
                return new Group(Loc.T(group.Key), Loc.T(subtitle), glyph, color, [.. group.Select(tweak => Row.For(tweak, states[tweak.Id], journaledParts.GetValueOrDefault(tweak.Id) ?? [], OnPartChanged))]);
            })];

        // Services: what the knowledge base advises, then what was already changed (kept so it can be switched back).
        var byName = services.ToDictionary(service => service.Name, StringComparer.OrdinalIgnoreCase);
        var serviceRows = ServiceTuning.Knowledge.Recommend(services, AppServices.Policy).Select(Row.For)
            .Concat(changed.OrderBy(tweak => tweak.Service, StringComparer.OrdinalIgnoreCase)
                .Select(tweak => Row.For(tweak, byName.GetValueOrDefault(tweak.Service), ServiceTuning.Knowledge.Find(tweak.Service))))
            .ToList();
        if (serviceRows.Count > 0)
        {
            _groups.Add(new Group(ServicesTitle, Loc.T("Services that start with Windows although they are only needed now and then."), "", Palette.Container, serviceRows));
        }

        var recommended = Rows.Where(row => row.IsRecommended || (row.IsService && row.IsApplied)).ToList();
        var applied = recommended.Count(row => row.IsApplied);
        ScoreValue.Text = Loc.F("{0} of {1}", applied, recommended.Count);
        ScoreBar.Value = recommended.Count == 0 ? 0 : applied * 100d / recommended.Count;
        Summary.Text = Rows.Any()
            ? Loc.F("{0} more settings are optional. Recommendations come from a survey of {1} open-source Windows optimizers whose code was read, and from the service knowledge base shipped with WinModes.",
                Rows.Count(row => !row.IsRecommended && !row.IsApplied), SurveyedTools)
            : Loc.T("The tweak catalog (data/tweaks.json) and the knowledge base (data/db) are missing from this copy of WinModes.");
        SelectRecommendedButton.IsEnabled = recommended.Any(row => row.CanToggle && !row.IsApplied);
        UndoAllButton.IsEnabled = _hasChangedServices || undoable.Count > 0;

        ApplyFilter();
        UpdateReview();
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        // Filter events fire while the page is still being built.
        if (Groups is null || SearchBox is null || HideApplied is null)
        {
            return;
        }

        var search = SearchBox.Text.Trim();
        var hideApplied = HideApplied.IsChecked == true;
        foreach (var group in _groups)
        {
            // A row with a pending change stays visible, whatever the filter says.
            group.Show(row => (!hideApplied || !row.IsApplied || row.IsPending)
                && (search.Length == 0
                    || row.Title.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                    || row.Description.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                    || row.Subtitle.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                    || row.Parts.Any(part => part.Label.Contains(search, StringComparison.CurrentCultureIgnoreCase))));
        }

        // The selected category can vanish after a change (the Services group when nothing is left to advise).
        if (_selectedCategory is not null && _groups.All(group => group.Title != _selectedCategory))
        {
            _selectedCategory = null;
        }

        var visible = _groups.Where(group => group.Rows.Count > 0 && (_selectedCategory is null || group.Title == _selectedCategory)).ToList();
        Groups.ItemsSource = visible;
        EmptyText.Visibility = visible.Count == 0 && _groups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowCategories();
    }

    /// <summary>One chip per category plus "All"; the numbers follow the search, so they tell where the matches are.</summary>
    private void ShowCategories()
    {
        List<(string? Key, string Title, string Glyph, Brush Color, int Count)> entries =
            [(null, Loc.T("All"), AllGlyph, Palette.BrandBrush, _groups.Sum(group => group.Rows.Count)), .. _groups.Select(group => ((string?)group.Title, group.Title, group.Glyph, group.Color, group.Rows.Count))];
        _chips = CategoryChips.Sync(CategoryBar, _chips, entries, _selectedCategory);
    }

    private void OnCategoryClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CategoryChip chip)
        {
            return;
        }

        _selectedCategory = chip.Key;
        ApplyFilter();
        ListScroll.ScrollToTop();
    }

    private void OnRowToggled(object sender, RoutedEventArgs e) => UpdateReview();

    /// <summary>A part of a tweak was ticked or unticked: the review bar counts it.</summary>
    private void OnPartChanged() => UpdateReview();

    private void OnToggleDetails(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Row row)
        {
            row.IsExpanded = !row.IsExpanded;
        }
    }

    private void UpdateReview()
    {
        var pending = Rows.Where(row => row.IsPending).ToList();
        ReviewBar.Visibility = pending.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (pending.Count == 0)
        {
            return;
        }

        var toApply = pending.Count(row => row.WillApply);
        var toUndo = pending.Count(row => row.WillUndo);
        ReviewTitle.Text = (toApply, toUndo) switch
        {
            (_, 0) => Loc.F("{0} to apply", toApply),
            (0, _) => Loc.F("{0} to undo", toUndo),
            _ => Loc.F("{0} to apply, {1} to undo", toApply, toUndo),
        };
        ReviewDetail.Text = pending.Any(row => row.NeedsElevationForChange)
            ? Loc.T("Nothing has changed yet. Windows will ask for administrator permission once.")
            : Loc.T("Nothing has changed yet. These settings belong to your account: no administrator permission is needed.");
        StopNow.Visibility = pending.Any(row => row is { IsService: true, IsOn: true, IsRunning: true }) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSelectRecommended(object sender, RoutedEventArgs e)
    {
        foreach (var row in Rows.Where(row => row.IsRecommended && row.CanToggle && !row.IsApplied))
        {
            row.IsOn = true;
        }

        UpdateReview();
    }

    private void OnDiscard(object sender, RoutedEventArgs e)
    {
        foreach (var row in Rows.Where(row => row.IsPending))
        {
            row.Discard();
        }

        ApplyFilter();
        UpdateReview();
    }

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        var pending = Rows.Where(row => row.IsPending).ToList();
        var apply = pending.Where(row => row.WillApply).ToList();
        var undo = pending.Where(row => row.WillUndo).ToList();
        var groups = new List<(TuneAction, IReadOnlyList<string>)>
        {
            (TuneAction.Untweak, [.. undo.Where(row => !row.IsService).Select(row => row.Selection(apply: false))]),
            (TuneAction.Restore, [.. undo.Where(row => row.IsService).Select(row => row.Id)]),
            (TuneAction.Tweak, [.. apply.Where(row => !row.IsService).Select(row => row.Selection(apply: true))]),
            (TuneAction.Manual, [.. apply.Where(row => row.ServiceTarget == ServiceStartMode.Manual).Select(row => row.Id)]),
            (TuneAction.Disabled, [.. apply.Where(row => row.ServiceTarget == ServiceStartMode.Disabled).Select(row => row.Id)]),
        };
        if (StopNow.IsVisible && StopNow.IsChecked == true)
        {
            groups.Add((TuneAction.Stop, [.. apply.Where(row => row is { IsService: true, IsRunning: true }).Select(row => row.Id)]));
        }

        await RunAsync([.. groups.Where(group => group.Item2.Count > 0)]);
    }

    private async void OnUndoAll(object sender, RoutedEventArgs e)
    {
        var confirm = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.T("Undo everything WinModes changed?"),
            Content = Loc.T("Every setting and service changed from this page or from the Services page goes back to the value recorded before the change. A value that something else has changed since is left as it is."),
            PrimaryButtonText = Loc.T("Undo everything"),
            CloseButtonText = Loc.T("Cancel"),
        };
        if (await confirm.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            return;
        }

        var groups = new List<(TuneAction, IReadOnlyList<string>)>();
        if (_undoable.Count > 0)
        {
            groups.Add((TuneAction.Untweak, [.. _undoable]));
        }

        if (_hasChangedServices)
        {
            // No name: the helper restores every service it has on record.
            groups.Add((TuneAction.Restore, []));
        }

        await RunAsync(groups);
    }

    private async Task RunAsync(List<(TuneAction Action, IReadOnlyList<string> Targets)> groups)
    {
        if (_busy || groups.Count == 0)
        {
            return;
        }

        _busy = true;
        IsEnabled = false;
        try
        {
            var report = await ServiceTuning.RunAsync(groups);
            ResultText.Text = report.Summary;
            ResultCard.Visibility = Visibility.Visible;
            // A refused permission prompt changes nothing: keep the switches so the user can try again.
            if (report.Succeeded)
            {
                await RefreshAsync();
            }
        }
        finally
        {
            _busy = false;
            IsEnabled = true;
        }
    }

    private sealed class Group(string title, string subtitle, string glyph, Brush color, List<Row> all)
    {
        public string Title { get; } = title;
        public string Subtitle { get; } = subtitle;
        public string Glyph { get; } = glyph;
        public Brush Color { get; } = color;
        public Brush Tint { get; } = Palette.Tint(color);

        private readonly List<Row> _all = all;

        /// <summary>Every row, whatever the filter.</summary>
        public IReadOnlyList<Row> All => _all;

        /// <summary>Rows that pass the current filter.</summary>
        public List<Row> Rows { get; private set; } = [];

        public string Progress => Loc.F("{0} of {1} applied", _all.Count(row => row.IsApplied), _all.Count);
        public double Percent => _all.Count == 0 ? 0 : _all.Count(row => row.IsApplied) * 100d / _all.Count;

        public void Show(Func<Row, bool> filter) => Rows = [.. _all.Where(filter)];
    }

    /// <summary>One change inside a tweak, with its own tick box: the user can take some of a tweak and leave the rest.</summary>
    private sealed class PartRow : INotifyPropertyChanged
    {
        private readonly Action _changed;
        private bool _isOn;

        public PartRow(TweakPart part, bool applied, bool canUndo, bool hasChoice, Action changed)
        {
            Index = part.Index;
            Label = Loc.T(part.Label);
            Detail = part.Setting is null ? Loc.F("Disables the scheduled task {0}", part.Target) : $"{part.Target} = {part.Setting}";
            MachineWide = part.MachineWide;
            IsApplied = applied;
            _isOn = applied;
            // Applied before WinModes touched it: there is no earlier value on record to put back.
            CanToggle = !applied || canUndo;
            ToggleTip = applied && !canUndo ? Loc.T("Already set on this PC before WinModes changed anything, so there is no earlier value to put back.") : null;
            HasChoice = hasChoice;
            _changed = changed;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public int Index { get; }
        public string Label { get; }
        public string Detail { get; }
        public bool MachineWide { get; }
        public bool IsApplied { get; }
        public bool CanToggle { get; }
        public string? ToggleTip { get; }
        public bool HasChoice { get; }

        /// <summary>Only a tweak with several changes lets you pick; a single change is just described.</summary>
        public Visibility ChoiceVisibility => HasChoice ? Visibility.Visible : Visibility.Collapsed;

        public bool IsOn
        {
            get => _isOn;
            set
            {
                if (_isOn == value)
                {
                    return;
                }

                _isOn = value;
                foreach (var name in (string[])[nameof(IsOn), nameof(Status), nameof(StatusBrush)])
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
                }

                _changed();
            }
        }

        public bool IsPending => IsOn != IsApplied;

        public string Status => Loc.T(IsPending ? IsOn ? "Will be applied" : "Will be undone" : IsApplied ? "Applied" : "Not applied");

        public Brush StatusBrush => IsPending
            ? Palette.Apps
            : IsApplied ? Palette.Start
            : Application.Current.TryFindResource("TextFillColorSecondaryBrush") as Brush ?? Palette.Neutral;
    }

    private sealed class Row : INotifyPropertyChanged
    {
        private const string ChevronDown = "";
        private const string ChevronUp = "";

        private bool _isOn;
        private bool _isExpanded;

        public event PropertyChangedEventHandler? PropertyChanged;

        public required string Id { get; init; }
        public required string Title { get; init; }
        public string Subtitle { get; init; } = "";
        public string Description { get; init; } = "";
        public string? Warning { get; init; }
        public bool IsRecommended { get; init; }
        public bool IsApplied { get; init; }
        public bool IsPartial { get; init; }
        public bool NeedsElevation { get; init; }
        public bool IsService { get; init; }
        public bool IsRunning { get; init; }
        public string? ToggleTip { get; init; }

        /// <summary>The tweak a row stands for; null for a service.</summary>
        public Tweak? Tweak { get; init; }

        /// <summary>The changes of a tweak that this PC can show (a task it does not have is left out). Empty for a service.</summary>
        public List<PartRow> Parts { get; init; } = [];

        /// <summary>The start type to set when a service row is switched on.</summary>
        public ServiceStartMode? ServiceTarget { get; init; }

        public required string Note { get; init; }
        public required Brush NoteTint { get; init; }
        public required string NoteTip { get; init; }
        public string Tools { get; init; } = "";
        public string ToolsTip { get; init; } = "";
        public string Change { get; init; } = "";
        public string Restart { get; init; } = "";
        public string RestartTip { get; init; } = "";

        /// <summary>The switch can be used when at least one change can still be put back (or has not been made).</summary>
        public bool CanToggle => Parts.Count == 0 || Parts.Any(part => part.CanToggle);

        /// <summary>What the user wants; differs from <see cref="IsApplied"/> while a change is pending. For a tweak, the switch is on when every change is.</summary>
        public bool IsOn
        {
            get => Parts.Count == 0 ? _isOn : Parts.All(part => part.IsOn);
            set
            {
                if (Parts.Count > 0)
                {
                    // Each part tells the row through the callback it was built with.
                    foreach (var part in Parts.Where(part => part.CanToggle))
                    {
                        part.IsOn = value;
                    }

                    return;
                }

                if (_isOn == value)
                {
                    return;
                }

                _isOn = value;
                Notify();
            }
        }

        public bool IsPending => Parts.Count == 0 ? IsOn != IsApplied : Parts.Any(part => part.IsPending);

        public bool WillApply => Parts.Count == 0 ? IsOn && !IsApplied : Parts.Any(part => part.IsOn && !part.IsApplied);

        public bool WillUndo => Parts.Count == 0 ? !IsOn && IsApplied : Parts.Any(part => !part.IsOn && part.IsApplied);

        /// <summary>True when what is pending reaches a machine-wide setting, so Windows asks for administrator permission.</summary>
        public bool NeedsElevationForChange => Parts.Count == 0 ? NeedsElevation : Parts.Any(part => part.IsPending && part.MachineWide);

        /// <summary>"id" for the whole tweak, or "id#parts" for the parts to apply (or to undo): what the engines are given.</summary>
        public string Selection(bool apply) => Tweak is null
            ? Id
            : TweakSelection.Format(Tweak, Parts.Where(part => apply ? part.IsOn && !part.IsApplied : !part.IsOn && part.IsApplied).Select(part => part.Index));

        public void Discard()
        {
            if (Parts.Count == 0)
            {
                IsOn = IsApplied;
                return;
            }

            foreach (var part in Parts)
            {
                part.IsOn = part.IsApplied;
            }
        }

        public string Status => Loc.T(
            IsPending
                ? WillApply && WillUndo ? "Will be changed" : WillApply ? "Will be applied" : "Will be undone"
                : IsApplied ? "Applied" : IsPartial ? "Partly applied" : "Not applied");

        public Brush StatusBrush => IsPending
            ? Palette.Apps
            : IsApplied ? Palette.Start
            : IsPartial ? Palette.Power
            : Application.Current.TryFindResource("TextFillColorSecondaryBrush") as Brush ?? Palette.Neutral;

        public string ScopeGlyph => NeedsElevation ? AdminGlyph : AccountGlyph;

        public string ScopeTip => Loc.T(NeedsElevation
            ? "Applies to the whole PC. Windows asks for administrator permission."
            : "Applies to your account only. No administrator permission needed.");

        public Visibility WarningVisibility => Visible(!string.IsNullOrEmpty(Warning));
        public Visibility ToolsVisibility => Visible(Tools.Length > 0);
        public Visibility ChangeVisibility => Visible(Change.Length > 0);
        public Visibility RestartVisibility => Visible(Restart.Length > 0);

        /// <summary>A tweak can be opened to see, and choose among, the changes it makes.</summary>
        public Visibility ExpanderVisibility => Visible(Parts.Count > 0);

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value)
                {
                    return;
                }

                _isExpanded = value;
                foreach (var name in (string[])[nameof(IsExpanded), nameof(Details), nameof(DetailsVisibility), nameof(ChevronGlyph), nameof(ExpanderTip)])
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
                }
            }
        }

        /// <summary>The row itself while it is open, null while it is closed, so the list of changes is only built when someone looks at it.</summary>
        public Row? Details => _isExpanded ? this : null;

        /// <summary>A template with no content is still drawn, so the closed state is hidden as well.</summary>
        public Visibility DetailsVisibility => Visible(_isExpanded);

        public string ChevronGlyph => _isExpanded ? ChevronUp : ChevronDown;

        public string ExpanderTip => Loc.T(_isExpanded ? "Hide the details" : "Show what it changes");

        public string ChoiceHint => Parts.Count > 1
            ? Loc.T("Choose which of these changes to make. Unticking one that is applied puts its earlier value back.")
            : "";

        public Visibility ChoiceHintVisibility => Visible(Parts.Count > 1);

        /// <summary>Tells the page that the switch, the status or the pending state of the row changed.</summary>
        public void Notify()
        {
            foreach (var name in (string[])[nameof(IsOn), nameof(Status), nameof(StatusBrush)])
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }

        public static Row For(Tweak tweak, IReadOnlyList<bool?> partStates, HashSet<int> journaledParts, Action changed)
        {
            var (restart, restartTip) = tweak.Restart switch
            {
                "sign-out" => (Loc.T("Shows after sign-out"), Loc.T("Visible after you sign out and back in.")),
                "explorer" => (Loc.T("Shows in new Explorer windows"), Loc.T("Visible in File Explorer windows opened afterwards.")),
                "restart" => (Loc.T("Shows after a restart"), Loc.T("Visible after Windows restarts.")),
                _ => ("", ""),
            };
            var medium = tweak.Risk != "low";
            var shown = tweak.Parts.Where(part => partStates[part.Index] is not null).ToList();
            Row? row = null;
            var parts = shown.Select(part => new PartRow(part, partStates[part.Index]!.Value, journaledParts.Contains(part.Index), shown.Count > 1, () =>
            {
                row?.Notify();
                changed();
            })).ToList();
            var applied = parts.All(part => part.IsApplied);

            row = new Row
            {
                Id = tweak.Id,
                Title = Loc.T(tweak.Title),
                Description = Loc.T(tweak.Description),
                Warning = string.IsNullOrEmpty(tweak.Warning) ? tweak.Warning : Loc.T(tweak.Warning),
                IsRecommended = tweak.Recommended,
                IsApplied = applied,
                IsPartial = !applied && parts.Any(part => part.IsApplied),
                NeedsElevation = tweak.NeedsElevation,
                Tweak = tweak,
                Parts = parts,
                // Every change was made before WinModes touched it: there is no earlier value to put back.
                ToggleTip = parts.All(part => part.IsApplied && !part.CanToggle) ? parts[0].ToggleTip : null,
                Note = Loc.T(tweak.Recommended ? "Recommended" : medium ? "Check first" : "Optional"),
                NoteTint = Palette.Tint(tweak.Recommended ? Palette.Start : medium ? Palette.Power : Palette.Neutral),
                NoteTip = Loc.T(tweak.Recommended
                    ? "Low risk and shipped by several open-source optimizers whose code was read."
                    : tweak.Tools.Count > 0
                        ? "A matter of preference or with a side effect: read the note before applying it."
                        : "Well-known setting, but not part of the code-verified survey: optional."),
                Tools = tweak.Tools.Count > 0 ? Loc.N(tweak.Tools.Count, "1 optimizer", "{0} optimizers") : "",
                ToolsTip = Loc.F("Also set by: {0}.", string.Join(", ", tweak.Tools)),
                Restart = restart,
                RestartTip = restartTip,
            };
            return row;
        }

        public static Row For(ServiceRecommendation recommendation)
        {
            var target = recommendation.Advice.Recommended ?? ServiceStartMode.Manual;
            return new Row
            {
                Id = recommendation.Service.Name,
                Title = recommendation.Service.DisplayName,
                Subtitle = recommendation.Service.Name,
                Description = recommendation.Advice.Description,
                IsRecommended = recommendation.IsConfident,
                NeedsElevation = true,
                IsService = true,
                IsRunning = recommendation.Service.IsRunning,
                ServiceTarget = target,
                Note = Loc.T(recommendation.IsConfident ? "Recommended" : "Check first"),
                NoteTint = Palette.Tint(recommendation.IsConfident ? Palette.Start : Palette.Power),
                NoteTip = Loc.T(recommendation.IsConfident
                    ? "Backed by a published source and rated low risk."
                    : recommendation.Advice.IsSourced
                        ? "Rated medium risk: switch it on only if you do not use this feature."
                        : "Identified on a PC but not backed by a published source yet: switch it on only if you know you do not need it."),
                Change = $"{recommendation.Service.StartMode} → {target}",
                Restart = Loc.T(recommendation.Service.IsRunning ? "Running now" : "Not running"),
                RestartTip = Loc.T("Changing the start type does not stop the service: it applies the next time Windows starts."),
            };
        }

        /// <summary>A service whose start type was already changed: switching it off restores the original.</summary>
        public static Row For(ServiceTweak tweak, ServiceInfo? service, ServiceAdvice? advice) => new()
        {
            Id = tweak.Service,
            Title = service?.DisplayName ?? tweak.Service,
            Subtitle = tweak.Service,
            Description = advice?.Description ?? "",
            IsApplied = true,
            _isOn = true,
            NeedsElevation = true,
            IsService = true,
            IsRunning = service?.IsRunning ?? false,
            Note = Loc.T("Changed by you"),
            NoteTint = Palette.Tint(Palette.Neutral),
            NoteTip = Loc.F("Changed {0:g}. Switch it off to restore {1}.", tweak.ChangedUtc.ToLocalTime(), tweak.OriginalStartMode),
            Change = $"{tweak.OriginalStartMode} → {service?.StartMode ?? tweak.SetTo}",
        };

        private static Visibility Visible(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
    }
}
