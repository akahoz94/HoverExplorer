using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace HoverExplorer
{
    // Win11 窗口外观：暗色标题区 + 圆角 + 描边色（dwmapi）
    // 亚克力(Mica/Acrylic)需控件全透明才能透出，当前 Label/Panel 不透明故未启用
    internal static class Win11
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);

        static void Set(IntPtr h, int attr, int v) { DwmSetWindowAttribute(h, attr, ref v, 4); }
        static int Build { get { return Environment.OSVersion.Version.Build; } }

        // DWMWA_USE_IMMERSIVE_DARK_MODE=20, DWMWA_WINDOW_CORNER_PREFERENCE=33(2=Round), DWMWA_BORDER_COLOR=34
        public static void Apply(IntPtr handle, bool dark, Color? borderColor = null)
        {
            if (handle == IntPtr.Zero) return;
            Set(handle, 20, dark ? 1 : 0);
            if (Build < 22000) return;
            Set(handle, 33, 2);
            if (borderColor.HasValue)
            {
                var c = borderColor.Value;
                Set(handle, 34, c.R | (c.G << 8) | (c.B << 16));
            }
        }
    }
}