using System.Security.Cryptography;
using Xplore.Core.Localization;
using Xplore.Core.Model;
using Xplore.Core.Storage;

namespace Xplore.Sync;

public static class DiffService
{
    private const int HashParallelism = 4;

    public static async Task<DiffDocument> RunAsync(DiffRequest request, IProgress<ScanProgress>? progress = null, CancellationToken ct = default)
    {
        Roots.Validate(request.Left, request.Right);
        var globs = request.Globs is [] ? ["**/*"] : request.Globs;

        var (left, right) = await Task.Run(() => (Collect(request.Left, globs, ct), Collect(request.Right, globs, ct)), ct);
        var pairs = left.Keys.Union(right.Keys, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(p => (Path: p, L: left.GetValueOrDefault(p), R: right.GetValueOrDefault(p)))
            .ToList();
        progress?.Report(new(ScanPhase.Scanning, pairs.Count, pairs.Count));

        var kept = request.Options.ExpandSubtrees ? pairs : CollapseSubtrees(pairs);
        var entries = await Task.Run(() => Compare(kept, request.Options, progress, ct), ct);
        return new(request.Left, request.Right, globs, request.Options, entries);
    }

    private static Dictionary<string, StoreEntry> Collect(string root, IEnumerable<string> globs, CancellationToken ct) =>
        globs.SelectMany(g => GlobSearch.Enumerate(root, g, ct))
            .DistinctBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(i => Stores.Relative(root, i.Path), StringComparer.OrdinalIgnoreCase);

    // A folder present on one side only stands for its whole subtree.
    private static List<(string Path, StoreEntry? L, StoreEntry? R)> CollapseSubtrees(List<(string Path, StoreEntry? L, StoreEntry? R)> pairs)
    {
        var oneSidedFolders = pairs.Where(p => (p.L is null) != (p.R is null) && (p.L ?? p.R)!.IsDirectory)
            .Select(p => p.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return pairs.Where(p => !Ancestors(p.Path).Any(oneSidedFolders.Contains)).ToList();
    }

    internal static IEnumerable<string> Ancestors(string path)
    {
        for (var parent = Path.GetDirectoryName(path); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
        {
            yield return parent;
        }
    }

    private static List<DiffEntry> Compare(List<(string Path, StoreEntry? L, StoreEntry? R)> pairs, DiffOptions options, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        var sides = pairs.Select(p => (L: Info(p.L), R: Info(p.R))).ToArray();
        var status = pairs.Select((p, i) => Classify(sides[i].L, sides[i].R, options)).ToArray();
        var needsHash = Enumerable.Range(0, pairs.Count).Where(i => status[i] is null).ToArray();

        var done = 0;
        Parallel.ForEach(needsHash, new ParallelOptions { MaxDegreeOfParallelism = HashParallelism, CancellationToken = ct }, i =>
        {
            var l = FileHash.TryOf(pairs[i].L!.Path);
            var r = FileHash.TryOf(pairs[i].R!.Path);
            sides[i] = (sides[i].L! with { Hash = l }, sides[i].R! with { Hash = r });
            status[i] = l is not null && l == r ? DiffStatus.Identical : DiffStatus.Different;
            progress?.Report(new(ScanPhase.Hashing, Interlocked.Increment(ref done), needsHash.Length));
        });

        return [.. pairs.Select((p, i) => new DiffEntry(status[i]!.Value, p.Path, sides[i].L, sides[i].R))];
    }

    // Null means the pair has to be hashed to decide.
    private static DiffStatus? Classify(SideInfo? l, SideInfo? r, DiffOptions options) => (l, r) switch
    {
        (not null, null) => DiffStatus.LeftOnly,
        (null, not null) => DiffStatus.RightOnly,
        _ when l!.Kind != r!.Kind => DiffStatus.TypeConflict,
        _ when l.Kind == EntryKind.Folder => DiffStatus.Identical,
        _ when l.Size != r.Size => DiffStatus.Different,
        _ when options.Hash == HashMode.Trust && (l.Modified - r.Modified).Duration() <= options.Tolerance => DiffStatus.Identical,
        _ => null,
    };

    private static SideInfo? Info(StoreEntry? i) => i switch
    {
        { IsDirectory: true } => new(EntryKind.Folder, Syntax.Truncate(i.ModifiedUtc ?? DateTime.UnixEpoch), 0),
        { } => new(EntryKind.File, Syntax.Truncate(i.ModifiedUtc ?? DateTime.UnixEpoch), i.Size),
        _ => null,
    };
}

internal static class Roots
{
    public static void Validate(string a, string b)
    {
        var roots = new[] { a, b };
        if (roots.FirstOrDefault(r => !Stores.IsDirectory(r)) is { } missing)
            throw new DirectoryNotFoundException(Loc.Format("Sync.Error.FolderMissing", missing));

        if (Core.Operations.FileOperations.IsSameOrInside(a, b) || Core.Operations.FileOperations.IsSameOrInside(b, a))
            throw new ArgumentException(Loc.Get("Sync.Error.Nested"));
    }
}

internal static class FileHash
{
    public static string Of(string path)
    {
        using var stream = Stores.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public static string? TryOf(string path)
    {
        try
        {
            return Of(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
