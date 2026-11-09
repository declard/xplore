using MediaDevices;
using Xplore.Core.Localization;

namespace Xplore.Core.Phone;

// Portable devices over the Windows Portable Devices (MTP) API.
public sealed class MediaDevicesBackend : IPhoneBackend
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, MediaDevice> _connected = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Devices() => Guard(() =>
    {
        var devices = (MediaDeviceManager.Instance.GetDevices() ?? []).ToList();
        try
        {
            return (IReadOnlyList<string>)[.. devices.Select(NameOf)];
        }
        finally
        {
            devices.ForEach(d => d.Dispose());
        }
    });

    public IEnumerable<PhoneItem> List(string device, string inside)
    {
        using var entries = Guard(() => Open(device).GetDirectoryInfo(Location(inside)).EnumerateFileSystemInfos().GetEnumerator());
        while (Guard(entries.MoveNext))
        {
            yield return Guard(() => ToItem(entries.Current));
        }
    }

    public PhoneItem? Find(string device, string inside) => Guard(() =>
    {
        var media = Open(device);
        if (inside.Length == 0 || media.DirectoryExists(inside))
            return new PhoneItem(inside.Length == 0 ? device : Path.GetFileName(inside), true, null, null);

        return media.FileExists(inside) ? ToItem(media.GetFileInfo(inside)) : null;
    });

    public void CreateDirectory(string device, string inside) => Guard(() => Open(device).CreateDirectory(inside));

    public void Delete(string device, string inside, bool isDirectory) => Guard(() =>
    {
        var media = Open(device);
        if (isDirectory)
        {
            media.DeleteDirectory(inside, true);
            return;
        }

        media.DeleteFile(inside);
    });

    public void Rename(string device, string inside, string newName) => Guard(() => Open(device).Rename(inside, newName));

    public void Download(string device, string inside, Stream destination) => Guard(() => Open(device).DownloadFile(inside, destination));

    public void Upload(string device, string inside, Stream content) => Guard(() => Open(device).UploadFile(content, inside));

    private static string Location(string inside) => inside.Length == 0 ? @"\" : inside;

    private static string NameOf(MediaDevice device) =>
        (string.IsNullOrWhiteSpace(device.FriendlyName) ? device.Description : device.FriendlyName)?.Replace('\\', '_') ?? "";

    private static PhoneItem ToItem(MediaFileSystemInfo info) =>
        info is MediaDirectoryInfo
            ? new(info.Name, true, info.LastWriteTime ?? info.CreationTime, null)
            : new(info.Name, false, info.LastWriteTime ?? info.CreationTime, (long)info.Length);

    // A connection is kept until the device stops answering, then a fresh one is made on the next call.
    private MediaDevice Open(string name)
    {
        lock (_lock)
        {
            if (_connected.TryGetValue(name, out var cached) && cached.IsConnected)
                return cached;

            var devices = (MediaDeviceManager.Instance.GetDevices() ?? []).ToList();
            var device = devices.FirstOrDefault(d => string.Equals(NameOf(d), name, StringComparison.OrdinalIgnoreCase));
            devices.Where(d => d != device).ToList().ForEach(d => d.Dispose());
            if (device is null)
            {
                _connected.Remove(name);
                throw new IOException(Loc.Format("Error.PhoneNotConnected", name));
            }

            device.Connect();
            _connected[name] = device;
            return device;
        }
    }

    private static T Guard<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex) when (ex is not (IOException or OperationCanceledException))
        {
            throw new IOException(ex.Message, ex);
        }
    }

    private static void Guard(Action action) => Guard(() =>
    {
        action();
        return 0;
    });
}
