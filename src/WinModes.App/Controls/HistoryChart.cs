using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace WinModes.App.Controls;

/// <summary>
/// Real-time line chart on a fixed 0-100 scale: square grid, gradient fill, and a glow made of wider
/// translucent strokes (no bitmap effect). Between two samples the curve scrolls on every frame,
/// so it moves continuously instead of jumping once per sample (at most 30 images a second).
/// </summary>
public sealed class HistoryChart : FrameworkElement
{
    private const int VisibleSamples = 60;
    // One extra sample waits just outside the right edge and slides in.
    private const int Capacity = VisibleSamples + 2;
    private const double GridCell = 18;
    private const double CornerRadius = 4;

    // The scroll is slow (one grid cell takes seconds), so 30 images a second look the same as 60 for half the work.
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(33);

    private readonly double[] _samples = new double[Capacity];
    private readonly Stopwatch _sinceLastSample = new();
    private readonly Stopwatch _sinceLastFrame = Stopwatch.StartNew();
    private int _count;
    private int _next;
    private bool _isRendering;

    private Pen _linePen = CreatePen(Colors.MediumPurple, 1.6, 0xFF);
    private Pen _glowPen = CreatePen(Colors.MediumPurple, 5, 0x40);
    private Pen _haloPen = CreatePen(Colors.MediumPurple, 10, 0x1C);
    private Pen _borderPen = CreatePen(Colors.MediumPurple, 1, 0x99);
    private Pen _gridPen = CreatePen(Colors.MediumPurple, 1, 0x1E);
    private Brush _fill = Brushes.Transparent;

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Color), typeof(HistoryChart),
        new FrameworkPropertyMetadata(Colors.MediumPurple, FrameworkPropertyMetadataOptions.AffectsRender, OnAccentChanged));

    public static readonly DependencyProperty SampleIntervalProperty = DependencyProperty.Register(
        nameof(SampleInterval), typeof(TimeSpan), typeof(HistoryChart), new PropertyMetadata(TimeSpan.FromSeconds(1)));

    public Color Accent
    {
        get => (Color)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    /// <summary>Expected time between two <see cref="Push"/> calls; drives the scroll speed.</summary>
    public TimeSpan SampleInterval
    {
        get => (TimeSpan)GetValue(SampleIntervalProperty);
        set => SetValue(SampleIntervalProperty, value);
    }

    public HistoryChart()
    {
        SnapsToDevicePixels = true;
        RebuildBrushes(Accent);
        // Redraw per frame only while the chart is on screen.
        IsVisibleChanged += (_, _) => UpdateRendering();
        Unloaded += (_, _) => StopRendering();
    }

    /// <summary>Adds a value (0-100). The oldest value is dropped once the window is full.</summary>
    public void Push(double value)
    {
        _samples[_next] = Math.Clamp(value, 0, 100);
        _next = (_next + 1) % Capacity;
        _count = Math.Min(_count + 1, Capacity);
        _sinceLastSample.Restart();
        UpdateRendering();
    }

    private void UpdateRendering()
    {
        if (IsVisible && !_isRendering)
        {
            CompositionTarget.Rendering += OnFrame;
            _isRendering = true;
        }
        else if (!IsVisible)
        {
            StopRendering();
        }
    }

    private void StopRendering()
    {
        if (_isRendering)
        {
            CompositionTarget.Rendering -= OnFrame;
            _isRendering = false;
        }
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (_sinceLastFrame.Elapsed < FrameInterval)
        {
            return;
        }

        _sinceLastFrame.Restart();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var step = width / (VisibleSamples - 1);
        // 0 right after a sample arrived, 1 when the next one is due.
        var phase = _count == 0 ? 0 : Math.Clamp(_sinceLastSample.Elapsed / SampleInterval, 0, 1);

        var bounds = new Rect(0.5, 0.5, width - 1, height - 1);
        drawingContext.PushClip(new RectangleGeometry(bounds, CornerRadius, CornerRadius));
        DrawGrid(drawingContext, width, height, phase * step);

        if (_count > 1)
        {
            DrawSeries(drawingContext, width, height, step, phase);
        }

        drawingContext.Pop();
        drawingContext.DrawRoundedRectangle(null, _borderPen, bounds, CornerRadius, CornerRadius);
    }

    private void DrawGrid(DrawingContext drawingContext, double width, double height, double scroll)
    {
        // Vertical lines travel with the data, like a paper chart recorder.
        for (var x = width - scroll % GridCell; x > 0; x -= GridCell)
        {
            drawingContext.DrawLine(_gridPen, new Point(x, 0), new Point(x, height));
        }

        for (var y = height; y > 0; y -= GridCell)
        {
            drawingContext.DrawLine(_gridPen, new Point(0, Math.Round(y) + 0.5), new Point(width, Math.Round(y) + 0.5));
        }
    }

    private void DrawSeries(DrawingContext drawingContext, double width, double height, double step, double phase)
    {
        // The newest sample starts one step beyond the right edge and reaches it when the next one is due.
        var newestX = width + step * (1 - phase);
        var firstX = newestX - (_count - 1) * step;

        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var lineContext = line.Open())
        using (var areaContext = area.Open())
        {
            areaContext.BeginFigure(new Point(firstX, height), isFilled: true, isClosed: true);
            for (var i = 0; i < _count; i++)
            {
                var value = _samples[(_next - _count + i + Capacity) % Capacity];
                var point = new Point(firstX + i * step, height - value / 100 * height);
                if (i == 0)
                {
                    lineContext.BeginFigure(point, isFilled: false, isClosed: false);
                }
                else
                {
                    lineContext.LineTo(point, isStroked: true, isSmoothJoin: true);
                }

                areaContext.LineTo(point, isStroked: false, isSmoothJoin: false);
            }

            areaContext.LineTo(new Point(newestX, height), isStroked: false, isSmoothJoin: false);
        }

        line.Freeze();
        area.Freeze();
        drawingContext.DrawGeometry(_fill, null, area);
        drawingContext.DrawGeometry(null, _haloPen, line);
        drawingContext.DrawGeometry(null, _glowPen, line);
        drawingContext.DrawGeometry(null, _linePen, line);
    }

    private static void OnAccentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((HistoryChart)sender).RebuildBrushes((Color)e.NewValue);

    private void RebuildBrushes(Color accent)
    {
        _linePen = CreatePen(accent, 1.6, 0xFF);
        _glowPen = CreatePen(accent, 5, 0x40);
        _haloPen = CreatePen(accent, 10, 0x1C);
        _borderPen = CreatePen(accent, 1, 0x99);
        _gridPen = CreatePen(accent, 1, 0x1E);

        var fill = new LinearGradientBrush(
            Color.FromArgb(0x55, accent.R, accent.G, accent.B), Color.FromArgb(0x05, accent.R, accent.G, accent.B), 90);
        fill.Freeze();
        _fill = fill;
    }

    private static Pen CreatePen(Color color, double thickness, byte alpha)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)), thickness)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        pen.Freeze();
        return pen;
    }
}
