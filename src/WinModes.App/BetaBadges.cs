using System.Windows;
using System.Windows.Controls;

namespace WinModes.App;

/// <summary>The small "Beta" tag next to the version, shown only in a beta build.</summary>
internal static class BetaBadges
{
    public static void Show(Border badge, TextBlock text)
    {
        ArgumentNullException.ThrowIfNull(badge);
        ArgumentNullException.ThrowIfNull(text);
        if (!AppInfo.IsBeta)
        {
            return;
        }

        text.Text = Loc.T("Beta");
        text.Foreground = Palette.Power;
        badge.Background = Palette.Tint(Palette.Power);
        badge.Visibility = Visibility.Visible;
    }
}
