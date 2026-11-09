using Microsoft.VisualBasic.FileIO;

namespace Xplore.Core.Storage;

// Disks, including UNC paths; it takes every path no other store claims.
public sealed class LocalStore : IFileStore
{
    private static readonly EnumerationOptions Flat = new() { IgnoreInaccessible = true, AttributesToSkip = 0 };

    public StoreCapabilities Capabilities => StoreCapabilities.RealPath | StoreCapabilities.RecycleBin | StoreCapabilities.Timestamps;

    public bool Handles(string path) => true;

    public StoreEntry? Info(string path)
    {
        if (Directory.Exists(path))
            return ToEntry(new DirectoryInfo(path));

        return File.Exists(path) ? ToEntry(new FileInfo(path)) : null;
    }

    public IEnumerable<StoreEntry> Children(string directory) =>
        new DirectoryInfo(directory).EnumerateFileSystemInfos("*", Flat).Select(ToEntry);

    public bool HasSubfolders(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public Stream OpenRead(string path) => File.OpenRead(path);

    public void Write(string path, Stream content, bool overwrite)
    {
        using var file = new FileStream(path, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write);
        content.CopyTo(file);
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void Delete(string path, bool recycle)
    {
        var isDirectory = Directory.Exists(path);
        if (recycle && isDirectory)
        {
            FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            return;
        }

        if (recycle)
        {
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            return;
        }

        if (isDirectory)
        {
            Directory.Delete(path, recursive: true);
            return;
        }

        File.Delete(path);
    }

    // A rename that only changes the case goes through a temporary name, which Windows needs.
    public string Rename(string path, string newName)
    {
        var directory = Path.GetDirectoryName(path)!;
        var dest = Path.Combine(directory, newName);
        if (!string.Equals(dest, path, StringComparison.OrdinalIgnoreCase))
        {
            MoveItem(path, dest);
            return dest;
        }

        var temp = Path.Combine(directory, Guid.NewGuid().ToString("N"));
        MoveItem(path, temp);
        MoveItem(temp, dest);
        return dest;
    }

    public void SetModified(string path, DateTime utc)
    {
        if (Directory.Exists(path))
        {
            Directory.SetLastWriteTimeUtc(path, utc);
            return;
        }

        File.SetLastWriteTimeUtc(path, utc);
    }

    public bool TryMove(string from, string to, bool overwrite)
    {
        if (!Directory.Exists(from))
        {
            File.Move(from, to, overwrite);
            return true;
        }

        if (!string.Equals(Path.GetPathRoot(from), Path.GetPathRoot(to), StringComparison.OrdinalIgnoreCase))
            return false;

        Directory.Move(from, to);
        return true;
    }

    public bool TryCopyFile(string from, string to, bool overwrite)
    {
        if (!ReferenceEquals(Stores.For(to), this))
            return false;

        File.Copy(from, to, overwrite);
        return true;
    }

    public bool TryTransfer(string from, string to, bool move, bool showDialogs)
    {
        if (!ReferenceEquals(Stores.For(to), this))
            return false;

        var ui = showDialogs ? UIOption.AllDialogs : UIOption.OnlyErrorDialogs;
        Action transfer = (Directory.Exists(from), move) switch
        {
            (true, true) => () => FileSystem.MoveDirectory(from, to, ui),
            (true, false) => () => FileSystem.CopyDirectory(from, to, ui),
            (false, true) => () => FileSystem.MoveFile(from, to, ui),
            (false, false) => () => FileSystem.CopyFile(from, to, ui),
        };
        transfer();
        return true;
    }

    public string? Parent(string path) => Path.GetDirectoryName(path);

    public string Relative(string root, string path) => Path.GetRelativePath(root, path);

    public string ToLocalFile(string path) => path;

    private static void MoveItem(string from, string to)
    {
        if (Directory.Exists(from))
        {
            Directory.Move(from, to);
            return;
        }

        File.Move(from, to);
    }

    private static StoreEntry ToEntry(FileSystemInfo i) => i switch
    {
        DirectoryInfo d => new(d.FullName, d.Name, true, d.LastWriteTimeUtc, 0, IsLink(d), IsHidden(d)),
        FileInfo f => new(f.FullName, f.Name, false, f.LastWriteTimeUtc, f.Length, IsLink(f), IsHidden(f)),
        _ => throw new NotSupportedException(),
    };

    private static bool IsLink(FileSystemInfo i) => i.Attributes.HasFlag(FileAttributes.ReparsePoint);

    private static bool IsHidden(FileSystemInfo i) => i.Attributes.HasFlag(FileAttributes.Hidden);
}
