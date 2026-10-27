#pragma warning disable S2971

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Xplore.Core.Settings;

namespace Xplore.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly ActionHistoryStore _history;
    private readonly TabFactory _tabs;
    private readonly SessionStore _sessions;

    public MainViewModel(ISettings settings, PathFormat format, ActionHistoryStore history, TabFactory tabs, SessionStore sessions, StartTabs start)
    {
        Settings = settings;
        PathFormat = format;
        _history = history;
        _tabs = tabs;
        _sessions = sessions;
        _history.FilesChanged = ReloadAllTabsAsync;
        settings.Changed += _ => ReloadAllTabsAsync().Forget();
        UndoCommand = new AsyncRelayCommand(_history.UndoAsync);
        RedoCommand = new AsyncRelayCommand(_history.RedoAsync);
        NewTabCommand = new RelayCommand(() => NewTab());
        CloseTabCommand = new RelayCommand(p => CloseTab((TabViewModel)p!));
        start.Locations.ToList().ForEach(l => NewTab(l));
        SelectedTab = Tabs[start.Selected];
    }

    public ISettings Settings { get; }
    public PathFormat PathFormat { get; }
    public ObservableCollection<TabViewModel> Tabs { get; } = [];
    public ActionHistoryStore History => _history;
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }
    public ICommand NewTabCommand { get; }
    public ICommand CloseTabCommand { get; }

    public TabViewModel? SelectedTab
    {
        get;
        set => Set(ref field, value);
    }

    public Task ReloadAllTabsAsync() => Task.WhenAll(Tabs.ToList().Select(t => t.ReloadAsync()));

    // The folders of the active tab and of the next tab (if any) as the default roots of the sync wizard.
    public (string Left, string Right) SyncRoots()
    {
        static string Folder(TabViewModel? t) => t?.CurrentPath is { } p && Directory.Exists(p) ? p : "";
        return (Folder(SelectedTab), Tabs.Count > 1 ? Folder(Tabs[(Tabs.IndexOf(SelectedTab!) + 1) % Tabs.Count]) : "");
    }

    public async Task SaveSessionAsync()
    {
        if (Settings.Current.StartLocation?.Type != StartMode.Restore)
            return;

        await _sessions.SaveAsync(Session.Of(Tabs.Select(t => t.CurrentPath), Tabs.IndexOf(SelectedTab!)));
    }

    private void NewTab(string? location = null, bool select = true)
    {
        var tab = _tabs.Create(location);
        Tabs.Add(tab);
        if (select)
        {
            SelectedTab = tab;
        }
    }

    // Like the "Open in new tab" of Windows Explorer: the new tab opens in the background.
    public void OpenInNewTab(string path) => NewTab(path, select: false);

    private void CloseTab(TabViewModel tab)
    {
        if (Tabs.Count == 1)
        {
            Application.Current.MainWindow.Close();
            return;
        }

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        tab.Dispose();
        if (SelectedTab == tab || SelectedTab is null)
        {
            SelectedTab = Tabs[Math.Min(index, Tabs.Count - 1)];
        }
    }
}
