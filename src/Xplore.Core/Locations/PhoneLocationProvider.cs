using Xplore.Core.Model;
using Xplore.Core.Phone;
using Xplore.Core.Storage;

namespace Xplore.Core.Locations;

// Phones and cameras connected by USB, listed in Storage; their folders are browsed and edited like any other.
public sealed class PhoneLocationProvider : ILocationProvider
{
    public LocationCapabilities Capabilities => LocationCapabilities.Write | LocationCapabilities.Subfolders | LocationCapabilities.Streaming;

    public IReadOnlyList<LocationVerb> Verbs => [];

    public string? Canonical(string text) => text.Trim().TrimEnd('\\') is var location && PhonePath.Is(location) ? location : null;

    public Task<IReadOnlyList<FsEntry>> ListAsync(string location, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<FsEntry>>(() => [.. Stores.Children(location).Select(FsEntry.From)], ct);

    public IAsyncEnumerable<FsEntry> EnumerateAsync(string location, CancellationToken ct = default) =>
        Streaming.Run(() => Stores.Children(location).Select(FsEntry.From), ct);

    public Task<IReadOnlyList<Breadcrumb>> BreadcrumbsAsync(string location, CancellationToken ct = default)
    {
        var (device, inside) = PhonePath.Split(location);
        var segments = inside.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return Task.FromResult<IReadOnlyList<Breadcrumb>>(
        [
            new(device, PhonePath.Combine(device)),
            .. segments.Select((s, i) => new Breadcrumb(s, PhonePath.Combine(device, "\\" + string.Join('\\', segments.Take(i + 1))))),
        ]);
    }

    // A failing device enumeration must not take the drives down with it.
    public Task<IReadOnlyList<FsEntry>> StorageEntriesAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<FsEntry>>(() =>
        {
            try
            {
                return Phones.DeviceEntries();
            }
            catch (IOException)
            {
                return [];
            }
        }, ct);
}
