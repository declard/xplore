using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Xplore.App.ViewModels;
using Xplore.Core.Locations;

namespace Xplore.App.Interaction;

// Tab header behaviour: reorder by dragging, close with the middle button, switch and drop files onto a hovered tab.
public sealed class TabStripInteractions(MainWindow window, DragDropController drag)
{
    private const string TabFormat = "XploreTab";
    private static readonly TimeSpan SpringLoadDelay = TimeSpan.FromMilliseconds(600);

    private Point? _dragStart;
    private (TabViewModel Tab, DateTime Since)? _hoveredTab;

    public void MouseDown(MouseButtonEventArgs e) =>
        _dragStart = VisualTree.FindAncestor<Button>(e.OriginalSource as DependencyObject) is null ? e.GetPosition(null) : null;

    public void MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStart is not { } start || !VisualTree.Moved(start, e.GetPosition(null)))
            return;

        _dragStart = null;
        if (sender is TabItem { DataContext: TabViewModel tab } item)
        {
            DragDrop.DoDragDrop(item, new DataObject(TabFormat, tab), DragDropEffects.Move);
        }
    }

    public void MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && sender is TabItem { DataContext: TabViewModel tab })
        {
            window.Main.CloseTabCommand.Execute(tab);
        }
    }

    public void DragOver(object sender, DragEventArgs e)
    {
        if (sender is not TabItem { DataContext: TabViewModel tab })
            return;

        if (e.Data.GetDataPresent(TabFormat))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        drag.Accept(e, DragDropController.Paths(e.Data), tab.PathWith(LocationCapabilities.Write));
        if (_hoveredTab is not { } hovered || hovered.Tab != tab)
        {
            _hoveredTab = (tab, DateTime.UtcNow);
            return;
        }

        if (DateTime.UtcNow - hovered.Since > SpringLoadDelay && window.Main.SelectedTab != tab)
        {
            window.Main.SelectedTab = tab;
        }
    }

    public void Drop(object sender, DragEventArgs e)
    {
        _hoveredTab = null;
        if (sender is not TabItem { DataContext: TabViewModel target })
            return;

        var main = window.Main;
        if (e.Data.GetData(TabFormat) is TabViewModel dragged)
        {
            main.Tabs.Move(main.Tabs.IndexOf(dragged), main.Tabs.IndexOf(target));
            main.SelectedTab = dragged;
            e.Handled = true;
            return;
        }

        var selected = main.SelectedTab;
        if (DragDropController.Paths(e.Data) is { } sources && target.PathWith(LocationCapabilities.Write) is { } dest)
        {
            drag.DropOnto(target, sources, dest, e);
        }

        e.Handled = true;
        main.SelectedTab = selected;
    }
}
