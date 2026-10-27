using Xplore.Core.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Xplore.App.ViewModels;

namespace Xplore.App.Interaction;

// The folder tree: context menu, drag start, and bookmark rename.
public sealed class TreeInteractions
{
    private readonly MainWindow _window;
    private readonly DragDropController _drag;
    private readonly DispatcherTimer _renameTimer = new() { Interval = VisualTree.RenameDelay };
    private Point? _dragStart;
    private TreeNodeViewModel? _renameCandidate;

    public TreeInteractions(MainWindow window, DragDropController drag)
    {
        _window = window;
        _drag = drag;
        _renameTimer.Tick += (_, _) =>
        {
            _renameTimer.Stop();
            if (_renameCandidate is { IsSelected: true, IsRenaming: false } node)
            {
                BeginRename(node);
            }

            _renameCandidate = null;
        };
    }

    private TreeView Tree => _window.Tree;

    public void RightClick(MouseButtonEventArgs e)
    {
        if (VisualTree.FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is not { DataContext: TreeNodeViewModel { Path: { } path } node })
            return;

        e.Handled = true;
        var menu = new ContextMenu { PlacementTarget = Tree, StaysOpen = false };
        var open = new MenuItem { Header = Loc.Get("Menu.Open") };
        open.Click += (_, _) => node.IsSelected = true;
        var openInNewTab = new MenuItem { Header = Loc.Get("Menu.OpenInNewTab") };
        openInNewTab.Click += (_, _) => _window.Main.OpenInNewTab(path);
        menu.Items.Add(open);
        menu.Items.Add(openInNewTab);
        if (node.IsBookmark)
        {
            var rename = new MenuItem { Header = Loc.Get("Menu.Rename"), InputGestureText = "F2" };
            rename.Click += (_, _) => BeginRename(node);
            menu.Items.Add(new Separator());
            menu.Items.Add(rename);
        }

        menu.IsOpen = true;
    }

    public void MouseDown(MouseButtonEventArgs e)
    {
        _renameTimer.Stop();
        _renameCandidate = null;
        var src = e.OriginalSource as DependencyObject;
        var node = VisualTree.FindAncestor<TreeViewItem>(src)?.DataContext as TreeNodeViewModel;
        _dragStart = node is { Path: { } p } && Path.GetPathRoot(p) != p && VisualTree.FindAncestor<TextBox>(src) is null ? e.GetPosition(null) : null;
        if (e.ChangedButton != MouseButton.Left || e.ClickCount != 1 || src is not TextBlock)
            return;

        if (node is not { IsBookmark: true, IsSelected: true, IsRenaming: false })
            return;

        _renameCandidate = node;
        _renameTimer.Start();
    }

    public void PreviewKeyDown(KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox || e.Key != Key.F2 || Keyboard.Modifiers != ModifierKeys.None)
            return;

        if (Tree.SelectedItem is not TreeNodeViewModel { IsBookmark: true } node)
            return;

        BeginRename(node);
        e.Handled = true;
    }

    public void MouseMove(object sender, MouseEventArgs e)
    {
        var right = e.RightButton == MouseButtonState.Pressed;
        if ((e.LeftButton != MouseButtonState.Pressed && !right) || _dragStart is not { } start || !VisualTree.Moved(start, e.GetPosition(null)))
            return;

        _dragStart = null;
        _renameTimer.Stop();
        _renameCandidate = null;
        if (VisualTree.FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is not { DataContext: TreeNodeViewModel { Path: { } path } })
            return;

        _drag.StartDrag((DependencyObject)sender, [path], right);
    }

    public void RenameKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: TreeNodeViewModel node } box)
            return;

        if (e.Key == Key.Enter)
        {
            _window.Tab?.Tree.CommitBookmarkRenameAsync(node, box.Text).Forget();
            Tree.Focus();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Escape)
            return;

        FolderTree.CancelBookmarkRename(node);
        Tree.Focus();
        e.Handled = true;
    }

    public void RenameLostFocus(object sender)
    {
        if (sender is TextBox { DataContext: TreeNodeViewModel node, IsVisible: true } box)
        {
            _window.Tab?.Tree.CommitBookmarkRenameAsync(node, box.Text).Forget();
        }
    }

    private void BeginRename(TreeNodeViewModel node)
    {
        node.BeginRename();
        _window.Dispatcher.BeginInvoke(() => FocusRename(node), DispatcherPriority.ContextIdle);
    }

    private void FocusRename(TreeNodeViewModel node)
    {
        if (!node.IsRenaming || _window.Tab is not { } tab)
            return;

        if (Tree.ItemContainerGenerator.ContainerFromItem(tab.Tree.BookmarksRoot) is not TreeViewItem section)
            return;

        section.UpdateLayout();
        if (section.ItemContainerGenerator.ContainerFromItem(node) is not TreeViewItem row || VisualTree.FindDescendant<TextBox>(row) is not { } box)
            return;

        box.Focus();
        box.SelectAll();
    }
}
