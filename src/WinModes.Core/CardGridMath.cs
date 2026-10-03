namespace WinModes.Core;

/// <summary>The arithmetic of a card grid that decides its columns from the room it has.</summary>
public static class CardGridMath
{
    /// <summary>How many cards of at least <paramref name="minItemWidth"/> fit in <paramref name="availableWidth"/>, between 1 and <paramref name="maxColumns"/>.</summary>
    public static int ColumnCount(double availableWidth, double minItemWidth, double spacing, int maxColumns = int.MaxValue)
    {
        if (double.IsNaN(availableWidth) || double.IsInfinity(availableWidth) || minItemWidth <= 0 || maxColumns < 1)
        {
            return 1;
        }

        var fitting = (int)Math.Floor((availableWidth + spacing) / (minItemWidth + spacing));
        return Math.Clamp(fitting, 1, maxColumns);
    }

    /// <summary>Width of each card when <paramref name="columns"/> of them share <paramref name="availableWidth"/>.</summary>
    public static double ItemWidth(double availableWidth, int columns, double spacing) =>
        columns < 1 ? availableWidth : Math.Max(0, (availableWidth - (columns - 1) * spacing) / columns);
}
