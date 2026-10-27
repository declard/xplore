#pragma warning disable S2971
#pragma warning disable S4036

using Xplore.Core.Localization;
using Xplore.Core.Locations;
using Xplore.Core.Model;
using Xplore.Core.Navigation;
using Xplore.Core.Settings;
using Xplore.Core.Shell;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Windows.Input;
using System.Windows.Media;

namespace Xplore.App.ViewModels;

public sealed record Crumb(string Name, string? Path);

public sealed class TabViewModel : ObservableObject, IFolderContext, IDisposable
{
    private sealed record ShowRequest(NavigationHistory Target, IReadOnlyList<string>? Select)
    {
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public static string RootName => Loc.Get("Root.Name");

    // The name of the root as typed into the address bar in every language and stored in the session.
    public const string RootId = "Storage";

    private readonly IFileSystemService _fs;
    private readonly IUserPrompts _prompts;
    private readonly BookmarkStore _bookmarks;
    private readonly OperationRunner _runner;
    private readonly FolderWatcher _watcher = new();
    private NavigationHistory _history = NavigationHistory.Start();
    private List<FsEntry> _entries = [];
    private SortColumn _sortColumn = SortColumn.Name;
    private bool _sortAscending = true;
    private readonly ISettings _settings;
    private readonly TimestampFormat _time;
    private IReadOnlyList<ItemViewModel> _selection = [];
    private readonly Subject<ShowRequest> _requests = new();
    private readonly IDisposable _showing;
    private int _loading;

