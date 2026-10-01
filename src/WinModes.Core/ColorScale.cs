namespace WinModes.Core;

/// <summary>A colour without any UI type, so a scale can be tested and used by any front end.</summary>
public readonly record struct Rgb(byte R, byte G, byte B);

/// <summary>A colour that changes smoothly with a value, for example green when plenty of a limit is left and red when little is.</summary>
public static class ColorScale
{
    /// <param name="stops">Positions in ascending order, each with the colour it has exactly there.</param>
    /// <returns>The colour at <paramref name="value"/>: blended between its two neighbours, and the first or last stop beyond the ends.</returns>
    public static Rgb At(double value, IReadOnlyList<(double Position, Rgb Color)> stops)
    {
        ArgumentNullException.ThrowIfNull(stops);
        if (stops.Count == 0)
        {
            throw new ArgumentException("A scale needs at least one stop.", nameof(stops));
        }

        if (double.IsNaN(value) || value <= stops[0].Position)
        {
            return stops[0].Color;
        }

        for (var index = 1; index < stops.Count; index++)
        {
            var (position, color) = stops[index];
            if (value <= position)
            {
                var (previousPosition, previousColor) = stops[index - 1];
                var share = (value - previousPosition) / (position - previousPosition);
                return new Rgb(Blend(previousColor.R, color.R, share), Blend(previousColor.G, color.G, share), Blend(previousColor.B, color.B, share));
            }
        }

        return stops[^1].Color;
    }

    private static byte Blend(byte from, byte to, double share) => (byte)Math.Round(from + (to - from) * share);
}
