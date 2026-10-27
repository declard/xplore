using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Xplore.Core.Shell;

public static partial class Shortcuts
{
    [GeneratedRegex(@"^(.*?\.lnk)(\\.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex LnkInPath();

    public static (string Target, string Arguments)? Resolve(string lnk)
    {
        try
        {
            dynamic link = Shell().CreateShortcut(lnk);
            var target = (string)link.TargetPath;
            return target.Length == 0 ? null : (target, (string)link.Arguments);
        }
        catch (COMException)
        {
            return null;
        }
    }

    public static void Create(string lnk, string target)
    {
        dynamic link = Shell().CreateShortcut(lnk);
        link.TargetPath = target;
        link.WorkingDirectory = Directory.Exists(target) ? target : Path.GetDirectoryName(target) ?? "";
        link.Save();
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

    private static dynamic Shell() => Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
}
