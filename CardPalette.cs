using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace HoverExplorer
{
    // 桌面卡片·标签带配色方案。
    // 与面板主题（Config.Theme）完全独立：卡片配色由 Config.CardPaletteIndex 单独记忆，
    // 鼠标在卡片上滚轮即循环切换（Program.CardWheelFilter）。
    // 每套配色含：卡片底色/文字/硬阴影/描边/徽章文字/文件夹卡片文字 + 9 类文件类型色
    // （script 脚本、doc 文档、image 图像、text 文本、link 链接、lnk 快捷方式、pdf、code 代码、folder 文件夹）。
    internal sealed class CardPalette
    {
        public readonly string Name;
        public readonly Color Bg;         // 卡片底色
        public readonly Color Fg;         // 主文字
        public readonly Color Shadow;     // 右下 2px 硬阴影（透明 = 不画）
        public readonly Color Border;     // 描边色
        public readonly int BorderWidth;  // 描边宽度（0 = 不画）
        public readonly Color BadgeText;  // 扩展名徽章文字色
        public readonly Color FolderText; // 文件夹卡片（整条填类型色）的文字色
        public readonly Color Script, Doc, Image, Text, Link, Lnk, Pdf, Code, Folder;
        public readonly Color Hover;      // 悬停底色（由 Bg 明暗自动推导）
        public readonly Color Sub;        // 副标题文字色（Fg 与 Bg 的中间调）

        public CardPalette(string name, string bg, string fg, Color shadow, string border, int borderWidth,
            string badgeText, string folderText,
            string script, string doc, string image, string text, string link, string lnk, string pdf, string code, string folder)
        {
            Name = name;
            Bg = Hx(bg);
            Fg = Hx(fg);
            Shadow = shadow;
            Border = Hx(border);
            BorderWidth = borderWidth;
            BadgeText = Hx(badgeText);
            FolderText = Hx(folderText);
            Script = Hx(script);
            Doc = Hx(doc);
            Image = Hx(image);
            Text = Hx(text);
            Link = Hx(link);
            Lnk = Hx(lnk);
            Pdf = Hx(pdf);
            Code = Hx(code);
            Folder = Hx(folder);
            bool dark = Lum(Bg) < 128;
            Hover = Mix(Bg, dark ? Color.White : Color.Black, dark ? 0.10 : 0.07);
            Sub = Mix(Fg, Bg, 0.45);
        }

        public Color TypeOf(string key)
        {
            switch (key)
            {
                case "script": return Script;
                case "doc": return Doc;
                case "image": return Image;
                case "text": return Text;
                case "link": return Link;
                case "lnk": return Lnk;
                case "pdf": return Pdf;
                case "folder": return Folder;
                default: return Code;
            }
        }

        // 卡片实际类型 → 配色键（folder：整条填类型色，文字用 FolderText）
        public static string TypeKeyOf(PinnedItem item)
        {
            if (item.Kind == "url") return "link";
            if (item.Kind == "text") return "text";
            if (item.Kind == "file" && Directory.Exists(item.Path)) return "folder";
            string ext = Path.GetExtension(item.Path ?? "").ToLowerInvariant();
            if (ext == ".lnk" || ext == ".appref-ms") return "lnk";
            if (ext == ".url" || ext == ".webloc") return "link";
            if (ext == ".pdf") return "pdf";
            if (Config.ImgExts.Contains(ext) || Config.IcoExts.Contains(ext) || Config.SvgExts.Contains(ext)) return "image";
            if (Config.VideoExts.Contains(ext) || Config.AudioExts.Contains(ext)) return "image";
            if (ext == ".txt" || ext == ".rtf" || ext == ".log") return "text";
            if (ext == ".md" || ext == ".markdown" || ext == ".csv" || ext == ".epub"
                || Config.OfficeExts.Contains(ext)) return "doc";
            if (ext == ".bat" || ext == ".cmd" || ext == ".ps1" || ext == ".sh" || ext == ".ahk"
                || ext == ".vbs" || ext == ".exe" || ext == ".msi" || ext == ".reg") return "script";
            return "code";
        }

        public static Color Hx(string s)
        {
            if (string.IsNullOrEmpty(s) || s == "transparent") return Color.Transparent;
            try { return ColorTranslator.FromHtml(s); }
            catch { return Color.Transparent; }
        }

        public static int Lum(Color c) { return (int)(0.299 * c.R + 0.587 * c.G + 0.114 * c.B); }

        public static Color Mix(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }
    }

    internal static class CardPalettes
    {
        public static readonly List<CardPalette> All = new List<CardPalette>
        {
            // 1 墨黑 · 彩标：默认深色底 + 高饱和类型色
            new CardPalette("墨黑 · 彩标", "#1b1b1f", "#ffffff", Color.FromArgb(115, 30, 50, 80), "transparent", 0,
                "#111111", "#1b1b1f",
                "#f58220", "#4c8dff", "#2fc27a", "#19b5c9", "#9b7bff", "#f0589b", "#ef4444", "#6b7a90", "#f2a900"),
            // 2 纸白 · 彩标：浅底同一套类型色
            new CardPalette("纸白 · 彩标", "#f7f7fa", "#222228", Color.FromArgb(89, 30, 50, 80), "transparent", 0,
                "#ffffff", "#1b1b1f",
                "#f58220", "#4c8dff", "#2fc27a", "#19b5c9", "#9b7bff", "#f0589b", "#ef4444", "#6b7a90", "#f2a900"),
            // 3 石墨单色：全灰阶，类型靠深浅区分
            new CardPalette("石墨单色", "#26262b", "#e6e6ea", Color.FromArgb(102, 0, 0, 0), "transparent", 0,
                "#1b1b1f", "#26262b",
                "#9aa0aa", "#b5bac4", "#8a909c", "#a3a8b2", "#c2c6ce", "#7d8390", "#aeb3bd", "#6f7580", "#d8dae0"),
            // 4 荧光绿终端
            new CardPalette("荧光绿终端", "#0b130e", "#b8f5c9", Color.FromArgb(140, 0, 0, 0), "transparent", 0,
                "#04140a", "#04140a",
                "#3dff7a", "#5fe6a0", "#8cff5f", "#2fd0a0", "#7dffd1", "#b4ff7a", "#e0ff6b", "#38a86a", "#c8ff3d"),
            // 5 琥珀 CRT
            new CardPalette("琥珀 CRT", "#1a1206", "#ffd98a", Color.FromArgb(128, 0, 0, 0), "transparent", 0,
                "#1a1206", "#1a1206",
                "#ffb000", "#ffc84a", "#e6a23c", "#ffd98a", "#ff9a3c", "#ffcf70", "#ff7a3c", "#b8862f", "#ffb000"),
            // 6 糖果硬边：奶油底 + 2px 硬描边 + 实心阴影
            new CardPalette("糖果硬边", "#fff4d6", "#1b1b1f", Color.FromArgb(255, 27, 27, 31), "#1b1b1f", 2,
                "#1b1b1f", "#1b1b1f",
                "#ff9f43", "#54a0ff", "#1dd1a1", "#48dbfb", "#a29bfe", "#ff6b9d", "#ff6b6b", "#feca57", "#feca57"),
            // 7 海军蓝金
            new CardPalette("海军蓝金", "#14213d", "#eef1f7", Color.FromArgb(128, 5, 10, 25), "transparent", 0,
                "#14213d", "#14213d",
                "#fca311", "#8fb8ff", "#fca311", "#cfd6e4", "#8fb8ff", "#fca311", "#fca311", "#8a97b3", "#fca311"),
            // 8 樱粉
            new CardPalette("樱粉", "#fff0f5", "#5a1f3a", Color.FromArgb(77, 120, 30, 70), "transparent", 0,
                "#ffffff", "#5a1f3a",
                "#ff7a8a", "#c06c9b", "#f4a6b8", "#d98ba7", "#a56bc9", "#ff8fb3", "#e0425f", "#9a7a8c", "#ffb347"),
            // 9 敦煌青绿
            new CardPalette("敦煌青绿", "#1f3b3a", "#f3e6c4", Color.FromArgb(128, 10, 25, 25), "transparent", 0,
                "#1f3b3a", "#1f3b3a",
                "#d9693f", "#5aa6a0", "#7fbf9a", "#d8b56a", "#9b7fb5", "#c98a6a", "#c9473f", "#8aa39a", "#d8b56a"),
            // 10 赛博霓虹
            new CardPalette("赛博霓虹", "#140c2e", "#ece6ff", Color.FromArgb(153, 5, 0, 20), "transparent", 0,
                "#140c2e", "#140c2e",
                "#ff3df2", "#3dd9ff", "#7dff6b", "#3dffd0", "#b78cff", "#ff7ab8", "#ff4d6d", "#6e7ccf", "#ffe14d"),
            // 11 高对比黑白黄：1px 白描边，类型色统一黄
            new CardPalette("高对比黑白黄", "#000000", "#ffffff", Color.Transparent, "#ffffff", 1,
                "#000000", "#000000",
                "#ffe600", "#ffe600", "#ffe600", "#ffe600", "#ffe600", "#ffe600", "#ffe600", "#ffe600", "#ffffff"),
        };

        public static CardPalette Get(int index)
        {
            if (All.Count == 0) return null;
            if (index < 0 || index >= All.Count) index = 0;
            return All[index];
        }

        // 当前生效的卡片配色（索引来自 Config.CardPaletteIndex）
        public static CardPalette Current { get { return Get(Config.CardPaletteIndex); } }
    }
}
