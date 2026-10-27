using System.Text;
using System.Text.RegularExpressions;

namespace Xplore.Core.Model;

// Glob matching relative to a root folder: '*' and '?' within a name, '{a,b}' alternatives,
// '/' (or '\') separating levels and '**' standing for any number of folders (zero included).
// A trailing separator restricts the matches to folders.
public static class GlobSearch
{
    private static readonly EnumerationOptions Options = new() { IgnoreInaccessible = true, AttributesToSkip = 0 };

    public static IEnumerable<FileSystemInfo> Enumerate(string root, string pattern, CancellationToken ct)
    {
        var segments = pattern.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).ToList();
        if (segments is [])
            return [];

        if (segments[^1] == "**")
        {
            segments.Add("*");
        }

        var compiled = segments.Select(s => s == "**" ? null : ToRegex(s)).ToArray();
        var foldersOnly = pattern.EndsWith('/') || pattern.EndsWith('\\');
        return Walk(new(root), compiled, 0, foldersOnly, ct).DistinctBy(i => i.FullName, StringComparer.OrdinalIgnoreCase);
    }

    // The flat variant for locations without folder levels: only a single-level pattern can match, against the entry names.
    public static IEnumerable<FsEntry> Filter(IEnumerable<FsEntry> entries, string pattern) => entries.Where(FlatMatcher(pattern));

    public static Func<FsEntry, bool> FlatMatcher(string pattern)
    {
        var foldersOnly = pattern.EndsWith('/') || pattern.EndsWith('\\');
        if (pattern.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries) is not [var segment] || segment == "**")
            return _ => false;

        var regex = ToRegex(segment);
        return e => (!foldersOnly || e.IsDirectory) && regex.IsMatch(e.Name);
    }

    private static IEnumerable<FileSystemInfo> Walk(DirectoryInfo dir, Regex?[] segments, int index, bool foldersOnly, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var segment = segments[index];
        var found = segment switch
        {
            null => Walk(dir, segments, index + 1, foldersOnly, ct)
                .Concat(Subfolders(dir).SelectMany(sub => Walk(sub, segments, index, foldersOnly, ct))),
            _ when index == segments.Length - 1 =>
                Children(dir).Where(e => (!foldersOnly || e is DirectoryInfo) && segment.IsMatch(e.Name)),
            _ => Subfolders(dir)
                .Where(d => segment.IsMatch(d.Name))
                .SelectMany(sub => Walk(sub, segments, index + 1, foldersOnly, ct)),
        };

        foreach (var entry in found)
        {
            yield return entry;
        }
    }

    private static List<FileSystemInfo> Children(DirectoryInfo dir)
    {
        try
        {
            return dir.EnumerateFileSystemInfos("*", Options).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<DirectoryInfo> Subfolders(DirectoryInfo dir) =>
        Children(dir).OfType<DirectoryInfo>().Where(d => !d.Attributes.HasFlag(FileAttributes.ReparsePoint));

    private static Regex ToRegex(string glob)
    {
        var sb = new StringBuilder("^");
        var open = 0;
        foreach (var c in glob)
        {
            switch (c)
            {
                case '{':
                    open++;
                    sb.Append("(?:");
                    break;
                case '}' when open > 0:
                    open--;
                    sb.Append(')');
                    break;
                case ',' when open > 0:
                    sb.Append('|');
                    break;
                default:
                    sb.Append(c switch { '*' => ".*", '?' => ".", _ => Regex.Escape(c.ToString()) });
                    break;
            }
        }

        sb.Append(')', open).Append('$');
        return new(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
