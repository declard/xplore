namespace Xplore.Core.Storage;

// An item in a file store. ModifiedUtc: last write time (null when unknown); Size: bytes, 0 for folders;
// IsLink: a reparse point, which is not followed; IsHidden: has the Hidden attribute.
public sealed record StoreEntry(string Path, string Name, bool IsDirectory, DateTime? ModifiedUtc, long Size, bool IsLink = false, bool IsHidden = false);

// What a store offers beyond the basics. RealPath: paths work with the shell and other programs (shortcuts, properties, "Open with", console);
// RecycleBin: deleted items can go to the Recycle Bin; Timestamps: the last write time of an item can be set.
[Flags]
public enum StoreCapabilities
{
    None = 0,
    RealPath = 1,
    RecycleBin = 2,
    Timestamps = 4,
}

// One kind of storage addressed by path. Failures are reported as IOException or UnauthorizedAccessException.
public interface IFileStore
{
    StoreCapabilities Capabilities { get; }

    bool Handles(string path);

    // Null when nothing exists at the path.
    StoreEntry? Info(string path);

    IEnumerable<StoreEntry> Children(string directory);

    // Whether a folder holds folders; a store that cannot tell cheaply says yes.
    bool HasSubfolders(string directory);

    Stream OpenRead(string path);

    void Write(string path, Stream content, bool overwrite);

    // Creates the missing parent folders too.
    void CreateDirectory(string path);

    // A folder goes with its content; recycle is honoured only by a store with a Recycle Bin.
    void Delete(string path, bool recycle);

    // Returns the new path.
    string Rename(string path, string newName);

    // Does nothing where timestamps cannot be set.
    void SetModified(string path, DateTime utc);

    // False when the store cannot do it itself and the item has to be copied and deleted.
    bool TryMove(string from, string to, bool overwrite);

    bool TryCopyFile(string from, string to, bool overwrite);

    // A copy or move the store does with its own user interface (progress, conflict dialogs); false when it cannot do it.
    bool TryTransfer(string from, string to, bool move, bool showDialogs);

    // The containing folder; null at the top of the store.
    string? Parent(string path);

    string Relative(string root, string path);

    // A path of a file that other programs can open; the file is copied out first where the store has no real paths.
    string ToLocalFile(string path);
}
