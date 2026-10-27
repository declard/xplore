using System.Collections.ObjectModel;
using System.Windows.Input;
using Xplore.Core.History;
using Xplore.Core.Locations;
using Xplore.Core.Model;
using Xplore.Core.Operations;
using Xplore.Core.Settings;
using Xplore.Core.Shell;

namespace Xplore.App.ViewModels;

// What a file-actions component needs to know about the folder it works in.
public interface IFolderContext
{
    string? CurrentPath { get; }
    bool ItemsEditable { get; }
    IReadOnlyList<LocationVerb> Verbs { get; }
    IReadOnlyList<ItemViewModel> Selection { get; }
    ObservableCollection<ItemViewModel> Items { get; }

    Task RefreshAsync(IEnumerable<string>? select = null);
}

// Clipboard, create, rename, delete and drop operations on the current folder's items; every change is recorded in the action history.
public sealed class FileActions
{
    private readonly IFolderContext _folder;
    private readonly IUserPrompts _prompts;
    private readonly ActionHistoryStore _actions;
    private readonly OperationRunner _runner;
    private readonly ISettings _settings;
    private readonly PathFormat _format;

    public FileActions(IFolderContext folder, IUserPrompts prompts, ActionHistoryStore actions, OperationRunner runner, ISettings settings, PathFormat format)
    {
        _folder = folder;
        _prompts = prompts;
        _actions = actions;
        _runner = runner;
        _settings = settings;
        _format = format;
        CopyLocationCommand = new AsyncRelayCommand(() => ClipboardHelper.SetTextAsync(Quote(_folder.CurrentPath!)), () => _folder.CurrentPath is not null);
        CutCommand = new AsyncRelayCommand(() => ClipboardHelper.SetFilesAsync(SelectedPaths, cut: true), () => HasSelection && _folder.ItemsEditable);
        CopyCommand = new AsyncRelayCommand(() => ClipboardHelper.SetFilesAsync(SelectedPaths, cut: false), () => HasSelection && _folder.ItemsEditable);
        CopyPathCommand = new AsyncRelayCommand(() => ClipboardHelper.SetTextAsync(string.Join(Environment.NewLine, SelectedPaths.Select(Quote))), () => HasSelection);
        PasteCommand = new AsyncRelayCommand(Paste, () => _folder.CurrentPath is not null && ClipboardHelper.HasFiles);
        PasteShortcutCommand = new AsyncRelayCommand(PasteShortcut, () => _folder.CurrentPath is not null && ClipboardHelper.HasFiles);
        CreateShortcutCommand = new AsyncRelayCommand(
            () => _runner.RunAsync(async () => await _folder.RefreshAsync(await MakeShortcuts(SelectedPaths))),
            () => _folder.CurrentPath is not null && HasSelection);
        DeleteCommand = new AsyncRelayCommand(() => Delete(permanent: false), () => HasSelection);
        PermanentDeleteCommand = new AsyncRelayCommand(() => Delete(permanent: true), () => HasSelection);
        RenameCommand = new RelayCommand(() => BeginRename(_folder.Selection[0]), () => _folder.Selection is [_] && _folder.ItemsEditable);
        NewFolderCommand = new AsyncRelayCommand(() => NewItem(isFolder: true), () => _folder.CurrentPath is not null);
        NewFileCommand = new AsyncRelayCommand(() => NewItem(isFolder: false), () => _folder.CurrentPath is not null);
        PropertiesCommand = new AsyncRelayCommand(
            () => _runner.Run(() => ShellVerbs.Properties(HasSelection ? SelectedPaths : [_folder.CurrentPath!])),
            () => HasSelection || _folder.CurrentPath is not null);
    }

    public event Action<ItemViewModel>? RenameStarted;

    public ICommand CopyLocationCommand { get; }
    public ICommand CutCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand CopyPathCommand { get; }
    public ICommand PasteCommand { get; }
    public ICommand PasteShortcutCommand { get; }
    public ICommand CreateShortcutCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand PermanentDeleteCommand { get; }
    public ICommand RenameCommand { get; }
    public ICommand NewFolderCommand { get; }
    public ICommand NewFileCommand { get; }
    public ICommand PropertiesCommand { get; }

    private bool HasSelection => _folder.Selection is not [];

    private IReadOnlyList<string> SelectedPaths => [.. _folder.Selection.Select(i => i.Entry.Path)];

    public static void CancelRename(ItemViewModel item) => item.IsRenaming = false;

    public ICommand CommandFor(LocationVerb verb) =>
        new AsyncRelayCommand(() => RunVerb(verb), () => verb.Role == VerbRole.Location || HasSelection);

    public Task DropAsync(IEnumerable<string> paths, string destDir, bool move)
    {
        var sources = paths.ToList();
        return _runner.RunAsync(async () => await _folder.RefreshAsync(await TransferAsync(sources, destDir, move)));
    }

