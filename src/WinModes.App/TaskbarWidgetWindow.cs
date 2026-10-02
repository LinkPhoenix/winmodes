using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using WinModes.App.Controls;
using WinModes.App.Services;
using WinModes.Core;
using WinModes.Core.Usage;
using Glyph = Wpf.Ui.Controls.SymbolRegular;
using GlyphIcon = Wpf.Ui.Controls.SymbolIcon;

namespace WinModes.App;

/// <summary>
/// The widget docked on the Windows taskbar, in the free room left by the app icons: mode, CPU and memory, network, AI
/// tools and the two limits of each plan, each block switched on or off by the options of the Widget page. It shows as much
/// as fits: the less room there is, the more it drops (network, then mode and CPU, then AI), and it hides when nothing fits.
/// </summary>
internal sealed class TaskbarWidgetWindow : Window
{
    private const double BarWidth = 36;
    private const double BarHeight = 4;
    private const double Gap = 8;
    private const double CellMargin = 6;
    private const double LabelSize = 10;
    private const double ValueSize = 11.5;
    private const double IconSize = 13;
    private const double ValueColumnWidth = 30;
    private const long StyleToolWindow = 0x00000080;
    private const long StyleNoActivate = 0x08000000;
    private const int ExtendedStyleIndex = -20;
    private const int KeptAlways = int.MaxValue;
    private const int WmSettingChange = 0x001A;
    private const int WmDisplayChange = 0x007E;
    private const int WmDpiChanged = 0x02E0;

