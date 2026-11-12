using Xplore.Core.Localization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Xplore.App.ViewModels;
using Xplore.Core.Locations;
using Xplore.Core.Settings;
using Xplore.Core.Shell;
using Xplore.Core.Storage;

namespace Xplore.App.Interaction;

// Menus built in code: the details list (item and background), back/forward history, breadcrumb overflow and breadcrumb subfolders.
public sealed class ContextMenus(MainWindow window)
{
    public async Task DetailsContextMenuOpening(ContextMenuEventArgs e)
    {
        if (window.Tab is not { } tab)
            return;

        e.Handled = true;
        var onItem = VisualTree.FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject) is not null;
        var menu = await MenuAsync(tab, onItem);
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            var more = menu.Items.OfType<MenuItem>().FirstOrDefault(i => i.Tag is Func<Task> && i.IsEnabled);
            if (more is not null)
            {
                ((Func<Task>)more.Tag).Invoke().Forget();
            }
        }
    }

    public void BackRightClick(object sender) =>
        ShowHistoryMenu((Button)sender, window.Tab?.BackList, steps => window.Tab?.JumpBackAsync(steps).Forget());

    public void ForwardRightClick(object sender) =>
        ShowHistoryMenu((Button)sender, window.Tab?.ForwardList, steps => window.Tab?.JumpForwardAsync(steps).Forget());

    public void ShowCrumbOverflow()
    {
        if (window.Tab is not { } tab)
            return;

        var menu = new ContextMenu { PlacementTarget = window.CrumbOverflow, Placement = PlacementMode.Bottom, MaxHeight = 400 };
        foreach (var crumb in tab.Address.Breadcrumbs)
        {
            var item = new MenuItem { Header = crumb.Name.Replace("_", "__"), ToolTip = crumb.Path ?? crumb.Name };
            item.Click += (_, _) => tab.NavigateToAsync(crumb.Path).Forget();
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    public async Task ShowCrumbMenuAsync(FrameworkElement anchor, Crumb crumb)
    {
        if (window.Tab is not { } tab)
            return;

        var subfolders = await tab.SubfoldersAsync(crumb.Path);
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom, MaxHeight = 400 };
        var chosen = false;
        foreach (var f in subfolders)
        {
            var item = new MenuItem { Header = f.Name.Replace("_", "__"), Icon = new Image { Source = IconCache.For(f), Width = 16, Height = 16 } };
            item.Click += (_, _) =>
            {
                chosen = true;
                tab.NavigateToAsync(f.Path).Forget();
                window.Details.Focus();
            };
            menu.Items.Add(item);
        }

        menu.Closed += (_, _) =>
        {
            if (!chosen)
            {
                anchor.Focus();
            }
        };
        menu.IsOpen = true;
        if (anchor.Focusable)
        {
            _ = menu.Dispatcher.BeginInvoke(() => (menu.ItemContainerGenerator.ContainerFromIndex(0) as MenuItem)?.Focus(), DispatcherPriority.Input);
        }
    }

    private static void ShowHistoryMenu(Button anchor, IReadOnlyList<Crumb>? entries, Action<int> jump)
    {
        if (entries is not { Count: > 0 })
            return;

        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom, MaxHeight = 400 };
        foreach (var (entry, index) in entries.Select((c, i) => (c, i)))
        {
            var item = new MenuItem { Header = entry.Name.Replace("_", "__"), ToolTip = entry.Path ?? entry.Name };
            item.Click += (_, _) => jump(index + 1);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private MenuItem Entry(string header, ICommand command, string? gesture = null)
    {
        var item = new MenuItem { Header = header, InputGestureText = gesture, IsEnabled = command.CanExecute(null) };

        // Deferred so system dialogs are created after the menu popup is gone and not owned by it.
        item.Click += (_, _) => window.Dispatcher.BeginInvoke(() => command.Execute(null), DispatcherPriority.Background);
        return item;
    }

    private MenuItem QuickOpenEntry(TabViewModel tab, OpenWithApp app)
    {
        var item = new MenuItem { Header = Loc.Format("Menu.OpenWithApp", app.Name.Replace("_", "__")), ToolTip = Loc.Format("Menu.OpenWithApp.ToolTip", app.Path) };
        if (IconCache.ForApp(app) is { } icon)
        {
            item.Icon = new Image { Source = icon, Width = 16, Height = 16 };
        }

        item.Click += (_, _) => window.Dispatcher.BeginInvoke(() => tab.OpenWith.LaunchAsync(app).Forget(), DispatcherPriority.Background);
        item.PreviewMouseRightButtonUp += (_, e) =>
        {
            tab.OpenWith.ForgetAsync(app).Forget();
            (item.Parent as ItemsControl)?.Items.Remove(item);
            e.Handled = true;
        };
        return item;
    }

    private async Task<ContextMenu> ItemMenuAsync(TabViewModel tab)
    {
        var quickApps = await tab.OpenWith.QuickAppsAsync();
        var sendTo = new MenuItem { Header = Loc.Get("Menu.SendTo") };
        (await ShellVerbs.SendToTargetsAsync()).ToList().ForEach(t =>
        {
            var item = new MenuItem { Header = t.Name.Replace("_", "__") };
            item.Click += (_, _) => window.Dispatcher.BeginInvoke(() => tab.Files.SendToAsync(t).Forget(), DispatcherPriority.Background);
            sendTo.Items.Add(item);
        });
        sendTo.IsEnabled = sendTo.Items.Count > 0 && tab.SelectionHasShellPaths;

        var menu = new ContextMenu();
        Control[] entries =
        [
            Entry(Loc.Get("Menu.Open"), tab.OpenSelectedCommand), Entry(Loc.Get("Menu.OpenWith"), tab.OpenWith.DialogCommand),
            .. quickApps.Select(app => QuickOpenEntry(tab, app)), new Separator(),
            Entry(Loc.Get("Menu.Cut"), tab.Files.CutCommand, "Ctrl+X"), Entry(Loc.Get("Menu.Copy"), tab.Files.CopyCommand, "Ctrl+C"),
            Entry(Loc.Get("Menu.CopyPath"), tab.Files.CopyPathCommand), Entry(Loc.Get("Menu.CreateShortcut"), tab.Files.CreateShortcutCommand), new Separator(),
            sendTo, Entry(Loc.Get("Common.Delete"), tab.Files.DeleteCommand, "Del"), Entry(Loc.Get("Menu.Rename"), tab.Files.RenameCommand, "F2"), new Separator(),
            MoreEntry(menu, tab.SelectionHasShellPaths && tab.SelectedPaths is [_, ..], owner => ShellContextMenu.ForItems(tab.SelectedPaths, owner)),
            Entry(Loc.Get("Menu.Properties"), tab.Files.PropertiesCommand),
        ];
        entries.ToList().ForEach(c => menu.Items.Add(c));
        return menu;
    }

    private async Task<ContextMenu> MenuAsync(TabViewModel tab, bool onItem)
    {
        if (!tab.ItemsEditable)
            return ProviderMenu(tab, onItem);

        if (!onItem)
            return BackgroundMenu(tab);

        return await ItemMenuAsync(tab);
    }

    // The menu of a location whose items are not edited in place: only what its provider offers.
    private ContextMenu ProviderMenu(TabViewModel tab, bool onItem)
    {
        var menu = new ContextMenu();
        var verbs = tab.Verbs.Where(v => (v.Role != VerbRole.Location) == onItem).Select(v => VerbEntry(tab, v));
        Control[] entries = onItem
            ? [Entry(Loc.Get("Menu.Open"), tab.OpenSelectedCommand), .. verbs, new Separator(), Entry(Loc.Get("Menu.Properties"), tab.Files.PropertiesCommand)]
            : [.. verbs];

        foreach(var entry in entries)
        {
            menu.Items.Add(entry);
        }

        return menu;
    }

    private MenuItem VerbEntry(TabViewModel tab, LocationVerb verb)
    {
        if (verb.Role == VerbRole.Delete)
            return Entry(verb.Name, tab.Files.CommandFor(verb), "Del");

        return Entry(verb.Name, tab.Files.CommandFor(verb));
    }

    private ContextMenu BackgroundMenu(TabViewModel tab)
    {
        var menu = new ContextMenu();
        new Control[]
        {
            Entry(Loc.Get("Menu.Paste"), tab.Files.PasteCommand, "Ctrl+V"), Entry(Loc.Get("Menu.PasteShortcut"), tab.Files.PasteShortcutCommand), new Separator(),
            Entry(Loc.Get("NewFolder.Title"), tab.Files.NewFolderCommand, "Ctrl+Shift+N"), Entry(Loc.Get("Menu.NewFile"), tab.Files.NewFileCommand), new Separator(),
            MoreEntry(menu, tab.CurrentPath is { } here && Stores.Has(here, StoreCapabilities.RealPath), owner => ShellContextMenu.ForFolder(tab.CurrentPath!, owner)),
            Entry(Loc.Get("Menu.Properties"), tab.Files.PropertiesCommand),
        }.ToList().ForEach(c => menu.Items.Add(c));
        return menu;
    }

    // "More…" replaces itself with what the shell's own menu has beyond the application's entries (7-Zip, Git Extensions...). The shell menu stays alive until the menu closes or the chosen command has run.
    private MenuItem MoreEntry(ContextMenu menu, bool available, Func<IntPtr, ShellContextMenu> open)
    {
        var item = new MenuItem { Header = Loc.Get("Menu.More"), StaysOpenOnClick = true, IsEnabled = available };
        item.Tag = (Func<Task>)(() => ExpandAsync(menu, item, open));
        item.Click += (_, _) => ((Func<Task>)item.Tag).Invoke().Forget();
        return item;
    }

    private async Task ExpandAsync(ContextMenu menu, MenuItem more, Func<IntPtr, ShellContextMenu> open)
    {
        var owner = new WindowInteropHelper(window).Handle;
        more.IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;
        (ShellContextMenu Shell, IReadOnlyList<ShellMenuItem> Extras, Dictionary<IntPtr, ImageSource> Icons) loaded;
        try
        {
            loaded = await ShellThread.Run(() => Load(open, owner));
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException or IOException)
        {
            return;
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        var session = new ShellSession(loaded.Shell, owner);
        if (!menu.IsOpen || loaded.Extras.Count == 0)
        {
            session.Release();
            return;
        }

        menu.Closed += (_, _) => session.Release();
        var index = menu.Items.IndexOf(more);
        menu.Items.RemoveAt(index);
        var entries = loaded.Extras.Select(e => ShellEntry(e, session, loaded.Icons)).Append(new Separator()).ToList();
        entries.ForEach(e => menu.Items.Insert(index++, e));
    }

    // Runs on the shell thread: the handlers' bitmaps are only valid there, so they are copied into frozen images before anything leaves it.
    private static (ShellContextMenu, IReadOnlyList<ShellMenuItem>, Dictionary<IntPtr, ImageSource>) Load(Func<IntPtr, ShellContextMenu> open, IntPtr owner)
    {
        var shell = open(owner);
        try
        {
            var extras = ShellMenuFilter.Extras(shell.Items);
            var icons = new Dictionary<IntPtr, ImageSource>();
            Collect(extras, icons);
            return (shell, extras, icons);
        }
        catch
        {
            shell.Dispose();
            throw;
        }
    }

    private static void Collect(IEnumerable<ShellMenuItem> items, Dictionary<IntPtr, ImageSource> icons)
    {
        foreach (var item in items)
        {
            if (item.Bitmap != IntPtr.Zero && ImageOf(item.Bitmap) is { } image)
            {
                icons[item.Bitmap] = image;
            }

            Collect(item.Children, icons);
        }
    }

    private static Control ShellEntry(ShellMenuItem shell, ShellSession session, IReadOnlyDictionary<IntPtr, ImageSource> icons)
    {
        if (shell.IsSeparator)
            return new Separator();

        var item = new MenuItem { Header = ShellMenuFilter.Header(shell.Text), IsEnabled = shell.Enabled, IsChecked = shell.Checked };
        if (icons.TryGetValue(shell.Bitmap, out var image))
        {
            item.Icon = new Image { Source = image, Width = 16, Height = 16 };
        }

        shell.Children.ToList().ForEach(c => item.Items.Add(ShellEntry(c, session, icons)));
        if (!shell.IsSubmenu)
        {
            item.Click += (_, _) => session.Invoke(shell.Id);
        }

        return item;
    }

    private static ImageSource? ImageOf(IntPtr bitmap)
    {
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException)
        {
            return null;
        }
    }

    private sealed class ShellSession(ShellContextMenu menu, IntPtr owner)
    {
        private bool _used;
        private bool _disposed;

        public void Invoke(int id)
        {
            _used = true;
            ShellThread.Run(() =>
            {
                try
                {
                    menu.Invoke(id, owner);
                }
                catch (ExternalException)
                {
                    // handlers report their own failures and cancelling a command is reported as an error too
                }
                finally
                {
                    Dispose();
                }
            }).Forget();
        }

        // The menu's Closed event comes after the click of the chosen entry, which marks the session as used.
        public void Release()
        {
            if (!_used)
            {
                ShellThread.Run(Dispose).Forget();
            }
        }

        private void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            menu.Dispose();
        }
    }
}
