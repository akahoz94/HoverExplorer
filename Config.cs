using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using System.Web.Script.Serialization;

namespace HoverExplorer
{
    internal static class Config
    {
        public static readonly HashSet<string> ImgExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };
        public static readonly HashSet<string> IcoExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".ico", ".tiff", ".tif" };
        public static readonly HashSet<string> OfficeExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".docx", ".xlsx", ".pptx" };
        public static readonly HashSet<string> ZipExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".zip" };
        public static readonly HashSet<string> SevenZipExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".7z", ".rar" };
        public static readonly HashSet<string> FontExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".ttf", ".otf", ".ttc" };
        public static readonly HashSet<string> ExeExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".dll" };
        public static readonly HashSet<string> LnkExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".lnk" };
        public static readonly HashSet<string> EpubExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".epub" };
        public static int PreviewDelay = 400;
        public static int PreviewMax = 240;
        public static double HoverDelay = 0.30;
        public static int SubHoverDelay = 150;
        public static double CloseDelay = 0.35;
        public static int CheckInterval = 40;
        public static int PopupOffset = 16;
        public static int SubGap = 4;
        public static int MaxItems = 40;
        public static bool ExplorerEnabled = false;
        public static bool PreviewFiles = true;
        public static bool DebugLog = false;
        public static bool HideTooltip = false;
        public static bool HotkeyEnabled = true;
        public static int HotkeyModifiers = 0x2 | 0x1; // Ctrl+Alt
        public static int HotkeyKey = 81; // Q
        public static string Language = "zh";
        public static string Theme = "dark";
        public static readonly string FontName = "Microsoft YaHei UI";
        public static readonly float FontSize = 10f;
        public static Color BG, FG, BgHover, BgDir, FgDir, DropHighlight, BgSel, Border, PlaceholderFg;

        static Config()
        {
            DropHighlight = ColorTranslator.FromHtml("#2d7a3d");
            ApplyTheme();
        }

        public static void ApplyTheme()
        {
            bool dark = Theme != "light";
            if (dark)
            {
                BG = ColorTranslator.FromHtml("#1e1e24");
                FG = ColorTranslator.FromHtml("#ececf1");
                BgHover = ColorTranslator.FromHtml("#2c2c33");
                BgDir = ColorTranslator.FromHtml("#2a2f38");
                FgDir = ColorTranslator.FromHtml("#6aa9ff");
                BgSel = ColorTranslator.FromHtml("#6aa9ff");
                Border = ColorTranslator.FromHtml("#42424a");
                PlaceholderFg = ColorTranslator.FromHtml("#8d8d99");
            }
            else
            {
                BG = ColorTranslator.FromHtml("#fbfbfd");
                FG = ColorTranslator.FromHtml("#1c1c22");
                BgHover = ColorTranslator.FromHtml("#efeff2");
                BgDir = ColorTranslator.FromHtml("#e8e8f0");
                FgDir = ColorTranslator.FromHtml("#0067c0");
                BgSel = ColorTranslator.FromHtml("#0067c0");
                Border = ColorTranslator.FromHtml("#e8e8ec");
                PlaceholderFg = ColorTranslator.FromHtml("#7a7a88");
            }
        }
        public static int TextPreviewLines = 10;
        public static readonly HashSet<string> TextExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".md", ".markdown", ".py", ".js", ".ts", ".json", ".log",
          ".ini", ".cfg", ".yaml", ".yml", ".xml", ".html", ".css",
          ".ahk", ".bat", ".cmd", ".ps1", ".csv", ".srt", ".toml", ".vue" };
        public static Dictionary<string, string> SortLabels = new Dictionary<string, string>
        { {"name","名称"}, {"size","大小"}, {"date","日期"} };

        public static void RefreshSortLabels()
        {
            SortLabels["name"] = I18n.T("sort_name");
            SortLabels["size"] = I18n.T("sort_size");
            SortLabels["date"] = I18n.T("sort_date");
        }

        public static void Load()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            var p = Path.Combine(dir, "config.json");
            if (!File.Exists(p)) return;
            try
            {
                var text = File.ReadAllText(p);
                var jss = new JavaScriptSerializer();
                var data = jss.Deserialize<Dictionary<string, object>>(text);
                foreach (var kv in data)
                {
                    string k = kv.Key;
                    var v = kv.Value;
                    switch (k)
                    {
                        case "PreviewDelay": PreviewDelay = Convert.ToInt32(v); break;
                        case "PreviewMax": PreviewMax = Convert.ToInt32(v); break;
                        case "HoverDelay": HoverDelay = Convert.ToDouble(v); break;
                        case "SubHoverDelay": SubHoverDelay = Convert.ToInt32(v); break;
                        case "CloseDelay": CloseDelay = Convert.ToDouble(v); break;
                        case "CheckInterval": CheckInterval = Convert.ToInt32(v); break;
                        case "PopupOffset": PopupOffset = Convert.ToInt32(v); break;
                        case "SubGap": SubGap = Convert.ToInt32(v); break;
                        case "MaxItems": MaxItems = Convert.ToInt32(v); break;
                        case "TextPreviewLines": TextPreviewLines = Convert.ToInt32(v); break;
                        case "ExplorerEnabled": ExplorerEnabled = Convert.ToBoolean(v); break;
                        case "PreviewFiles": PreviewFiles = Convert.ToBoolean(v); break;
                        case "DebugLog": DebugLog = Convert.ToBoolean(v); break;
                        case "HideTooltip": HideTooltip = Convert.ToBoolean(v); break;
                        case "HotkeyEnabled": HotkeyEnabled = Convert.ToBoolean(v); break;
                        case "HotkeyModifiers": HotkeyModifiers = Convert.ToInt32(v); break;
                        case "HotkeyKey": HotkeyKey = Convert.ToInt32(v); break;
                        case "Language": Language = Convert.ToString(v); break;
                        case "Theme": Theme = Convert.ToString(v); break;
                    }
                }
                I18n.Lang = Language;
                RefreshSortLabels();
                ApplyTheme();
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                var dir = AppDomain.CurrentDomain.BaseDirectory;
                var p = Path.Combine(dir, "config.json");
                var data = new Dictionary<string, object>();
                if (File.Exists(p))
                {
                    var text = File.ReadAllText(p);
                    var jss = new JavaScriptSerializer();
                    var existing = jss.Deserialize<Dictionary<string, object>>(text);
                    if (existing != null) data = existing;
                }
                data["ExplorerEnabled"] = ExplorerEnabled;
                data["PreviewFiles"] = PreviewFiles;
                data["DebugLog"] = DebugLog;
                data["HideTooltip"] = HideTooltip;
                data["HotkeyEnabled"] = HotkeyEnabled;
                data["HotkeyModifiers"] = HotkeyModifiers;
                data["HotkeyKey"] = HotkeyKey;
                data["Language"] = Language;
                data["Theme"] = Theme;
                var jss2 = new JavaScriptSerializer();
                File.WriteAllText(p, jss2.Serialize(data));
            }
            catch { }
        }

        public static string FmtSize(long n)
        {
            double d = n;
            string[] units = { "B", "KB", "MB", "GB" };
            for (int i = 0; i < units.Length; i++)
            {
                if (d < 1024 || i == 3)
                    return i == 0 ? string.Format("{0:0} {1}", d, units[i]) : string.Format("{0:0.0} {1}", d, units[i]);
                d /= 1024;
            }
            return string.Format("{0:0.0} GB", d);
        }
    }
}