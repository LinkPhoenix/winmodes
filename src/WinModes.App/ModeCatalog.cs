using System.Windows.Media;
using WinModes.Core.Profiles;

namespace WinModes.App;

/// <summary>Modes in display order with their icon and accent colour, shared by every page.</summary>
internal static class ModeCatalog
{
    // Display order of the built-in modes; any other profile follows alphabetically.
    private static readonly string[] PreferredOrder = ["code", "work", "game", "focus", "eco"];

    private const string DefaultGlyph = "";
    private static readonly Dictionary<string, string> Glyphs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["code"] = "",
        ["work"] = "",
        ["game"] = "",
        ["focus"] = "",
        ["eco"] = "",
    };

    public static IReadOnlyList<Entry> Load() =>
        [.. AppServices.Store.ListModes()
            .OrderBy(mode => Array.IndexOf(PreferredOrder, mode) is var index && index >= 0 ? index : int.MaxValue)
            .Select(AppServices.Store.Load)
            .Select(profile => new Entry(profile, Glyphs.GetValueOrDefault(profile.Mode, DefaultGlyph), Palette.ModeGradient(profile.Mode)))];

    internal sealed record Entry(ModeProfile Profile, string Glyph, Brush Accent);
}
