using System.Globalization;

namespace WinModes.Core.Tuning;

/// <summary>
/// A tweak and the parts of it to apply or undo, written "id" (all parts) or "id#0,2,5" (those parts) so it can travel as one
/// argument to the elevated helper. The text comes from a process that is not elevated: nothing but small numbers is accepted.
/// </summary>
public sealed record TweakSelection(string Id, IReadOnlySet<int>? Parts)
{
    private const char Separator = '#';
    private const int MaxParts = 64;

    /// <summary>"id" for every part, "id#0,2" for some. A selection of every part is written plain.</summary>
    public static string Format(Tweak tweak, IEnumerable<int> parts)
    {
        ArgumentNullException.ThrowIfNull(tweak);
        var chosen = parts.Distinct().Order().ToList();
        return chosen.Count == tweak.Parts.Count
            ? tweak.Id
            : tweak.Id + Separator + string.Join(',', chosen.Select(index => index.ToString(CultureInfo.InvariantCulture)));
    }

    /// <returns>False when the text is not "id" or "id#number,number".</returns>
    public static bool TryParse(string? text, out TweakSelection selection)
    {
        selection = new TweakSelection("", null);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var cut = text.IndexOf(Separator, StringComparison.Ordinal);
        if (cut < 0)
        {
            selection = new TweakSelection(text, null);
            return true;
        }

        var numbers = text[(cut + 1)..].Split(',');
        if (cut == 0 || numbers.Length is 0 or > MaxParts)
        {
            return false;
        }

        var parts = new HashSet<int>();
        foreach (var number in numbers)
        {
            if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index >= MaxParts)
            {
                return false;
            }

            parts.Add(index);
        }

        selection = new TweakSelection(text[..cut], parts);
        return true;
    }
}
