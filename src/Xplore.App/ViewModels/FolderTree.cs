using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Xplore.Core.Localization;
using Xplore.Core.Model;
using Xplore.Core.Operations;

namespace Xplore.App.ViewModels;

// The navigation tree: the folder hierarchy and the bookmarks section, kept in step with the current folder.
public sealed class FolderTree : IDisposable
{
    private readonly BookmarkStore _bookmarks;
    private readonly Func<string?> _currentPath;
    private readonly Func<string?, Task> _navigate;
    private readonly HashSet<TreeNodeViewModel> _lit = [];
    private bool _revealing;
    private TreeNodeViewModel? _revealed;

    public FolderTree(IFileSystemService fs, BookmarkStore bookmarks, Func<string?> currentPath, Func<string?, Task> navigate)
    {
        _bookmarks = bookmarks;
        _currentPath = currentPath;
        _navigate = navigate;
        Root = new TreeNodeViewModel(null, TabViewModel.RootName, null, fs, OnSelected) { IsExpanded = true };
        BookmarksRoot = TreeNodeViewModel.Section(Loc.Get("Tree.Bookmarks"), OnSelected);
        Nodes = [BookmarksRoot, Root];
        CollapseCommand = new RelayCommand(CollapseOthers);
        _bookmarks.Changed += RefreshBookmarkNodes;
        RefreshBookmarkNodes();
    }

    public TreeNodeViewModel Root { get; }
    public TreeNodeViewModel BookmarksRoot { get; }
    public IReadOnlyList<TreeNodeViewModel> Nodes { get; }
    public ICommand CollapseCommand { get; }

    public static void CancelBookmarkRename(TreeNodeViewModel node) => node.IsRenaming = false;

    public Task CommitBookmarkRenameAsync(TreeNodeViewModel node, string newName)
    {
        if (!node.IsRenaming)
            return Task.CompletedTask;

        node.IsRenaming = false;
        return newName.Trim() is { Length: > 0 } name && name != node.Name ? _bookmarks.RenameAsync(node.Path!, name) : Task.CompletedTask;
    }

    public async Task RevealAsync(string? path, CancellationToken ct)
    {
        var node = path is null ? Root : await Root.RevealAsync(path);
        if (node is null || ct.IsCancellationRequested)
            return;

        _revealing = true;
        node.IsSelected = true;
        _revealing = false;
        _revealed = node;
        ClearStaleHighlights(node);
        Application.Current?.Dispatcher.BeginInvoke(
            () =>
            {
                if (_revealed != node)
                    return;

                ClearStaleHighlights(node);
            },
            DispatcherPriority.Background);
    }

    // Keeps the folders leading to the current location expanded and collapses every other expanded folder.
    public void CollapseOthers() => CollapseExcept(Root, _currentPath());

    public void Dispose() => _bookmarks.Changed -= RefreshBookmarkNodes;

    private void OnSelected(TreeNodeViewModel node)
    {
        _lit.Add(node);
        if (_revealing || node.IsSection || node.Path == _currentPath())
            return;

        _navigate(node.Path).Forget();
    }

    private static void CollapseExcept(TreeNodeViewModel node, string? current)
    {
        foreach (var child in node.Children.Where(c => c.IsExpanded))
        {
            var onPath = current is not null && child.Path is not null && FileOperations.IsSameOrInside(current, child.Path);
            if (!onPath)
            {
                child.IsExpanded = false;
            }

            CollapseExcept(child, current);
        }
    }

    // A node selected while another node's selection event is still being processed can stay highlighted
    // after the tree moved on, so every node ever selected is explicitly cleared.
    private void ClearStaleHighlights(TreeNodeViewModel current)
    {
        _lit.Where(n => n != current).ToList().ForEach(n => n.IsSelected = false);
        _lit.RemoveWhere(n => n != current);
        _lit.Add(current);
    }

    // Existing nodes are kept (and renamed in place) so that a selected or edited bookmark node survives the refresh.
    private void RefreshBookmarkNodes()
    {
        var existing = BookmarksRoot.Children.ToDictionary(n => n.Path!, StringComparer.OrdinalIgnoreCase);
        var nodes = _bookmarks.Items.Select(b =>
        {
            if (!existing.TryGetValue(b.Path, out var node))
                return TreeNodeViewModel.Leaf(b.Path, b.Name, IconCache.Folder, OnSelected);

            node.Rename(b.Name);
            return node;
        }).ToList();
        BookmarksRoot.Children.Clear();
        nodes.ForEach(BookmarksRoot.Children.Add);
    }
}
