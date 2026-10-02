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
        ReadClaudeOnline.IsChecked = widget.ClaudeOnline;
        ReadCodexOnline.IsChecked = widget.CodexOnline;
        ShowClaudePlan.IsChecked = widget.ShowClaudePlan;
        ShowCodexPlan.IsChecked = widget.ShowCodexPlan;
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

    /// <summary>The logos of the two tools, as they look on the taskbar and in the widget; the glyphs stay when a tool is not found.</summary>
    private void ShowToolLogos()
    {
        var claude = ToolIcons.For("Claude");
        var codex = ToolIcons.For("Codex");
        (ClaudeSection.Picture, ClaudeAccountRow.Picture) = (claude, claude);
        (CodexSection.Picture, CodexAccountRow.Picture) = (codex, codex);
    }

    private string? _claudeOnlineText;
    private string? _codexOnlineText;

    private void ShowAccounts()
    {
        ShowToolLogos();
        ShowAccount(AccountProvider.Claude, ClaudeAccountRow, ClaudeAccountButton);
        ShowAccount(AccountProvider.ChatGpt, CodexAccountRow, CodexAccountButton);

        // Signed in, WinModes reads online with its own session whatever the option says: the option would only mislead.
        _claudeOnlineText ??= ClaudeOnlineRow.Description;
        _codexOnlineText ??= CodexOnlineRow.Description;
        ShowOnlineOption(ClaudeOnlineRow, ReadClaudeOnline, AccountSession.IsSignedIn(AccountProvider.Claude), _claudeOnlineText);
        ShowOnlineOption(CodexOnlineRow, ReadCodexOnline, AccountSession.IsSignedIn(AccountProvider.ChatGpt), _codexOnlineText);
    }

    private static void ShowOnlineOption(Controls.SettingRow row, Wpf.Ui.Controls.ToggleSwitch toggle, bool signedIn, string original)
    {
        toggle.IsEnabled = !signedIn;
        row.Description = signedIn ? Loc.T("Not needed while WinModes is signed in: it reads the usage with its own session.") : original;
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

    /// <summary>Signs out at once; signing in asks first, opens the browser and waits for it to come back.</summary>
    private async Task ToggleAccountAsync(AccountProvider provider)
    {
        if (AccountSession.IsSignedIn(provider))
        {
            AccountSession.SignOut(provider);
            SubscriptionMonitor.RefreshNow();
            ShowAccounts();
            return;
        }

        // The same themed dialog as the other confirmations of the app, with the logo and what is read and kept.
        var consent = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.F("Sign in to {0}", provider.DisplayName),
            Content = SignInPrompt.Create(provider, warnAboutRisk: provider == AccountProvider.Claude),
            PrimaryButtonText = Loc.T("Open the browser"),
            CloseButtonText = Loc.T("Cancel"),
        };
        if (await consent.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            return;
        }

        var (row, button) = provider == AccountProvider.Claude ? (ClaudeAccountRow, ClaudeAccountButton) : (CodexAccountRow, CodexAccountButton);
        button.IsEnabled = false;
        row.Description = Loc.T("Waiting for you to sign in, in your browser…");
        var outcome = await AccountSession.SignInAsync(provider);
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

    private async void OnCheckClaude(object sender, RoutedEventArgs e)
    {
        CheckClaudeButton.IsEnabled = false;
        try
        {
            var readOnline = ReadClaudeOnline.IsChecked == true;
            var signedIn = AccountSession.IsSignedIn(AccountProvider.Claude);
            var items = await Task.Run(() => ProviderCheck.Claude(Subscriptions.DefaultClaudeSettings, ClaudeStatusLineSetup.DefaultSettingsPath,
                ClaudeStatusLine.DefaultRecordPath, OnlineUsage.DefaultClaudeCredentials, ClaudeStatusLine.DefaultCallPath, readOnline, DateTimeOffset.UtcNow, signedIn));
            ClaudeCheck.Show(items);
        }
        finally
        {
            CheckClaudeButton.IsEnabled = true;
        }
    }

    private async void OnCheckCodex(object sender, RoutedEventArgs e)
    {
        CheckCodexButton.IsEnabled = false;
        try
        {
            var readOnline = ReadCodexOnline.IsChecked == true;
            var signedIn = AccountSession.IsSignedIn(AccountProvider.ChatGpt);
            var items = await Task.Run(() => ProviderCheck.Codex(Subscriptions.DefaultCodexHome, OnlineUsage.DefaultCodexAuth, readOnline, DateTimeOffset.UtcNow, signedIn));
            CodexCheck.Show(items);
        }
        finally
        {
            CheckCodexButton.IsEnabled = true;
        }
    }

    private const string StatusLineFileName = "WinModes.StatusLine.exe";
    private static string RecordClaudeText => Loc.T("Claude Code gives the usage of your plan only to its status line. This adds a WinModes status line to Claude Code's settings (a backup is kept); it records the 5-hour and weekly usage for the widget and shows them in Claude Code. Updated while a Claude Code session is open.");

    private static string OtherStatusLineText => Loc.T("Claude Code already has a status line of its own, so WinModes leaves it alone. To record the usage, remove it from Claude Code's settings first.");

    private void ShowClaudeUsage()
    {
        var state = ClaudeStatusLineSetup.Read(ClaudeStatusLineSetup.DefaultSettingsPath);
        ClaudeUsage.IsChecked = state == ClaudeStatusLineSetup.State.Ours;
        ClaudeUsage.IsEnabled = state != ClaudeStatusLineSetup.State.Other;
        ClaudeUsageRow.Description = state == ClaudeStatusLineSetup.State.Other ? OtherStatusLineText : RecordClaudeText;
    }

    private void OnClaudeUsageClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var settingsPath = ClaudeStatusLineSetup.DefaultSettingsPath;
            if (ClaudeUsage.IsChecked == true)
            {
                var program = System.IO.Path.Combine(AppContext.BaseDirectory, StatusLineFileName);
                if (System.IO.File.Exists(program))
                {
                    ClaudeStatusLineSetup.Install(settingsPath, ClaudeStatusLineSetup.CommandFor(program, ShortPath));
                }
            }
            else
            {
                ClaudeStatusLineSetup.Remove(settingsPath);
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ClaudeUsageRow.Description = Loc.T("Claude Code's settings could not be changed.");
        }

        // Show what the settings really say, whatever was clicked.
        ShowClaudeUsage();
    }

    /// <summary>The 8.3 form of a folder, or null when the volume keeps none.</summary>
    private static string? ShortPath(string path)
    {
        const int MaxPath = 1024;
        var buffer = new char[MaxPath];
        var length = GetShortPathName(path, buffer, MaxPath);
        return length is > 0 and < MaxPath ? new string(buffer, 0, length) : null;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern int GetShortPathName(string longPath, [System.Runtime.InteropServices.Out] char[] shortPath, int size);

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
                ReadClaudeOnline = ReadClaudeOnline.IsChecked == true,
                ReadCodexOnline = ReadCodexOnline.IsChecked == true,
                ShowClaudePlan = ShowClaudePlan.IsChecked == true,
                ShowCodexPlan = ShowCodexPlan.IsChecked == true,
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
