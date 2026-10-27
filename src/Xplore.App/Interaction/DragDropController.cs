using Xplore.Core.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Xplore.App.ViewModels;
using Xplore.Core.Locations;
using Xplore.Core.Operations;
using Xplore.Core.Settings;

namespace Xplore.App.Interaction;

// Starts file drags from the details list and the tree and handles drops onto the details list, breadcrumbs, tree, tab headers and the console.
public sealed class DragDropController(MainWindow window)
{
    private static readonly TimeSpan FolderSpringDelay = TimeSpan.FromSeconds(1);

    private bool _rightDrag;
    private (string Path, DateTime Since)? _hoverFolder;
    private (TabViewModel Tab, string[] Sources, string Dest)? _pendingDrop;

    public static string[]? Paths(IDataObject data) => data.GetData(DataFormats.FileDrop) as string[];

    public void StartDrag(DependencyObject source, string[] paths, bool right)
    {
        _rightDrag = right;
        _pendingDrop = null;
        DragDrop.DoDragDrop(source, new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Copy | DragDropEffects.Move);
        _rightDrag = false;
        _hoverFolder = null;
        if (_pendingDrop is not { } drop)
            return;

        _pendingDrop = null;
        window.Dispatcher.BeginInvoke(() => ShowDropMenu(drop.Tab, drop.Sources, drop.Dest), DispatcherPriority.Background);
    }

    public void Accept(DragEventArgs e, string[]? sources, string? destDir)
    {
        e.Effects = sources is null ? DragDropEffects.None : EffectFor(sources, destDir, e);
        e.Handled = true;
    }

    public void DropOnto(TabViewModel tab, string[] sources, string destDir, DragEventArgs e)
    {
        if (_rightDrag)
        {
            if (CanDrop(sources, destDir))
            {
                _pendingDrop = (tab, sources, destDir);
            }

            return;
        }

        if (EffectFor(sources, destDir, e) is var effect and not DragDropEffects.None)
        {
            tab.Files.DropAsync(sources, destDir, effect == DragDropEffects.Move).Forget();
        }
    }

    public void DetailsDragOver(DragEventArgs e)
    {
        Accept(e, Paths(e.Data), DetailsTarget(e));
        if (VisualTree.FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject) is { DataContext: ItemViewModel { Entry.IsDirectory: true } f }
            && f.Entry.Path != window.Tab?.CurrentPath)
        {
            SpringLoad(f.Entry.Path, () => window.Tab?.NavigateToAsync(f.Entry.Path).Forget());
            return;
        }

