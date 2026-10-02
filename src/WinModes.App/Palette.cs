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

    public static readonly Brush BrandBrush = Freeze(new SolidColorBrush(Brand));
    public static readonly Brush Stop = Solid(0xF8, 0x71, 0x71);
    public static readonly Brush Start = Solid(0x34, 0xD3, 0x99);
    public static readonly Brush Power = Solid(0xFB, 0xBF, 0x24);
    public static readonly Brush Container = Solid(0x60, 0xA5, 0xFA);
    public static readonly Brush Apps = Solid(0xC0, 0x84, 0xFC);
    public static readonly Brush Neutral = Solid(0x94, 0xA3, 0xB8);

    // Usage left: comfortable above 60 %, amber around 30 %, red as it nears 0 %.
    private const double ComfortablePercent = 60;
    private const double WarningPercent = 30;

    private static readonly (double Position, WinModes.Core.Rgb Color)[] RemainingStops =
    [
        (0, ToRgb(Stop)),
        (WarningPercent, ToRgb(Power)),
        (ComfortablePercent, ToRgb(Start)),
        (100, ToRgb(Start)),
    ];

    /// <summary>Colour of a meter that shows what is left of a limit: green when there is plenty, redder the closer it gets to 0.</summary>
    public static Color ForRemaining(double remainingPercent)
    {
        var rgb = WinModes.Core.ColorScale.At(remainingPercent, RemainingStops);
        return Color.FromRgb(rgb.R, rgb.G, rgb.B);
    }

    public static Brush RemainingBrush(double remainingPercent) => Freeze(new SolidColorBrush(ForRemaining(remainingPercent)));

    private static WinModes.Core.Rgb ToRgb(Brush brush)
    {
        var color = ((SolidColorBrush)brush).Color;
        return new WinModes.Core.Rgb(color.R, color.G, color.B);
    }

    /// <summary>Normal text colour of the current theme.</summary>
    public static Brush Text => Application.Current.TryFindResource("TextFillColorPrimaryBrush") as Brush ?? Brushes.White;

    /// <summary>Replaces the surface brushes with their light values. Called once, before any window is created.</summary>
    public static void UseLightSurfaces(ResourceDictionary resources)
    {
        resources["AppBackgroundBrush"] = Solid(0xF3, 0xF3, 0xF7);
        resources["AppCardBrush"] = Solid(0xFF, 0xFF, 0xFF);
        resources["AppCardStrokeBrush"] = Solid(0xDD, 0xDD, 0xE6);
        resources["AppWidgetBrush"] = Freeze(new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)));
    }

    public static Brush ModeGradient(string mode) => mode.ToUpperInvariant() switch
    {
        "CODE" => Gradient(Color.FromRgb(0x7C, 0x5C, 0xFC), Color.FromRgb(0x4F, 0x8B, 0xFF)),
        "WORK" => Gradient(Color.FromRgb(0x14, 0xB8, 0xA6), Color.FromRgb(0x22, 0xC5, 0x5E)),
        "GAME" => Gradient(Color.FromRgb(0xF9, 0x73, 0x16), Color.FromRgb(0xEC, 0x48, 0x99)),
        "FOCUS" => Gradient(Color.FromRgb(0x38, 0xBD, 0xF8), Color.FromRgb(0x63, 0x66, 0xF1)),
        "ECO" => Gradient(Color.FromRgb(0x84, 0xCC, 0x16), Color.FromRgb(0xFA, 0xCC, 0x15)),
        _ => Gradient(Color.FromRgb(0x64, 0x74, 0x8B), Color.FromRgb(0x94, 0xA3, 0xB8)),
    };

    /// <summary>One colour for a mode (where its gradient starts), for outlines and tints that a gradient cannot give.</summary>
    public static Brush ModeColor(string mode) => mode.ToUpperInvariant() switch
    {
        "CODE" => BrandBrush,
        "WORK" => Solid(0x14, 0xB8, 0xA6),
        "GAME" => Solid(0xF9, 0x73, 0x16),
        "FOCUS" => Solid(0x38, 0xBD, 0xF8),
        "ECO" => Solid(0x84, 0xCC, 0x16),
        _ => Neutral,
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
