using Xplore.Core.Locations;
using Xplore.Core.Shell;

namespace Xplore.Tests;

public class RecycleBinPurgeTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory();

    public void Dispose() => _root.Delete(true);

    [Fact]
    public void PurgeDeletesTheItemAndItsInfoRecord()
    {
        var bin = _root.CreateSubdirectory(@"$Recycle.Bin\S-1-5-21");
        var file = Path.Combine(bin.FullName, "$RAB12.txt");
        var info = Path.Combine(bin.FullName, "$IAB12.txt");
        var folder = bin.CreateSubdirectory("$RCD34");
        File.WriteAllText(file, "x");
        File.WriteAllText(info, "meta");
        File.WriteAllText(Path.Combine(folder.FullName, "inner.txt"), "y");
        File.WriteAllText(Path.Combine(bin.FullName, "$ICD34"), "meta");

        RecycleBin.Purge([file, folder.FullName]);

        Assert.Empty(bin.GetFileSystemInfos());
    }

    [Fact]
    public void PurgeRefusesAnythingOutsideTheBin()
    {
        var file = Path.Combine(_root.FullName, "$Rkeep.txt");
        File.WriteAllText(file, "x");

        Assert.Throws<ArgumentException>(() => RecycleBin.Purge([file]));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void TheBinOffersRestoreDeleteAndEmpty()
    {
        var verbs = new RecycleBinLocationProvider().Verbs;

        Assert.Equal([("Restore", VerbRole.Item), ("Delete", VerbRole.Delete), ("Empty Recycle Bin", VerbRole.Location)], verbs.Select(v => (v.Name, v.Role)));
    }
}
