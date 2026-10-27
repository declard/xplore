using Xplore.Core.Operations;

namespace Xplore.Tests;

public class ThreadPoolFileOperationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("xp-mta").FullName;
    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public async Task ShellFileOperationsWorkOnAThreadPoolThread()
    {
        Assert.Equal(ApartmentState.MTA, await Task.Run(() => Thread.CurrentThread.GetApartmentState()));
        var sub = Directory.CreateDirectory(Path.Combine(_dir, "sub")).FullName;
        var f = Path.Combine(_dir, "a.txt");
        var d = Directory.CreateDirectory(Path.Combine(_dir, "d")).FullName;
        await File.WriteAllTextAsync(f, "x");
        await File.WriteAllTextAsync(Path.Combine(d, "in.txt"), "y");

        await Task.Run(() => FileOperations.Transfer([f, d], sub, move: false));
        Assert.True(File.Exists(Path.Combine(sub, "a.txt")) && File.Exists(Path.Combine(sub, "d", "in.txt")));

        await Task.Run(() => FileOperations.Transfer([f, d], Directory.CreateDirectory(Path.Combine(_dir, "sub2")).FullName, move: true));
        Assert.False(File.Exists(f) || Directory.Exists(d));

        await Task.Run(() => FileOperations.Move(Path.Combine(sub, "a.txt"), Path.Combine(_dir, "m.txt")));
        await Task.Run(() => FileOperations.Copy(Path.Combine(_dir, "m.txt"), Path.Combine(_dir, "c.txt")));
        Assert.True(File.Exists(Path.Combine(_dir, "c.txt")));

        await Task.Run(() => FileOperations.Delete([Path.Combine(_dir, "m.txt")], permanent: false));
        Assert.False(File.Exists(Path.Combine(_dir, "m.txt")));
        await Task.Run(() => FileOperations.Delete([Path.Combine(sub, "d")], permanent: true));
        Assert.False(Directory.Exists(Path.Combine(sub, "d")));
    }
}
