using Xplore.Core.History;
using Xplore.Core.Settings;

namespace Xplore.Tests;

public class ActionHistoryTests
{
    private sealed class Fake(string name, List<string> log, bool undoable = true, bool failUndo = false) : IHistoryAction
    {
        public string Name => name;
        public bool Undoable => undoable;
        public Task UndoAsync() { if (failUndo) throw new IOException(); log.Add("-" + name); return Task.CompletedTask; }
        public Task RedoAsync() { log.Add("+" + name); return Task.CompletedTask; }
        public string Describe(bool extended, PathFormat format) => name;
    }

    private readonly List<string> _log = [];

    private HistoryNode Add(ActionHistory h, string name, bool undoable = true, bool failUndo = false) =>
        h.Record(new Fake(name, _log, undoable, failUndo));

    private static string[] Path(ActionHistory h) =>
        Enumerable.Range(0, h.MaxDepth).Aggregate((Node: h.Current, Names: new List<string>()), (s, _) =>
            s.Node.IsRoot ? s : (s.Node.Parent!, [s.Node.Action!.Name, .. s.Names])).Names.ToArray();

    [Fact]
    public async Task LinearUndoRedo()
    {
        var h = new ActionHistory(10);
        Add(h, "a"); Add(h, "b");
        Assert.True(await h.UndoAsync());
        Assert.True(await h.RedoAsync());
        Assert.False(await h.RedoAsync());
        Assert.Equal(["-b", "+b"], _log);
    }

    [Fact]
    public async Task ActionAfterUndoBecomesRightmostChildAndRedoFollowsIt()
    {
        var h = new ActionHistory(10);
        var a = Add(h, "a"); Add(h, "b");
        await h.UndoAsync();
        var c = Add(h, "c");
        Assert.Equal(["b", "c"], a.Children.Select(n => n.Action!.Name));
        await h.UndoAsync();
        await h.RedoAsync();
        Assert.Same(c, h.Current);
    }

    [Fact]
    public async Task ReplayToWalksBothWaysAndRotatesPathToRightmost()
    {
        var h = new ActionHistory(10);
        var a = Add(h, "a"); var b = Add(h, "b");
        await h.UndoAsync();
        Add(h, "c");
        _log.Clear();

        var failed = await h.ReplayToAsync(b);

        Assert.Empty(failed);
        Assert.Equal(["-c", "+b"], _log);
        Assert.Same(b, h.Current);
        Assert.Equal(["c", "b"], a.Children.Select(n => n.Action!.Name));
    }

    [Fact]
    public async Task FailedUndoGreysTheNodeAndIsSkipped()
    {
        var h = new ActionHistory(10);
        Add(h, "a"); var b = Add(h, "b", failUndo: true);
        Assert.True(await h.UndoAsync());
        Assert.True(b.Failed && b.Greyed);
        Assert.Equal(["-a"], _log);
        Assert.True(h.Current.IsRoot);
    }

    [Fact]
    public async Task NonUndoableNodesArePassedThrough()
    {
        var h = new ActionHistory(10);
        Add(h, "a"); Add(h, "hard", undoable: false);
        await h.UndoAsync();
        Assert.Equal(["-a"], _log);
        _log.Clear();
        await h.RedoAsync();
        Assert.Equal(["+a"], _log);
    }

    [Fact]
    public async Task ReplayReportsFailures()
    {
        var h = new ActionHistory(10);
        var a = Add(h, "a"); var b = Add(h, "b", failUndo: true);
        Assert.Equal([b], await h.ReplayToAsync(h.Root.Children[0].Parent!));
    }

    [Fact]
    public async Task OverlappingReplaysRunOneAfterAnother()
    {
        var h = new ActionHistory(10);
        Add(h, "a"); Add(h, "b"); Add(h, "c");
        await Task.WhenAll(h.UndoAsync(), h.UndoAsync());
        Assert.Equal(["-c", "-b"], _log);
        Assert.Equal("a", h.Current.Action!.Name);
    }

    [Fact]
    public void TrimmingPromotesChildrenOfTheDroppedRoot()
    {
        var h = new ActionHistory(2);
        Add(h, "a"); Add(h, "b"); Add(h, "c");
        Assert.Equal(["b"], h.Root.Children.Select(n => n.Action!.Name));
        Assert.Equal(["b", "c"], Path(h));
    }
}

