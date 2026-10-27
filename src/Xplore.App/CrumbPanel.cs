using System.Windows;
using System.Windows.Controls;

namespace Xplore.App;

// Lays out breadcrumbs left to right, hiding the leftmost ones when they do not fit so that the deepest ones stay visible
// with a little room left over (to click and edit the path).
public sealed class CrumbPanel : Panel
{
    private const double SpareWidth = 28;
    private int _hidden;

    public CrumbPanel() => ClipToBounds = true;

    public event Action<int>? HiddenCountChanged;

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = InternalChildren.Cast<UIElement>().ToList();
        children.ForEach(c => c.Measure(new Size(double.PositiveInfinity, availableSize.Height)));
        var widths = children.Select(c => c.DesiredSize.Width).ToList();

        var hidden = 0;
        if (widths.Sum() > availableSize.Width)
        {
            var room = availableSize.Width - SpareWidth;
            var fitting = widths.AsEnumerable().Reverse().Aggregate((Kept: 0.0, Count: 0, Full: false), (s, w) =>
                s.Full || (s.Count > 0 && s.Kept + w > room) ? (s.Kept, s.Count, true) : (s.Kept + w, s.Count + 1, false));
            hidden = widths.Count - fitting.Count;
        }

        if (hidden != _hidden)
        {
            _hidden = hidden;
            Dispatcher.BeginInvoke(() => HiddenCountChanged?.Invoke(_hidden));
        }

        return new Size(Math.Min(widths.Skip(hidden).Sum(), availableSize.Width), children.Count == 0 ? 0 : children.Max(c => c.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        foreach (var (child, index) in InternalChildren.Cast<UIElement>().Select((c, i) => (c, i)))
        {
            if (index < _hidden)
            {
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }

            child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
            x += child.DesiredSize.Width;
        }

        return finalSize;
    }
}
