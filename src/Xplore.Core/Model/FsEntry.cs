using Xplore.Core.Localization;
using Xplore.Core.Shell;
using Xplore.Core.Storage;

namespace Xplore.Core.Model;

// A file, folder or drive shown in the details view.
// Path: full path; Name: display name; IsDirectory: folder or drive; Modified: last write time (null for drives);
// Size: bytes (null for folders); TypeName: Explore-style type description; IsHidden: has the Hidden attribute.
public sealed record FsEntry(
    string Path,
    string Name,
    bool IsDirectory,
    DateTime? Modified,
    long? Size,
    string TypeName,
    bool IsHidden = false)
{
    public static FsEntry From(StoreEntry e) =>
        new(e.Path, e.Name, e.IsDirectory, e.ModifiedUtc?.ToLocalTime(), e.IsDirectory ? null : e.Size, TypeOf(e), e.IsHidden);

    private static string TypeOf(StoreEntry e) => e.IsDirectory ? Loc.Get("Type.FileFolder") : ShellInfo.TypeName(e.Name);
}

public enum SortColumn { Name, Modified, Type, Size }
