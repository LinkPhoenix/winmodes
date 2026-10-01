using System.Windows;
using System.Windows.Media;

namespace WinModes.App.Controls;

/// <summary>One small vertical bar per logical processor, filled according to its load (0 to 100).</summary>
public sealed class CoreBars : FrameworkElement
{
    private const double Gap = 4;
    private const double Radius = 2;
    private const byte TrackAlpha = 0x26;

    private IReadOnlyList<double> _values = [];

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Color), typeof(CoreBars), new FrameworkPropertyMetadata(Colors.MediumPurple, FrameworkPropertyMetadataOptions.AffectsRender));

    public Color Accent
    {
        get => (Color)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public void Show(IReadOnlyList<double> values)
    {
        _values = values;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        if (_values.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var accent = Accent;
        var fill = new SolidColorBrush(accent);
        var track = new SolidColorBrush(Color.FromArgb(TrackAlpha, accent.R, accent.G, accent.B));
        fill.Freeze();
        track.Freeze();

        var width = Math.Max((ActualWidth - Gap * (_values.Count - 1)) / _values.Count, 1);
        for (var i = 0; i < _values.Count; i++)
        {
            var x = i * (width + Gap);
            drawingContext.DrawRoundedRectangle(track, null, new Rect(x, 0, width, ActualHeight), Radius, Radius);

            var height = Math.Clamp(_values[i], 0, 100) / 100 * ActualHeight;
            if (height > 0)
            {
                drawingContext.DrawRoundedRectangle(fill, null, new Rect(x, ActualHeight - height, width, height), Radius, Radius);
            }
        }
    }
}
