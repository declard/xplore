using Xplore.Core.Localization;
using Xplore.Core.Shell;
using Xplore.Core.Storage;

namespace Xplore.Core.Operations;

public static class FileOperations
{
    private static string InvalidNameMessage => Loc.Get("Error.InvalidName");

    public static string UniquePath(string dir, string stem, string ext = "") =>
        Enumerable.Range(1, int.MaxValue)
            .Select(i => Path.Combine(dir, (i == 1 ? stem : $"{stem} ({i})") + ext))
            .First(p => !Exists(p));

    public static string CreateFolder(string dir)
    {
        var path = UniquePath(dir, Loc.Get("Default.NewFolder"));
        Create(path, isFolder: true);
        return path;
    }

    public static string CreateFolder(string dir, string name)
    {
        if (NameError(dir, name) is { } error)
            throw new ArgumentException(error);

        var path = Path.Combine(dir, name);
        Create(path, isFolder: true);
        return path;
    }

    public static string? NameError(string dir, string name)
    {
        if (IsInvalidName(name))
            return InvalidNameMessage;

        return Exists(Path.Combine(dir, name)) ? Loc.Format("Error.NameExists", name) : null;
    }

    public static string CreateFile(string dir, string name)
    {
        if (NameError(dir, name) is { } error)
            throw new ArgumentException(error);

        var path = Path.Combine(dir, name);
        Create(path, isFolder: false);
        return path;
    }

    public static string CreateFile(string dir)
    {
        var path = UniquePath(dir, Loc.Get("Default.NewFile"), ".txt");
        Create(path, isFolder: false);
        return path;
    }

    public static void Create(string path, bool isFolder)
    {
        if (isFolder)
        {
            Stores.CreateDirectory(path);
            return;
        }

        Stores.CreateFile(path);
    }

    public static string Rename(string path, string newName)
    {
        if (IsInvalidName(newName))
            throw new ArgumentException(InvalidNameMessage);

        var dest = Path.Combine(Stores.Parent(path)!, newName);
        if (dest == path)
            return path;

        var caseOnly = string.Equals(dest, path, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && Exists(dest))
            throw new IOException(Loc.Format("Error.NameExists", newName));

        return Stores.Rename(path, newName);
    }

    public static void Delete(IEnumerable<string> paths, bool permanent = false) =>
        paths.ToList().ForEach(p => Stores.Delete(p, recycle: !permanent));

    public static IReadOnlyList<string> CreateShortcuts(IEnumerable<string> targets, string destDir) =>
        targets.ToList().Select(target =>
        {
            if (!Stores.Has(target, StoreCapabilities.RealPath) || !Stores.Has(destDir, StoreCapabilities.RealPath))
                throw new IOException(Loc.Get("Error.PhoneShortcut"));

            var lnk = UniquePath(destDir, Loc.Format("Default.ShortcutName", Path.GetFileName(target.TrimEnd('\\'))), ".lnk");
            Shortcuts.Create(lnk, target);
            return lnk;
        }).ToList();

    public static IReadOnlyList<string> Transfer(IEnumerable<string> sources, string destDir, bool move) =>
        TransferPairs(sources, destDir, move).Select(p => p.To).ToList();

    public static IReadOnlyList<(string From, string To)> TransferPairs(IEnumerable<string> sources, string destDir, bool move) =>
        sources.ToList().Select(s => (From: s, To: TransferOne(s, destDir, move))).ToList();

    public static void Move(string from, string to) => Stores.Transfer(from, to, move: true, showDialogs: false);

    public static void Copy(string from, string to) => Stores.Transfer(from, to, move: false, showDialogs: false);

    public static Task<string> CreateFolderAsync(string dir) => Task.Run(() => CreateFolder(dir));

    public static Task<string> CreateFolderAsync(string dir, string name) => Task.Run(() => CreateFolder(dir, name));

    public static Task<string> CreateFileAsync(string dir, string name) => Task.Run(() => CreateFile(dir, name));

    public static Task<string> CreateFileAsync(string dir) => Task.Run(() => CreateFile(dir));

    public static Task<string> RenameAsync(string path, string newName) => Task.Run(() => Rename(path, newName));

    public static Task DeleteAsync(IEnumerable<string> paths, bool permanent = false)
    {
        var list = paths.ToList();
        return Task.Run(() => Delete(list, permanent));
    }

    public static Task<IReadOnlyList<string>> CreateShortcutsAsync(IEnumerable<string> targets, string destDir)
    {
        var list = targets.ToList();
        return Sta.Run(() => CreateShortcuts(list, destDir));
    }

    public static Task<IReadOnlyList<(string From, string To)>> TransferPairsAsync(IEnumerable<string> sources, string destDir, bool move)
    {
        var list = sources.ToList();
        return Task.Run(() => TransferPairs(list, destDir, move));
    }

    public static bool IsSameOrInside(string path, string container) =>
        Normalize(path).StartsWith(Normalize(container), StringComparison.OrdinalIgnoreCase);

    public static bool SameParent(string path, string dir) =>
        string.Equals(Normalize(Path.GetDirectoryName(path.TrimEnd('\\')) ?? ""), Normalize(dir), StringComparison.OrdinalIgnoreCase);

    public static bool Exists(string path) => Stores.Exists(path);

    public static bool IsDirectory(string path) => Stores.IsDirectory(path);

    private static bool IsInvalidName(string name) => name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;

    private static string TransferOne(string src, string destDir, bool move)
    {
        var isDir = IsDirectory(src);
        if (isDir && IsSameOrInside(destDir, src))
            throw new IOException(Loc.Get("Error.DestInSource"));

        var sameFolder = SameParent(src, destDir);
        if (move && sameFolder)
            return src;

        var dest = sameFolder ? CopyPath(src, destDir, isDir) : Path.Combine(destDir, Path.GetFileName(src.TrimEnd('\\')));
        Stores.Transfer(src, dest, move, showDialogs: true);
        return dest;
    }

    private static string CopyPath(string src, string destDir, bool isDir)
    {
        var name = Path.GetFileName(src.TrimEnd('\\'));
        if (isDir)
            return UniquePath(destDir, Loc.Format("Default.CopyName", name));

        return UniquePath(destDir, Loc.Format("Default.CopyName", Path.GetFileNameWithoutExtension(name)), Path.GetExtension(name));
    }

    private static string Normalize(string p) => p.TrimEnd('\\') + "\\";
}
