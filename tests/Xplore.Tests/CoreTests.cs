using Xplore.Core.Model;
using Xplore.Core.Navigation;
using Xplore.Core.Operations;
using Xplore.Core.Shell;

namespace Xplore.Tests;

public class NavigationHistoryTests
{
    [Fact]
    public void BackAndForwardRoundTrip()
    {
        var back = NavigationHistory.Start(@"C:\").Navigate(@"C:\a").Navigate(@"C:\a\b").Back();
        Assert.Equal(@"C:\a", back.Current);
        Assert.Equal(@"C:\a\b", back.Forward().Current);
    }

    [Fact]
    public void NavigateClearsForward() =>
        Assert.False(NavigationHistory.Start(@"C:\").Navigate(@"C:\a").Back().Navigate(@"C:\x").CanGoForward);

    [Fact]
    public void UpFromDriveRootGoesToThisPc() =>
        Assert.Null(NavigationHistory.Start(@"C:\").Up().Current);

    [Fact]
    public void UpFromFolderGoesToParent() =>
        Assert.Equal(@"C:\a", NavigationHistory.Start(@"C:\a\b").Up().Current);
}

public class FsSortingTests
{
    private static FsEntry F(string n, long s = 0, bool dir = false) =>
        new(n, n, dir, DateTime.Today, dir ? null : s, "x");

    [Fact]
    public void FoldersFirstEvenWhenDescending() =>
        Assert.Equal(["z", "b.txt"], FsSorting.Sort([F("b.txt"), F("z", dir: true)], SortColumn.Name, false).Select(e => e.Name));

    [Fact]
    public void NaturalNameOrder() =>
        Assert.Equal(["a2", "a10"], FsSorting.Sort([F("a10"), F("a2")], SortColumn.Name, true).Select(e => e.Name));

    [Fact]
    public void SizeDescending() =>
        Assert.Equal(["big", "small"], FsSorting.Sort([F("small", 1), F("big", 9)], SortColumn.Size, false).Select(e => e.Name));
}

public class FileSystemServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public async Task ListsFilesAndFolders()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "sub"));
        File.WriteAllText(Path.Combine(_dir, "a.txt"), "hi");
        var r = await new FileSystemService().ListAsync(_dir);
        Assert.Contains(r, e => e.Name == "sub" && e.IsDirectory);
        Assert.Contains(r, e => e.Name == "a.txt" && e.Size == 2);
    }

    [Fact]
    public async Task SearchFindsNestedAndCancels()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "sub"));
        File.WriteAllText(Path.Combine(_dir, "sub", "needle.txt"), "");
        var svc = new FileSystemService();
        Assert.Single(await svc.Search(_dir, "**/needle*", CancellationToken.None).ToListAsync());
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await svc.Search(_dir, "**/needle*", cts.Token).ToListAsync());
    }
}

public class FileOperationsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void NewFoldersGetUniqueNames()
    {
        Assert.Equal("New folder", Path.GetFileName(Xplore.Core.Operations.FileOperations.CreateFolder(_dir)));
        Assert.Equal("New folder (2)", Path.GetFileName(Xplore.Core.Operations.FileOperations.CreateFolder(_dir)));
    }

    [Fact]
    public void RenameHandlesCaseOnlyAndConflicts()
    {
        var a = Path.Combine(_dir, "a.txt");
        File.WriteAllText(a, "x");
        File.WriteAllText(Path.Combine(_dir, "b.txt"), "y");
        var upper = Xplore.Core.Operations.FileOperations.Rename(a, "A.txt");
        Assert.Contains(Directory.EnumerateFiles(_dir).Select(Path.GetFileName), n => n == "A.txt");
        Assert.Throws<IOException>(() => Xplore.Core.Operations.FileOperations.Rename(upper, "b.txt"));
        Assert.Throws<ArgumentException>(() => Xplore.Core.Operations.FileOperations.Rename(upper, "a?b"));
    }

    [Fact]
    public void TransferCopiesMovesAndDuplicatesInPlace()
    {
        var sub = Directory.CreateDirectory(Path.Combine(_dir, "sub")).FullName;
        var f = Path.Combine(_dir, "f.txt");
        File.WriteAllText(f, "x");
        var copy = Xplore.Core.Operations.FileOperations.Transfer([f], sub, move: false).Single();
        Assert.True(File.Exists(copy) && File.Exists(f));
        var dup = Xplore.Core.Operations.FileOperations.Transfer([f], _dir, move: false).Single();
        Assert.Equal("f - Copy.txt", Path.GetFileName(dup));
        Xplore.Core.Operations.FileOperations.Transfer([dup], sub, move: true);
        Assert.False(File.Exists(dup));
        Assert.Throws<IOException>(() => Xplore.Core.Operations.FileOperations.Transfer([_dir], sub, move: false));
    }
}

