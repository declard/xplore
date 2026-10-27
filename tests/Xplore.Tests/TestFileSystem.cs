using Xplore.Core.Locations;
using Xplore.Core.Model;

namespace Xplore.Tests;

internal static class TestFileSystem
{
    public static FileSystemService Create() => new(new FileLocationProvider(), [new RecycleBinLocationProvider()]);
}
