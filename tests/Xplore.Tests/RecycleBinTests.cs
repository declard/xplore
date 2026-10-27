using Xplore.Core.History;
using Xplore.Core.Operations;

namespace Xplore.Tests;

public class RecycleBinTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("xp-bin").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public async Task UndoOfADeleteRestoresFilesAndFolders()
    {
        var file = Path.Combine(_dir, "bin-restore.txt");
        var folder = Directory.CreateDirectory(Path.Combine(_dir, "bin-restore-dir")).FullName;
        await File.WriteAllTextAsync(file, "x");
        await File.WriteAllTextAsync(Path.Combine(folder, "in.txt"), "y");
        await FileOperations.DeleteAsync([file, folder]);
        Assert.False(File.Exists(file) || Directory.Exists(folder));

        await new DeleteAction([file, folder], [false, true], permanent: false).UndoAsync();

        Assert.True(File.Exists(file));
        Assert.True(File.Exists(Path.Combine(folder, "in.txt")));
    }
}
