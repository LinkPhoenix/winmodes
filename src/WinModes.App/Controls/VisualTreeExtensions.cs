using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace WinModes.App.Controls;

internal static class VisualTreeExtensions
{
    /// <summary>Every visual under <paramref name="root"/>, depth first.</summary>
    public static IEnumerable<DependencyObject> Descendants(this DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var deeper in child.Descendants())
            {
                yield return deeper;
            }
        }
    }

    /// <summary>The nearest parent of the given type, or null. Text inside a run is not a visual, so the logical tree is used from there.</summary>
    public static T? FindAncestor<T>(this DependencyObject start)
        where T : DependencyObject
    {
        var current = start;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current is Visual or Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }
}