public class HistoryActionTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;
    private static readonly PathFormat Format = new(new FakeSettings(AppSettings.Default));
    public void Dispose() => Directory.Delete(_dir, true);

    private string P(string name) => Path.Combine(_dir, name);

    [Fact]
    public async Task MoveUndoRedo()
    {
        File.WriteAllText(P("a.txt"), "x");
        File.Move(P("a.txt"), P("b.txt"));
        var action = new Xplore.Core.History.MoveAction([(P("a.txt"), P("b.txt"))]);

        await action.UndoAsync();
        Assert.True(File.Exists(P("a.txt")));
        await action.RedoAsync();
        Assert.True(File.Exists(P("b.txt")));
    }

    [Fact]
    public Task UndoFailsWhenTargetMissing() =>
        Assert.ThrowsAsync<IOException>(() => new Xplore.Core.History.MoveAction([(P("a.txt"), P("b.txt"))]).UndoAsync());

    [Fact]
    public async Task RenameUndoRedo()
    {
        File.WriteAllText(P("old.txt"), "x");
        File.Move(P("old.txt"), P("new.txt"));
        var action = new Xplore.Core.History.RenameAction(P("old.txt"), P("new.txt"));
        await action.UndoAsync();
        Assert.True(File.Exists(P("old.txt")));
        await action.RedoAsync();
        Assert.True(File.Exists(P("new.txt")));
    }

    [Fact]
    public async Task CreateRedoRecreates()
    {
        var action = new Xplore.Core.History.CreateAction(P("f"), isFolder: true);
        await action.RedoAsync();
        Assert.True(Directory.Exists(P("f")));
        await Assert.ThrowsAsync<IOException>(action.RedoAsync);
    }

    [Fact]
    public void MoveDescriptionListsNamesAndFolders()
    {
        var pairs = Enumerable.Range(1, 5).Select(i => (P($"{i}.txt"), Path.Combine(_dir, "dest", $"{i}.txt"))).ToList();
        var action = new Xplore.Core.History.MoveAction(pairs);
        Assert.Equal($"Moved 5 files: \"1.txt\", \"2.txt\", \"3.txt\", ...\nfrom \"{_dir}\"\nto \"{Path.Combine(_dir, "dest")}\"", action.Describe(false, Format));
    }

    [Fact]
    public void NamesDescribeTheResultingState()
    {
        Assert.Equal("Renamed \"b.txt\"", new Xplore.Core.History.RenameAction(P("a.txt"), P("b.txt")).Name);
        Assert.Equal("Created folder \"f\"", new Xplore.Core.History.CreateAction(P("f"), isFolder: true).Name);
        Assert.Equal("Deleted \"a.txt\"", new Xplore.Core.History.DeleteAction([P("a.txt")], [false], permanent: false).Name);
        Assert.Equal("Moved \"b.txt\"", new Xplore.Core.History.MoveAction([(P("a.txt"), P("b.txt"))]).Name);
    }

    [Fact]
    public void RenameDescription()
    {
        var action = new Xplore.Core.History.RenameAction(P("a.txt"), P("b.txt"));
        Assert.Equal($"Renamed\nold name \"a.txt\"\nnew name \"b.txt\"\nlocation \"{_dir}\"", action.Describe(false, Format));
    }
}

public class SettingsTests
{
    [Fact]
    public void OldSettingsWithoutHistoryDepthGetDefault() =>
        Assert.Equal(100, Xplore.Core.Settings.Yaml.Reader.Deserialize<AppSettings>("ForwardSlashes: false")!.HistoryDepth);
}

public class OpenWithTests
{
    [Theory]
    [InlineData(@"C:\x\Readme.TXT", ".txt")]
    [InlineData(@"C:\x\.gitignore", ".gitignore")]
    [InlineData(@"C:\x\Makefile", "makefile")]
    public void OpenWithKeyIsExtensionOrFileName(string path, string key) =>
        Assert.Equal(key, AppSettings.OpenWithKey(path));
}

public class AssocHandlersTests
{
    [Fact]
    public void ListsApplicationsForAKnownExtension()
    {
        var handlers = Xplore.Core.Shell.AssocHandlers.List(@"C:\x\a.txt");
        Assert.NotEmpty(handlers);
        Assert.All(handlers, h => Assert.False(string.IsNullOrEmpty(h.App.Name)));
        Assert.Equal(handlers.Count, handlers.Select(h => h.App.Path.ToLowerInvariant()).Distinct().Count());
    }
}
