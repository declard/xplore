using System.Windows;

namespace Xplore.App;

// Sort-direction marker for a column header: 0 none, 1 ascending, 2 descending.
public static class HeaderSort
{
    public static readonly DependencyProperty DirectionProperty =
        DependencyProperty.RegisterAttached("Direction", typeof(int), typeof(HeaderSort), new PropertyMetadata(0));

    public static int GetDirection(DependencyObject d) => (int)d.GetValue(DirectionProperty);
    public static void SetDirection(DependencyObject d, int value) => d.SetValue(DirectionProperty, value);
}
