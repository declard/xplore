using Xplore.Core.Model;

namespace Xplore.Core.Locations;

// What a location supports. Write: create, paste and drop into it; Bookmark: can be bookmarked; Console: a console can run in it;
// Watch: changes are watched on disk; Subfolders: folders in it can be navigated into and expanded in the tree;
// Streaming: listing it is slow, so its entries are shown as they arrive (not part of All).
[Flags]
public enum LocationCapabilities
{
    None = 0,
    Write = 1,
    Bookmark = 2,
    Console = 4,
    Watch = 8,
    Subfolders = 16,
    Streaming = 32,
    All = Write | Bookmark | Console | Watch | Subfolders,
}

// A source of entries for one kind of location. Entries carry the real path of the item they stand for, so file operations work on them directly.
public interface ILocationProvider
{
    LocationCapabilities Capabilities { get; }

    IReadOnlyList<LocationVerb> Verbs { get; }

    // The canonical location string when the text addresses a location of this provider, otherwise null.
    string? Canonical(string text);

    Task<IReadOnlyList<FsEntry>> ListAsync(string location, CancellationToken ct = default);

    IAsyncEnumerable<FsEntry> EnumerateAsync(string location, CancellationToken ct = default);

    // The entries this provider contributes to the root level "Storage".
    Task<IReadOnlyList<FsEntry>> StorageEntriesAsync(CancellationToken ct = default);
}
