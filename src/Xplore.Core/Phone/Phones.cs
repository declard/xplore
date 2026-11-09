using Xplore.Core.Localization;
using Xplore.Core.Model;

namespace Xplore.Core.Phone;

public static class Phones
{
    // Replaceable so that what depends on a device can be tested without one.
    public static IPhoneBackend Backend { get; set; } = new MediaDevicesBackend();

    public static IReadOnlyList<FsEntry> DeviceEntries() =>
        [.. Backend.Devices().Select(d => new FsEntry(PhonePath.Combine(d), d, true, null, null, Loc.Get("Type.Phone")))];
}
