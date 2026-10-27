using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Xplore.Core.Settings;

namespace Xplore.Core.Shell;

public sealed record AssocHandler(OpenWithApp App, bool Recommended);

// The shell's list of applications that can open a file type, and launching a file with one of them.
public static class AssocHandlers
{
    [ComImport, Guid("F04061AC-1659-4A3F-A954-775AA57FC083"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAssocHandler
    {
        void GetName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void GetUIName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] out string path, out int index);
        [PreserveSig] int IsRecommended();
        void MakeDefault([MarshalAs(UnmanagedType.LPWStr)] string description);
        void Invoke(IDataObject dataObject);
        void CreateInvoker(IDataObject dataObject, out IntPtr invoker);
    }

    [ComImport, Guid("973810AE-9599-4B88-9E4D-6EE98C9552DA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumAssocHandlers
    {
        [PreserveSig] int Next(uint count, [MarshalAs(UnmanagedType.Interface)] out IAssocHandler handler, out uint fetched);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHAssocEnumHandlers(string extension, int filter, out IEnumAssocHandlers handlers);

    private const int FilterNone = 0;
    private const int FilterRecommended = 1;

    public static IReadOnlyList<AssocHandler> List(string path)
    {
        var recommended = Enumerate(path, FilterRecommended).Select(h => h.App.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Enumerate(path, FilterNone)
            .Select(h => h with { Recommended = recommended.Contains(h.App.Path) })
            .DistinctBy(h => h.App.Path, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(h => h.Recommended)
            .ThenBy(h => h.App.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static void Launch(OpenWithApp app, IReadOnlyList<string> files)
    {
        if (File.Exists(app.Path))
        {
            files.ToList().ForEach(f => Process.Start(new ProcessStartInfo(app.Path) { ArgumentList = { f } }));
            return;
        }

        Visit(files[0], FilterNone, (id, _, _, handler) =>
        {
            if (!string.Equals(id, app.Path, StringComparison.OrdinalIgnoreCase))
                return false;

            handler.Invoke(ShellVerbs.CreateDataObject(files));
            return true;
        });
    }

    private static List<AssocHandler> Enumerate(string path, int filter)
    {
        List<AssocHandler> found = [];
        Visit(path, filter, (id, name, icon, _) =>
        {
            found.Add(new(new(name, id, icon), false));
            return false;
        });
        return found;
    }

    // Calls visit for each handler until it returns true.
    private static void Visit(string path, int filter, Func<string, string, string?, IAssocHandler, bool> visit)
    {
        var extension = Path.GetExtension(path) is { Length: > 0 } e ? e : ".";
        if (SHAssocEnumHandlers(extension, filter, out var handlers) != 0)
            return;

        try
        {
            while (handlers.Next(1, out var handler, out var fetched) == 0 && fetched == 1)
            {
                try
                {
                    handler.GetName(out var id);
                    handler.GetUIName(out var name);
                    handler.GetIconLocation(out var iconPath, out var iconIndex);
                    var icon = iconPath is { Length: > 0 } ? $"{iconPath},{iconIndex}" : null;
                    if (visit(id, name, icon, handler))
                        return;
                }
                finally
                {
                    Marshal.ReleaseComObject(handler);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(handlers);
        }
    }
}
