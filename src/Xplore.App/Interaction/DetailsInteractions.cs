using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Xplore.App.ViewModels;
using Xplore.Core.Model;
using Xplore.Core.Navigation;
using Xplore.Core.Settings;

namespace Xplore.App.Interaction;

// The details list: mouse selection (click, rubber band), drag start, rename, keyboard commands, type-to-select and sorting by header.
public sealed class DetailsInteractions
{
    private enum Press
    {
        None,
        Drag,
        Band,
    }

    private readonly MainWindow _window;
    private readonly DragDropController _drag;
    private readonly DispatcherTimer _renameTimer = new() { Interval = VisualTree.RenameDelay };
    private readonly PrefixSearch _prefix = new();
    private Press _press;
    private Point _pressPoint;
    private bool _banding;
    private ItemViewModel? _collapseOnRelease;
    private ItemViewModel? _renameCandidate;
    private HashSet<ItemViewModel> _bandBase = [];
    private Point? _rightPress;
    private Task _typing = Task.CompletedTask;
    private TabViewModel? _watchedTab;

    public DetailsInteractions(MainWindow window, DragDropController drag)
    {
        _window = window;
        _drag = drag;
        _renameTimer.Tick += (_, _) =>
        {
            _renameTimer.Stop();
            if (_renameCandidate is { IsSelected: true } item && Details.SelectedItems.Count == 1)
            {
                Tab?.Files.BeginRename(item);
            }

            _renameCandidate = null;
        };
    }

    private ListView Details => _window.Details;

    private TabViewModel? Tab => _window.Tab;

    public IEnumerable<GridViewColumnHeader> Headers() =>
        ((GridView)Details.View).Columns.Select(c => c.Header).OfType<GridViewColumnHeader>();

    public void UpdateSortIndicators()
    {
        if (Tab is not { } tab)
            return;

        foreach (var col in ((GridView)Details.View).Columns)
        {
            if (col.Header is GridViewColumnHeader { Tag: string tag } h)
            {
                HeaderSort.SetDirection(h, SortDirection(tab, Enum.Parse<SortColumn>(tag)));
            }
        }
    }

    private static int SortDirection(TabViewModel tab, SortColumn column)
    {
        if (column != tab.SortColumn)
            return 0;

        return tab.SortAscending ? 1 : 2;
    }

    public void WatchTab()
    {
        if (_watchedTab is not null)
        {
            _watchedTab.Files.RenameStarted -= OnRenameStarted;
        }

        _watchedTab = Tab;
        if (_watchedTab is not null)
        {
            _watchedTab.Files.RenameStarted += OnRenameStarted;
        }
    }

    public void SelectionChanged() => Tab?.SetSelection(Details.SelectedItems.Cast<ItemViewModel>());

    public void HeaderClick(RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader { Tag: string tag } || Tab is not { } tab)
            return;

