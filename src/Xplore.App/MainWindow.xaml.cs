using Xplore.Core.Localization;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Xplore.App.Interaction;
using Xplore.App.ViewModels;
using Xplore.Core.Settings;

namespace Xplore.App;

public partial class MainWindow : Window
{
    private readonly DragDropController _drag;
    private readonly TabStripInteractions _tabStrip;
    private readonly DetailsInteractions _details;
    private readonly TreeInteractions _tree;
    private readonly ContextMenus _menus;
    private readonly FocusRing _focus;
    private SyncWizardWindow? _syncWizard;
    private bool _sessionSaved;

    public MainWindow(MainViewModel main)
    {
        _drag = new DragDropController(this);
        _tabStrip = new TabStripInteractions(this, _drag);
        _details = new DetailsInteractions(this, _drag);
        _tree = new TreeInteractions(this, _drag);
        _menus = new ContextMenus(this);
        _focus = new FocusRing(this, _details, _menus);
        InitializeComponent();
        DataContext = main;
        Main.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(MainViewModel.SelectedTab))
                return;

            _details.UpdateSortIndicators();
            _details.WatchTab();
        };
        _details.WatchTab();
        Loaded += (_, _) => _details.UpdateSortIndicators();
    }

    internal MainViewModel Main => (MainViewModel)DataContext;

    internal TabViewModel? Tab => Main.SelectedTab;

    // The close is held back until the session is written, then repeated.
    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_sessionSaved)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        try
        {
            await Main.SaveSessionAsync();
        }
        finally
        {
            _sessionSaved = true;
            Close();
        }
    }

    // ---- keyboard / address bar ----

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (ctrl && (e.Key is Key.Tab or Key.PageDown or Key.PageUp))
        {
            CycleTabs(e.Key == Key.PageUp || (e.Key == Key.Tab && shift) ? -1 : 1);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Alt && e.SystemKey is Key.Left or Key.Right or Key.Up && Tab is { } nav)
        {
            ICommand go = e.SystemKey switch
            {
                Key.Left => nav.BackCommand,
                Key.Right => nav.ForwardCommand,
                _ => nav.UpCommand,
            };
            if (go.CanExecute(null))
            {
                go.Execute(null);
            }

            e.Handled = true;
        }
        else if (e.Key == Key.F4 || (ctrl && !shift && e.Key == Key.L) || (Keyboard.Modifiers == ModifierKeys.Alt && e.SystemKey == Key.D))
        {
            ShowAddressEditor();
            e.Handled = true;
        }
        else if (ctrl && !shift && e.Key is Key.Z or Key.Y && Keyboard.FocusedElement is not TextBox)
        {
            (e.Key == Key.Z ? Main.UndoCommand : Main.RedoCommand).Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F12 && Keyboard.Modifiers == ModifierKeys.None && Tab is { } console)
        {
            console.Terminal.IsExpanded = true;
            FocusConsoleInput();
            e.Handled = true;
        }
        else if (ctrl && !shift && e.Key is Key.E or Key.F)
        {
            SearchBox.Focus();
            e.Handled = true;
        }
        else if (ctrl && !shift && e.Key == Key.OemTilde && Tab is { } t)
        {
            t.Terminal.IsExpanded = !t.Terminal.IsExpanded;
            if (t.Terminal.IsExpanded)
            {
                FocusConsoleInput();
            }

            e.Handled = true;
        }
        else if (ctrl && shift && e.Key == Key.N && Tab is { Files.NewFolderCommand: var newFolder } && newFolder.CanExecute(null))
        {
            newFolder.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Tab && (Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift) && !_focus.KeepsDefaultTab())
        {
            _focus.Move(shift ? -1 : 1);
            e.Handled = true;
        }
        else if (_focus.HandleArrowKey(e.Key))
        {
            e.Handled = true;
        }
    }

    private void CycleTabs(int step)
    {
        var tabs = Main.Tabs;
        if (tabs.Count < 2 || Tab is not { } current)
            return;

        Main.SelectedTab = tabs[(tabs.IndexOf(current) + step + tabs.Count) % tabs.Count];
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (AddressBox.IsVisible && !ReferenceEquals(e.OriginalSource is DependencyObject clicked ? VisualTree.FindAncestor<TextBox>(clicked) : null, AddressBox))
        {
            HideAddressEditor();
            Details.Focus();
        }

        if (e.OriginalSource is DependencyObject d && VisualTree.FindAncestor<TextBox>(d) is not null)
            return;

        if (Tab?.Items.FirstOrDefault(i => i.IsRenaming) is { } item)
        {
            Tab.Files.CommitRenameAsync(item, item.EditName).Forget();
        }
    }

    // A mouse click must not leave the button focused, which would keep it highlighted like a focused Tab stop.
    private void ToolButton_MouseDown(object sender, MouseButtonEventArgs e) => ((Button)sender).Focusable = false;

    private void ToolButton_LostMouseCapture(object sender, MouseEventArgs e) => ((Button)sender).Focusable = true;

    private void ShowAddressEditor()
    {
        CrumbBar.Visibility = Visibility.Collapsed;
        AddressBox.Visibility = Visibility.Visible;
        AddressBox.Focus();
    }

    private void HideAddressEditor()
    {
        AddressBox.Visibility = Visibility.Collapsed;
        CrumbBar.Visibility = Visibility.Visible;
    }

    private void FocusConsoleInput() =>
        Dispatcher.BeginInvoke(() => ConsoleInput.Focus(), DispatcherPriority.Input);

    private void CrumbPanel_Loaded(object sender, RoutedEventArgs e) =>
        ((CrumbPanel)sender).HiddenCountChanged += hidden => CrumbOverflow.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;

    private void CrumbOverflow_Click(object sender, RoutedEventArgs e) => _menus.ShowCrumbOverflow();

    private void CrumbBar_MouseDown(object sender, MouseButtonEventArgs e) => ShowAddressEditor();

    private void Address_GotFocus(object sender, KeyboardFocusChangedEventArgs e) => AddressBox.SelectAll();

    private void Address_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (Tab is { } t)
        {
            t.Address.Text = t.CurrentPath ?? TabViewModel.RootName;
        }

        HideAddressEditor();
    }

    private void Address_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                var tab = Tab;
                HideAddressEditor();
                tab?.Address.NavigateCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                Details.Focus();
                e.Handled = true;
                break;
        }
    }

    private void Search_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || Tab is not { } t)
            return;

        t.Search.Text = "";
        Details.Focus();
    }

    private void Crumb_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).DataContext is Crumb c)
        {
            Tab?.NavigateToAsync(c.Path).Forget();
        }
    }

    private void CrumbArrow_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        if (button.DataContext is Crumb c)
        {
            _menus.ShowCrumbMenuAsync(button, c).Forget();
        }
    }

    // ---- console ----

    private void ConsoleHeader_Click(object sender, MouseButtonEventArgs e)
    {
        if (Tab is not { } tab)
            return;

        tab.Terminal.Toggle();
        if (tab.Terminal.IsExpanded)
        {
            FocusConsoleInput();
        }
    }

    private void Console_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || Tab is not { } tab)
            return;

        tab.Terminal.IsExpanded = false;
        Details.Focus();
        e.Handled = true;
    }

    private void ConsoleSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var available = ((FrameworkElement)Details.Parent).ActualHeight - 120;
        ConsoleBody.Height = Math.Clamp(ConsoleBody.Height - e.VerticalChange, 60, Math.Max(60, available));
    }

    private void ConsoleButton_Click(object sender, RoutedEventArgs e) => ConsoleHeader_Click(sender, null!);

    private void ConsoleOutput_TextChanged(object sender, TextChangedEventArgs e) => ConsoleOutput.ScrollToEnd();

    private void ConsoleInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (Tab?.Terminal is not { } terminal)
            return;

        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        switch (e.Key)
        {
            case Key.Enter:
                terminal.Submit();
                break;
            case Key.Up:
                terminal.PreviousCommand();
                ConsoleInput.CaretIndex = ConsoleInput.Text.Length;
                break;
            case Key.Down:
                terminal.NextCommand();
                ConsoleInput.CaretIndex = ConsoleInput.Text.Length;
                break;
            case Key.C when ctrl && terminal.IsRunning && ConsoleInput.SelectionLength == 0:
                terminal.Cancel();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void Console_DragOver(object sender, DragEventArgs e) => _drag.ConsoleDragOver(e);

    private void Console_Drop(object sender, DragEventArgs e) => _drag.ConsoleDrop(e);

    // ---- toolbar ----

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = await SettingsStore.LoadAsync();
            var window = new SettingsWindow(settings) { Owner = this };
            if (window.ShowDialog() == true)
            {
                await SettingsStore.SaveAsync(window.Result);
                return;
            }

            if (window.OpenFile)
            {
                await OpenSettingsFileAsync(settings);
            }
        }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        {
            MessageBox.Show(ex.Message, Loc.Get("App.Name"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static async Task OpenSettingsFileAsync(AppSettings settings)
    {
        if (!File.Exists(SettingsStore.FilePath))
        {
            await SettingsStore.SaveAsync(settings);
        }

        try
        {
            Process.Start(new ProcessStartInfo(SettingsStore.FilePath) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            Process.Start("notepad.exe", $"\"{SettingsStore.FilePath}\"");
        }
    }

    private void SyncButton_Click(object sender, RoutedEventArgs e)
    {
        if (_syncWizard is { } open)
        {
            open.Activate();
            return;
        }

        var (left, right) = Main.SyncRoots();
        _syncWizard = new SyncWizardWindow(left, right, () => Main.ReloadAllTabsAsync().Forget()) { Owner = this };
        _syncWizard.Closed += (_, _) => _syncWizard = null;
        _syncWizard.Show();
    }

    private void Back_RightClick(object sender, MouseButtonEventArgs e) => _menus.BackRightClick(sender);

    private void Forward_RightClick(object sender, MouseButtonEventArgs e) => _menus.ForwardRightClick(sender);

    // ---- action history ----

    private static HistoryNodeViewModel? NodeAt(object? source) =>
        VisualTree.FindAncestor<TreeViewItem>(source as DependencyObject)?.DataContext as HistoryNodeViewModel;

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        HistoryTree.ItemsSource = Main.History.Roots;
        HistoryPopup.IsOpen = true;
    }

    private void HistoryNode_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeViewItem { DataContext: HistoryNodeViewModel node } || NodeAt(e.OriginalSource) != node)
            return;

        e.Handled = true;
        if (node.IsCurrent)
            return;

        HistoryPopup.IsOpen = false;
        Main.History.ReplayToAsync(node).Forget();
    }

    private void HistoryTree_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (NodeAt(e.OriginalSource) is not { } node)
            return;

        e.Handled = true;
        var menu = new ContextMenu { PlacementTarget = HistoryTree, StaysOpen = false };
        var details = new MenuItem { Header = Loc.Get("History.Details") };
        details.Click += (_, _) => new HistoryDetailsWindow(node) { Owner = this }.ShowDialog();
        var replay = new MenuItem { Header = Loc.Get("History.ReplayTo"), IsEnabled = !node.IsCurrent };
        replay.Click += (_, _) =>
        {
            HistoryPopup.IsOpen = false;
            Main.History.ReplayToAsync(node).Forget();
        };
        menu.Items.Add(details);
        menu.Items.Add(replay);
        menu.Items.Add(new MenuItem { Header = Loc.Get("Common.Cancel") });
        menu.IsOpen = true;
    }

    // ---- tree ----

    private void TreeItem_Selected(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem item)
        {
            item.BringIntoView();
        }
    }

    private void Tree_RightClick(object sender, MouseButtonEventArgs e) => _tree.RightClick(e);

    private void Tree_MouseDown(object sender, MouseButtonEventArgs e) => _tree.MouseDown(e);

    private void Tree_PreviewKeyDown(object sender, KeyEventArgs e) => _tree.PreviewKeyDown(e);

    private void Tree_MouseMove(object sender, MouseEventArgs e) => _tree.MouseMove(sender, e);

    private void BookmarkRename_KeyDown(object sender, KeyEventArgs e) => _tree.RenameKeyDown(sender, e);

    private void BookmarkRename_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => _tree.RenameLostFocus(sender);

    private void Tree_DragOver(object sender, DragEventArgs e) => _drag.TreeDragOver(e);

    private void Tree_Drop(object sender, DragEventArgs e) => _drag.TreeDrop(e);

    // ---- details list ----

    private void Item_DoubleClick(object sender, MouseButtonEventArgs e) => _details.ItemDoubleClick(sender, e);

    private void Details_SelectionChanged(object sender, SelectionChangedEventArgs e) => _details.SelectionChanged();

    private void Header_Click(object sender, RoutedEventArgs e) => _details.HeaderClick(e);

    private void Details_PreviewKeyDown(object sender, KeyEventArgs e) => _details.PreviewKeyDown(e);

    private void Details_TextInput(object sender, TextCompositionEventArgs e) => _details.TextInput(e);

    private void Rename_KeyDown(object sender, KeyEventArgs e) => _details.RenameKeyDown(sender, e);

    private void Rename_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => _details.RenameLostFocus(sender);

    private void Details_RightDown(object sender, MouseButtonEventArgs e) => _details.RightDown(e);

    private async void Details_ContextMenuOpening(object sender, ContextMenuEventArgs e) => await _menus.DetailsContextMenuOpening(e);

    private void Details_MouseDown(object sender, MouseButtonEventArgs e) => _details.MouseDown(e);

    private void Details_MouseMove(object sender, MouseEventArgs e) => _details.MouseMove(e);

    private void Details_MouseUp(object sender, MouseButtonEventArgs e) => _details.MouseUp();

    private void Details_LostCapture(object sender, MouseEventArgs e) => _details.LostCapture();

    private void Details_DragOver(object sender, DragEventArgs e) => _drag.DetailsDragOver(e);

    private void Details_Drop(object sender, DragEventArgs e) => _drag.DetailsDrop(e);

    private void Crumb_DragOver(object sender, DragEventArgs e) => _drag.CrumbDragOver(sender, e);

    private void Crumb_Drop(object sender, DragEventArgs e) => _drag.CrumbDrop(sender, e);

    // ---- tabs ----

    private void Tab_MouseDown(object sender, MouseButtonEventArgs e) => _tabStrip.MouseDown(e);

    private void Tab_MouseMove(object sender, MouseEventArgs e) => _tabStrip.MouseMove(sender, e);

    private void Tab_MouseUp(object sender, MouseButtonEventArgs e) => _tabStrip.MouseUp(sender, e);

    private void Tab_DragOver(object sender, DragEventArgs e) => _tabStrip.DragOver(sender, e);

    private void Tab_Drop(object sender, DragEventArgs e) => _tabStrip.Drop(sender, e);
}
