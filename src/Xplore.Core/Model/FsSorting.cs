using System.Runtime.InteropServices;

namespace Xplore.Core.Model;

public class FsSorting(bool foldersFirst)
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string x, string y);

    public IEnumerable<FsEntry> Sort(IEnumerable<FsEntry> entries, SortColumn column, bool ascending)
    {
        var sign = ascending ? 1 : -1;
        Func<FsEntry, FsEntry, int> byColumn = column switch
        {
            SortColumn.Modified => (a, b) => Nullable.Compare(a.Modified, b.Modified),
            SortColumn.Type => (a, b) => string.Compare(a.TypeName, b.TypeName, StringComparison.CurrentCultureIgnoreCase),
            SortColumn.Size => (a, b) => Nullable.Compare(a.Size, b.Size),
            _ => (a, b) => StrCmpLogicalW(a.Name, b.Name),
        };
        var comparer = Comparer<FsEntry>.Create((a, b) =>
        {
            if (foldersFirst && a.IsDirectory != b.IsDirectory)
                return a.IsDirectory ? -1 : 1;

            var primary = sign * byColumn(a, b);
            return primary != 0 ? primary : StrCmpLogicalW(a.Name, b.Name);
        });
        return entries.OrderBy(e => e, comparer);
    }
}
