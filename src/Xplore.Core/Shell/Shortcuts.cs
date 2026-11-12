using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.RegularExpressions;

namespace Xplore.Core.Shell;

public static partial class Shortcuts
{
    private const int MaxPath = 32768;
    private const int ArgumentsMax = 32768;

    [GeneratedRegex(@"^(.*?\.lnk)(\\.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex LnkInPath();

    public static (string Target, string Arguments)? Resolve(string lnk)
    {
        var link = NewLink();
        try
        {
            ((IPersistFile)link).Load(lnk, 0);
            var target = new StringBuilder(MaxPath);
            var arguments = new StringBuilder(ArgumentsMax);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
            link.GetArguments(arguments, arguments.Capacity);
            return target.Length == 0 ? null : (target.ToString(), arguments.ToString());
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    public static void Create(string lnk, string target)
    {
        var link = NewLink();
        try
        {
            link.SetPath(target);
            link.SetWorkingDirectory(Directory.Exists(target) ? target : Path.GetDirectoryName(target) ?? "");
            ((IPersistFile)link).Save(lnk, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    // Lets a path walk through shortcuts to folders: "C:\links\proj.lnk\src" becomes "D:\work\proj\src".
    public static string ResolvePath(string path)
    {
        for (var hops = 0; hops < 8; hops++)
        {
            var m = LnkInPath().Match(path);
            if (!m.Success || !File.Exists(m.Groups[1].Value))
                return path;

            if (Resolve(m.Groups[1].Value) is not { Target: var target } || !Directory.Exists(target))
                return path;

            var rest = m.Groups[2].Value;
            path = rest.Length == 0 ? target : target.TrimEnd('\\') + rest;
        }

        return path;
    }

    public static Task<string> ResolvePathAsync(string path) => Sta.Run(() => ResolvePath(path));

    public static Task<(string Target, string Arguments)?> ResolveAsync(string lnk) => Sta.Run(() => Resolve(lnk));

    // A typed interface rather than WScript.Shell through `dynamic`: the dynamic COM binder shares cached state between the short-lived apartment threads and failed intermittently.
    private static IShellLink NewLink() => (IShellLink)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))!)!;

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLink
    {
        void GetPath([MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, IntPtr findData, uint flags);

        void GetIDList(out IntPtr pidl);

        void SetIDList(IntPtr pidl);

        void GetDescription([MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int max);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int max);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);

        void GetArguments([MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int max);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);

        void GetHotkey(out short hotkey);

        void SetHotkey(short hotkey);

        void GetShowCmd(out int show);

        void SetShowCmd(int show);

        void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int max, out int index);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);

        void Resolve(IntPtr owner, uint flags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
