using Xplore.Core.Localization;
using Xplore.Core.Shell;
using Microsoft.VisualBasic.FileIO;

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
        Directory.CreateDirectory(path);
        return path;
    }

    public static string CreateFolder(string dir, string name)
    {
        if (NameError(dir, name) is { } error)
            throw new ArgumentException(error);

        var path = Path.Combine(dir, name);
        Directory.CreateDirectory(path);
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
        File.Create(path).Dispose();
        return path;
    }

    public static string CreateFile(string dir)
    {
        var path = UniquePath(dir, Loc.Get("Default.NewFile"), ".txt");
        File.Create(path).Dispose();
        return path;
    }

    public static string Rename(string path, string newName)
    {
        if (IsInvalidName(newName))
            throw new ArgumentException(InvalidNameMessage);

        var dir = Path.GetDirectoryName(path)!;
        var dest = Path.Combine(dir, newName);
        if (dest == path)
            return path;

        var caseOnly = string.Equals(dest, path, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && Exists(dest))
            throw new IOException(Loc.Format("Error.NameExists", newName));

        var isDir = Directory.Exists(path);
        void MoveItem(string from, string to)
        {
            if (isDir)
            {
                Directory.Move(from, to);
                return;
            }

            File.Move(from, to);
        }

        if (!caseOnly)
        {
            MoveItem(path, dest);
            return dest;
        }

        var temp = UniquePath(dir, Guid.NewGuid().ToString("N"));
        MoveItem(path, temp);
        MoveItem(temp, dest);
        return dest;
    }

    public static void Delete(IEnumerable<string> paths, bool permanent = false)
    {
        var option = permanent ? RecycleOption.DeletePermanently : RecycleOption.SendToRecycleBin;
        paths.ToList().ForEach(p =>
        {
            if (Directory.Exists(p))
            {
                FileSystem.DeleteDirectory(p, UIOption.OnlyErrorDialogs, option);
                return;
            }

            FileSystem.DeleteFile(p, UIOption.OnlyErrorDialogs, option);
        });
    }

    public static IReadOnlyList<string> CreateShortcuts(IEnumerable<string> targets, string destDir) =>
        targets.ToList().Select(target =>
        {
            var lnk = UniquePath(destDir, Loc.Format("Default.ShortcutName", Path.GetFileName(target.TrimEnd('\\'))), ".lnk");
            Shortcuts.Create(lnk, target);
            return lnk;
        }).ToList();

    public static IReadOnlyList<string> Transfer(IEnumerable<string> sources, string destDir, bool move) =>
        TransferPairs(sources, destDir, move).Select(p => p.To).ToList();

    public static IReadOnlyList<(string From, string To)> TransferPairs(IEnumerable<string> sources, string destDir, bool move) =>
        sources.ToList().Select(s => (From: s, To: TransferOne(s, destDir, move))).ToList();

    public static void Move(string from, string to) => TransferExact(from, to, move: true, UIOption.OnlyErrorDialogs);

    public static void Copy(string from, string to) => TransferExact(from, to, move: false, UIOption.OnlyErrorDialogs);

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

    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static bool IsInvalidName(string name) => name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;

    private static string TransferOne(string src, string destDir, bool move)
    {
        var isDir = Directory.Exists(src);
        if (isDir && IsSameOrInside(destDir, src))
            throw new IOException(Loc.Get("Error.DestInSource"));

        var sameFolder = SameParent(src, destDir);
        if (move && sameFolder)
            return src;

        var dest = sameFolder ? CopyPath(src, destDir, isDir) : Path.Combine(destDir, Path.GetFileName(src.TrimEnd('\\')));
        TransferExact(src, dest, move, UIOption.AllDialogs);
        return dest;
    }

    private static string CopyPath(string src, string destDir, bool isDir)
    {
        var name = Path.GetFileName(src.TrimEnd('\\'));
        if (isDir)
            return UniquePath(destDir, Loc.Format("Default.CopyName", name));

        return UniquePath(destDir, Loc.Format("Default.CopyName", Path.GetFileNameWithoutExtension(name)), Path.GetExtension(name));
    }

    private static void TransferExact(string from, string to, bool move, UIOption ui)
    {
        Action transfer = (Directory.Exists(from), move) switch
        {
            (true, true) => () => FileSystem.MoveDirectory(from, to, ui),
            (true, false) => () => FileSystem.CopyDirectory(from, to, ui),
            (false, true) => () => FileSystem.MoveFile(from, to, ui),
            (false, false) => () => FileSystem.CopyFile(from, to, ui),
        };
        transfer();
    }

    private static string Normalize(string p) => p.TrimEnd('\\') + "\\";
}
