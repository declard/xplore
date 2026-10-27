using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Xplore.App.ViewModels;
using Xplore.Core.Model;

namespace Xplore.App.Interaction;

// Tab / Shift+Tab cycling through the window's focus stops, and arrow-key navigation among column headers and breadcrumb elements.
public sealed class FocusRing(MainWindow window, DetailsInteractions details, ContextMenus menus)
{
    private sealed record Stop(Func<UIElement?> Target, Func<DependencyObject, bool> Owns);

    public void Move(int step)
    {
        var ring = Ring();
        var at = Keyboard.FocusedElement is DependencyObject focused ? ring.FindIndex(s => s.Owns(focused)) : -1;
        var start = Math.Max(at, step > 0 ? -1 : 0);
        var target = Enumerable.Range(1, ring.Count)
            .Select(i => ring[((start + step * i) % ring.Count + ring.Count) % ring.Count].Target())
            .FirstOrDefault(t => t is not null);
        target?.Focus();
    }

    public bool KeepsDefaultTab() =>
        Keyboard.FocusedElement is MenuItem or MenuBase or TextBox { DataContext: ItemViewModel or TreeNodeViewModel };

    // Left/Right move between column headers and between breadcrumb elements; Down opens a breadcrumb's dropdown.
    public bool HandleArrowKey(Key key)
    {
        if (Keyboard.Modifiers != ModifierKeys.None)
            return false;

        switch (Keyboard.FocusedElement)
        {
            case GridViewColumnHeader { Tag: string tag } when key == Key.Space && window.Tab is { } sortTab:
                sortTab.SortBy(Enum.Parse<SortColumn>(tag));
                details.UpdateSortIndicators();
                return true;
            case GridViewColumnHeader { Tag: string } header when key is Key.Left or Key.Right:
                var headers = details.Headers().ToList();
                headers.ElementAtOrDefault(headers.IndexOf(header) + (key == Key.Left ? -1 : 1))?.Focus();
                return true;
            case FrameworkElement { Tag: "CrumbItem", DataContext: Crumb crumb } item:
                return HandleCrumbKey(key, item, crumb);
            default:
                return false;
        }
    }

    private static bool Inside(FrameworkElement container, DependencyObject d) =>
        ReferenceEquals(container, d) || (d is Visual && container.IsAncestorOf(d));

    private static Stop ButtonStop(Button button) =>
        new(() => button.IsEnabled && button.IsVisible ? button : null, d => ReferenceEquals(VisualTree.FindAncestor<Button>(d), button));

    private static bool IsCrumb(DependencyObject d) => d is FrameworkElement { Tag: "CrumbItem" };

    private static TreeViewItem? SelectedTreeItem(ItemsControl parent) =>
        parent.Items.Cast<object>()
            .Select(i => parent.ItemContainerGenerator.ContainerFromItem(i) as TreeViewItem)
            .OfType<TreeViewItem>()
            .Select(SelectedWithin)
            .FirstOrDefault(c => c is not null);

    private static TreeViewItem? SelectedWithin(TreeViewItem item)
    {
        if (item.IsSelected)
            return item;

        if (!item.IsExpanded)
            return null;

        return SelectedTreeItem(item);
    }

    private List<Stop> Ring() =>
    [
        new(DetailsStop, d => Inside(window.Details, d) && VisualTree.FindAncestor<GridViewColumnHeader>(d) is null),
        new(() => window.Tab?.Terminal is { IsExpanded: true, IsActive: true } ? window.ConsoleInput : window.ConsoleToggle,
            d => Inside(window.ConsolePanel, d) || ReferenceEquals(VisualTree.FindAncestor<Button>(d), window.ConsoleToggle)),
        ButtonStop(window.SyncButton),
        ButtonStop(window.HistoryButton),
        ButtonStop(window.SettingsButton),
        ButtonStop(window.BackButton),
        ButtonStop(window.ForwardButton),
        ButtonStop(window.UpButton),
        ButtonStop(window.CollapseTreeButton),
        ButtonStop(window.BookmarkButton),
        new(() => window.CrumbBar.IsVisible ? CrumbItems().FirstOrDefault() : null,
            d => VisualTree.FindAncestor(d, IsCrumb) is not null || ReferenceEquals(d, window.AddressBox)),
        ButtonStop(window.CopyLocationButton),
        ButtonStop(window.OpenCmdButton),
        new(() => window.SearchBox, d => Inside(window.SearchBox, d)),
        new(TreeStop, d => Inside(window.Tree, d)),
        new(SortedHeader, d => VisualTree.FindAncestor<GridViewColumnHeader>(d) is not null),
    ];

    private bool HandleCrumbKey(Key key, FrameworkElement item, Crumb crumb)
    {
        var items = CrumbItems();
        switch (key)
        {
            case Key.Left:
                items.ElementAtOrDefault(items.IndexOf(item) - 1)?.Focus();
                return true;
            case Key.Right:
                items.ElementAtOrDefault(items.IndexOf(item) + 1)?.Focus();
                return true;
            case Key.Down:
                menus.ShowCrumbMenuAsync(item, crumb).Forget();
                return true;
            case Key.Enter or Key.Space:
                window.Tab?.NavigateToAsync(crumb.Path).Forget();
                window.Details.Focus();
                return true;
            default:
                return false;
        }
    }

    private UIElement? SortedHeader() =>
        window.Tab is { } tab ? details.Headers().FirstOrDefault(h => (string?)h.Tag == tab.SortColumn.ToString()) : null;

    private List<FrameworkElement> CrumbItems() =>
        Enumerable.Range(0, window.CrumbList.Items.Count)
            .Select(i => window.CrumbList.ItemContainerGenerator.ContainerFromIndex(i))
            .Select(c => c is not null && VisualTreeHelper.GetChildrenCount(c) > 0 ? VisualTreeHelper.GetChild(c, 0) as FrameworkElement : null)
            .OfType<FrameworkElement>()
            .Where(e => e.ActualWidth > 0)
            .ToList();

    private UIElement TreeStop() =>
        SelectedTreeItem(window.Tree) ?? window.Tree.ItemContainerGenerator.ContainerFromIndex(0) as UIElement ?? window.Tree;

    private UIElement DetailsStop()
    {
        var list = window.Details;
        var row = list.SelectedItems.Count > 0
            ? list.ItemContainerGenerator.ContainerFromItem(list.SelectedItems[0])
            : list.ItemContainerGenerator.ContainerFromIndex(0);
        return row as UIElement ?? list;
    }
}
