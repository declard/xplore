using System.Runtime.InteropServices;

namespace Xplore.Core.Shell;

// One entry of a shell context menu: Id is what Invoke takes, Verb is the language-independent command name (null for submenus and many third-party items), Bitmap is the handler's own icon (valid while the menu lives).
public sealed record ShellMenuItem(
    string Text,
    int Id,
    string? Verb,
    bool Enabled,
    bool Checked,
    bool IsSeparator,
    IntPtr Bitmap,
    IReadOnlyList<ShellMenuItem> Children,
    bool IsSubmenu = false)
{
    public static ShellMenuItem Separator { get; } = new("", 0, null, false, false, true, IntPtr.Zero, []);
}

// The menu the shell shows for a selection (or a folder's background), including the registered third-party handlers (7-Zip, Git Extensions...), read without showing it.
// Must be created, invoked and disposed on one STA thread with a message pump: handlers are in-process COM objects.
public sealed partial class ShellContextMenu : IDisposable
{
    private const uint FirstId = 1;
    private const uint LastId = 0x7FFF;
    private const uint CmfExplore = 0x4;
    private const uint CmicMaskUnicode = 0x4000;
    private const uint GcsVerbW = 4;
    private const uint WmInitMenuPopup = 0x117;
    private const uint MfsGrayedOrDisabled = 0x3;
    private const uint MfsChecked = 0x8;
    private const uint MftSeparator = 0x800;
    private const uint MftOwnerDraw = 0x100;

