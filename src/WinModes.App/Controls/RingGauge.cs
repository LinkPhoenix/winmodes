using System.Windows;
using System.Windows.Media;

namespace WinModes.App.Controls;

/// <summary>Circular gauge: a track ring with an arc proportional to <see cref="Value"/> (0 to 100).</summary>
public sealed class RingGauge : FrameworkElement
{
    private const double FullCircleDegrees = 360;
    private const double StartAngleDegrees = -90;
    // An arc cannot describe a full circle; stop just short of it.
    private const double MaxSweepDegrees = 359.9;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(RingGauge), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(RingGauge), new FrameworkPropertyMetadata(Brushes.MediumPurple, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(RingGauge), new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(RingGauge), new FrameworkPropertyMetadata(8d, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public Brush Track
    {
        get => (Brush)GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);

        var radius = (Math.Min(ActualWidth, ActualHeight) - Thickness) / 2;
        if (radius <= 0)
        {
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        drawingContext.DrawEllipse(null, new Pen(Track, Thickness), center, radius, radius);

        var sweep = Math.Clamp(Value, 0, 100) / 100 * FullCircleDegrees;
        if (sweep <= 0)
        {
            return;
        }

        sweep = Math.Min(sweep, MaxSweepDegrees);
        var start = PointOnCircle(center, radius, StartAngleDegrees);
        var end = PointOnCircle(center, radius, StartAngleDegrees + sweep);

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, isFilled: false, isClosed: false);
            context.ArcTo(end, new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        var pen = new Pen(Stroke, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        drawingContext.DrawGeometry(null, pen, geometry);
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }
}
