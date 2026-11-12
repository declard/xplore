using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Xplore.Core.Shell;

public static partial class Shortcuts
{
    [GeneratedRegex(@"^(.*?\.lnk)(\\.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex LnkInPath();

    // The COM objects are held and released explicitly: left to the garbage collector, a wrapper can be finalized while a dynamic call is still using it.
    public static (string Target, string Arguments)? Resolve(string lnk)
    {
        dynamic shell = Shell();
        object? link = null;
        try
        {
            dynamic shortcut = shell.CreateShortcut(lnk);
            link = shortcut;
            var target = (string)shortcut.TargetPath;
            return target.Length == 0 ? null : (target, (string)shortcut.Arguments);
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            Release(link);
            Release((object)shell);
        }
    }

    public static void Create(string lnk, string target)
    {
        dynamic shell = Shell();
        object? link = null;
        try
        {
            dynamic shortcut = shell.CreateShortcut(lnk);
            link = shortcut;
            shortcut.TargetPath = target;
            shortcut.WorkingDirectory = Directory.Exists(target) ? target : Path.GetDirectoryName(target) ?? "";
            shortcut.Save();
        }
        finally
        {
            Release(link);
            Release((object)shell);
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

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            Marshal.FinalReleaseComObject(com);
        }
    }

    private static dynamic Shell() => Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
}
