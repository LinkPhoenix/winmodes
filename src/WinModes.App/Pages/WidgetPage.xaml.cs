using System.Windows;
using System.Windows.Controls;
using WinModes.App.Controls;
using WinModes.App.Services;
using WinModes.Core;
using WinModes.Core.Accounts;
using WinModes.Core.Usage;

namespace WinModes.App.Pages;

/// <summary>Every option of the desktop widget. Each change is saved and applied to the widget at once.</summary>
public partial class WidgetPage : Page
{
    private static readonly Option[] RefreshChoices = [new(1, Loc.T("1 second")), new(3, Loc.T("3 seconds")), new(5, Loc.T("5 seconds"))];
    private static readonly Option[] ScaleChoices = [new(80, Loc.T("Small")), new(100, Loc.T("Medium")), new(125, Loc.T("Large")), new(150, Loc.T("Extra large"))];
    private static readonly Option[] PlaceChoices =
    [
        new((int)WidgetPlacement.Desktop, Loc.T("On the desktop")),
        new((int)WidgetPlacement.Taskbar, Loc.T("On the taskbar")),
        new((int)WidgetPlacement.Both, Loc.T("On both")),
    ];
    private static readonly Option[] SideChoices = [new((int)TaskbarSide.Auto, Loc.T("Automatic")), new((int)TaskbarSide.Left, Loc.T("Left")), new((int)TaskbarSide.Right, Loc.T("Right"))];
    private static readonly Option[] PlanStyleChoices = [new((int)PlanStyle.Bars, Loc.T("Bars")), new((int)PlanStyle.Rings, Loc.T("Rings"))];
    private static readonly Option[] MaxToolChoices = [new(2, "2"), new(4, "4"), new(6, "6"), new(10, "10")];

    private readonly bool _loaded;

    public WidgetPage()
    {
        InitializeComponent();

        var settings = AppSettings.Load();
        var widget = settings.Widget;
        Enabled.IsChecked = settings.ShowDesktopWidget;
        AlwaysOnTop.IsChecked = widget.AlwaysOnTop;
        OpacitySlider.Value = widget.OpacityPercent;
        OpacityText.Text = $"{widget.OpacityPercent} %";
        ShowCpu.IsChecked = widget.ShowCpu;
        ShowMemory.IsChecked = widget.ShowMemory;
        ShowNetwork.IsChecked = widget.ShowNetwork;
        ShowMode.IsChecked = widget.ShowMode;
        ShowAiTools.IsChecked = widget.ShowAiTools;
        ShowToolDetail.IsChecked = widget.ShowToolDetail;
        ShowSubscriptions.IsChecked = widget.ShowSubscriptions;
        ShowClaudePlan.IsChecked = widget.ShowClaudePlan;
        ShowCodexPlan.IsChecked = widget.ShowCodexPlan;
        ShowGrokPlan.IsChecked = widget.ShowGrokPlan;
        ShowResetCredits.IsChecked = widget.ShowResetCredits;
        ShowClaudeUsage();
        ShowAccounts();
        LockPosition.IsChecked = widget.LockPosition;
        HideOnFullScreen.IsChecked = widget.HideOnFullScreen;
        Compact.IsChecked = widget.Compact;
        ShowGraph.IsChecked = widget.ShowGraph;
        ClickThrough.IsChecked = widget.ClickThrough;
        Select(Refresh, RefreshChoices, widget.RefreshSeconds);
        Select(Scale, ScaleChoices, widget.ScalePercent);
        Select(MaxTools, MaxToolChoices, widget.MaxTools);
        Select(Place, PlaceChoices, (int)widget.Placement);
        Select(Side, SideChoices, (int)widget.TaskbarSide);
        Select(PlanStyleBox, PlanStyleChoices, (int)widget.PlanStyle);
        ShowPlaceOptions(widget.Placement);

        // Setting the initial values raises the change events; only user changes are saved.
        _loaded = true;
        ShowPreview(settings);

        Loaded += (_, _) =>
        {
            if (Application.Current is App app)
            {
                app.Stats.Updated += OnStats;
            }
        };
        Unloaded += (_, _) =>
        {
            AccountSession.CancelSignIn();
            if (Application.Current is App app)
            {
                app.Stats.Updated -= OnStats;
            }
        };
    }

