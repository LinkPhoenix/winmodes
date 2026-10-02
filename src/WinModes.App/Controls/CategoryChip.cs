using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinModes.App.Controls;

/// <summary>A button of a category bar. Its colours are set here because each category has its own.</summary>
internal sealed class CategoryChip(string? key, string title, string glyph, Brush color) : INotifyPropertyChanged
{
    private const double DimmedOpacity = 0.5;

    private int _count;
    private bool _selected;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>What the page filters on; null for the chip that shows everything.</summary>
    public string? Key => key;
    public string Title => title;
    public string Glyph => glyph;
    public Brush Color => color;
    public string Count => _count.ToString(CultureInfo.CurrentCulture);
    public Brush Fill => _selected ? Palette.Tint(color) : Application.Current.TryFindResource("ControlFillColorDefaultBrush") as Brush ?? Palette.Tint(Palette.Neutral);
    public Brush Stroke => _selected ? color : Application.Current.TryFindResource("AppCardStrokeBrush") as Brush ?? Palette.Neutral;

    /// <summary>A category without a match stays clickable but fades.</summary>
    public double Emphasis => _count == 0 && !_selected ? DimmedOpacity : 1;

    public void Update(int count, bool selected)
    {
        if (count == _count && selected == _selected)
        {
            return;
        }

        (_count, _selected) = (count, selected);
        foreach (var name in (string[])[nameof(Count), nameof(Fill), nameof(Stroke), nameof(Emphasis)])
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

internal static class CategoryChips
{
    /// <summary>
    /// Shows one chip per entry, the first being the one that stands for everything. The chips are kept while the entries stay the same,
    /// so only their numbers and their selection change.
    /// </summary>
    public static List<CategoryChip> Sync(
        ItemsControl bar, List<CategoryChip> current, IReadOnlyList<(string? Key, string Title, string Glyph, Brush Color, int Count)> entries, string? selected)
    {
        ArgumentNullException.ThrowIfNull(bar);
        ArgumentNullException.ThrowIfNull(current);

        if (!current.Select(chip => chip.Key).SequenceEqual(entries.Select(entry => entry.Key)))
        {
            current = [.. entries.Select(entry => new CategoryChip(entry.Key, entry.Title, entry.Glyph, entry.Color))];
            bar.ItemsSource = current;
        }

        for (var i = 0; i < entries.Count; i++)
        {
            current[i].Update(entries[i].Count, current[i].Key == selected);
        }

        return current;
    }
}
