using System.Collections.Concurrent;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xplore.Core.Model;
using Xplore.Core.Settings;
using Xplore.Core.Shell;

namespace Xplore.App;

public static class IconCache
{
    private static readonly ConcurrentDictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock ShellLock = new();
    private static readonly HashSet<string> PerFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".lnk", ".ico", ".scr", ".cur", ".url", ".msi",
    };

    public static ImageSource? For(FsEntry e) => e switch
    {
        { Path: RecycleBin.Location } => Cached("recycle-bin", () => ToImage(ShellInfo.IconAt(@"%SystemRoot%\System32\imageres.dll,-54"))),
        { IsDirectory: true, Modified: null } => Load(e.Path, e.Path, true, true),
        { IsDirectory: true } => Folder,
        _ => PerFileExtensions.Contains(Path.GetExtension(e.Name))
            ? Load(e.Path, e.Path, false, true)
            : Load("ext:" + Path.GetExtension(e.Name), "x" + Path.GetExtension(e.Name), false, false),
    };

    public static ImageSource? Folder => Load("dir", "folder", true, false);

    public static ImageSource? ForApp(OpenWithApp app) =>
        Cached("app:" + app.Path, () => FromLocation(app.Icon) ?? (File.Exists(app.Path) ? Load(app.Path, app.Path, false, true) : null));

    private static ImageSource? FromLocation(string? location)
    {
        if (string.IsNullOrEmpty(location))
            return null;

        var comma = location.LastIndexOf(',');
        var file = (comma > 0 ? location[..comma] : location).Trim('"');
        if (!Path.GetExtension(file).Equals(".png", StringComparison.OrdinalIgnoreCase))
            return ToImage(ShellInfo.IconAt(location));

        if (!File.Exists(file))
            return null;

        var png = new BitmapImage();
        png.BeginInit();
        png.UriSource = new Uri(file);
        png.DecodePixelWidth = 16;
        png.CacheOption = BitmapCacheOption.OnLoad;
        png.EndInit();
        png.Freeze();
        return png;
    }

    private static ImageSource? Load(string key, string path, bool isDirectory, bool realPath) =>
        Cached(key, () => ToImage(ShellInfo.SmallIcon(path, isDirectory, realPath)));

    // Concurrent SHGetFileInfo calls for a drive root fail for all of the callers, so the lookups are serialized; failures are not cached.
    private static ImageSource? Cached(string key, Func<ImageSource?> create)
    {
        if (Cache.TryGetValue(key, out var hit))
            return hit;

        lock (ShellLock)
        {
            if (Cache.TryGetValue(key, out hit))
                return hit;

            var image = create();
            if (image is not null)
            {
                Cache[key] = image;
            }

            return image;
        }
    }

    private static ImageSource? ToImage(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return null;

        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        finally
        {
            ShellInfo.DestroyIconHandle(handle);
        }
    }
}
