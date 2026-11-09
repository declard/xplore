using Xplore.Core.Localization;

namespace Xplore.Core.Storage;

// The one entry point to files and folders: every path goes to the store that owns it, so the callers do not tell disks and phones apart.
public static class Stores
{
    // The first store that handles a path owns it; the local store takes the rest.
    public static IReadOnlyList<IFileStore> All { get; } = [new PhoneStore(), new LocalStore()];

    public static IFileStore For(string path) => All.First(s => s.Handles(path));

    public static bool Has(string path, StoreCapabilities capability) => For(path).Capabilities.HasFlag(capability);

    public static StoreEntry? Info(string path) => For(path).Info(path);

    // A location that cannot be reached does not exist.
    public static bool Exists(string path) => Probe(path) is not null;

    public static bool IsDirectory(string path) => Probe(path) is { IsDirectory: true };

    public static IEnumerable<StoreEntry> Children(string directory) => For(directory).Children(directory);

    public static bool HasSubfolders(string directory) => For(directory).HasSubfolders(directory);

    public static Stream OpenRead(string path) => For(path).OpenRead(path);

    public static string? Parent(string path) => For(path).Parent(path);

    public static string Relative(string root, string path) => For(root).Relative(root, path);

    public static string ToLocalFile(string path) => For(path).ToLocalFile(path);

    public static void CreateDirectory(string path) => For(path).CreateDirectory(path);

    public static void CreateFile(string path) => For(path).Write(path, Stream.Null, overwrite: false);

    public static string Rename(string path, string newName) => For(path).Rename(path, newName);

    public static void Delete(string path, bool recycle = false) => For(path).Delete(path, recycle);

    // Copies a file or a folder with its content, keeping the last write times where the target can store them.
    // afterFile runs for each copied file with the source entry and the target path.
    public static void Copy(string from, string to, bool overwrite, Action<StoreEntry, string>? afterFile = null)
    {
        var source = Info(from) ?? throw new FileNotFoundException(Loc.Format("Error.NoLongerExists", Path.GetFileName(from)));
        EnsureParent(to);
        CopyEntry(source, to, overwrite, afterFile);
    }

    public static void Move(string from, string to, bool overwrite, Action<StoreEntry, string>? afterFile = null)
    {
        EnsureParent(to);
        if (ReferenceEquals(For(from), For(to)) && For(from).TryMove(from, to, overwrite))
            return;

        Copy(from, to, overwrite, afterFile);
        Delete(from);
    }

    // A copy or move for the user: the store's own dialogs where it has them, and an existing target is never overwritten.
    public static void Transfer(string from, string to, bool move, bool showDialogs)
    {
        if (For(from).TryTransfer(from, to, move, showDialogs))
            return;

        if (Exists(to))
            throw new IOException(Loc.Format("Error.AlreadyExists", Path.GetFileName(to)));

        if (move)
        {
            Move(from, to, overwrite: false);
            return;
        }

        Copy(from, to, overwrite: false);
    }

    private static StoreEntry? Probe(string path)
    {
        try
        {
            return Info(path);
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void EnsureParent(string path)
    {
        if (Parent(path) is not { Length: > 0 } parent)
            return;

        CreateDirectory(parent);
    }

    private static void CopyEntry(StoreEntry source, string to, bool overwrite, Action<StoreEntry, string>? afterFile)
    {
        var target = For(to);
        if (source.IsDirectory)
        {
            target.CreateDirectory(to);
            foreach (var child in Children(source.Path).Where(c => !c.IsLink).ToList())
            {
                CopyEntry(child, Path.Combine(to, child.Name), overwrite, afterFile);
            }

            SetModified(target, to, source);
            return;
        }

        if (!For(source.Path).TryCopyFile(source.Path, to, overwrite))
        {
            using var input = OpenRead(source.Path);
            target.Write(to, input, overwrite);
            SetModified(target, to, source);
        }

        afterFile?.Invoke(source, to);
    }

    private static void SetModified(IFileStore target, string path, StoreEntry source)
    {
        if (source.ModifiedUtc is not { } modified)
            return;

        target.SetModified(path, modified);
    }
}
