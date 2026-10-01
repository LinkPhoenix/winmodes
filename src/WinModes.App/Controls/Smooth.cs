using System.Windows;
using System.Windows.Media.Animation;

namespace WinModes.App.Controls;

/// <summary>Eases a numeric property to a new value so gauges and bars glide instead of jumping.</summary>
internal static class Smooth
{
    private static readonly Duration Duration = new(TimeSpan.FromMilliseconds(700));
    private static readonly IEasingFunction Easing = CreateEasing();

    public static void To(UIElement element, DependencyProperty property, double value) =>
        element.BeginAnimation(property, new DoubleAnimation(value, Duration) { EasingFunction = Easing });

    private static CubicEase CreateEasing()
    {
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        easing.Freeze();
        return easing;
    }
}