public class GlobSearchTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    private IEnumerable<string> Find(string pattern) =>
        GlobSearch.Enumerate(_dir, pattern, CancellationToken.None).Select(i => Path.GetRelativePath(_dir, i.FullName)).Order();

    public GlobSearchTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "sub", "deep"));
        foreach (var f in new[] { "a.xml", "b.txt", @"sub\c.xml", @"sub\deep\d.xml" }) File.WriteAllText(Path.Combine(_dir, f), "");
    }

    [Fact]
    public void StarStaysInCurrentFolder() => Assert.Equal(["a.xml"], Find("*.xml"));

    [Fact]
    public void DoubleStarSlashStarFindsEverything() =>
        Assert.Equal(["a.xml", "b.txt", "sub", @"sub\c.xml", @"sub\deep", @"sub\deep\d.xml"], Find("**/*"));

    [Fact]
    public void DoubleStarThenExtensionRecurses() =>
        Assert.Equal(["a.xml", @"sub\c.xml", @"sub\deep\d.xml"], Find("**/*.xml"));

    [Fact]
    public void TrailingSlashMatchesFoldersOnly()
    {
        Assert.Equal(["sub", @"sub\deep"], Find("**/"));
        Assert.Equal(["sub"], Find("*/"));
        Assert.Equal([@"sub\deep"], Find("*/*/"));
        Assert.Empty(Find("*.xml/"));
    }

    [Fact]
    public void BracesAndQuestionMark() => Assert.Equal(["a.xml", "b.txt"], Find("?.{xml,txt}"));

    [Fact]
    public void PlainNameIsExact() => Assert.Empty(Find("a"));
}

public class AsyncFileOperationsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("xp-async").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public async Task TransferRenameAndPermanentDeleteRunAsynchronously()
    {
        var sub = Directory.CreateDirectory(Path.Combine(_dir, "sub")).FullName;
        var file = Path.Combine(_dir, "a.txt");
        await File.WriteAllTextAsync(file, "x");

        var pairs = await FileOperations.TransferPairsAsync([file], sub, move: true);
        Assert.Equal([(file, Path.Combine(sub, "a.txt"))], pairs);

        var renamed = await FileOperations.RenameAsync(pairs[0].To, "b.txt");
        Assert.True(File.Exists(renamed));

        await FileOperations.DeleteAsync([renamed], permanent: true);
        Assert.False(File.Exists(renamed));
    }

    [Fact]
    public async Task FailuresSurfaceAsTheOriginalException() =>
        await Assert.ThrowsAsync<ArgumentException>(() => FileOperations.CreateFolderAsync(_dir, "a?b"));

    [Fact]
    public async Task ShortcutsAreCreatedAndResolvedOnAStaThread()
    {
        var target = Directory.CreateDirectory(Path.Combine(_dir, "target")).FullName;
        var link = (await FileOperations.CreateShortcutsAsync([target], _dir)).Single();
        Assert.Equal(target, (await Shortcuts.ResolveAsync(link))?.Target);
        Assert.Equal(target, await Shortcuts.ResolvePathAsync(link));
    }
}
