using WinModes.App.Controls;

namespace WinModes.App.Services;

/// <summary>Stores per-page presentation preferences without changing Windows startup settings.</summary>
internal static class PageViewModeStore
{
    public static PageViewMode Load(string pageKey, PageViewMode fallback)
    {
        var modes = AppSettings.Load().PageViewModes;
        return modes is not null && modes.TryGetValue(pageKey, out var value)
            && Enum.TryParse(value, ignoreCase: true, out PageViewMode mode)
            && Enum.IsDefined(mode)
                ? mode
                : fallback;
    }

    public static void Save(string pageKey, PageViewMode mode)
    {
        var settings = AppSettings.Load();
        var modes = new Dictionary<string, string>(settings.PageViewModes ?? [], StringComparer.OrdinalIgnoreCase)
        {
            [pageKey] = mode.ToString(),
        };

        (settings with { PageViewModes = modes }).SaveUiPreference();
    }
}
