using System.Windows;
using System.Windows.Controls;
using WinModes.App.Services;

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
        LockPosition.IsChecked = widget.LockPosition;
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
                MaxTools = (MaxTools.SelectedItem as Option)?.Value ?? 4,
                RefreshSeconds = (Refresh.SelectedItem as Option)?.Value ?? 3,
                LockPosition = LockPosition.IsChecked == true,
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