        _hoverFolder = null;
    }

    public void DetailsDrop(DragEventArgs e) => PerformDrop(e, DetailsTarget(e));

    public void CrumbDragOver(object sender, DragEventArgs e)
    {
        var path = (sender as FrameworkElement)?.DataContext is Crumb c ? c.Path : null;
        Accept(e, Paths(e.Data), path);
    }

    public void CrumbDrop(object sender, DragEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Crumb { Path: { } path })
            return;

        PerformDrop(e, path);
    }

    public void TreeDragOver(DragEventArgs e)
    {
        Accept(e, Paths(e.Data), TreeTarget(e));
        if (VisualTree.FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is { DataContext: TreeNodeViewModel { Path: { } path } node })
        {
            SpringLoad(path, () => node.IsExpanded = true);
            return;
        }

        _hoverFolder = null;
    }

    public void TreeDrop(DragEventArgs e) => PerformDrop(e, TreeTarget(e));

    public void ConsoleDragOver(DragEventArgs e)
    {
        e.Effects = Paths(e.Data) is not null && window.Tab?.Terminal is { IsRunning: false } ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    public void ConsoleDrop(DragEventArgs e)
    {
        e.Handled = true;
        if (Paths(e.Data) is not { } paths || window.Tab?.Terminal is not { IsRunning: false })
            return;

        var input = window.ConsoleInput;
        var text = input.Text;
        var at = Math.Clamp(input.CaretIndex, 0, text.Length);
        var insert = string.Join(' ', paths.Select(ShellQuote));
        var lead = at > 0 && !char.IsWhiteSpace(text[at - 1]) ? " " : "";
        input.Text = text[..at] + lead + insert + " " + text[at..];
        input.CaretIndex = at + lead.Length + insert.Length + 1;
        input.Focus();
    }

    public void PerformDrop(DragEventArgs e, string? destDir)
    {
        e.Handled = true;
        if (Paths(e.Data) is { } sources && window.Tab is { } tab && destDir is not null)
        {
            DropOnto(tab, sources, destDir, e);
        }
    }

    private static bool CanDrop(string[] sources, string? destDir) =>
        destDir is not null && sources.Length > 0 && !sources.Any(s => FileOperations.IsSameOrInside(destDir, s) && Directory.Exists(s));

    private static bool CanMove(string[] sources, string destDir) =>
        CanDrop(sources, destDir) && !sources.All(s => FileOperations.SameParent(s, destDir));

    private string ShellQuote(string path) =>
        window.Main.PathFormat.Show(path) is var p && p.Any(c => char.IsWhiteSpace(c) || "'\"`$&;(){}[],@#".Contains(c)) ? $"'{p.Replace("'", "''")}'" : p;

    private DragDropEffects EffectFor(string[] sources, string? destDir, DragEventArgs e)
    {
        if (!CanDrop(sources, destDir))
            return DragDropEffects.None;

        if (_rightDrag)
            return CanMove(sources, destDir!) ? DragDropEffects.Move : DragDropEffects.Copy;

        var ctrl = e.KeyStates.HasFlag(DragDropKeyStates.ControlKey);
        var shift = e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey);
        var sameDrive = string.Equals(Path.GetPathRoot(sources[0]), Path.GetPathRoot(destDir), StringComparison.OrdinalIgnoreCase);
        var move = shift || (!ctrl && sameDrive);
        if (move && sources.All(s => FileOperations.SameParent(s, destDir!)))
            return DragDropEffects.None;

        return move ? DragDropEffects.Move : DragDropEffects.Copy;
    }

    private void ShowDropMenu(TabViewModel tab, string[] sources, string dest)
    {
        MenuItem Choice(string header, bool move, bool enabled)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += (_, _) => window.Dispatcher.BeginInvoke(() => tab.Files.DropAsync(sources, dest, move).Forget(), DispatcherPriority.Background);
            return item;
        }

        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        new Control[] { Choice(Loc.Get("Drop.Move"), true, CanMove(sources, dest)), Choice(Loc.Get("Drop.Copy"), false, true), new Separator(), new MenuItem { Header = Loc.Get("Common.Cancel") } }
            .ToList()
            .ForEach(c => menu.Items.Add(c));
        menu.IsOpen = true;
    }

    private string? DetailsTarget(DragEventArgs e) =>
        VisualTree.FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject) switch
        {
            { DataContext: ItemViewModel { Entry.IsDirectory: true } folder } when window.Tab?.CanDropOnItems != false => folder.Entry.Path,
            not null when _rightDrag => null,
            _ => window.Tab?.PathWith(LocationCapabilities.Write),
        };

    private string? TreeTarget(DragEventArgs e) =>
        VisualTree.FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is { DataContext: TreeNodeViewModel { AcceptsDrops: true, Path: var path } } ? path : null;

    // Hovering a folder during a drag triggers the action after a delay without ending the drag.
    private void SpringLoad(string folder, Action action)
    {
        if (_hoverFolder is not { } hover || hover.Path != folder)
        {
            _hoverFolder = (folder, DateTime.UtcNow);
            return;
        }

        if (DateTime.UtcNow - hover.Since < FolderSpringDelay)
            return;

        _hoverFolder = null;
        action();
    }
}
