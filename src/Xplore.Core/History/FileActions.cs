using Xplore.Core.Localization;
using Xplore.Core.Operations;
using Xplore.Core.Settings;
using Xplore.Core.Shell;

namespace Xplore.Core.History;

internal static class Text
{
    private const int Preview = 3;

    public static string Name(string path) => Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } n ? n : path;

    public static string Quoted(string text) => Loc.Format("History.Quoted", text);

    public static string Dir(string path) => PathFormat.Show(Path.GetDirectoryName(path.TrimEnd('\\')) ?? path);

    public static string Noun(IReadOnlyCollection<bool> isDirectory) => (isDirectory.Count, isDirectory.All(d => d), isDirectory.Any(d => d)) switch
    {
        (1, true, _) => Loc.Get("Noun.Folder"),
        (1, _, _) => Loc.Get("Noun.File"),
        (_, true, _) => Loc.Get("Noun.Folders"),
        (_, _, false) => Loc.Get("Noun.Files"),
        _ => Loc.Get("Noun.Items"),
    };

    public static string Label(string verb, IReadOnlyList<string> paths, string noun) =>
        paths.Count == 1 ? Loc.Format("History.Label.One", verb, Name(paths[0])) : Loc.Format("History.Label.Many", verb, paths.Count, noun);

    public static string Names(IEnumerable<string> paths, bool extended)
    {
        var names = paths.Select(p => Quoted(Name(p))).ToList();
        if (extended)
            return string.Join("\n", names.Prepend(""));

        var more = names.Count > Preview ? ", ..." : "";
        return string.Join(", ", names.Take(Preview)) + more;
    }

    public static string Dirs(IEnumerable<string> paths) =>
        string.Join(", ", paths.Select(Dir).Distinct(StringComparer.OrdinalIgnoreCase).Select(Quoted));

    public static string Summary(string past, IReadOnlyList<string> paths, string noun, bool extended)
    {
        if (paths is [_] && !extended)
            return Loc.Format("History.Summary.One", past, noun, Names(paths, false));

        var separator = extended ? "" : " ";
        return Loc.Format("History.Summary.Many", past, paths.Count, noun, separator + Names(paths, extended));
    }

    public static string Moves(string summary, IReadOnlyList<(string From, string To)> pairs) =>
        string.Join("\n", summary, Loc.Format("History.From", Dirs(pairs.Select(p => p.From))), Loc.Format("History.To", Dirs(pairs.Select(p => p.To))));

    public static string Linked(string summary, IReadOnlyList<(string Link, string Target)> links) =>
        string.Join("\n", summary, Loc.Format("History.Location", Dirs(links.Select(l => l.Link))), Loc.Format("History.Targets", string.Join(", ", links.Select(l => Quoted(PathFormat.Show(l.Target))))));

    public static string Deleted(string summary, IReadOnlyList<string> paths, bool permanent)
    {
        var text = string.Join("\n", summary, Loc.Format("History.From", Dirs(paths)));
        if (!permanent)
            return text;

        return text + "\n" + Loc.Get("History.CannotUndo");
    }

    public static void Require(bool condition, string message)
    {
        if (condition)
            return;

        throw new IOException(message);
    }
}

// Move: Pairs: each source path and where it ended up.
public sealed class MoveAction(IReadOnlyList<(string From, string To)> pairs) : IHistoryAction
{
    private readonly string _noun = Text.Noun(pairs.Select(p => Directory.Exists(p.To)).ToList());

    public string Name => Text.Label(Loc.Get("Verb.Moved"), pairs.Select(p => p.To).ToList(), _noun);
    public bool Undoable => true;

    public Task UndoAsync() => Apply(pairs.Select(p => (From: p.To, To: p.From)));

    public Task RedoAsync() => Apply(pairs);

    public string Describe(bool extended) =>
        Text.Moves(Text.Summary(Loc.Get("Verb.Moved"), pairs.Select(p => p.From).ToList(), _noun, extended), pairs);

    private static Task Apply(IEnumerable<(string From, string To)> steps) =>
        Task.Run(() => steps.ToList().ForEach(s =>
        {
            Text.Require(FileOperations.Exists(s.From), Loc.Format("Error.NoLongerExists", Text.Name(s.From)));
            Text.Require(!FileOperations.Exists(s.To), Loc.Format("Error.AlreadyExists", Text.Name(s.To)));
            FileOperations.Move(s.From, s.To);
        }));
}

// Copy: Pairs: each source path and the path of its copy.
public sealed class CopyAction(IReadOnlyList<(string From, string To)> pairs) : IHistoryAction
{
    private readonly string _noun = Text.Noun(pairs.Select(p => Directory.Exists(p.To)).ToList());

    public string Name => Text.Label(Loc.Get("Verb.Copied"), pairs.Select(p => p.To).ToList(), _noun);
    public bool Undoable => true;

    public Task UndoAsync() =>
        Task.Run(() =>
        {
            pairs.ToList().ForEach(p => Text.Require(FileOperations.Exists(p.To), Loc.Format("Error.NoLongerExists", Text.Name(p.To))));
            FileOperations.Delete(pairs.Select(p => p.To));
        });

