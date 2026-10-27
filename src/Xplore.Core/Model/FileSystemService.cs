using System.Runtime.CompilerServices;
using Xplore.Core.Locations;

namespace Xplore.Core.Model;

public interface IFileSystemService
{
    Task<IReadOnlyList<FsEntry>> DrivesAsync(CancellationToken ct = default);

    // Everything listed at the root level: the drives and the other providers' locations.
    Task<IReadOnlyList<FsEntry>> StorageAsync(CancellationToken ct = default);

    Task<IReadOnlyList<FsEntry>> ListAsync(string path, CancellationToken ct = default);

    // The entries of a location as they become available.
    IAsyncEnumerable<FsEntry> EnumerateAsync(string path, CancellationToken ct = default);

    LocationCapabilities CapabilitiesOf(string? location);

    IReadOnlyList<LocationVerb> VerbsOf(string? location);

    // The canonical location when the text names a non-file location such as "Recycle Bin", otherwise null.
    string? Canonical(string text);

    IAsyncEnumerable<FsEntry> Search(string path, string pattern, CancellationToken ct);
}

public sealed class FileSystemService : IFileSystemService
{
    private readonly FileLocationProvider _files = new();
    private readonly ILocationProvider[] _virtual = [new RecycleBinLocationProvider()];

    private ILocationProvider[] AllProviders => [_files, .. _virtual];

    public Task<IReadOnlyList<FsEntry>> DrivesAsync(CancellationToken ct = default) => _files.StorageEntriesAsync(ct);

    public async Task<IReadOnlyList<FsEntry>> StorageAsync(CancellationToken ct = default) =>
        [.. (await Task.WhenAll(AllProviders.Select(p => p.StorageEntriesAsync(ct)))).SelectMany(e => e)];

    public Task<IReadOnlyList<FsEntry>> ListAsync(string path, CancellationToken ct = default) =>
        ProviderFor(path).ListAsync(path, ct);

    public IAsyncEnumerable<FsEntry> EnumerateAsync(string path, CancellationToken ct = default) =>
        ProviderFor(path).EnumerateAsync(path, ct);

    public LocationCapabilities CapabilitiesOf(string? location) =>
        location is null ? LocationCapabilities.None : ProviderFor(location).Capabilities;

    public IReadOnlyList<LocationVerb> VerbsOf(string? location) => location is null ? [] : ProviderFor(location).Verbs;

    public string? Canonical(string text) => _virtual.Select(p => p.Canonical(text)).OfType<string>().FirstOrDefault();

    public async IAsyncEnumerable<FsEntry> Search(
        string path, string pattern, [EnumeratorCancellation] CancellationToken ct)
    {
        if (ProviderFor(path) is var provider && provider != _files)
        {
            var matches = GlobSearch.FlatMatcher(pattern);
            await foreach (var e in provider.EnumerateAsync(path, ct).Where(matches))
            {
                yield return e;
            }

            yield break;
        }

        await foreach (var e in Streaming.Run(() => GlobSearch.Enumerate(path, pattern, ct).Select(FileLocationProvider.ToEntry), ct))
        {
            yield return e;
        }
    }

    private ILocationProvider ProviderFor(string location) =>
        _virtual.FirstOrDefault(p => p.Canonical(location) is not null) ?? _files;
}
