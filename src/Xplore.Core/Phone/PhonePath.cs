namespace Xplore.Core.Phone;

// Locations on a portable device (a phone or camera connected by USB) are written Phone:\<device name>\<storage>\<folders>.
public static class PhonePath
{
    public const string Prefix = @"Phone:\";

    public static bool Is(string? path) => path?.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) == true;

    public static string Combine(string device, string inside = "") => Prefix + device + inside;

    // The device name and the path inside it: empty for the device itself, otherwise starting with a backslash.
    public static (string Device, string Inside) Split(string path)
    {
        var rest = path[Prefix.Length..].TrimEnd('\\');
        var slash = rest.IndexOf('\\');
        return slash < 0 ? (rest, "") : (rest[..slash], rest[slash..]);
    }

    public static bool IsDevice(string path) => Is(path) && Split(path).Inside.Length == 0;

    // The containing location; null for a device, which lies directly in Storage.
    public static string? Parent(string path)
    {
        var (device, inside) = Split(path);
        return inside.Length == 0 ? null : Combine(device, inside[..inside.LastIndexOf('\\')]);
    }
}
