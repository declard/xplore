using Xplore.Core.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Xplore.App.ViewModels;
using Xplore.Core.Locations;
using Xplore.Core.Settings;
using Xplore.Core.Shell;

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
        sendTo.IsEnabled = sendTo.Items.Count > 0;

        var menu = new ContextMenu();
        Control[] entries =
        [
            Entry(Loc.Get("Menu.Open"), tab.OpenSelectedCommand), Entry(Loc.Get("Menu.OpenWith"), tab.OpenWith.DialogCommand),
            .. quickApps.Select(app => QuickOpenEntry(tab, app)), new Separator(),
            Entry(Loc.Get("Menu.Cut"), tab.Files.CutCommand, "Ctrl+X"), Entry(Loc.Get("Menu.Copy"), tab.Files.CopyCommand, "Ctrl+C"),
            Entry(Loc.Get("Menu.CopyPath"), tab.Files.CopyPathCommand), Entry(Loc.Get("Menu.CreateShortcut"), tab.Files.CreateShortcutCommand), new Separator(),
            sendTo, Entry(Loc.Get("Common.Delete"), tab.Files.DeleteCommand, "Del"), Entry(Loc.Get("Menu.Rename"), tab.Files.RenameCommand, "F2"), new Separator(),
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
            Entry(Loc.Get("Menu.Properties"), tab.Files.PropertiesCommand),
        }.ToList().ForEach(c => menu.Items.Add(c));
        return menu;
    }
}
