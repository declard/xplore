using Xplore.Core.Localization;
using Xplore.Core.Phone;

namespace Xplore.Core.Storage;

// Portable devices, addressed as Phone:\<device>\<storage>\<folders>. A phone has no Recycle Bin and its timestamps cannot be set.
public sealed class PhoneStore : IFileStore
{
    public StoreCapabilities Capabilities => StoreCapabilities.None;

    public bool Handles(string path) => PhonePath.Is(path);

    public StoreEntry? Info(string path)
    {
        var (device, inside) = PhonePath.Split(path);
        return Phones.Backend.Find(device, inside) is { } item ? ToEntry(path.TrimEnd('\\'), item) : null;
    }

    public IEnumerable<StoreEntry> Children(string directory)
    {
        var parent = directory.TrimEnd('\\');
        var (device, inside) = PhonePath.Split(parent);
        return Phones.Backend.List(device, inside).Select(i => ToEntry(parent + "\\" + i.Name, i));
    }

    // Listing a folder to find out is slow on a phone.
    public bool HasSubfolders(string directory) => true;

    // The device streams into the stream it is given, so the file is received in a temporary file that goes away when the stream is closed.
    public Stream OpenRead(string path)
    {
        var (device, inside) = PhonePath.Split(path);
        var received = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
        try
        {
            Phones.Backend.Download(device, inside, received);
            received.Position = 0;
            return received;
        }
        catch
        {
            received.Dispose();
            throw;
        }
    }

    public void Write(string path, Stream content, bool overwrite)
    {
        if (Info(path) is not null)
        {
            if (!overwrite)
                throw new IOException(Loc.Format("Error.AlreadyExists", Path.GetFileName(path)));

            Delete(path, recycle: false);
        }

        var (device, inside) = PhonePath.Split(path);
        Phones.Backend.Upload(device, inside, content);
    }

    public void CreateDirectory(string path)
    {
        if (Info(path) is { IsDirectory: true })
            return;

        if (PhonePath.Parent(path) is { } parent)
        {
            CreateDirectory(parent);
        }

        var (device, inside) = PhonePath.Split(path);
        Phones.Backend.CreateDirectory(device, inside);
    }

    public void Delete(string path, bool recycle)
    {
        var item = Info(path) ?? throw new FileNotFoundException(Loc.Format("Error.NoLongerExists", Path.GetFileName(path)));
        var (device, inside) = PhonePath.Split(path);
        Phones.Backend.Delete(device, inside, item.IsDirectory);
    }

    public string Rename(string path, string newName)
    {
        var (device, inside) = PhonePath.Split(path);
        Phones.Backend.Rename(device, inside, newName);
        return PhonePath.Parent(path) + "\\" + newName;
    }

    public void SetModified(string path, DateTime utc)
    {
    }

    public bool TryMove(string from, string to, bool overwrite) => false;

    public bool TryCopyFile(string from, string to, bool overwrite) => false;

    public bool TryTransfer(string from, string to, bool move, bool showDialogs) => false;

    public string? Parent(string path) => PhonePath.Parent(path);

    public string Relative(string root, string path) => path[root.TrimEnd('\\').Length..].TrimStart('\\');

    public string ToLocalFile(string path)
    {
        var local = Path.Combine(Path.GetTempPath(), "Xplore", Guid.NewGuid().ToString("N"), Path.GetFileName(path));
        Stores.Copy(path, local, overwrite: false);
        return local;
    }

    private static StoreEntry ToEntry(string path, PhoneItem item) =>
        new(path, item.Name, item.IsDirectory, item.Modified?.ToUniversalTime(), item.Size ?? 0);
}
