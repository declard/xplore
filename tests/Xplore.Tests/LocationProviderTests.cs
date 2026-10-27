using Xplore.Core.Locations;
using Xplore.Core.Model;

namespace Xplore.Tests;

public class LocationProviderTests
{
    private readonly FileSystemService _fs = TestFileSystem.Create();

    [Theory]
    [InlineData("Recycle Bin")]
    [InlineData("  recycle bin ")]
    [InlineData("Recycle Bin\\")]
    public void RecycleBinNamesAreCanonical(string text) =>
        Assert.Equal("Recycle Bin", _fs.Canonical(text));

    [Theory]
    [InlineData(@"C:\Windows")]
    [InlineData(@"\\wsl.localhost\Ubuntu")]
    [InlineData("Storage")]
    public void OtherTextIsNotVirtual(string text) =>
        Assert.Null(_fs.Canonical(text));

    [Fact]
    public void FoldersOnDiskSupportEverything() =>
        Assert.Equal(LocationCapabilities.All, _fs.CapabilitiesOf(Path.GetTempPath()));

    [Fact]
    public void RecycleBinIsFlatReadOnlyAndStreamed() =>
        Assert.Equal(LocationCapabilities.Streaming, _fs.CapabilitiesOf("Recycle Bin"));

    [Fact]
    public void StorageIsNotAFolder() =>
        Assert.Equal(LocationCapabilities.None, _fs.CapabilitiesOf(null));

    [Fact]
    public async Task EntriesCarryTheHiddenAttribute()
    {
        var dir = Directory.CreateTempSubdirectory("xp-hidden").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "plain.txt"), "");
            File.WriteAllText(Path.Combine(dir, "secret.txt"), "");
            File.SetAttributes(Path.Combine(dir, "secret.txt"), FileAttributes.Hidden);

            var entries = (await _fs.ListAsync(dir)).ToDictionary(e => e.Name, e => e.IsHidden);

            Assert.Equal((false, true), (entries["plain.txt"], entries["secret.txt"]));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task StorageListsTheDesktopTheDrivesAndTheRecycleBin()
    {
        var storage = await _fs.StorageAsync();
        var drives = await _fs.DrivesAsync();
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        Assert.Equal([desktop, .. drives.Select(d => d.Path), "Recycle Bin"], storage.Select(e => e.Path));
    }

    [Fact]
    public async Task StorageOrdersDrivesByLetterBetweenTheDesktopAndTheRecycleBin()
    {
        var paths = (await _fs.StorageAsync()).Select(e => e.Path).ToList();
        var drives = paths[1..^1];

        Assert.Equal("Recycle Bin", paths[^1]);
        Assert.Equal(drives.Order(StringComparer.OrdinalIgnoreCase), drives);
    }

    [Theory]
    [InlineData("*.txt", new[] { "a.txt", "b.txt" })]
    [InlineData("a*", new[] { "a.txt", "arch" })]
    [InlineData("{a,b}.txt", new[] { "a.txt", "b.txt" })]
    [InlineData("*/", new[] { "arch" })]
    [InlineData("x/*.txt", new string[0])]
    public void FlatFilterMatchesNamesOnly(string pattern, string[] expected)
    {
        FsEntry[] entries =
        [
            new(@"C:\bin\1", "a.txt", false, null, 1, "Text"),
            new(@"C:\bin\2", "b.txt", false, null, 1, "Text"),
            new(@"C:\bin\3", "arch", true, null, null, "File folder"),
            new(@"C:\bin\4", "c.md", false, null, 1, "MD"),
        ];

        Assert.Equal(expected, GlobSearch.Filter(entries, pattern).Select(e => e.Name));
    }

    [Fact]
    public async Task ListingFollowsTheProviderOfTheLocation()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(folder.FullName, "a.txt"), "x");

            Assert.Equal(["a.txt"], (await _fs.ListAsync(folder.FullName)).Select(e => e.Name));
        }
        finally
        {
            folder.Delete(true);
        }
    }
}
