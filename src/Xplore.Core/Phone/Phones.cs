using Xplore.Core.Localization;
using Xplore.Core.Model;
using Xplore.Core.Shell;

namespace Xplore.Core.Phone;

// File operations on Phone:\ paths. A phone has no Recycle Bin, so deleting is always permanent.
public static class Phones
{
    // Replaceable so that the operations can be tested without a device.
    public static IPhoneBackend Backend { get; set; } = new MediaDevicesBackend();

    public static IReadOnlyList<FsEntry> DeviceEntries() =>
        [.. Backend.Devices().Select(d => new FsEntry(PhonePath.Combine(d), d, true, null, null, Loc.Get("Type.Phone")))];

    public static IEnumerable<FsEntry> List(string location)
    {
        var parent = location.TrimEnd('\\');
        var (device, inside) = PhonePath.Split(parent);
        return Backend.List(device, inside).Select(i => i.IsDirectory
            ? new FsEntry(parent + "\\" + i.Name, i.Name, true, i.Modified, null, Loc.Get("Type.FileFolder"))
            : new FsEntry(parent + "\\" + i.Name, i.Name, false, i.Modified, i.Size, ShellInfo.TypeName(i.Name)));
    }

    public static bool Exists(string path) => Find(path) is not null;

    public static bool IsDirectory(string path) => Find(path)?.IsDirectory == true;

    public static void CreateFolder(string path)
    {
        var (device, inside) = PhonePath.Split(path);
        Backend.CreateDirectory(device, inside);
    }

    public static void CreateFile(string path)
    {
        var (device, inside) = PhonePath.Split(path);
        Backend.Upload(device, inside, Stream.Null);
    }

    public static void Delete(string path)
    {
        var (device, inside) = PhonePath.Split(path);
        Backend.Delete(device, inside, IsDirectory(path));
    }

    public static void Rename(string path, string newName)
    {
        var (device, inside) = PhonePath.Split(path);
        Backend.Rename(device, inside, newName);
    }

    public static void Transfer(string from, string to, bool move)
    {
        if (Exists(to))
            throw new IOException(Loc.Format("Error.AlreadyExists", Path.GetFileName(to)));

        Copy(from, to);
        if (!move)
            return;

        DeleteSource(from);
    }

    // The file in a temporary folder, for programs that cannot read from the phone.
    public static string Extract(string path)
    {
        var local = Path.Combine(Path.GetTempPath(), "Xplore", Guid.NewGuid().ToString("N"), Path.GetFileName(path));
        Copy(path, local);
        return local;
    }

    private static PhoneItem? Find(string path)
    {
        var (device, inside) = PhonePath.Split(path);
        return Backend.Find(device, inside);
    }

    private static void Copy(string from, string to)
    {
        switch (PhonePath.Is(from), PhonePath.Is(to))
        {
            case (true, true):
                CopyThroughTemp(from, to);
                break;
            case (true, false):
                Download(from, to);
                break;
            default:
                Upload(from, to);
                break;
        }
    }

    private static void CopyThroughTemp(string from, string to)
    {
        var folder = Path.Combine(Path.GetTempPath(), "Xplore", Guid.NewGuid().ToString("N"));
        var local = Path.Combine(folder, Path.GetFileName(from));
        try
        {
            Download(from, local);
            Upload(local, to);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static void Download(string from, string to)
    {
        if (!IsDirectory(from))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            var (device, inside) = PhonePath.Split(from);
            using var stream = File.Create(to);
            Backend.Download(device, inside, stream);
            return;
        }

        Directory.CreateDirectory(to);
        List(from).ToList().ForEach(e => Download(e.Path, Path.Combine(to, e.Name)));
    }

    private static void Upload(string from, string to)
    {
        if (!Directory.Exists(from))
        {
            var (device, inside) = PhonePath.Split(to);
            using var stream = File.OpenRead(from);
            Backend.Upload(device, inside, stream);
            return;
        }

        CreateFolder(to);
        Directory.EnumerateFileSystemEntries(from).ToList().ForEach(e => Upload(e, to.TrimEnd('\\') + "\\" + Path.GetFileName(e)));
    }

    private static void DeleteSource(string from)
    {
        if (PhonePath.Is(from))
        {
            Delete(from);
            return;
        }

        if (Directory.Exists(from))
        {
            Directory.Delete(from, true);
            return;
        }

        File.Delete(from);
    }
}
