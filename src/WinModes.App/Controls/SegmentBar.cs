using System.Windows;
using System.Windows.Media;

namespace WinModes.App.Controls;

/// <summary>Horizontal meter made of small rounded blocks; lit blocks show <see cref="Value"/> (0 to 100).</summary>
public sealed class SegmentBar : FrameworkElement
{
    private const double SegmentWidth = 8;
    private const double SegmentGap = 3;
    private const double SegmentRadius = 2;
    private const byte UnlitAlpha = 0x26;
    private const byte GlowAlpha = 0x40;
    private const double GlowSpread = 2;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(SegmentBar), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Color), typeof(SegmentBar), new FrameworkPropertyMetadata(Colors.MediumPurple, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Color Accent
    {
        get => (Color)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var count = (int)((ActualWidth + SegmentGap) / (SegmentWidth + SegmentGap));
        if (count <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var accent = Accent;
        var lit = new SolidColorBrush(accent);
        var glow = new SolidColorBrush(Color.FromArgb(GlowAlpha, accent.R, accent.G, accent.B));
        var unlit = new SolidColorBrush(Color.FromArgb(UnlitAlpha, accent.R, accent.G, accent.B));
        lit.Freeze();
        glow.Freeze();
        unlit.Freeze();

        var litCount = (int)Math.Round(Math.Clamp(Value, 0, 100) / 100 * count);
        for (var i = 0; i < count; i++)
        {
            var rect = new Rect(i * (SegmentWidth + SegmentGap), GlowSpread, SegmentWidth, Math.Max(ActualHeight - 2 * GlowSpread, 1));
            if (i < litCount)
            {
                var halo = Rect.Inflate(rect, GlowSpread, GlowSpread);
                drawingContext.DrawRoundedRectangle(glow, null, halo, SegmentRadius + GlowSpread, SegmentRadius + GlowSpread);
                drawingContext.DrawRoundedRectangle(lit, null, rect, SegmentRadius, SegmentRadius);
            }
            else
            {
                drawingContext.DrawRoundedRectangle(unlit, null, rect, SegmentRadius, SegmentRadius);
            }
        }
    }
}
