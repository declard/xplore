using System.Collections.ObjectModel;
using System.Windows.Media;
using Xplore.Core.Locations;
using Xplore.Core.Model;

namespace Xplore.App.ViewModels;

// Lazily-populated tree node. Path == null with a file-system service is the "Storage" root;
// IsSection marks a non-navigable group header such as "Bookmarks".
public sealed class TreeNodeViewModel : ObservableObject
{
    private readonly IFileSystemService? _fs;
    private readonly Action<TreeNodeViewModel>? _selected;
    private Task? _load;

    public string? Path { get; }

    public string Name
    {
        get;
        private set => Set(ref field, value);
    } = "";

    public bool IsBookmark { get; }

    public bool IsRenaming
    {
        get;
        set => Set(ref field, value);
    }

    public string EditName
    {
        get;
        set => Set(ref field, value);
    } = "";

    public ImageSource? Icon { get; }

    public bool IsSection { get; }

    public bool AcceptsDrops { get; } = true;

    public double? UsedFraction { get; }

    public bool HasUsage => UsedFraction is not null;

    public bool IsNearlyFull => UsedFraction >= 0.9;

    public ObservableCollection<TreeNodeViewModel> Children { get; } = [];

    public TreeNodeViewModel(
        string? path,
        string name,
        ImageSource? icon,
        IFileSystemService fs,
        Action<TreeNodeViewModel> selected,
        bool hasChildren = true,
        double? usedFraction = null)
    {
        Path = path;
        Name = name;
        Icon = icon;
        _fs = fs;
        _selected = selected;
        UsedFraction = usedFraction;
        AcceptsDrops = path is null || fs.CapabilitiesOf(path).HasFlag(LocationCapabilities.Write);
        if (path is null || hasChildren)
        {
            Children.Add(new());
        }
    }

    private TreeNodeViewModel(string? path, string name, ImageSource? icon, bool isSection, bool isBookmark, Action<TreeNodeViewModel> selected)
    {
        Path = path;
        Name = name;
        Icon = icon;
        IsSection = isSection;
        IsBookmark = isBookmark;
        _selected = selected;
        IsExpanded = isSection;
    }

    private TreeNodeViewModel()
    {
    }

    public static TreeNodeViewModel Section(string name, Action<TreeNodeViewModel> selected) =>
        new(null, name, null, true, false, selected);

    public static TreeNodeViewModel Leaf(string path, string name, ImageSource? icon, Action<TreeNodeViewModel> selected) =>
        new(path, name, icon, false, true, selected);

    public void Rename(string name) => Name = name;

    public void BeginRename()
    {
        EditName = Name;
        IsRenaming = true;
    }

    public bool IsExpanded
    {
        get;
        set
        {
            if (!Set(ref field, value) || !value)
                return;

            EnsureLoadedAsync().Forget();
        }
    }

    public bool IsSelected
    {
        get;
        set
        {
            if (!Set(ref field, value) || !value)
                return;

            _selected?.Invoke(this);
        }
    }

    public Task EnsureLoadedAsync() => _load ??= _fs is null ? Task.CompletedTask : LoadAsync(_fs);

    public async Task<TreeNodeViewModel?> RevealAsync(string path)
    {
        IsExpanded = true;
        await EnsureLoadedAsync();
        var target = Normalize(path);
        var next = Children.FirstOrDefault(c => c.Path is not null && target.StartsWith(Normalize(c.Path), StringComparison.OrdinalIgnoreCase));
        if (next is null)
            return null;

        if (Normalize(next.Path!).Length == target.Length)
            return next;

        return await next.RevealAsync(path);
    }

    // Icons, whether a child has subfolders and a drive's usage are probed together with the listing so the UI thread never touches the disk.
    private async Task LoadAsync(IFileSystemService fs)
    {
        var isRoot = Path is null;
        try
        {
            var probed = await Task.Run(async () => (isRoot
                    ? await fs.StorageAsync()
                    : [.. new FsSorting(foldersFirst: true).Sort(await SubfoldersAsync(fs), SortColumn.Name, true)])
                .Select(e => (
                    Entry: e,
                    Icon: IconCache.For(e),
                    HasChildren: fs.CapabilitiesOf(e.Path).HasFlag(LocationCapabilities.Subfolders) && HasSubfolders(e.Path),
                    Usage: isRoot ? Usage(e.Path) : null))
                .ToList());

            Children.Clear();
            foreach (var (e, icon, hasChildren, usage) in probed)
            {
                Children.Add(new TreeNodeViewModel(e.Path, e.Name, icon, fs, _selected!, hasChildren, usage));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Children.Clear();
        }
    }

    private async Task<IReadOnlyList<FsEntry>> SubfoldersAsync(IFileSystemService fs) =>
        fs.CapabilitiesOf(Path).HasFlag(LocationCapabilities.Subfolders) ? [.. (await fs.ListAsync(Path!)).Where(e => e.IsDirectory)] : [];

    private static bool HasSubfolders(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static double? Usage(string path)
    {
        try
        {
            var drive = new DriveInfo(path);
            if (!drive.IsReady || drive.TotalSize <= 0)
                return null;

            return (double)(drive.TotalSize - drive.TotalFreeSpace) / drive.TotalSize;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            return null;
        }
    }

    private static string Normalize(string p) => p.TrimEnd('\\') + "\\";
}
