using System;
using System.Drawing;

namespace HoverExplorer
{
    // 面板/预览窗的语义配色：由桌面卡片的标签带配色（CardPalette）派生而来。
    // 旧的"深色/浅色主题"已移除——现在所有界面配色统一走 CardPalettes 的 11 套方案，
    // 列表与预览各自选一套（互相独立），卡片仍是逐张独立。
    internal sealed class PanelPalette
    {
        public Color Bg, Fg, BgHover, BgSel, FgSel, Accent, Border, PlaceholderFg, BgDir, FgDir, DropHighlight;
        public bool Dark;
        public CardStyle Style;      // 面板风格化装饰（来自所选配色）
        public bool Textured;        // 有背景纹理（扫描线/网格）时面板表面要透出来
        public int BorderWidth;      // 描边宽度（跟随配色）

        public static PanelPalette From(CardPalette p)
        {
            bool dark = CardPalette.Lum(p.Bg) < 128;
            var r = new PanelPalette
            {
                Bg = p.Bg,
                Fg = p.Fg,
                BgHover = p.Hover,
                // 选中/拖放提示：用配色的主题色（folder）掺一点底色，弱于纯主题色但可辨
                BgSel = CardPalette.Mix(p.Folder, p.Bg, dark ? 0.30 : 0.22),
                Accent = p.Folder,
                Border = CardPalette.Mix(p.Fg, p.Bg, 0.22),
                PlaceholderFg = CardPalette.Mix(p.Fg, p.Bg, 0.45),
                BgDir = CardPalette.Mix(p.Doc, p.Bg, 0.16),
                FgDir = p.Doc,
                DropHighlight = CardPalette.Mix(p.Image, p.Bg, 0.35),
                Dark = dark,
                Style = p.Style ?? CardStyle.None,
                BorderWidth = p.BorderWidth,
            };
            r.FgSel = CardPalette.Lum(r.BgSel) < 128 ? Color.White : Color.Black;
            r.Textured = r.Style.Scanline || r.Style.Grid;
            return r;
        }
    }

    // 配色代理：按索引缓存派生结果，索引变化后调 Invalidate() 重算。
    // 图标缓存键用 Tag 区分（跟着配色走，否则换色后图标还是旧色）
    internal sealed class PaletteSet
    {
        private readonly Func<int> _index;
        private int _cachedIdx = int.MinValue;
        private PanelPalette _cur;

        public PaletteSet(Func<int> index) { _index = index; }

        public PanelPalette Pal
        {
            get
            {
                int i = _index();
                if (i < 0 || i >= CardPalettes.All.Count) i = 0;   // 旧配置里的 -1（跟随主题）按第 1 套算
                if (_cachedIdx != i)
                {
                    _cachedIdx = i;
                    _cur = PanelPalette.From(CardPalettes.All[i]);
                }
                return _cur;
            }
        }

        public void Invalidate() { _cachedIdx = int.MinValue; }

        public string Tag { get { return "p" + _index(); } }

        public bool Dark { get { return Pal.Dark; } }
        public Color BG { get { return Pal.Bg; } }
        public Color FG { get { return Pal.Fg; } }
        public Color BgHover { get { return Pal.BgHover; } }
        public Color BgSel { get { return Pal.BgSel; } }
        public Color FgSel { get { return Pal.FgSel; } }
        public Color Accent { get { return Pal.Accent; } }
        public Color Border { get { return Pal.Border; } }
        public Color PlaceholderFg { get { return Pal.PlaceholderFg; } }
        public Color BgDir { get { return Pal.BgDir; } }
        public Color FgDir { get { return Pal.FgDir; } }
        public Color DropHighlight { get { return Pal.DropHighlight; } }
    }

    // 文件夹列表面板配色（滚轮在面板操作栏滚动切换）
    internal static class ListPal
    {
        private static readonly PaletteSet _set = new PaletteSet(() => Config.ListPaletteIndex);
        public static void Invalidate() { _set.Invalidate(); }
        public static PanelPalette Pal { get { return _set.Pal; } }
        public static string Tag { get { return _set.Tag; } }
        public static bool Dark { get { return _set.Dark; } }
        public static Color BG { get { return _set.BG; } }
        public static Color FG { get { return _set.FG; } }
        public static Color BgHover { get { return _set.BgHover; } }
        public static Color BgSel { get { return _set.BgSel; } }
        public static Color FgSel { get { return _set.FgSel; } }
        public static Color Accent { get { return _set.Accent; } }
        public static Color Border { get { return _set.Border; } }
        public static Color PlaceholderFg { get { return _set.PlaceholderFg; } }
        public static Color BgDir { get { return _set.BgDir; } }
        public static Color FgDir { get { return _set.FgDir; } }
        public static Color DropHighlight { get { return _set.DropHighlight; } }
    }

    // 预览窗口配色（滚轮在预览窗操作栏滚动切换）
    internal static class PvPal
    {
        private static readonly PaletteSet _set = new PaletteSet(() => Config.PreviewPaletteIndex);
        public static void Invalidate() { _set.Invalidate(); }
        public static PanelPalette Pal { get { return _set.Pal; } }
        public static string Tag { get { return _set.Tag; } }
        public static bool Dark { get { return _set.Dark; } }
        public static Color BG { get { return _set.BG; } }
        public static Color FG { get { return _set.FG; } }
        public static Color BgHover { get { return _set.BgHover; } }
        public static Color BgSel { get { return _set.BgSel; } }
        public static Color FgSel { get { return _set.FgSel; } }
        public static Color Accent { get { return _set.Accent; } }
        public static Color Border { get { return _set.Border; } }
        public static Color PlaceholderFg { get { return _set.PlaceholderFg; } }
        public static Color BgDir { get { return _set.BgDir; } }
        public static Color FgDir { get { return _set.FgDir; } }
        public static Color DropHighlight { get { return _set.DropHighlight; } }
    }
}
