using Xplore.Core.Localization;
using System.Globalization;
using Xplore.Core.Settings;
using Xplore.Sync;

namespace Xplore.App;

public sealed record DiffRow(DiffEntry Entry, TimestampFormat Time)
{
    public DiffStatus Status => Entry.Status;
    public string Symbol => Entry.Status.Symbol().ToString();
    public string Path => Entry.Path;
    public string LeftModified => Modified(Entry.Left);
    public string LeftSize => Size(Entry.Left);
    public string RightModified => Modified(Entry.Right);
    public string RightSize => Size(Entry.Right);
    public string Hashes => $"left:  {Entry.Left?.Hash ?? "-"}\nright: {Entry.Right?.Hash ?? "-"}";

    private string Modified(SideInfo? s) => s is null ? "" : Time.Long(s.Modified.ToLocalTime());

    private static string Size(SideInfo? s) =>
        s switch
        {
            null => "",
            { Kind: EntryKind.Folder } => Loc.Get("Sync.FolderSize"),
            _ => s.Size.ToString("N0", CultureInfo.CurrentCulture),
        };
}