    private static Guid IidShellFolder = new("000214E6-0000-0000-C000-000000000046");
    private static Guid IidContextMenu = new("000214e4-0000-0000-c000-000000000046");

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InvokeInfo
    {
        public uint cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr lpTitle;
        public IntPtr lpVerbW;
        public IntPtr lpParametersW;
        public IntPtr lpDirectoryW;
        public IntPtr lpTitleW;
        public Point ptInvoke;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MenuItemInfo
    {
        public uint cbSize;
        public uint fMask;
        public uint fType;
        public uint fState;
        public uint wID;
        public IntPtr hSubMenu;
        public IntPtr hbmpChecked;
        public IntPtr hbmpUnchecked;
        public UIntPtr dwItemData;
        public IntPtr dwTypeData;
        public uint cch;
        public IntPtr hbmpItem;
    }

    [ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);

        [PreserveSig]
        int InvokeCommand(ref InvokeInfo info);

        [PreserveSig]
        int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint cchMax);
    }

    [ComImport, Guid("000214f4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2 : IContextMenu
    {
        [PreserveSig]
        new int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);

        [PreserveSig]
        new int InvokeCommand(ref InvokeInfo info);

        [PreserveSig]
        new int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint cchMax);

        [PreserveSig]
        int HandleMenuMsg(uint msg, IntPtr wParam, IntPtr lParam);
    }

    [ComImport, Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3 : IContextMenu2
    {
        [PreserveSig]
        new int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);

        [PreserveSig]
        new int InvokeCommand(ref InvokeInfo info);

        [PreserveSig]
        new int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint cchMax);

        [PreserveSig]
        new int HandleMenuMsg(uint msg, IntPtr wParam, IntPtr lParam);

        [PreserveSig]
        int HandleMenuMsg2(uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result);
    }

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig]
        int ParseDisplayName(IntPtr hwnd, IntPtr bindingContext, [MarshalAs(UnmanagedType.LPWStr)] string name, out uint eaten, out IntPtr pidl, ref uint attributes);

        [PreserveSig]
        int EnumObjects(IntPtr hwnd, uint flags, out IntPtr enumIdList);

        [PreserveSig]
        int BindToObject(IntPtr pidl, IntPtr bindingContext, ref Guid iid, out IntPtr result);

        [PreserveSig]
        int BindToStorage(IntPtr pidl, IntPtr bindingContext, ref Guid iid, out IntPtr result);

        [PreserveSig]
        int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);

        [PreserveSig]
        int CreateViewObject(IntPtr owner, ref Guid iid, out IntPtr result);

        [PreserveSig]
        int GetAttributesOf(uint count, IntPtr pidls, ref uint attributes);

        [PreserveSig]
        int GetUIObjectOf(IntPtr owner, uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] pidls, ref Guid iid, IntPtr reserved, out IntPtr result);

        [PreserveSig]
        int GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr name);

        [PreserveSig]
        int SetNameOf(IntPtr owner, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IntPtr newPidl);
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint attributesIn, out uint attributesOut);

    [LibraryImport("shell32.dll")]
    private static partial int SHBindToParent(IntPtr pidl, ref Guid iid, out IntPtr result, out IntPtr pidlLast);

    [LibraryImport("shell32.dll")]
    private static partial int SHBindToObject(IntPtr folder, IntPtr pidl, IntPtr bindingContext, ref Guid iid, out IntPtr result);

    [LibraryImport("shell32.dll")]
    private static partial void ILFree(IntPtr pidl);

    [LibraryImport("user32.dll")]
    private static partial IntPtr CreatePopupMenu();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(IntPtr menu);

    [LibraryImport("user32.dll")]
    private static partial int GetMenuItemCount(IntPtr menu);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMenuItemInfoW(IntPtr menu, uint item, [MarshalAs(UnmanagedType.Bool)] bool byPosition, ref MenuItemInfo info);

    private readonly IContextMenu _menu;
    private readonly IntPtr _hmenu;
    private readonly string _directory;

    private ShellContextMenu(IContextMenu menu, string directory)
    {
        _menu = menu;
        _directory = directory;
        _hmenu = CreatePopupMenu();
        Marshal.ThrowExceptionForHR(_menu.QueryContextMenu(_hmenu, 0, FirstId, LastId, CmfExplore));
        Items = Read(_hmenu);
    }

    public IReadOnlyList<ShellMenuItem> Items { get; }

    // The items share a folder: the menu is the one of the first item's folder, and the items elsewhere are left out.
    public static ShellContextMenu ForItems(IReadOnlyList<string> paths, IntPtr owner)
    {
        var folder = Path.GetDirectoryName(paths[0]);
        var same = paths.Where(p => string.Equals(Path.GetDirectoryName(p), folder, StringComparison.OrdinalIgnoreCase)).ToList();
        var pidls = same.Select(Parse).ToList();
        try
        {
            Marshal.ThrowExceptionForHR(SHBindToParent(pidls[0], ref IidShellFolder, out var parent, out _));
            var shellFolder = ToObject<IShellFolder>(parent);
            var children = pidls.Select(ChildId).ToArray();
            Marshal.ThrowExceptionForHR(shellFolder.GetUIObjectOf(owner, (uint)children.Length, children, ref IidContextMenu, IntPtr.Zero, out var menu));
            return new(ToObject<IContextMenu>(menu), folder ?? "");
        }
        finally
        {
            pidls.ForEach(ILFree);
        }
    }

    public static ShellContextMenu ForFolder(string folder, IntPtr owner)
    {
        var pidl = Parse(folder);
        try
        {
            Marshal.ThrowExceptionForHR(SHBindToObject(IntPtr.Zero, pidl, IntPtr.Zero, ref IidShellFolder, out var bound));
            var shellFolder = ToObject<IShellFolder>(bound);
            Marshal.ThrowExceptionForHR(shellFolder.CreateViewObject(owner, ref IidContextMenu, out var menu));
            return new(ToObject<IContextMenu>(menu), folder);
        }
        finally
        {
            ILFree(pidl);
        }
    }

    public void Invoke(int id, IntPtr owner) => InvokeCore(owner, verb => (IntPtr)(id - (int)FirstId), verbW => (IntPtr)(id - (int)FirstId));

    // Runs a command by its canonical name ("properties") the way Explorer does, which also covers selections of several items.
    public void InvokeVerb(string verb, IntPtr owner)
    {
        var ansi = Marshal.StringToHGlobalAnsi(verb);
        var unicode = Marshal.StringToHGlobalUni(verb);
        try
        {
            InvokeCore(owner, _ => ansi, _ => unicode);
        }
        finally
        {
            Marshal.FreeHGlobal(ansi);
            Marshal.FreeHGlobal(unicode);
        }
    }

    private void InvokeCore(IntPtr owner, Func<int, IntPtr> verbA, Func<int, IntPtr> verbW)
    {
        var directory = Marshal.StringToHGlobalUni(_directory);
        var directoryAnsi = Marshal.StringToHGlobalAnsi(_directory);
        try
        {
            var info = new InvokeInfo
            {
                fMask = CmicMaskUnicode,
                hwnd = owner,
                lpVerb = verbA(0),
                lpVerbW = verbW(0),
                lpDirectory = directoryAnsi,
                lpDirectoryW = directory,
                nShow = 1,
            };
            info.cbSize = (uint)Marshal.SizeOf(info);
            Marshal.ThrowExceptionForHR(_menu.InvokeCommand(ref info));
        }
        finally
        {
            Marshal.FreeHGlobal(directory);
            Marshal.FreeHGlobal(directoryAnsi);
        }
    }

    public void Dispose()
    {
        DestroyMenu(_hmenu);
        Marshal.ReleaseComObject(_menu);
    }

    private static IntPtr Parse(string path)
    {
        Marshal.ThrowExceptionForHR(SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _));
        return pidl;
    }

    // The last item id of an absolute id list; it points into the list and must not be freed.
    private static IntPtr ChildId(IntPtr pidl)
    {
        var last = pidl;
        for (var next = pidl; Marshal.ReadInt16(next) is var size and > 0; next += size)
        {
            last = next;
        }

        return last;
    }

    private static T ToObject<T>(IntPtr unknown)
        where T : class
    {
        try
        {
            return (T)Marshal.GetObjectForIUnknown(unknown);
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    private List<ShellMenuItem> Read(IntPtr hmenu)
    {
        var items = new List<ShellMenuItem>();
        var buffer = Marshal.AllocHGlobal(1024 * sizeof(char));
        try
        {
            for (var i = 0; i < GetMenuItemCount(hmenu); i++)
            {
                if (ReadItem(hmenu, i, buffer) is { } item)
                {
                    items.Add(item);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return items;
    }

    private ShellMenuItem? ReadItem(IntPtr hmenu, int index, IntPtr buffer)
    {
        const uint mask = 0x1 | 0x2 | 0x4 | 0x40 | 0x80 | 0x100;
        var info = new MenuItemInfo { fMask = mask, dwTypeData = buffer, cch = 1023 };
        info.cbSize = (uint)Marshal.SizeOf(info);
        if (!GetMenuItemInfoW(hmenu, (uint)index, true, ref info))
            return null;

        if ((info.fType & MftSeparator) != 0)
            return ShellMenuItem.Separator;

        if ((info.fType & MftOwnerDraw) != 0 && info.cch == 0)
            return null;

        var text = Marshal.PtrToStringUni(buffer, (int)info.cch);
        var children = info.hSubMenu == IntPtr.Zero ? [] : ReadSubmenu(info.hSubMenu, index);
        var verb = info.hSubMenu == IntPtr.Zero ? VerbOf((int)info.wID, buffer) : null;
        var bitmap = (long)info.hbmpItem > 12 ? info.hbmpItem : IntPtr.Zero;
        return new(text, (int)info.wID, verb, (info.fState & MfsGrayedOrDisabled) == 0, (info.fState & MfsChecked) != 0, false, bitmap, children, info.hSubMenu != IntPtr.Zero);
    }

    // Handlers fill their submenus when the window is told the submenu opens.
    private List<ShellMenuItem> ReadSubmenu(IntPtr submenu, int index)
    {
        var lParam = (IntPtr)(index & 0xFFFF);
        try
        {
            if (_menu is IContextMenu3 three)
            {
                three.HandleMenuMsg2(WmInitMenuPopup, submenu, lParam, out _);
            }
            else if (_menu is IContextMenu2 two)
            {
                two.HandleMenuMsg(WmInitMenuPopup, submenu, lParam);
            }
        }
        catch (COMException)
        {
            // the submenu is shown as the handler left it
        }

        return Read(submenu);
    }

    private string? VerbOf(int id, IntPtr buffer)
    {
        if (id < FirstId || id > LastId)
            return null;

        try
        {
            var result = _menu.GetCommandString((UIntPtr)(id - FirstId), GcsVerbW, IntPtr.Zero, buffer, 512);
            return result == 0 ? Marshal.PtrToStringUni(buffer) : null;
        }
        catch (COMException)
        {
            return null;
        }
    }
}
