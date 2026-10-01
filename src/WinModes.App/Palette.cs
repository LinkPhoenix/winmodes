using System.Windows;
using System.Windows.Media;

namespace WinModes.App;

/// <summary>
/// Semantic colours of the app. Mid-tone hues chosen to stay readable on both the light and the dark card fill;
/// tints (low alpha) are used for backgrounds so text keeps the theme's own contrast.
/// </summary>
internal static class Palette
{
    private const byte TintAlpha = 0x33;

    public static readonly Color Brand = Color.FromRgb(0x7C, 0x5C, 0xFC);

    public static readonly Brush Stop = Solid(0xF8, 0x71, 0x71);
    public static readonly Brush Start = Solid(0x34, 0xD3, 0x99);
    public static readonly Brush Power = Solid(0xFB, 0xBF, 0x24);
    public static readonly Brush Container = Solid(0x60, 0xA5, 0xFA);
    public static readonly Brush Apps = Solid(0xC0, 0x84, 0xFC);
    public static readonly Brush Neutral = Solid(0x94, 0xA3, 0xB8);

    public static Brush ModeGradient(string mode) => mode.ToUpperInvariant() switch
    {
        "CODE" => Gradient(Color.FromRgb(0x7C, 0x5C, 0xFC), Color.FromRgb(0x4F, 0x8B, 0xFF)),
        "WORK" => Gradient(Color.FromRgb(0x14, 0xB8, 0xA6), Color.FromRgb(0x22, 0xC5, 0x5E)),
        "GAME" => Gradient(Color.FromRgb(0xF9, 0x73, 0x16), Color.FromRgb(0xEC, 0x48, 0x99)),
        _ => Gradient(Color.FromRgb(0x64, 0x74, 0x8B), Color.FromRgb(0x94, 0xA3, 0xB8)),
    };

    public static Brush Tint(Brush brush)
    {
        var color = ((SolidColorBrush)brush).Color;
        return Freeze(new SolidColorBrush(Color.FromArgb(TintAlpha, color.R, color.G, color.B)));
    }

    private static Brush Solid(byte r, byte g, byte b) => Freeze(new SolidColorBrush(Color.FromRgb(r, g, b)));

    private static Brush Gradient(Color from, Color to) =>
        Freeze(new LinearGradientBrush(from, to, new Point(0, 0), new Point(1, 1)));

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