    private void OnStats(object? sender, StatsReading reading) => Preview.Show(reading);

    private void OnClaudeAccount(object sender, RoutedEventArgs e) => _ = ToggleAccountAsync(AccountProvider.Claude);

    private void OnCodexAccount(object sender, RoutedEventArgs e) => _ = ToggleAccountAsync(AccountProvider.ChatGpt);

    private void OnGrokAccount(object sender, RoutedEventArgs e) => _ = ToggleAccountAsync(AccountProvider.Grok);

    /// <summary>The logos of the two tools, as they look on the taskbar and in the widget; the glyphs stay when a tool is not found.</summary>
    private void ShowToolLogos()
    {
        var claude = ToolIcons.For("Claude");
        var codex = ToolIcons.For("Codex");
        (ClaudeSection.Picture, ClaudeAccountRow.Picture) = (claude, claude);
        (CodexSection.Picture, CodexAccountRow.Picture) = (codex, codex);
        var grok = ToolIcons.For("Grok");
        (GrokSection.Picture, GrokAccountRow.Picture) = (grok, grok);
    }

    private void ShowAccounts()
    {
        ShowToolLogos();
        ShowAccount(AccountProvider.Claude, ClaudeAccountRow, ClaudeAccountButton);
        ShowAccount(AccountProvider.ChatGpt, CodexAccountRow, CodexAccountButton);
        ShowAccount(AccountProvider.Grok, GrokAccountRow, GrokAccountButton);
    }

    private static void ShowAccount(AccountProvider provider, Controls.SettingRow row, Wpf.Ui.Controls.Button button)
    {
        var signedIn = AccountSession.IsSignedIn(provider);
        button.Content = signedIn ? Loc.T("Sign out") : Loc.T("Sign in");
        button.IsEnabled = true;
        row.Description = !signedIn
            ? Loc.F("Not signed in. Sign in so WinModes reads your {0} usage reliably, with a session of its own.", provider.DisplayName)
            : !Privacy.Enabled && AccountSession.Email(provider) is { Length: > 0 } email
                ? Loc.F("Signed in as {0}. The usage is read online with this session.", email)
                : Loc.T("Signed in. The usage is read online with this session.");
    }

    /// <summary>Both directions ask first. Signing out deletes the tokens; signing in opens the browser and waits for it to come back.</summary>
    private async Task ToggleAccountAsync(AccountProvider provider)
    {
        if (AccountSession.IsSignedIn(provider))
        {
            // The same themed dialog as the other confirmations of the app, with the logo, the account and what is deleted and kept.
            var confirmSignOut = new Wpf.Ui.Controls.MessageBox
            {
                Title = Loc.F("Sign out of {0}?", provider.DisplayName),
                Content = AccountPrompt.CreateSignOut(provider),
                PrimaryButtonText = Loc.T("Sign out"),
                CloseButtonText = Loc.T("Cancel"),
            };
            if (await confirmSignOut.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
            {
                return;
            }

            AccountSession.SignOut(provider);
            SubscriptionMonitor.RefreshNow();
            ShowAccounts();
            return;
        }

        // The same themed dialog, with the logo and what is read and kept.
        var consent = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.F("Sign in to {0}", provider.DisplayName),
            Content = AccountPrompt.CreateSignIn(provider, warnAboutRisk: provider == AccountProvider.Claude),
            PrimaryButtonText = Loc.T("Open the browser"),
            CloseButtonText = Loc.T("Cancel"),
        };
        if (await consent.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            return;
        }

        var (row, button) = provider == AccountProvider.Claude ? (ClaudeAccountRow, ClaudeAccountButton)
            : provider == AccountProvider.Grok ? (GrokAccountRow, GrokAccountButton) : (CodexAccountRow, CodexAccountButton);
        button.IsEnabled = false;
        row.Description = Loc.T("Waiting for you to sign in, in your browser…");
        row.Footer = CodeEntry();
        SignInOutcome outcome;
        try
        {
            outcome = await AccountSession.SignInAsync(provider);
        }
        finally
        {
            row.Footer = null;
        }

        ShowAccounts();
        if (outcome == SignInOutcome.SignedIn)
        {
            SubscriptionMonitor.RefreshNow();
            return;
        }

        row.Description = outcome switch
        {
            SignInOutcome.PortBusy => Loc.F("Port {0} is used by another program (Claude Code or Codex may be signing in). Close it and try again.", provider.CallbackPort),
            SignInOutcome.Refused => Loc.T("The sign-in was refused."),
            SignInOutcome.TimedOut => Loc.T("The sign-in did not finish in time."),
            _ => Loc.T("The sign-in failed. Try again."),
        };
    }