    public Task RedoAsync() =>
        Task.Run(() => pairs.ToList().ForEach(p =>
        {
            Text.Require(FileOperations.Exists(p.From), Loc.Format("Error.NoLongerExists", Text.Name(p.From)));
            Text.Require(!FileOperations.Exists(p.To), Loc.Format("Error.AlreadyExists", Text.Name(p.To)));
            FileOperations.Copy(p.From, p.To);
        }));

    public string Describe(bool extended) =>
        Text.Moves(Text.Summary(Loc.Get("Verb.Copied"), pairs.Select(p => p.From).ToList(), _noun, extended), pairs);
}

// Rename: OldPath and NewPath: the full path before and after.
public sealed class RenameAction(string oldPath, string newPath) : IHistoryAction
{
    public string Name => Loc.Format("History.Renamed.Label", Text.Name(newPath));
    public bool Undoable => true;

    public Task UndoAsync() => FileOperations.RenameAsync(newPath, Text.Name(oldPath));

    public Task RedoAsync() => FileOperations.RenameAsync(oldPath, Text.Name(newPath));

    public string Describe(bool extended) =>
        Loc.Format("History.Renamed.Details", Text.Name(oldPath), Text.Name(newPath), Text.Dir(oldPath));
}

// New file or folder: Path: the created item.
public sealed class CreateAction(string path, bool isFolder) : IHistoryAction
{
    public string Name => Loc.Format(isFolder ? "History.CreatedFolder.Label" : "History.CreatedFile.Label", Text.Name(path));
    public bool Undoable => true;

    public Task UndoAsync() =>
        Task.Run(() =>
        {
            Text.Require(FileOperations.Exists(path), Loc.Format("Error.NoLongerExists", Text.Name(path)));
            FileOperations.Delete([path]);
        });

    public Task RedoAsync() =>
        Task.Run(() =>
        {
            Text.Require(!FileOperations.Exists(path), Loc.Format("Error.AlreadyExists", Text.Name(path)));
            if (isFolder)
            {
                Directory.CreateDirectory(path);
                return;
            }

            File.Create(path).Dispose();
        });

    public string Describe(bool extended) =>
        Loc.Format(isFolder ? "History.CreatedFolder.Details" : "History.CreatedFile.Details", Text.Name(path), Text.Dir(path));
}

// Create shortcut: Links: each shortcut file and the target it points to.
public sealed class ShortcutAction(IReadOnlyList<(string Link, string Target)> links) : IHistoryAction
{
    public string Name => Text.Label(Loc.Get("Verb.CreatedShortcut"), links.Select(l => l.Link).ToList(), Loc.Get("Noun.Shortcuts"));
    public bool Undoable => true;

    public Task UndoAsync() =>
        Task.Run(() =>
        {
            links.ToList().ForEach(l => Text.Require(FileOperations.Exists(l.Link), Loc.Format("Error.NoLongerExists", Text.Name(l.Link))));
            FileOperations.Delete(links.Select(l => l.Link));
        });

    public Task RedoAsync() =>
        Sta.Run(() => links.ToList().ForEach(l =>
        {
            Text.Require(!FileOperations.Exists(l.Link), Loc.Format("Error.AlreadyExists", Text.Name(l.Link)));
            Shortcuts.Create(l.Link, l.Target);
        }));

    public string Describe(bool extended) =>
        Text.Linked(Text.Summary(Loc.Get("Verb.Created"), links.Select(l => l.Link).ToList(), Loc.Get(links.Count == 1 ? "Noun.Shortcut" : "Noun.Shortcuts"), extended), links);
}

// Delete: Paths: the deleted items; IsDirectory: what each was; Permanent: bypassed the Recycle Bin, so it cannot be undone.
public sealed class DeleteAction(IReadOnlyList<string> paths, IReadOnlyList<bool> isDirectory, bool permanent) : IHistoryAction
{
    private readonly string _noun = Text.Noun(isDirectory.ToList());

    public string Name => Text.Label(Loc.Get(permanent ? "Verb.DeletedPermanently" : "Verb.Deleted"), paths, _noun);
    public bool Undoable => !permanent;

    public Task UndoAsync() =>
        Sta.Run(() => paths.ToList().ForEach(p =>
        {
            Text.Require(!FileOperations.Exists(p), Loc.Format("Error.AlreadyExists", Text.Name(p)));
            RecycleBin.Restore(p);
        }));

    public Task RedoAsync() =>
        Task.Run(() =>
        {
            paths.ToList().ForEach(p => Text.Require(FileOperations.Exists(p), Loc.Format("Error.NoLongerExists", Text.Name(p))));
            FileOperations.Delete(paths);
        });

    public string Describe(bool extended) =>
        Text.Deleted(Text.Summary(Loc.Get(permanent ? "Verb.PermanentlyDeleted" : "Verb.Deleted"), paths, _noun, extended), paths, permanent);
}
