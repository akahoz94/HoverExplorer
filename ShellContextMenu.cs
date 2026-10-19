using System;
using System.Runtime.InteropServices;
using System.Text;

namespace HoverExplorer
{
    // Windows shell 原生右键菜单：通过 IContextMenu COM 接口弹出资源管理器同款菜单
    internal static class ShellContextMenu
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        struct CMINVOKECOMMANDINFO
        {
            public int cbSize;
            public int fMask;
            public IntPtr hwnd;
            [MarshalAs(UnmanagedType.LPStr)] public string lpVerb;
            [MarshalAs(UnmanagedType.LPStr)] public string lpParameters;
            [MarshalAs(UnmanagedType.LPStr)] public string lpDirectory;
            public int nShow;
            public int dwHotKey;
            public IntPtr hIcon;
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214E4-0000-0000-C000-000000000046")]
        interface IContextMenu
        {
            [PreserveSig] int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
            [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFO info);
            [PreserveSig] int GetCommandString(IntPtr idCmd, uint uType, IntPtr pwReserved, [MarshalAs(UnmanagedType.LPStr)] StringBuilder pszName, uint cchMax);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214E6-0000-0000-C000-000000000046")]
        interface IShellFolder
        {
            [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, ref uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);
            [PreserveSig] int EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenumIDList);
            [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, [In] ref Guid riid, out IntPtr ppv);
            [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, [In] ref Guid riid, out IntPtr ppv);
            [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
            [PreserveSig] int CreateViewObject(IntPtr hwnd, [In] ref Guid riid, out IntPtr ppv);
            [PreserveSig] int GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint rgfInOut);
            [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, [In] ref Guid riid, ref uint rgfReserved, out IntPtr ppv);
            [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint uFlags, IntPtr pName);
            [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHParseDisplayName([MarshalAs(UnmanagedType.LPWStr)] string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);
        [DllImport("shell32.dll")]
        static extern int SHBindToParent(IntPtr pidl, [In] ref Guid riid, out IntPtr ppv, out IntPtr ppidlLast);
        [DllImport("user32.dll")]
        static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll")]
        static extern bool DestroyMenu(IntPtr hMenu);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hwnd, IntPtr prcRect);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);
        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT lpPoint);
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

        const uint CMF_NORMAL = 0;
        const uint TPM_RETURNCMD = 0x0100;
        const uint TPM_LEFTALIGN = 0x0000;
        const uint TPM_TOPALIGN = 0x0000;
        const uint MF_SEPARATOR = 0x0800;
        const uint MF_STRING = 0x0000;
        const uint CMD_UNPIN = 0x8000;
        static readonly Guid IID_IContextMenu = new Guid("000214E4-0000-0000-C000-000000000046");
        static readonly Guid IID_IShellFolder = new Guid("000214E6-0000-0000-C000-000000000046");

        public static void Show(string path, IntPtr hwndOwner, Action onUnpin)
        {
            try
            {
                var iidSF = IID_IShellFolder;
                var iidCM = IID_IContextMenu;
                uint attr;
                IntPtr pidlFull;
                if (SHParseDisplayName(path, IntPtr.Zero, out pidlFull, 0, out attr) != 0 || pidlFull == IntPtr.Zero) return;
                IntPtr sfPtr;
                IntPtr pidlLast;
                if (SHBindToParent(pidlFull, ref iidSF, out sfPtr, out pidlLast) != 0) { Marshal.FreeCoTaskMem(pidlFull); return; }
                var sf = (IShellFolder)Marshal.GetObjectForIUnknown(sfPtr);
                var apidl = new IntPtr[] { pidlLast };
                uint reserved = 0;
                IntPtr cmPtr;
                sf.GetUIObjectOf(hwndOwner, 1, apidl, ref iidCM, ref reserved, out cmPtr);
                Marshal.Release(sfPtr);
                if (cmPtr == IntPtr.Zero) { Marshal.FreeCoTaskMem(pidlFull); return; }
                var cm = (IContextMenu)Marshal.GetObjectForIUnknown(cmPtr);
                var hMenu = CreatePopupMenu();
                cm.QueryContextMenu(hMenu, 0, 1, 0x7FFF, CMF_NORMAL);
                if (onUnpin != null)
                {
                    AppendMenu(hMenu, MF_SEPARATOR, 0, null);
                    AppendMenu(hMenu, MF_STRING, CMD_UNPIN, I18n.T("unpin"));
                }
                POINT pt; GetCursorPos(out pt);
                Program.ShellMenuOpen = true;
                Program.DebugLog("[shell] menu opening, flag=true");
                int cmd;
                try { cmd = TrackPopupMenu(hMenu, TPM_LEFTALIGN | TPM_TOPALIGN | TPM_RETURNCMD, pt.X, pt.Y, 0, hwndOwner, IntPtr.Zero); }
                finally { Program.ShellMenuOpen = false; Program.DebugLog("[shell] menu closed, flag=false"); }
                if (cmd == CMD_UNPIN) { if (onUnpin != null) onUnpin(); }
                else if (cmd > 0)
                {
                    var info = new CMINVOKECOMMANDINFO
                    {
                        cbSize = Marshal.SizeOf(typeof(CMINVOKECOMMANDINFO)),
                        lpVerb = (cmd - 1).ToString(),
                        nShow = 1,
                        hwnd = hwndOwner,
                    };
                    cm.InvokeCommand(ref info);
                }
                DestroyMenu(hMenu);
                Marshal.Release(cmPtr);
                Marshal.FreeCoTaskMem(pidlFull);
            }
            catch (Exception ex) { Program.DebugLog("[shellmenu] " + ex.Message); }
        }
    }
}