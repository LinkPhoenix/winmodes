using System.Windows;
using System.Windows.Controls;
using WinModes.App.Services;
using WinModes.Core.Usage;

namespace WinModes.App.Pages;

/// <summary>Every option of the desktop widget. Each change is saved and applied to the widget at once.</summary>
public partial class WidgetPage : Page
{
    private static readonly Option[] RefreshChoices = [new(1, "1 second"), new(3, "3 seconds"), new(5, "5 seconds")];
    private static readonly Option[] ScaleChoices = [new(80, "Small"), new(100, "Medium"), new(125, "Large"), new(150, "Extra large")];
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
        ReadUsageOnline.IsChecked = widget.ReadUsageOnline;
        ShowClaudePlan.IsChecked = widget.ShowClaudePlan;
        ShowCodexPlan.IsChecked = widget.ShowCodexPlan;
        ShowResetCredits.IsChecked = widget.ShowResetCredits;
        PlanAlert.IsChecked = widget.PlanAlert;
        ShowClaudeUsage();
        LockPosition.IsChecked = widget.LockPosition;
        HideOnFullScreen.IsChecked = widget.HideOnFullScreen;
        Compact.IsChecked = widget.Compact;
        ShowGraph.IsChecked = widget.ShowGraph;
        ClickThrough.IsChecked = widget.ClickThrough;
        Select(Refresh, RefreshChoices, widget.RefreshSeconds);
        Select(Scale, ScaleChoices, widget.ScalePercent);
        Select(MaxTools, MaxToolChoices, widget.MaxTools);

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
            if (Application.Current is App app)
            {
                app.Stats.Updated -= OnStats;
            }
        };
    }

    private void OnStats(object? sender, StatsReading reading) => Preview.Show(reading);

    private const string StatusLineFileName = "WinModes.StatusLine.exe";
    private const string OtherStatusLineText = "Claude Code already has a status line of its own, so WinModes leaves it alone. To record the usage, remove it from Claude Code's settings first.";

    private void ShowClaudeUsage()
    {
        var state = ClaudeStatusLineSetup.Read(ClaudeStatusLineSetup.DefaultSettingsPath);
        ClaudeUsage.IsChecked = state == ClaudeStatusLineSetup.State.Ours;
        ClaudeUsage.IsEnabled = state != ClaudeStatusLineSetup.State.Other;
        if (state == ClaudeStatusLineSetup.State.Other)
        {
            ClaudeUsageDetail.Text = OtherStatusLineText;
        }
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
            ClaudeUsageDetail.Text = "Claude Code's settings could not be changed.";
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
        var size = ScaleChoices.FirstOrDefault(choice => choice.Value == widget.ScalePercent)?.Label ?? "Medium";
        PreviewNote.Text = (settings.ShowDesktopWidget ? "The widget is on your desktop. " : "The widget is hidden. ")
            + $"Size on the desktop: {size}.";
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
                ReadUsageOnline = ReadUsageOnline.IsChecked == true,
                ShowClaudePlan = ShowClaudePlan.IsChecked == true,
                ShowCodexPlan = ShowCodexPlan.IsChecked == true,
                ShowResetCredits = ShowResetCredits.IsChecked == true,
                PlanAlert = PlanAlert.IsChecked == true,
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
