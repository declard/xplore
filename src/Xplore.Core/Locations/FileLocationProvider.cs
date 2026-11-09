using System.Runtime.CompilerServices;
using Xplore.Core.Localization;
using Xplore.Core.Model;

namespace Xplore.Core.Locations;

// Folders on disk, including UNC paths such as \\wsl.localhost\Ubuntu; its Storage entries are the local drives.
public sealed class FileLocationProvider : ILocationProvider
{
    private static readonly EnumerationOptions Flat = new() { IgnoreInaccessible = true, AttributesToSkip = 0 };

    public LocationCapabilities Capabilities => LocationCapabilities.All;

    public IReadOnlyList<LocationVerb> Verbs => [];

    public string? Canonical(string text) => null;

    public Task<IReadOnlyList<FsEntry>> ListAsync(string location, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<FsEntry>>(() => new DirectoryInfo(location).EnumerateFileSystemInfos("*", Flat).Select(ToEntry).ToList(), ct);

    public async IAsyncEnumerable<FsEntry> EnumerateAsync(string location, [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var entry in await ListAsync(location, ct))
        {
            yield return entry;
        }
    }

    public async Task<IReadOnlyList<Breadcrumb>> BreadcrumbsAsync(string location, CancellationToken ct = default)
    {
        var root = Path.GetPathRoot(location)!;
        var rootName = (await DrivesAsync(ct)).FirstOrDefault(d => string.Equals(d.Path, root, StringComparison.OrdinalIgnoreCase))?.Name ?? root;
        var segments = location[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return
        [
            new(rootName, root),
            .. segments.Select((s, i) => new Breadcrumb(s, Path.Combine(root, string.Join('\\', segments.Take(i + 1))))),
        ];
    }

    public async Task<IReadOnlyList<FsEntry>> StorageEntriesAsync(CancellationToken ct = default) =>
        [Desktop(), .. await DrivesAsync(ct)];

    public Task<IReadOnlyList<FsEntry>> DrivesAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<FsEntry>>(() =>
            DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Select(d => new FsEntry(d.Name, DriveLabel(d), true, null, null, Loc.Get("Type.LocalDisk")))
                .ToList(), ct);

    private static FsEntry Desktop() =>
        new(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Loc.Get("Desktop.Name"), true, null, null, Loc.Get("Type.FileFolder"));

    public static FsEntry ToEntry(FileSystemInfo i) => i switch
    {
        DirectoryInfo d => new(d.FullName, d.Name, true, d.LastWriteTime, null, Loc.Get("Type.FileFolder"), IsHidden(d)),
        FileInfo f => new(f.FullName, f.Name, false, f.LastWriteTime, f.Length, Shell.ShellInfo.TypeName(f.Name), IsHidden(f)),
        _ => throw new NotSupportedException(),
    };

    private static bool IsHidden(FileSystemInfo i) => i.Attributes.HasFlag(FileAttributes.Hidden);

    private static string DriveLabel(DriveInfo d) =>
        $"{(string.IsNullOrEmpty(d.VolumeLabel) ? Loc.Get("Type.LocalDisk") : d.VolumeLabel)} ({d.Name.TrimEnd('\\')})";
}
