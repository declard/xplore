namespace Xplore.Core.Model;

// A file, folder or drive shown in the details view.
// Path: full path; Name: display name; IsDirectory: folder or drive; Modified: last write time (null for drives);
// Size: bytes (null for folders); TypeName: Explore-style type description.
public sealed record FsEntry(
    string Path,
    string Name,
    bool IsDirectory,
    DateTime? Modified,
    long? Size,
    string TypeName);

public enum SortColumn { Name, Modified, Type, Size }