    /// <summary>
    /// The way out when the browser cannot come back to this PC (it blocks the local address, or the provider shows a code instead):
    /// the code, or the address the browser ended on, pasted here finishes the same sign-in.
    /// </summary>
    private static Grid CodeEntry()
    {
        var hint = new TextBlock { Text = Loc.T("If your browser shows a code instead of coming back to WinModes, paste it here."), TextWrapping = TextWrapping.Wrap, Foreground = Palette.Neutral };
        var box = new Wpf.Ui.Controls.TextBox { MinWidth = 240, Margin = new Thickness(0, 8, 8, 0), };
        System.Windows.Automation.AutomationProperties.SetName(box, Loc.T("Sign-in code"));
        var use = new Wpf.Ui.Controls.Button { Content = Loc.T("Use this code"), Margin = new Thickness(0, 8, 0, 0) };
        var refused = new TextBlock { Text = Loc.T("This is not the code of this sign-in."), Foreground = Palette.Stop, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
        void Submit()
        {
            refused.Visibility = AccountSession.SubmitCode(box.Text) ? Visibility.Collapsed : Visibility.Visible;
        }

        use.Click += (_, _) => Submit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                Submit();
            }
        };

        var entry = new Grid();
        entry.ColumnDefinitions.Add(new ColumnDefinition());
        entry.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        entry.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        entry.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        entry.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumnSpan(hint, 2);
        Grid.SetRow(box, 1);
        Grid.SetRow(use, 1);
        Grid.SetColumn(use, 1);
        Grid.SetRow(refused, 2);
        Grid.SetColumnSpan(refused, 2);
        foreach (var element in new UIElement[] { hint, box, use, refused })
        {
            entry.Children.Add(element);
        }