    public Task SendToAsync(SendToTarget target)
    {
        var paths = SelectedPaths.ToList();
        return _runner.RunAsync(() => ShellVerbs.SendToAsync(target, paths));
    }

    public Task CommitRenameAsync(ItemViewModel item, string newName)
    {
        if (!item.IsRenaming)
            return Task.CompletedTask;

        item.IsRenaming = false;
        if (newName == item.Name)
            return Task.CompletedTask;

        return _runner.RunAsync(async () =>
        {
            var renamed = await FileOperations.RenameAsync(item.Entry.Path, newName);
            if (renamed != item.Entry.Path)
            {
                _actions.Record(new RenameAction(item.Entry.Path, renamed));
            }

            await _folder.RefreshAsync([renamed]);
        });
    }

    public void BeginRename(ItemViewModel item)
    {
        item.IsRenaming = true;
        RenameStarted?.Invoke(item);
    }

    private string Quote(string path) => $"\"{_format.Show(path)}\"";

    private Task Paste() =>
        _runner.RunAsync(async () =>
        {
            var (paths, cut) = await ClipboardHelper.GetFilesAsync();
            var result = await TransferAsync(paths, _folder.CurrentPath!, cut);
            if (cut)
            {
                await ClipboardHelper.ClearAsync();
            }

            await _folder.RefreshAsync(result);
        });

    private Task PasteShortcut() =>
        _runner.RunAsync(async () => await _folder.RefreshAsync(await MakeShortcuts((await ClipboardHelper.GetFilesAsync()).Paths)));

    private async Task RunVerb(LocationVerb verb)
    {
        IReadOnlyList<FsEntry> entries = verb.Role == VerbRole.Location ? [] : [.. _folder.Selection.Select(i => i.Entry)];
        if (verb.Confirm?.Invoke(entries) is { } question && !await _prompts.ConfirmAsync(question))
            return;

        await _runner.RunAsync(async () =>
        {
            await verb.Run(entries);
            await _folder.RefreshAsync();
        });
    }

    private async Task Delete(bool permanent)
    {
        if (_folder.Verbs.FirstOrDefault(v => v.Role == VerbRole.Delete) is { } verb)
        {
            await RunVerb(verb);
            return;
        }

        var items = _folder.Selection.ToList();
        if (items.Count == 0 || !await _prompts.ConfirmDeleteAsync(items, permanent))
            return;

        var paths = items.Select(i => i.Entry.Path).ToList();
        await _runner.RunAsync(async () =>
        {
            await FileOperations.DeleteAsync(paths, permanent);
            _actions.Record(new DeleteAction(paths, items.Select(i => i.Entry.IsDirectory).ToList(), permanent));
            await _folder.RefreshAsync();
        });
    }

    private async Task<IReadOnlyList<string>> TransferAsync(IEnumerable<string> paths, string destDir, bool move)
    {
        var pairs = await FileOperations.TransferPairsAsync(paths, destDir, move);
        var done = pairs.Where(p => p.From != p.To).ToList();
        if (done.Count > 0)
        {
            _actions.Record(move ? new MoveAction(done) : new CopyAction(done));
        }

        return [.. pairs.Select(p => p.To)];
    }

    private async Task<IReadOnlyList<string>> MakeShortcuts(IReadOnlyList<string> targets)
    {
        var links = await FileOperations.CreateShortcutsAsync(targets, _folder.CurrentPath!);
        _actions.Record(new ShortcutAction(links.Zip(targets, (l, t) => (l, t)).ToList()));
        return links;
    }

    private async Task NewItem(bool isFolder)
    {
        if (!_settings.Current.NewItemPrompt)
        {
            await CreateNew(isFolder ? FileOperations.CreateFolderAsync : FileOperations.CreateFileAsync, isFolder);
            return;
        }

        if (await _prompts.AskNewItemNameAsync(_folder.CurrentPath!, isFolder) is not { } name)
            return;

        await _runner.RunAsync(async () =>
        {
            var path = await (isFolder
                ? FileOperations.CreateFolderAsync(_folder.CurrentPath!, name)
                : FileOperations.CreateFileAsync(_folder.CurrentPath!, name));
            _actions.Record(new CreateAction(path, isFolder));
            await _folder.RefreshAsync([path]);
        });
    }

    private Task CreateNew(Func<string, Task<string>> create, bool isFolder) =>
        _runner.RunAsync(async () =>
        {
            var path = await create(_folder.CurrentPath!);
            _actions.Record(new CreateAction(path, isFolder));
            await _folder.RefreshAsync([path]);
            if (_folder.Items.FirstOrDefault(i => i.Entry.Path == path) is not { } item)
                return;

            BeginRename(item);
        });
}