    // The taskbar is read at once when something changes and again while the icons settle (they slide for a few hundred ms).
    private static readonly TimeSpan WatchInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan SafetyInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan EventDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan[] SettleDelays = [TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1200)];

    private readonly Brush _text;
    private readonly Brush _dim;
    private readonly Border _content;
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Ellipse _modeDot = new() { Width = 8, Height = 8, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _mode = new();
    private readonly TextBlock _cpu = new();
    private readonly TextBlock _memory = new();
    private readonly TextBlock _down = new();
    private readonly TextBlock _up = new();
    private readonly TextBlock _ai = new();
    private readonly PlanCell _claude;
    private readonly PlanCell _codex;
    private readonly StackPanel _modeCell;
    private readonly StackPanel _cpuCell;
    private readonly StackPanel _netCell;
    private readonly StackPanel _aiCell;
    private readonly UIElement _cpuLine;
    private readonly UIElement _memoryLine;
    private readonly UIElement _aiLine;
    private readonly DispatcherTimer _watch = new() { Interval = WatchInterval };
    private readonly DispatcherTimer _safety = new() { Interval = SafetyInterval };
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private WidgetSettings _settings = new();
    private UIElement[][] _levels = [];
    private TaskbarArea? _area;
    private TaskbarSignature? _signature;
    private IntPtr _watched;
    private int _scanning;
    private int _again;
    private int _eventPending;
    private int _settling;

    public TaskbarWidgetWindow()
    {
        var light = Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) is 1;
        _text = light ? Brushes.Black : Brushes.White;
        _dim = Palette.Neutral;
        _claude = new PlanCell("Claude", _text, _dim, () => PlanHover.Current("Claude", _settings));
        _codex = new PlanCell("Codex", _text, _dim, () => PlanHover.Current("Codex", _settings));

        Title = "WinModes taskbar widget";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        _modeCell = Cell(new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _modeDot, Styled(_mode, bold: true) } });
        // Each icon has the colour of its row on the Widget page; the values of CPU and memory turn amber then red as the load rises.
        _cpuLine = Line(Glyph.DeveloperBoard16, _cpu, 30, Palette.BrandBrush);
        _memoryLine = Line(Glyph.Memory16, _memory, 30, Palette.Stop);
        _cpuCell = Cell(_cpuLine, _memoryLine);
        _netCell = Cell(Line(Glyph.ArrowDown16, _down, 34, Palette.Start), Line(Glyph.ArrowUp16, _up, 34, Palette.Container));
        // The AI total is one line: its icon and the figure, both in the AI colour.
        _aiLine = Line(Glyph.Sparkle16, _ai, 52, Palette.Apps, Palette.Apps);
        _aiCell = Cell(_aiLine);

        // An almost invisible fill so the whole readout takes the mouse, not only the glyphs.
        _content = new Border { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), Padding = new Thickness(2, 0, 2, 0), Child = _row };
        Content = _content;
        ContextMenu = new ContextMenu();
        AddMenuItem(Loc.T("Open WinModes"), () => OpenAppRequested?.Invoke(this, EventArgs.Empty));
        AddMenuItem(Loc.T("Widget settings"), () => OpenSettingsRequested?.Invoke(this, EventArgs.Empty));
        AddMenuItem(Loc.T("Hide widget"), () => HideRequested?.Invoke(this, EventArgs.Empty));
        MouseLeftButtonUp += (_, _) => OpenAppRequested?.Invoke(this, EventArgs.Empty);

        SourceInitialized += (_, _) =>
        {
            SetStyles();
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(OnWindowMessage);
        };
        Loaded += async (_, _) => await SettleAsync();
        _watch.Tick += async (_, _) => await OnWatchAsync();
        _safety.Tick += async (_, _) => await ScanAsync();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _watch.Start();
        _safety.Start();
        Closed += (_, _) =>
        {
            _watch.Stop();
            _safety.Stop();
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            TaskbarDock.StopWatching();
        };
    }

    public event EventHandler? OpenAppRequested;

    public event EventHandler? OpenSettingsRequested;

    public event EventHandler? HideRequested;

    /// <summary>Applies the options of the Widget page: which blocks exist, how transparent it is, and which side it prefers.</summary>
    internal void Apply(WidgetSettings settings)
    {
        _settings = settings;
        Opacity = Math.Clamp(settings.OpacityPercent, 40, 100) / 100d;
        _levels = BuildLevels(settings);
        _cpuLine.Visibility = Visible(settings.ShowCpu);
        _memoryLine.Visibility = Visible(settings.ShowMemory);
        _aiLine.Visibility = Visible(settings.ShowAiTools);
        Redock();
    }

    internal void Show(StatsReading reading)
    {
        var culture = CultureInfo.CurrentCulture;
        var mode = ModeSwitcher.ActiveMode;
        _mode.Text = mode is null ? Loc.T("No mode") : culture.TextInfo.ToTitleCase(mode);
        _modeDot.Fill = mode is null ? _dim : Palette.ModeGradient(mode);
        _cpu.Text = string.Create(culture, $"{reading.CpuPercent:0} %");
        _cpu.Foreground = Palette.RemainingBrush(100 - reading.CpuPercent);
        _memory.Text = string.Create(culture, $"{reading.Memory.UsedPercent:0} %");
        _memory.Foreground = Palette.RemainingBrush(100 - reading.Memory.UsedPercent);
        _down.Text = string.Create(culture, $"{reading.DownMbps:0.0}");
        _up.Text = string.Create(culture, $"{reading.UpMbps:0.0}");
        _ai.Text = string.Create(culture, $"{reading.AiMemoryMb / 1024:0.0} GB");

        ToolIcons.Remember(reading.AiTools);
        var now = DateTimeOffset.Now;
        var statuses = SubscriptionMonitor.Get(_settings.ClaudeOnline, _settings.CodexOnline, _settings.ShowClaudePlan, _settings.ShowCodexPlan);
        _claude.Show(statuses.FirstOrDefault(status => status.Tool == "Claude"), now, culture, _settings.ShowResetCredits);
        _codex.Show(statuses.FirstOrDefault(status => status.Tool == "Codex"), now, culture, _settings.ShowResetCredits);

        Redock();
    }

    /// <summary>
    /// The blocks to show at each level of detail, from all of them to the plans alone. A block drops out in the order
    /// network, then mode and CPU, then AI; plans stay. Levels that would show the same blocks are kept once.
    /// </summary>
    private UIElement[][] BuildLevels(WidgetSettings settings)
    {
        var cells = new List<(UIElement Cell, int DropsAt)>();
        if (settings.ShowMode)
        {
            cells.Add((_modeCell, 2));
        }

        if (settings.ShowCpu || settings.ShowMemory)
        {
            cells.Add((_cpuCell, 2));
        }

        if (settings.ShowNetwork)
        {
            cells.Add((_netCell, 1));
        }

        if (settings.ShowAiTools)
        {
            cells.Add((_aiCell, 3));
        }

        if (settings.ShowSubscriptions && settings.ShowClaudePlan)
        {
            cells.Add((_claude.Root, KeptAlways));
        }

        if (settings.ShowSubscriptions && settings.ShowCodexPlan)
        {
            cells.Add((_codex.Root, KeptAlways));
        }

        _row.Children.Clear();
        foreach (var (cell, _) in cells)
        {
            _row.Children.Add(cell);
        }

        var levels = new List<UIElement[]>();
        for (var level = 0; level <= 3; level++)
        {
            var shown = cells.Where(entry => entry.DropsAt > level).Select(entry => entry.Cell).ToArray();
            if (shown.Length > 0 && (levels.Count == 0 || !levels[^1].SequenceEqual(shown)))
            {
                levels.Add(shown);
            }
        }

        return [.. levels];
    }

    private void AddMenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        ContextMenu!.Items.Add(item);
    }

    private void SetStyles()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(handle, ExtendedStyleIndex).ToInt64() | StyleToolWindow | StyleNoActivate;
        SetWindowLongPtr(handle, ExtendedStyleIndex, new IntPtr(style));
    }

    /// <summary>Explorer restarted, the display or its scale changed, or a setting changed: the taskbar is about to be laid out again.</summary>
    private IntPtr OnWindowMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == _taskbarCreated || message is WmDisplayChange or WmDpiChanged or WmSettingChange)
        {
            _ = SettleAsync();
        }

        return IntPtr.Zero;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume && !Dispatcher.HasShutdownStarted)
        {
            _ = Dispatcher.InvokeAsync(SettleAsync);
        }
    }

    /// <summary>
    /// Runs every few hundred milliseconds and costs microseconds: when the alignment setting or the taskbar rectangle
    /// changed, the icons are about to move.
    /// </summary>
    private async Task OnWatchAsync()
    {
        var signature = TaskbarDock.ReadSignature();
        if (signature == _signature)
        {
            return;
        }

        _signature = signature;
        await SettleAsync();
    }

    /// <summary>Reads the taskbar at once and again while the icons settle; one run at a time.</summary>
    private async Task SettleAsync()
    {
        if (Interlocked.Exchange(ref _settling, 1) == 1)
        {
            return;
        }

        try
        {
            await ScanAsync();
            foreach (var delay in SettleDelays)
            {
                await Task.Delay(delay);
                await ScanAsync();
            }
        }
        finally
        {
            Interlocked.Exchange(ref _settling, 0);
        }
    }

    /// <summary>An icon was added or removed: read again shortly, once for a burst of events. Called from a UI Automation thread.</summary>
    private void OnTaskbarChanged()
    {
        if (Interlocked.Exchange(ref _eventPending, 1) == 1 || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        _ = Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(EventDelay);
            Interlocked.Exchange(ref _eventPending, 0);
            await ScanAsync();
        });
    }

    /// <summary>Reads the taskbar again off the UI thread (UI Automation takes some tens of milliseconds), then places the window.</summary>
    private async Task ScanAsync()
    {
        // A request that arrives during a read is not lost: the read is repeated once it ends.
        if (Interlocked.Exchange(ref _scanning, 1) == 1)
        {
            Volatile.Write(ref _again, 1);
            return;
        }

        try
        {
            do
            {
                Volatile.Write(ref _again, 0);
                _area = await Task.Run(ReadTaskbar);
                Redock();
            }
            while (Volatile.Read(ref _again) == 1);
        }
        finally
        {
            Interlocked.Exchange(ref _scanning, 0);
        }
    }

    private TaskbarArea? ReadTaskbar()
    {
        var area = TaskbarDock.Find(_area);
        // A new taskbar (Explorer restarted) has to be watched again; never with a zero handle, which would watch everything.
        if (area is { } found && found.Taskbar != _watched)
        {
            TaskbarDock.StopWatching();
            TaskbarDock.Watch(found.Taskbar, OnTaskbarChanged);
            _watched = found.Taskbar;
        }

        return area;
    }

    /// <summary>
    /// Shows the fullest level that fits in the free room and puts the window there; hides it when nothing fits, when the
    /// taskbar is not known, or while a full-screen app is in front. Also re-owns the window after an Explorer restart.
    /// </summary>
    private void Redock()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !IsLoaded)
        {
            return;
        }

        TaskbarSlot? slot = null;
        var hidden = _settings.HideOnFullScreen && WidgetWindow.IsFullScreenAppActive();
        if (_area is { } area && _levels.Length > 0 && !hidden)
        {
            var scale = VisualTreeHelper.GetDpi(this);
            var widths = new int[_levels.Length];
            for (var level = 0; level < widths.Length; level++)
            {
                ShowLevel(level);
                // A changed visibility alone leaves the cached measure of the parents in place.
                _row.InvalidateMeasure();
                _content.InvalidateMeasure();
                _content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                widths[level] = (int)Math.Ceiling(_content.DesiredSize.Width * scale.DpiScaleX);
            }

            slot = TaskbarPlacement.Choose(_settings.TaskbarSide, area.Left, area.ContentLeft, area.ContentRight, area.TrayLeft, widths, (int)Math.Ceiling(Gap * scale.DpiScaleX));
            if (slot is { } chosen)
            {
                ShowLevel(chosen.Level);
                UpdateLayout();
                TaskbarDock.Place(handle, area, chosen.X, (int)Math.Ceiling(ActualHeight * scale.DpiScaleY));
            }
        }

        Visibility = slot is null ? Visibility.Hidden : Visibility.Visible;
    }

    private void ShowLevel(int level)
    {
        var shown = _levels[level];
        foreach (UIElement cell in _row.Children)
        {
            cell.Visibility = Visible(shown.Contains(cell));
        }
    }

    private static Visibility Visible(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private static StackPanel Cell(params UIElement[] lines)
    {
        var cell = new StackPanel { Margin = new Thickness(CellMargin, 0, CellMargin, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach (var line in lines)
        {
            cell.Children.Add(line);
        }

        return cell;
    }

    private StackPanel Line(Glyph glyph, TextBlock value, double valueWidth, Brush accent, Brush? valueBrush = null)
    {
        var icon = new GlyphIcon { Symbol = glyph, FontSize = IconSize, Foreground = accent, Margin = new Thickness(0, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center };
        var styled = Styled(value, width: valueWidth);
        if (valueBrush is not null)
        {
            styled.Foreground = valueBrush;
        }

        return new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, styled } };
    }

    private TextBlock Styled(TextBlock block, bool bold = false, double width = 0)
    {
        block.FontSize = ValueSize;
        block.FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal;
        block.Foreground = _text;
        block.TextTrimming = TextTrimming.CharacterEllipsis;
        block.VerticalAlignment = VerticalAlignment.Center;
        if (width > 0)
        {
            block.Width = width;
        }

        return block;
    }

    /// <summary>The two limits of one plan: a bar and a percentage for each, in the colour of what is left.</summary>
    private sealed class PlanCell
    {
        private readonly string _tool;
        private readonly Brush _dim;
        private readonly Image _icon = new() { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
        private readonly TextBlock _name;
        private readonly TextBlock _credits;
        private readonly Row[] _rows = [new Row(), new Row()];

        public PlanCell(string tool, Brush text, Brush dim, Func<SubscriptionStatus?> current)
        {
            (_tool, _dim) = (tool, dim);
            // An almost invisible fill so the gaps between the bars take the mouse too: the hover card must not flicker across them.
            var grid = new Grid { Margin = new Thickness(CellMargin, 0, CellMargin, 0), VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
            PlanHover.Attach(grid, tool, current);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition());

            // The tool's own icon once known, its name until then.
            _name = new TextBlock { Text = tool, FontSize = ValueSize, FontWeight = FontWeights.SemiBold, Foreground = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
            var identity = new Grid { Children = { _icon, _name } };
            Grid.SetRowSpan(identity, 2);
            grid.Children.Add(identity);

            // Limit resets in reserve (the "banks"), shown only when the account has some.
            _credits = new TextBlock { FontSize = ValueSize, FontWeight = FontWeights.SemiBold, Foreground = Palette.BrandBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Visibility = Visibility.Collapsed };
            Grid.SetColumn(_credits, 4);
            Grid.SetRowSpan(_credits, 2);
            grid.Children.Add(_credits);
            for (var index = 0; index < _rows.Length; index++)
            {
                var row = _rows[index];
                row.Window = new TextBlock { FontSize = LabelSize, Foreground = dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
                row.Value = new TextBlock { FontSize = ValueSize, Foreground = text, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                row.Fill = new Border { Height = BarHeight, CornerRadius = new CornerRadius(BarHeight / 2), HorizontalAlignment = HorizontalAlignment.Left };
                row.Track = new Border
                {
                    Width = BarWidth, Height = BarHeight, CornerRadius = new CornerRadius(BarHeight / 2), Background = new SolidColorBrush(Color.FromArgb(0x33, 0x80, 0x80, 0x80)),
                    Child = row.Fill, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0),
                };
                foreach (var (element, column) in new (UIElement, int)[] { (row.Window, 1), (row.Track, 2), (row.Value, 3) })
                {
                    Grid.SetRow(element, index);
                    Grid.SetColumn(element, column);
                    grid.Children.Add(element);
                }
            }

            Root = grid;
        }

        public UIElement Root { get; }

        public void Show(SubscriptionStatus? status, DateTimeOffset now, CultureInfo culture, bool showCredits)
        {
            var icon = ToolIcons.For(_tool);
            _icon.Source = icon;
            _icon.Visibility = icon is null ? Visibility.Collapsed : Visibility.Visible;
            _name.Visibility = icon is null ? Visibility.Visible : Visibility.Collapsed;

            var limits = new[] { status?.Primary, status?.Secondary };
            for (var index = 0; index < _rows.Length; index++)
            {
                var row = _rows[index];
                // A limit that started over since it was recorded no longer holds. With no figure at all one dash is enough.
                if (limits[index] is not { } limit || limit.HasReset(now))
                {
                    row.Window.Text = "";
                    // Only the first line carries the dash; a second limit that is not known leaves its line empty.
                    row.Value.Text = index > 0 ? "" : "–";
                    row.Value.MinWidth = 0;
                    row.Value.Foreground = _dim;
                    row.Fill.Width = 0;
                    row.Track.Visibility = Visibility.Collapsed;
                    continue;
                }

                row.Track.Visibility = Visibility.Visible;
                row.Value.MinWidth = ValueColumnWidth;
                var left = limit.RemainingPercent;
                row.Window.Text = limit.WindowName;
                row.Value.Text = string.Create(culture, $"{left:0} %");
                row.Value.Foreground = Palette.RemainingBrush(left);
                row.Fill.Background = Palette.RemainingBrush(left);
                row.Fill.Width = BarWidth * left / 100;
            }

            _credits.Text = showCredits && status?.ResetCredits is { } credits && credits > 0 ? string.Create(culture, $"↻ {credits}") : "";
            _credits.Visibility = _credits.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private sealed class Row
        {
            public TextBlock Window { get; set; } = new();

            public TextBlock Value { get; set; } = new();

            public Border Fill { get; set; } = new();

            public Border Track { get; set; } = new();
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint RegisterWindowMessage(string name);
}