        return entry;
    }

    private const string StatusLineFileName = "WinModes.StatusLine.exe";

    /// <summary>
    /// Older versions could add a WinModes status line to Claude Code to record the usage. Nothing reads that record any more: the
    /// row exists only while it is still set, so it can be switched off and removed from Claude Code's settings.
    /// </summary>
    private void ShowClaudeUsage()
    {
        var recorded = ClaudeStatusLineSetup.Read(ClaudeStatusLineSetup.DefaultSettingsPath) == ClaudeStatusLineSetup.State.Ours;
        ClaudeUsage.IsChecked = recorded;
        ClaudeUsageRow.Visibility = recorded ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnClaudeUsageClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ClaudeUsage.IsChecked != true)
            {
                ClaudeStatusLineSetup.Remove(ClaudeStatusLineSetup.DefaultSettingsPath);
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ClaudeUsageRow.Description = Loc.T("Claude Code's settings could not be changed.");
        }

        // Show what the settings really say, whatever was clicked.
        ShowClaudeUsage();
    }

    private void ShowPreview(AppSettings settings)
    {
        var widget = settings.Widget;
        // The preview keeps its natural size so it always fits; the size option is described instead.
        Preview.Apply(widget, scaled: false);
        Preview.Opacity = Math.Clamp(widget.OpacityPercent, 40, 100) / 100d;
        var size = ScaleChoices.FirstOrDefault(choice => choice.Value == widget.ScalePercent)?.Label ?? Loc.T("Medium");
        var where = widget.Placement switch
        {
            WidgetPlacement.Taskbar => Loc.T("The widget is on your taskbar."),
            WidgetPlacement.Both => Loc.T("The widget is on your desktop and on your taskbar."),
            _ => Loc.T("The widget is on your desktop."),
        };
        PreviewNote.Text = (settings.ShowDesktopWidget ? where : Loc.T("The widget is hidden.")) + " "
            + (widget.Placement == WidgetPlacement.Taskbar ? Loc.T("The preview shows the desktop version.") : Loc.F("Size on the desktop: {0}.", size));
    }

    /// <summary>The side applies to the taskbar; size, position and the always-on-top choice to the desktop.</summary>
    private void ShowPlaceOptions(WidgetPlacement placement)
    {
        var onTaskbar = placement != WidgetPlacement.Desktop;
        var desktop = placement == WidgetPlacement.Taskbar ? Visibility.Collapsed : Visibility.Visible;
        SideRow.Visibility = onTaskbar ? Visibility.Visible : Visibility.Collapsed;
        AlwaysOnTopRow.Visibility = desktop;
        CompactRow.Visibility = desktop;
        ScaleRow.Visibility = desktop;
        PositionSection.Visibility = desktop;
    }

    private static void Select(ComboBox box, Option[] choices, int value)
    {
        box.ItemsSource = choices;
        box.SelectedItem = choices.FirstOrDefault(choice => choice.Value == value) ?? choices[0];
    }

    private void OnChanged(object sender, RoutedEventArgs e) => Save();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => Save();

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityText is not null)
        {
            OpacityText.Text = $"{(int)OpacitySlider.Value} %";
        }

        Save();
    }

    private void Save()
    {
        if (!_loaded)
        {
            return;
        }

        var current = AppSettings.Load();
        var updated = current with
        {
            ShowDesktopWidget = Enabled.IsChecked == true,
            Widget = current.Widget with
            {
                Placement = (WidgetPlacement)((Place.SelectedItem as Option)?.Value ?? (int)WidgetPlacement.Desktop),
                TaskbarSide = (TaskbarSide)((Side.SelectedItem as Option)?.Value ?? (int)TaskbarSide.Auto),
                PlanStyle = (PlanStyle)((PlanStyleBox.SelectedItem as Option)?.Value ?? (int)PlanStyle.Bars),
                AlwaysOnTop = AlwaysOnTop.IsChecked == true,
                OpacityPercent = (int)OpacitySlider.Value,
                ScalePercent = (Scale.SelectedItem as Option)?.Value ?? 100,
                ShowCpu = ShowCpu.IsChecked == true,
                ShowMemory = ShowMemory.IsChecked == true,
                ShowNetwork = ShowNetwork.IsChecked == true,
                ShowMode = ShowMode.IsChecked == true,
                ShowAiTools = ShowAiTools.IsChecked == true,
                ShowToolDetail = ShowToolDetail.IsChecked == true,
                ShowSubscriptions = ShowSubscriptions.IsChecked == true,
                ShowClaudePlan = ShowClaudePlan.IsChecked == true,
                ShowCodexPlan = ShowCodexPlan.IsChecked == true,
                ShowGrokPlan = ShowGrokPlan.IsChecked == true,
                ShowResetCredits = ShowResetCredits.IsChecked == true,
                MaxTools = (MaxTools.SelectedItem as Option)?.Value ?? 4,
                RefreshSeconds = (Refresh.SelectedItem as Option)?.Value ?? 3,
                LockPosition = LockPosition.IsChecked == true,
                HideOnFullScreen = HideOnFullScreen.IsChecked == true,
                Compact = Compact.IsChecked == true,
                ShowGraph = ShowGraph.IsChecked == true,
                ClickThrough = ClickThrough.IsChecked == true,
            },
        };
        updated.Save();
        ShowPlaceOptions(updated.Widget.Placement);
        ShowPreview(updated);
        (Application.Current as App)?.ApplyDisplaySettings();
    }

    private void OnCorner(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string name } && Enum.TryParse<WidgetCorner>(name, out var corner))
        {
            // Moving a hidden widget would do nothing visible, so show it first.
            if (Enabled.IsChecked != true)
            {
                Enabled.IsChecked = true;
            }

            (Application.Current as App)?.MoveWidget(corner);
        }
    }

    private sealed record Option(int Value, string Label);
}
