using Xplore.Core.Navigation;
using Xplore.Core.Operations;
using Xplore.Core.Settings;

namespace Xplore.Tests;

public class NewItemTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("xp-newitem").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void CreatesTheNamedFolder()
    {
        var path = FileOperations.CreateFolder(_dir, "Photos");
        Assert.Equal(Path.Combine(_dir, "Photos"), path);
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public void CreatesTheNamedFile()
    {
        var path = FileOperations.CreateFile(_dir, "notes.md");
        Assert.Equal(Path.Combine(_dir, "notes.md"), path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void RejectsAnInvalidOrExistingFileName()
    {
        File.WriteAllText(Path.Combine(_dir, "a.txt"), "");
        Assert.Throws<ArgumentException>(() => FileOperations.CreateFile(_dir, "a.txt"));
        Assert.Throws<ArgumentException>(() => FileOperations.CreateFile(_dir, "what?.txt"));
        Assert.Single(Directory.GetFileSystemEntries(_dir));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("what?")]
    public void RejectsInvalidNamesWithoutCreatingAnything(string name)
    {
        Assert.NotNull(FileOperations.NameError(_dir, name));
        Assert.Throws<ArgumentException>(() => FileOperations.CreateFolder(_dir, name));
        Assert.Empty(Directory.GetFileSystemEntries(_dir));
    }

    [Fact]
    public void RejectsAnExistingName()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "Taken"));
        Assert.NotNull(FileOperations.NameError(_dir, "taken"));
        Assert.Null(FileOperations.NameError(_dir, "Free"));
    }

    [Fact]
    public void PromptIsOffByDefault() => Assert.False(AppSettings.Default.NewItemPrompt);
}

public class StartTabsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("xp-start").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void DefaultOpensStorage() =>
        Assert.Equal(new string?[] { null }, StartTabs.Resolve(AppSettings.Default, Session.Of([_dir], 0)).Locations);

    [Fact]
    public void CustomOpensTheFolderOrFallsBackToStorage()
    {
        Assert.Equal([_dir], StartTabs.Resolve(AppSettings.Default with { StartLocation = new(StartMode.Custom, _dir) }, null).Locations);
        Assert.Equal(new string?[] { null }, StartTabs.Resolve(AppSettings.Default with { StartLocation = new(StartMode.Custom, Path.Combine(_dir, "gone")) }, null).Locations);
    }

    [Fact]
    public void RestoreReopensTabsAndDropsMissingFolders()
    {
        var settings = AppSettings.Default with { StartLocation = new(StartMode.Restore) };
        var start = StartTabs.Resolve(settings, Session.Of([_dir, null, Path.Combine(_dir, "gone")], 1));
        Assert.Equal(new string?[] { _dir, null, null }, start.Locations);
        Assert.Equal(1, start.Selected);
        Assert.Equal(new string?[] { null }, StartTabs.Resolve(settings, null).Locations);
        Assert.Equal(2, StartTabs.Resolve(settings, Session.Of([_dir, _dir, _dir], 9)).Selected);
    }

    [Fact]
    public void SessionRoundTripsThroughYaml()
    {
        var yaml = Yaml.Writer.Serialize(Session.Of([@"C:\a b", null], 1));
        Assert.Contains("Tabs:", yaml);
        var back = Yaml.Reader.Deserialize<Session>(yaml);
        Assert.Equal([@"C:\a b", "Storage"], back.Tabs);
        Assert.Equal(1, back.Selected);
    }

    [Fact]
    public void SettingsRoundTripThroughYaml()
    {
        var settings = new AppSettings(
            50, true, true, new StartLocation(StartMode.Custom, @"D:\x"));
        var yaml = Yaml.Writer.Serialize(settings);
        Assert.Contains("StartLocation:", yaml);
        var back = Yaml.Reader.Deserialize<AppSettings>(yaml);
        Assert.Equal((50, true, true, new StartLocation(StartMode.Custom, @"D:\x")), (back.HistoryDepth, back.ForwardSlashes, back.NewItemPrompt, back.StartLocation));
    }

    [Fact]
    public void BookmarksRoundTripThroughYamlAsAPlainList()
    {
        var bookmarks = new List<Bookmark> { new("Work", @"C:\work"), new("Docs", @"C:\docs") };
        var yaml = Yaml.Writer.Serialize(bookmarks);
        Assert.StartsWith("- Name: Work", yaml);
        Assert.Equal(bookmarks, Yaml.Reader.Deserialize<List<Bookmark>>(yaml));
    }

    [Fact]
    public void OpenWithAppsRoundTripThroughYamlAsAPlainMap()
    {
        var apps = new Dictionary<string, IReadOnlyList<OpenWithApp>> { [".txt"] = [new OpenWithApp("Notepad++", @"C:\n.exe", @"C:\n.exe,0")] };
        var yaml = Yaml.Writer.Serialize(apps);
        Assert.StartsWith(".txt:", yaml);
        Assert.Equal(apps[".txt"], Yaml.Reader.Deserialize<Dictionary<string, IReadOnlyList<OpenWithApp>>>(yaml)[".txt"]);
    }

    [Fact]
    public void MissingKeysTakeDefaultsAndNullsAreOmitted()
    {
        var back = Yaml.Reader.Deserialize<AppSettings>("StartLocation:\n  Type: Restore\n");
        Assert.Equal((StartMode.Restore, 100, false), (back.StartLocation!.Type, back.HistoryDepth, back.NewItemPrompt));
        Assert.DoesNotContain("CustomPath", Yaml.Writer.Serialize(new StartLocation(StartMode.Restore)));
    }
}

public class PrefixSearchTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);
    private static readonly DateTime T0 = new(2026, 1, 1);

    [Fact]
    public void CharactersWithinTheTimeoutExtendThePrefix()
    {
        var search = new PrefixSearch();
        Assert.Equal("a", search.Add('a', T0, Timeout));
        Assert.Equal("ab", search.Add('b', T0.AddMilliseconds(900), Timeout));
        Assert.Equal("abc", search.Add('c', T0.AddMilliseconds(1800), Timeout));
    }

    [Fact]
    public void ACharacterAfterTheTimeoutStartsANewPrefix()
    {
        var search = new PrefixSearch();
        search.Add('a', T0, Timeout);
        Assert.Equal("b", search.Add('b', T0.AddMilliseconds(1001), Timeout));
    }

    [Fact]
    public void FindsTheFirstCaseInsensitiveMatch()
    {
        string[] names = ["alpha", "Beta", "bravo"];
        Assert.Equal(1, PrefixSearch.IndexOf(names, "b"));
        Assert.Equal(2, PrefixSearch.IndexOf(names, "BR"));
        Assert.Equal(-1, PrefixSearch.IndexOf(names, "z"));
        Assert.Equal(2, PrefixSearch.IndexOf(names, "b", 2));
        Assert.Equal(1, PrefixSearch.IndexOf(names, "b", 3));
        Assert.Equal(-1, PrefixSearch.IndexOf([], "b"));
    }

    [Fact]
    public void TimeoutDefaultsToOneSecond() => Assert.Equal(1000, AppSettings.Default.PrefixTimeoutMs);
}