    public TabViewModel(
        IFileSystemService fs,
        IUserPrompts prompts,
        BookmarkStore bookmarks,
        ActionHistoryStore actions,
        OpenWithStore openWith,
        ISettings settings,
        PathFormat format,
        TimestampFormat time,
        string? location = null)
    {
        _fs = fs;
        _prompts = prompts;
        _bookmarks = bookmarks;
        _settings = settings;
        _time = time;
        _runner = new OperationRunner(prompts);
        Terminal = new ConsoleViewModel(format, () => PathWith(LocationCapabilities.Console), () => SelectedPaths);
        Tree = new FolderTree(fs, bookmarks, () => CurrentPath, NavigateToAsync);
        Address = new AddressBar(fs, prompts, format, () => CurrentPath, NavigateToAsync);
        Search = new FolderSearch(fs, prompts, () => CurrentPath, () => ShowAsync(_history));
        Files = new FileActions(this, prompts, actions, _runner, settings, format);
        OpenWith = new OpenWithService(prompts, openWith, _runner, () => _selection);
        _watcher.Elapsed += () => RefreshFromWatcherAsync().Forget();
        Search.Started += OnSearchStarted;
        Search.Found += OnSearchFound;
        Search.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FolderSearch.IsRunning))
            {
                OnPropertyChanged(nameof(IsBusy));
            }
        };
        _bookmarks.Changed += NotifyBookmarkState;
        _showing = _requests
            .Select(request => Observable.FromAsync(ct => ShowCoreAsync(request, ct))
                .Catch((Exception ex) =>
                {
                    request.Done.TrySetException(ex);
                    return Observable.Empty<Unit>();
                })
                .Finally(() => request.Done.TrySetResult()))
            .Switch()
            .Subscribe();

        BackCommand = new AsyncRelayCommand(() => ShowAsync(_history.Back()), () => _history.CanGoBack);
        ForwardCommand = new AsyncRelayCommand(() => ShowAsync(_history.Forward()), () => _history.CanGoForward);
        UpCommand = new AsyncRelayCommand(() => ShowAsync(_history.Up()), () => _history.CanGoUp);
        RefreshCommand = new AsyncRelayCommand(ReloadAsync);
        ToggleBookmarkCommand = new AsyncRelayCommand(() => _bookmarks.ToggleAsync(CurrentPath!), () => PathWith(LocationCapabilities.Bookmark) is not null);
        OpenCmdCommand = new AsyncRelayCommand(OpenCmd, () => PathWith(LocationCapabilities.Console) is not null);
        OpenSelectedCommand = new AsyncRelayCommand(OpenSelectedAsync, () => HasSelection);
        ShowAsync(location is null ? _history : _history.Navigate(location)).Forget();
    }

    private bool HasSelection => _selection is not [];

    public ConsoleViewModel Terminal { get; }
    public FolderTree Tree { get; }
    public AddressBar Address { get; }
    public FolderSearch Search { get; }
    public FileActions Files { get; }
    public OpenWithService OpenWith { get; }
    public ObservableCollection<ItemViewModel> Items { get; } = [];

    public ICommand BackCommand { get; }
    public ICommand ForwardCommand { get; }
    public ICommand UpCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ToggleBookmarkCommand { get; }
    public ICommand OpenCmdCommand { get; }
    public ICommand OpenSelectedCommand { get; }

    public bool IsLoading
    {
        get;
        private set
        {
            if (Set(ref field, value))
            {
                OnPropertyChanged(nameof(IsBusy));
            }
        }
    }

    // A location is loading or a search is still collecting results.
    public bool IsBusy => IsLoading || Search.IsRunning;

    public string? CurrentPath => _history.Current;

    string? IFolderContext.CurrentPath => PathWith(LocationCapabilities.Write);

    public IReadOnlyList<LocationVerb> Verbs => _fs.VerbsOf(CurrentPath);

    // Items can be cut, renamed and deleted; false where the location only offers its own verbs, as the Recycle Bin does.
    public bool ItemsEditable => CurrentPath is not { } path || _fs.CapabilitiesOf(path).HasFlag(LocationCapabilities.Write);

    public bool CanDropOnItems => CurrentPath is not { } path || _fs.CapabilitiesOf(path).HasFlag(LocationCapabilities.Subfolders);

    // The current location when it supports the capability; null at Storage and where the provider does not offer it.
    public string? PathWith(LocationCapabilities capability) =>
        CurrentPath is { } path && _fs.CapabilitiesOf(path).HasFlag(capability) ? path : null;
    public SortColumn SortColumn => _sortColumn;
    public bool SortAscending => _sortAscending;
    public ImageSource? FolderIcon => IconCache.Folder;
    public IReadOnlyList<string> SelectedPaths => _selection.Select(i => i.Entry.Path).ToList();
    public IReadOnlyList<Crumb> BackList => _history.BackStack.Select(ToCrumb).ToList();
    public IReadOnlyList<Crumb> ForwardList => _history.ForwardStack.Select(ToCrumb).ToList();

    public bool IsBookmarked => CurrentPath is { } p && _bookmarks.Contains(p);
    public string BookmarkGlyph => IsBookmarked ? "" : "";
    public string BookmarkTooltip => IsBookmarked ? Loc.Get("Bookmark.Remove") : Loc.Get("Bookmark.Add");

    public string SearchPlaceholder => Loc.Format("Main.Search.Placeholder", Title);

    public string Title => CurrentPath switch
    {
        null => RootName,
        RecycleBin.Location => RecycleBin.DisplayName,
        var p when Path.GetFileName(p.TrimEnd('\\')) is { Length: > 0 } n => n,
        var p => p,
    };

    public string StatusText => Loc.Format((Search.IsSearching, Items.Count) switch
    {
        (true, 1) => "Status.Found.One",
        (true, _) => "Status.Found.Many",
        (_, 1) => "Status.Items.One",
        _ => "Status.Items.Many",
    }, Items.Count);

    public string SelectionText => _selection.Count switch
    {
        0 => "",
        var n => Loc.Format(n == 1 ? "Status.Selected.One" : "Status.Selected.Many", n) +
                 (_selection.All(i => i.Entry.Size is not null) ? Loc.Format("Status.Bytes", _selection.Sum(i => i.Entry.Size!.Value)) : ""),
    };

    IReadOnlyList<ItemViewModel> IFolderContext.Selection => _selection;

    public void SetSelection(IEnumerable<ItemViewModel> selection)
    {
        _selection = [.. selection];
        OnPropertyChanged(nameof(SelectionText));
    }

    public Task NavigateToAsync(string? path) => ShowAsync(_history.Navigate(path));

    public Task ReloadAsync() => ShowAsync(_history, SelectedPaths);

    Task IFolderContext.RefreshAsync(IEnumerable<string>? select) => ShowAsync(_history, select);

    public Task JumpBackAsync(int steps) => ShowAsync(_history.BackBy(steps));

    public Task JumpForwardAsync(int steps) => ShowAsync(_history.ForwardBy(steps));

    public void SortBy(SortColumn column)
    {
        _sortAscending = column != _sortColumn || !_sortAscending;
        _sortColumn = column;
        Rebuild(SelectedPaths);
        OnPropertyChanged(nameof(SortColumn));
        OnPropertyChanged(nameof(SortAscending));
    }

    public async Task OpenAsync(ItemViewModel item)
    {
        var path = item.Entry.Path;
        if (item.Entry.IsDirectory && CurrentPath is { } here && !_fs.CapabilitiesOf(here).HasFlag(LocationCapabilities.Subfolders))
            return;

        if ((item.Entry.IsDirectory ? path : await FolderShortcutTarget(path)) is { } folder)
        {
            await NavigateToAsync(folder);
            return;
        }

        await _runner.Run(() => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }));
    }

    public async Task<IReadOnlyList<FsEntry>> SubfoldersAsync(string? path)
    {
        try
        {
            if (path is null)
                return await _fs.StorageAsync();

            var folders = (await _fs.ListAsync(path)).Where(e => e.IsDirectory);
            return [.. new FsSorting(foldersFirst: true).Sort(folders, SortColumn.Name, true)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Dispose()
    {
        _bookmarks.Changed -= NotifyBookmarkState;
        _watcher.Dispose();
        Search.Dispose();
        Tree.Dispose();
        _showing.Dispose();
        Terminal.Dispose();
    }

    private static async Task<string?> FolderShortcutTarget(string path) =>
        path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && (await Shortcuts.ResolveAsync(path))?.Target is { } t && await Task.Run(() => Directory.Exists(t)) ? t : null;

    private static Crumb ToCrumb(string? path) => path switch
    {
        null => new(RootName, null),
        RecycleBin.Location => new(RecycleBin.DisplayName, RecycleBin.Location),
        var p when Path.GetFileName(p.TrimEnd('\\')) is { Length: > 0 } n => new(n, p),
        var p => new(p, p),
    };

    private void NotifyBookmarkState()
    {
        OnPropertyChanged(nameof(IsBookmarked));
        OnPropertyChanged(nameof(BookmarkGlyph));
        OnPropertyChanged(nameof(BookmarkTooltip));
    }

    private void OnSearchStarted()
    {
        _entries = [];
        Items.Clear();
    }

    private void OnSearchFound(FsEntry entry)
    {
        _entries.Add(entry);
        Items.Add(new ItemViewModel(entry, _time));
        OnPropertyChanged(nameof(StatusText));
    }

    private Task OpenSelectedAsync() =>
        _selection is [var single]
            ? OpenAsync(single)
            : Task.WhenAll(_selection.Where(i => !i.Entry.IsDirectory).ToList().Select(OpenAsync));

    private Task OpenCmd() =>
        _runner.Run(() => Process.Start(new ProcessStartInfo("cmd.exe") { WorkingDirectory = CurrentPath!, UseShellExecute = true }));

    // The latest request wins: a new request unsubscribes from the load still in flight, which cancels it, and a request that was superseded completes its task at once.
    private Task ShowAsync(NavigationHistory target, IEnumerable<string>? select = null)
    {
        var request = new ShowRequest(target, select?.ToList());
        _requests.OnNext(request);
        return request.Done.Task;
    }

    // A superseded request finishes after its successor has started, so loads are counted rather than flagged.
    private async Task ShowCoreAsync(ShowRequest request, CancellationToken ct)
    {
        IsLoading = ++_loading > 0;
        try
        {
            await ShowLoadedAsync(request, ct);
        }
        finally
        {
            IsLoading = --_loading > 0;
        }
    }

    private async Task ShowLoadedAsync(ShowRequest request, CancellationToken ct)
    {
        var target = request.Target;
        var streamed = target.Current is { } location && _fs.CapabilitiesOf(location).HasFlag(LocationCapabilities.Streaming);
        if (await LoadSnapshot(target, streamed, ct) is not (var entries, var crumbs))
        {
            return;
        }

        ct.ThrowIfCancellationRequested();
        await Search.ResetAsync();
        var changedFolder = target.Current != CurrentPath;
        _history = target;
        _entries = entries;
        Address.Reset(target.Current);
        Rebuild(request.Select ?? []);
        if (changedFolder || !_watcher.IsWatching)
        {
            _watcher.Watch(target.Current is { } current && _fs.CapabilitiesOf(current).HasFlag(LocationCapabilities.Watch) ? current : null);
        }

        Address.Breadcrumbs = crumbs;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SearchPlaceholder));
        OnPropertyChanged(nameof(CurrentPath));
        Terminal.NotifyLocationChanged();
        OnPropertyChanged(nameof(BackList));
        OnPropertyChanged(nameof(ForwardList));
        NotifyBookmarkState();
        await Tree.RevealAsync(target.Current, ct);
        if (streamed)
        {
            await StreamAsync(target.Current!, request.Select ?? [], ct);
        }
    }

    // Entries of a slow location are shown as they arrive and sorted once the listing is complete.
    private async Task StreamAsync(string location, IEnumerable<string> select, CancellationToken ct)
    {
        var selected = select.ToHashSet(StringComparer.OrdinalIgnoreCase);
        await foreach (var e in _fs.EnumerateAsync(location, ct))
        {
            _entries.Add(e);
            Items.Add(new ItemViewModel(e, _time) { IsSelected = selected.Contains(e.Path) });
            OnPropertyChanged(nameof(StatusText));
        }

        Rebuild(selected);
    }

    private async Task<(List<FsEntry> entries, IReadOnlyList<Crumb> crumbs)?> LoadSnapshot(NavigationHistory target, bool streamed, CancellationToken ct)
    {
        try
        {
            var entries = await LoadEntries(target, streamed, ct);
            var crumbs = await Address.BuildCrumbsAsync(target.Current, ct);
            return (entries, crumbs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ct.ThrowIfCancellationRequested();
            await _prompts.ReportErrorAsync(ex.Message);
            Address.Reset(CurrentPath);
            return null;
        }
    }

    private async Task<List<FsEntry>> LoadEntries(
        NavigationHistory target,
        bool streamed,
        CancellationToken ct)
    {
        if (streamed)
            return [];
        
        var list = target.Current is null
            ? await _fs.StorageAsync(ct)
            : await _fs.ListAsync(target.Current, ct);
        
        return [.. list];
    }

    private void Rebuild(IEnumerable<string> select)
    {
        var selected = select.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Items.Clear();
        var ordered = CurrentPath is null ? _entries : new FsSorting(_settings.Current.FoldersFirst).Sort(_entries, _sortColumn, _sortAscending);
        foreach (var e in ordered)
        {
            Items.Add(new ItemViewModel(e, _time) { IsSelected = selected.Contains(e.Path) });
        }

        OnPropertyChanged(nameof(StatusText));
    }

    private async Task RefreshFromWatcherAsync()
    {
        if (Search.IsSearching || CurrentPath is not { } path)
            return;

        if (Items.Any(i => i.IsRenaming))
        {
            _watcher.Restart();
            return;
        }

        if (!await Task.Run(() => Directory.Exists(path)))
        {
            await ShowAsync(_history.Up());
            return;
        }

        await ReloadAsync();
    }
}
