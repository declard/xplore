using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Xplore.Core.Localization;
using Xplore.Core.Operations;
using IDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Xplore.Core.Shell;

public sealed record SendToTarget(string Name, string Path);

public static class ShellVerbs
{
    private const uint InvokeIdList = 0x0C;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string? lpVerb;
        public string lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIconOrMonitor;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref ShellExecuteInfo info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint attributesIn, out uint attributesOut);

    [DllImport("shell32.dll")]
    private static extern int SHCreateDataObject(IntPtr pidlFolder, uint count, IntPtr[] pidls, IntPtr inner, ref Guid iid, out IntPtr dataObject);

    [DllImport("shell32.dll")]
    private static extern int SHMultiFileProperties(IntPtr dataObject, uint flags);

    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);

    private static Guid IidDataObject = new("0000010e-0000-0000-C000-000000000046");

    public static void Properties(IReadOnlyList<string> paths)
    {
        if (paths is [var single])
        {
            Execute(single, "properties", InvokeIdList);
            return;
        }

        Marshal.ThrowExceptionForHR(WithDataObject(paths, data => SHMultiFileProperties(data, 0)));
    }

    internal static IDataObject CreateDataObject(IReadOnlyList<string> paths) =>
        WithDataObject(paths, data => (IDataObject)Marshal.GetObjectForIUnknown(data));

    private static T WithDataObject<T>(IReadOnlyList<string> paths, Func<IntPtr, T> use)
    {
        var pidls = paths.Select(p =>
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(p, IntPtr.Zero, out var pidl, 0, out _));
            return pidl;
        }).ToArray();
        try
        {
            Marshal.ThrowExceptionForHR(SHCreateDataObject(IntPtr.Zero, (uint)pidls.Length, pidls, IntPtr.Zero, ref IidDataObject, out var data));
            try
            {
                return use(data);
            }
            finally
            {
                Marshal.Release(data);
            }
        }
        finally
        {
            Array.ForEach(pidls, ILFree);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenAsInfo
    {
        public string File;
        public string? Class;
        public uint Flags;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHOpenWithDialog(IntPtr owner, ref OpenAsInfo info);

    private const uint AllowRegistration = 0x1;
    private const uint ExecuteAfterChoice = 0x4;
    private const int ErrorCancelled = unchecked((int)0x800704C7);

    // The "openas" verb fails for files without any association, so the Open With dialog is invoked directly.
    public static bool OpenWith(string path, IntPtr owner = default)
    {
        var info = new OpenAsInfo { File = path, Flags = AllowRegistration | ExecuteAfterChoice };
        var result = SHOpenWithDialog(owner, ref info);
        if (result == ErrorCancelled)
            return false;

        Marshal.ThrowExceptionForHR(result);
        return true;
    }

    private static void Execute(string path, string verb, uint mask)
    {
        var info = new ShellExecuteInfo { lpFile = path, lpVerb = verb, fMask = mask, nShow = 1 };
        info.cbSize = Marshal.SizeOf(info);
        if (!ShellExecuteEx(ref info))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    // Only shortcuts and folders are supported; other SendTo handlers (mail, zip, desktop) need COM drop targets.
    public static IReadOnlyList<SendToTarget> SendToTargets() =>
        Directory.EnumerateFileSystemEntries(Environment.GetFolderPath(Environment.SpecialFolder.SendTo))
            .Where(p => Directory.Exists(p) || Path.GetExtension(p).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            .Select(p => new SendToTarget(Path.GetFileNameWithoutExtension(p), p))
            .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public static Task<IReadOnlyList<SendToTarget>> SendToTargetsAsync() => Task.Run(SendToTargets);

    public static Task SendToAsync(SendToTarget target, IReadOnlyList<string> paths) => Sta.Run(() => SendTo(target, paths));

    public static void SendTo(SendToTarget target, IReadOnlyList<string> paths)
    {
        var (destination, arguments) = Directory.Exists(target.Path)
            ? (target.Path, "")
            : Shortcuts.Resolve(target.Path) ?? throw new IOException(Loc.Get("Error.ShortcutUnresolved"));
        if (Directory.Exists(destination))
        {
            FileOperations.Transfer(paths, destination, move: false);
            return;
        }

        var quoted = string.Join(' ', paths.Select(p => $"\"{p}\""));
        Process.Start(new ProcessStartInfo(destination, $"{arguments} {quoted}".Trim()) { UseShellExecute = true });
    }
}