        tab.SortBy(Enum.Parse<SortColumn>(tag));
        UpdateSortIndicators();
    }

    public void ItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        _renameTimer.Stop();
        _renameCandidate = null;
        if (e.OriginalSource is DependencyObject d && VisualTree.FindAncestor<TextBox>(d) is not null)
            return;

        if (sender is ListViewItem { DataContext: ItemViewModel item })
        {
            Tab?.OpenAsync(item).Forget();
        }
    }

    public void PreviewKeyDown(KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox || Tab is not { } tab)
            return;

        if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
        {
            Details.UnselectAll();
            e.Handled = true;
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        ICommand? command = (key, Keyboard.Modifiers) switch
        {
            (Key.Enter, ModifierKeys.None) => tab.OpenSelectedCommand,
            (Key.Enter, ModifierKeys.Alt) => tab.Files.PropertiesCommand,
            (Key.Back, ModifierKeys.None) => tab.UpCommand,
            (Key.F5, ModifierKeys.None) => tab.RefreshCommand,
            (Key.F2, ModifierKeys.None) => tab.Files.RenameCommand,
            (Key.Delete, ModifierKeys.None) => tab.Files.DeleteCommand,
            (Key.Delete, ModifierKeys.Shift) => tab.Files.PermanentDeleteCommand,
            (Key.C, ModifierKeys.Control) => tab.Files.CopyCommand,
            (Key.X, ModifierKeys.Control) => tab.Files.CutCommand,
            (Key.V, ModifierKeys.Control) => tab.Files.PasteCommand,
            _ => null,
        };
        if (command is null)
            return;

        if (command.CanExecute(null))
        {
            command.Execute(null);
        }

        e.Handled = true;
    }

    public void TextInput(TextCompositionEventArgs e)
    {
        if (e.OriginalSource is TextBox || Tab is not { } tab || e.Text is not [var c] || char.IsControl(c))
            return;

        if (char.IsWhiteSpace(c) && _prefix.Prefix.Length == 0)
            return;

        e.Handled = true;
        _typing = SelectByPrefixAsync(_typing, tab, c, DateTime.UtcNow);
        _typing.Forget();
    }

    public void RenameKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: ItemViewModel item } box)
            return;

        if (e.Key == Key.Enter)
        {
            Tab?.Files.CommitRenameAsync(item, box.Text).Forget();
            Details.Focus();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Escape)
            return;

        FileActions.CancelRename(item);
        Details.Focus();
        e.Handled = true;
    }

    public void RenameLostFocus(object sender)
    {
        if (sender is TextBox { DataContext: ItemViewModel item, IsVisible: true } box)
        {
            Tab?.Files.CommitRenameAsync(item, box.Text).Forget();
        }
    }

    public void RightDown(MouseButtonEventArgs e)
    {
        var row = VisualTree.FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
        if (row is null)
        {
            Details.UnselectAll();
        }
        else if (!row.IsSelected)
        {
            Details.UnselectAll();
            row.IsSelected = true;
        }

        _rightPress = row is null ? null : e.GetPosition(Details);
    }

    public void MouseDown(MouseButtonEventArgs e)
    {
        _press = Press.None;
        _renameTimer.Stop();
        _renameCandidate = null;
        _collapseOnRelease = null;
        if (e.OriginalSource is not DependencyObject src
            || VisualTree.FindAncestor<GridViewColumnHeader>(src) is not null
            || VisualTree.FindAncestor<ScrollBar>(src) is not null
            || VisualTree.FindAncestor<TextBox>(src) is not null)
            return;

        _pressPoint = e.GetPosition(Details);
        var additive = Keyboard.Modifiers is ModifierKeys.Control or ModifierKeys.Shift;
        if (VisualTree.FindAncestor<ListViewItem>(src) is { DataContext: ItemViewModel item, IsSelected: true })
        {
            _press = Press.Drag;
            if (e.ClickCount == 1 && Details.SelectedItems.Count == 1 && src is TextBlock { Tag: "NameCell" })
            {
                _renameCandidate = item;
                _renameTimer.Start();
            }
            else if (e.ClickCount == 1 && Details.SelectedItems.Count > 1 && !additive)
            {
                _collapseOnRelease = item;
                e.Handled = true;
            }

            return;
        }

        _press = Press.Band;
        if (!additive && VisualTree.FindAncestor<ListViewItem>(src) is null)
        {
            Details.UnselectAll();
        }

        _bandBase = additive ? Details.SelectedItems.Cast<ItemViewModel>().ToHashSet() : [];
    }

    public void MouseMove(MouseEventArgs e)
    {
        if (e.RightButton == MouseButtonState.Pressed && _rightPress is { } rightStart && VisualTree.Moved(rightStart, e.GetPosition(Details)))
        {
            _rightPress = null;
            if (Tab is { ItemsEditable: true, SelectedPaths: { Count: > 0 } selected })
            {
                _drag.StartDrag(Details, selected.ToArray(), right: true);
            }

            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed || _press == Press.None)
            return;

        var position = e.GetPosition(Details);
        if (!_banding && !VisualTree.Moved(_pressPoint, position))
            return;

        _renameTimer.Stop();
        _renameCandidate = null;
        _collapseOnRelease = null;

        if (_press == Press.Drag)
        {
            _press = Press.None;
            if (Tab is { ItemsEditable: true, SelectedPaths: { Count: > 0 } paths })
            {
                _drag.StartDrag(Details, paths.ToArray(), right: false);
            }

            return;
        }

        var bandRect = _window.BandRect;
        if (!_banding)
        {
            _banding = true;
            Details.CaptureMouse();
            bandRect.Visibility = Visibility.Visible;
        }

        var band = new Rect(_pressPoint, position);
        Canvas.SetLeft(bandRect, band.X);
        Canvas.SetTop(bandRect, band.Y);
        bandRect.Width = band.Width;
        bandRect.Height = band.Height;
        SelectWithin(band);
    }

    public void MouseUp()
    {
        if (_collapseOnRelease is { } item)
        {
            Details.UnselectAll();
            item.IsSelected = true;
            (Details.ItemContainerGenerator.ContainerFromItem(item) as ListViewItem)?.Focus();
        }

        _collapseOnRelease = null;
        _press = Press.None;
        if (_banding)
        {
            Details.ReleaseMouseCapture();
        }
    }

    public void LostCapture()
    {
        _banding = false;
        _window.BandRect.Visibility = Visibility.Collapsed;
    }

    private void SelectWithin(Rect band)
    {
        foreach (var item in Tab?.Items ?? [])
        {
            if (Details.ItemContainerGenerator.ContainerFromItem(item) is not ListViewItem row)
                continue;

            var bounds = row.TransformToAncestor(Details).TransformBounds(new Rect(row.RenderSize));
            item.IsSelected = bounds.IntersectsWith(band) || _bandBase.Contains(item);
        }
    }

    // Each keystroke waits for the settings file, so keystrokes are chained to be applied in the order they were typed.
    private async Task SelectByPrefixAsync(Task previous, TabViewModel tab, char c, DateTime typedAt)
    {
        await previous.ContinueWith(_ => { }, TaskScheduler.Default);
        var timeout = TimeSpan.FromMilliseconds((await SettingsStore.LoadAsync()).PrefixTimeoutMs);
        var prefix = _prefix.Add(c, typedAt, timeout);
        if (tab != Tab)
            return;

        var last = tab.Items.Select((it, i) => (it, i)).LastOrDefault(p => p.it.IsSelected, (it: null!, i: -1)).i;
        var at = PrefixSearch.IndexOf(tab.Items.Select(i => i.Name).ToList(), prefix, prefix.Length > 1 ? Math.Max(last, 0) : last + 1);
        if (at < 0)
            return;

        var item = tab.Items[at];
        Details.UnselectAll();
        item.IsSelected = true;
        Details.ScrollIntoView(item);
        Details.UpdateLayout();
        (Details.ItemContainerGenerator.ContainerFromItem(item) as ListViewItem)?.Focus();
    }

    // Deferred past the context menu's own focus restoration, which would otherwise take focus back.
    private void OnRenameStarted(ItemViewModel item) =>
        _window.Dispatcher.BeginInvoke(() => FocusRename(item), DispatcherPriority.ContextIdle);

    private void FocusRename(ItemViewModel item)
    {
        if (!item.IsRenaming)
            return;

        Details.ScrollIntoView(item);
        Details.UpdateLayout();
        if (Details.ItemContainerGenerator.ContainerFromItem(item) is not { } row || VisualTree.FindDescendant<TextBox>(row) is not { } box)
            return;

        box.Focus();
        box.SelectAll();
    }
}
