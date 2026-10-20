using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HoverExplorer
{
    // Win11 窗口外观：暗色标题区 + 圆角 + 描边色（dwmapi）
    // 亚克力(Mica/Acrylic)需控件全透明才能透出，当前 Label/Panel 不透明故未启用
    internal static class Win11
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);

        [DllImport("user32.dll", EntryPoint = "SetClassLong")]
        static extern uint SetClassLong32(IntPtr hWnd, int nIndex, uint dwNewLong);
        [DllImport("user32.dll", EntryPoint = "SetClassLongPtr")]
        static extern IntPtr SetClassLong64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        const int GCL_STYLE = -26;
        const int CS_DROPSHADOW = 0x00020000;

        static IntPtr SetClassLongPtrSafe(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8 ? SetClassLong64(hWnd, nIndex, dwNewLong) : (IntPtr)SetClassLong32(hWnd, nIndex, (uint)dwNewLong.ToInt32());
        }

        static void Set(IntPtr h, int attr, int v) { DwmSetWindowAttribute(h, attr, ref v, 4); }
        static int Build { get { return Environment.OSVersion.Version.Build; } }

        public static void Apply(IntPtr handle, bool dark, Color? borderColor = null)
        {
            if (handle == IntPtr.Zero) return;
            SetClassLongPtrSafe(handle, GCL_STYLE, (IntPtr)CS_DROPSHADOW);
            Set(handle, 20, dark ? 1 : 0);
            if (Build < 22000) return;
            Set(handle, 33, 1);
        }

        // 对话框用：只要暗色模式与圆角，不加 CS_DROPSHADOW（否则下/右会出现硬边投影）
        public static void ApplyRounded(IntPtr handle, bool dark)
        {
            if (handle == IntPtr.Zero) return;
            Set(handle, 20, dark ? 1 : 0);
            if (Build < 22000) return;
            Set(handle, 33, 2);
        }

        // 控件滚动条深色化：uxtheme 非公开入口 AllowDarkModeForWindow(#133) + DarkMode_Explorer 主题
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        [DllImport("uxtheme.dll", EntryPoint = "#133")]
        static extern bool AllowDarkModeForWindow(IntPtr hWnd, bool allow);

        public static void ApplyScrollTheme(Control c, bool dark)
        {
            if (c == null) return;
            try
            {
                var h = c.Handle;
                if (h == IntPtr.Zero) return;
                AllowDarkModeForWindow(h, dark);
                SetWindowTheme(h, dark ? "DarkMode_Explorer" : "Explorer", null);
            }
            catch { }
        }
    }
}