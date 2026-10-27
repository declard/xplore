using System.Runtime.InteropServices;
using Xplore.Core.Localization;
using Xplore.Core.Model;

namespace Xplore.Core.Shell;

public static class RecycleBin
{
    // The name under which the Recycle Bin is shown as a location next to the drives.
    public const string Location = "Recycle Bin";

    public static string DisplayName => Loc.Get("Bin.Name");

    private const int RecycleBinFolder = 10;

    // The items in the bin under their original names, at their real location inside the bin; Modified is the deletion time.
    public static IEnumerable<FsEntry> Enumerate() => Items().Select<dynamic, FsEntry>(i => ToEntry(i));

    // Restores the most recently deleted item that was originally at the given path.
    public static void Restore(string originalPath)
    {
        var dir = Path.GetDirectoryName(originalPath.TrimEnd('\\'))!;
        var file = Path.GetFileName(originalPath.TrimEnd('\\'));
        var stem = Path.GetFileNameWithoutExtension(file);

        var match = Items()
            .Where(i => string.Equals((string?)i.ExtendedProperty("System.Recycle.DeletedFrom"), dir, StringComparison.OrdinalIgnoreCase)
                        && ((string)i.Name is var n && (string.Equals(n, file, StringComparison.OrdinalIgnoreCase) || string.Equals(n, stem, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(i => (DateTime?)i.ExtendedProperty("System.Recycle.DateDeleted"))
            .FirstOrDefault();

        if (match is null)
            throw new FileNotFoundException(Loc.Format("Error.NotInBin", file));

        match.InvokeVerb("undelete");
    }

    // Restores the listed bin items (as returned by Enumerate) to where they were deleted from.
    public static void RestoreItems(IEnumerable<string> itemPaths)
    {
        var wanted = itemPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Items().Where(i => wanted.Contains((string)i.Path)).ToList())
        {
            item.InvokeVerb("undelete");
        }
    }

    // Deletes the listed bin items for good, together with the record that tells the bin where each came from.
    public static void Purge(IEnumerable<string> itemPaths)
    {
        foreach (var path in itemPaths.ToList())
        {
            var name = Path.GetFileName(path);
            if (!name.StartsWith("$R", StringComparison.Ordinal) || !path.Contains(@"\$Recycle.Bin\", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(Loc.Format("Error.NotBinItem", path));

            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
            else
            {
                File.Delete(path);
            }

            File.Delete(Path.Combine(Path.GetDirectoryName(path)!, "$I" + name[2..]));
        }
    }

    public static void Empty()
    {
        const uint noConfirmation = 1, noProgressUi = 2, noSound = 4;
        var result = EmptyRecycleBin(IntPtr.Zero, null, noConfirmation | noProgressUi | noSound);

        // The shell reports an error when there was nothing to empty.
        if (result < 0 && Items().Any())
            throw new IOException(Loc.Get("Error.BinNotEmptied"));
    }

    [DllImport("shell32.dll", EntryPoint = "SHEmptyRecycleBinW", CharSet = CharSet.Unicode)]
    private static extern int EmptyRecycleBin(IntPtr window, string? root, uint flags);

    private static FsEntry ToEntry(dynamic item)
    {
        var path = (string)item.Path;
        var isDirectory = Directory.Exists(path);
        var name = (string)item.Name;
        var deleted = (DateTime?)item.ExtendedProperty("System.Recycle.DateDeleted");
        return isDirectory
            ? new(path, name, true, deleted, null, Loc.Get("Type.FileFolder"))
            : new(path, name, false, deleted, (long)item.Size, ShellInfo.TypeName(name));
    }

    // The FolderItems COM object does not expose IEnumerable, so it is indexed.
    private static IEnumerable<dynamic> Items()
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        dynamic items = shell.NameSpace(RecycleBinFolder).Items();
        var count = (int)items.Count;
        for (var i = 0; i < count; i++)
        {
            yield return items.Item(i);
        }
    }
}
