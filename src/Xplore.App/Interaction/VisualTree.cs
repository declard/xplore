using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Xplore.App.Interaction;

public static class VisualTree
{
    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    // A second click on a selected name starts a rename only when it is slower than a double click.
    public static TimeSpan RenameDelay => TimeSpan.FromMilliseconds(GetDoubleClickTime() + 60);

    public static DependencyObject? FindAncestor(DependencyObject? d, Func<DependencyObject, bool> match)
    {
        for (; d is not null; d = d is Visual or Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (match(d))
                return d;
        }

        return null;
    }

    public static T? FindAncestor<T>(DependencyObject? d)
        where T : DependencyObject => FindAncestor(d, x => x is T) as T;

    public static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject =>
        Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(root))
            .Select(i => VisualTreeHelper.GetChild(root, i))
            .Select(c => c as T ?? FindDescendant<T>(c))
            .FirstOrDefault(c => c is not null);

    public static bool Moved(Point from, Point to) =>
        Math.Abs(to.X - from.X) > SystemParameters.MinimumHorizontalDragDistance ||
        Math.Abs(to.Y - from.Y) > SystemParameters.MinimumVerticalDragDistance;
}
