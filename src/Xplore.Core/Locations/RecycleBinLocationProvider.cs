using Xplore.Core.Localization;
using Xplore.Core.Model;
using Xplore.Core.Shell;

namespace Xplore.Core.Locations;

// The Recycle Bin as one flat list of the deleted items next to the drives; items are restored or deleted for good, not edited.
public sealed class RecycleBinLocationProvider : ILocationProvider
{
    private readonly FsEntry _entry = new(RecycleBin.Location, Loc.Get("Bin.Name"), true, null, null, Loc.Get("Type.SystemFolder"));

    public LocationCapabilities Capabilities => LocationCapabilities.Streaming;

    public IReadOnlyList<LocationVerb> Verbs { get; } =
    [
        new(Loc.Get("Bin.Restore"), VerbRole.Item, null, entries => Task.Run(() => RecycleBin.RestoreItems(entries.Select(e => e.Path)))),
        new(Loc.Get("Common.Delete"), VerbRole.Delete, entries => entries is [var one]
            ? Loc.Format("Bin.Purge.One", one.Name)
            : Loc.Format("Bin.Purge.Many", entries.Count), entries => Task.Run(() => RecycleBin.Purge(entries.Select(e => e.Path)))),
        new(Loc.Get("Bin.Empty"), VerbRole.Location, _ => Loc.Get("Bin.Empty.Confirm"), _ => Task.Run(RecycleBin.Empty)),
    ];

    public string? Canonical(string text) =>
        text.Trim().TrimEnd('\\') is var name && (IsName(name, RecycleBin.Location) || IsName(name, Loc.Get("Bin.Name"))) ? RecycleBin.Location : null;

    public Task<IReadOnlyList<FsEntry>> ListAsync(string location, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<FsEntry>>(() => [.. RecycleBin.Enumerate()], ct);

    public IAsyncEnumerable<FsEntry> EnumerateAsync(string location, CancellationToken ct = default) =>
        Streaming.Run(RecycleBin.Enumerate, ct);

    private static bool IsName(string text, string name) => string.Equals(text, name, StringComparison.OrdinalIgnoreCase);

    public Task<IReadOnlyList<FsEntry>> StorageEntriesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<FsEntry>>([_entry]);
}
