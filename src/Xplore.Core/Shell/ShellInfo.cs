using System.Collections.Concurrent;
using Xplore.Core.Localization;
using System.Runtime.InteropServices;

namespace Xplore.Core.Shell;

public static class ShellInfo
{
    private const uint Icon = 0x100;
    private const uint SmallIconFlag = 0x1;
    private const uint TypeNameFlag = 0x400;
    private const uint UseFileAttributes = 0x10;
    private const uint AttrDirectory = 0x10;
    private const uint AttrNormal = 0x80;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attrs, ref ShFileInfo info, uint size, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, uint count);

    // Small icon at an icon location such as "C:\app.exe,0"; the caller releases it with DestroyIconHandle. IntPtr.Zero when unavailable.
    public static IntPtr IconAt(string location)
    {
        var comma = location.LastIndexOf(',');
        var (file, index) = comma > 0 && int.TryParse(location[(comma + 1)..], out var i) ? (location[..comma], i) : (location, 0);
        var small = new IntPtr[1];
        return ExtractIconEx(Environment.ExpandEnvironmentVariables(file.Trim('"')), index, null, small, 1) > 0 ? small[0] : IntPtr.Zero;
    }

    private static readonly ConcurrentDictionary<string, string> TypeNames = new(StringComparer.OrdinalIgnoreCase);

    // Explorer's own type description ("Text Document", "PNG File"), cached per extension.
    public static string TypeName(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return TypeNames.GetOrAdd(ext, _ =>
        {
            var info = new ShFileInfo();
            SHGetFileInfo("x" + ext, AttrNormal, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), TypeNameFlag | UseFileAttributes);
            return string.IsNullOrEmpty(info.szTypeName) ? Loc.Get("Type.File") : info.szTypeName;
        });
    }

    // Returns a small-icon HICON the caller must release with DestroyIconHandle; IntPtr.Zero when unavailable.
    // realPath=false resolves by extension/attributes only (fast, no disk access).
    public static IntPtr SmallIcon(string path, bool isDirectory, bool realPath)
    {
        var info = new ShFileInfo();
        var flags = Icon | SmallIconFlag | (realPath ? 0 : UseFileAttributes);
        SHGetFileInfo(path, isDirectory ? AttrDirectory : AttrNormal, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), flags);
        return info.hIcon;
    }

    public static void DestroyIconHandle(IntPtr handle) => DestroyIcon(handle);
}
