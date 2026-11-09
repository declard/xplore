namespace Xplore.Core.Phone;

// A folder or file on a device; Modified and Size are null when the device does not report them.
public sealed record PhoneItem(string Name, bool IsDirectory, DateTime? Modified, long? Size);

// What a connected device offers. Paths are inside the device: "" is the device itself (its children are the storages), otherwise they start with a backslash.
// Failures are reported as IOException.
public interface IPhoneBackend
{
    IReadOnlyList<string> Devices();

    IEnumerable<PhoneItem> List(string device, string inside);

    PhoneItem? Find(string device, string inside);

    void CreateDirectory(string device, string inside);

    void Delete(string device, string inside, bool isDirectory);

    void Rename(string device, string inside, string newName);

    void Download(string device, string inside, Stream destination);

    void Upload(string device, string inside, Stream content);
}
