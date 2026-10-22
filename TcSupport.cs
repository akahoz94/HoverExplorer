using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;

namespace HoverExplorer
{
    // Total Commander 悬停检测。TC 11.x 是 Lazarus/LCL 界面，文件列表类名不稳定（"L"/"LCLListBox"）：
    // LB_GETTEXT 不写缓冲（LCL 实现缺陷），改用 LB_ITEMFROMPOINT 取行索引 + UIA 路径栏取面板目录 +
    // 目录枚举按 TC 当前排序对齐行索引。排序模式从 TC ini（[Left]/[Right] sortorder + negative Sortorder）读取：
    // 0=名称 1=扩展名 2=大小 3=日期；目录组前置（TC 默认），".." 恒首行。
    // LB_GETCOUNT 与枚举数校验不匹配（搜索/过滤/虚拟面板）时放弃，不误报。
    // 已知边界：TC 运行中临时点列头改排序不会立即写 ini（退出时才保存）→ 重启 TC 后对齐恢复。
    internal static class TcSupport
    {
        private const uint LB_GETCOUNT = 0x018B;
        private const uint LB_GETITEMHEIGHT = 0x01A1;
        private const uint LB_ITEMFROMPOINT = 0x01A9;

        private static readonly List<IntPtr> _lbCache = new List<IntPtr>();
        private static IntPtr _lbCacheMain;
        private static DateTime _lbCacheTime;

        private class Entry
        {
            public string Name;
            public bool IsDir;
            public long Size;
            public long Time;
            public string Ext;
        }
        private class PanelInfo
        {
            public string Dir;
            public List<Entry> Items = new List<Entry>();   // TC 排序后的条目（目录前置）
            public int LastCount = -1;
            public int NameColW;   // 名称列估算宽度（触发区域限制，与资源管理器行为一致）
            public DateTime Time;
        }
        private static readonly Dictionary<IntPtr, PanelInfo> _panels = new Dictionary<IntPtr, PanelInfo>();

        // TC ini 排序配置缓存（30s；ini 是 TC 退出时保存的快照）
        private static int _sortL = -2, _sortR = -2;   // -2 未读
        private static bool _negL, _negR;
        private static DateTime _sortTime;

        private static int NatCompare(string a, string b) { return NativeMethods.StrCmpLogicalW(a, b); }

        private static string _lastFailLog;
        private static void LogFail(object reason)
        {
            string s = reason == null ? null : reason.ToString();
            if (s == _lastFailLog) return;
            _lastFailLog = s;
            if (s != null) Program.DebugLog("[tc] " + s);
        }

        public static string DetectHoverItem(int mx, int my, out Rectangle rect, out bool isDir)
        {
            rect = Rectangle.Empty;
            isDir = false;
            var pt = new NativeMethods.POINT { X = mx, Y = my };
            var hwnd = NativeMethods.WindowFromPoint(pt);
            if (hwnd == IntPtr.Zero) return null;
            IntPtr tcMain = IntPtr.Zero;
            var cur = hwnd;
            while (cur != IntPtr.Zero)
            {
                if (NativeMethods.GetClassOf(cur) == "TTOTAL_CMD") { tcMain = cur; break; }
                cur = NativeMethods.GetParent(cur);
            }
            if (tcMain == IntPtr.Zero) { LogFail("not-tc-window"); return null; }

            foreach (var lb in GetListBoxes(tcMain))
            {
                NativeMethods.RECT r;
                if (!NativeMethods.GetWindowRect(lb, out r)) continue;
                if (mx < r.Left || mx > r.Right || my < r.Top || my > r.Bottom) continue;

                // LCL 子窗口无边框，窗口矩形即客户区
                int cx = mx - r.Left, cy = my - r.Top;
                IntPtr lparam = (IntPtr)((cy << 16) | (cx & 0xFFFF));
                long v = NativeMethods.SendMsg(lb, LB_ITEMFROMPOINT, IntPtr.Zero, lparam).ToInt64();
                if ((v >> 16) != 0) { LogFail("off-list"); return null; }
                int idx = (int)(v & 0xFFFF);
                if (idx < 0) return null;

                var info = GetPanelInfo(lb, tcMain);
                if (info == null) { LogFail("panel-info-null dir/list mismatch"); return null; }

                // 与资源管理器一致：只有名称列触发。TC 自绘列拿不到精确边界，
                // 用最长文件名像素宽估算名称列宽，x 超出则不触发
                if (cx > info.NameColW) { LogFail("not-in-name-col cx=" + cx + " w=" + info.NameColW); return null; }

                int itemH = Math.Max(1, NativeMethods.SendMsg(lb, LB_GETITEMHEIGHT, IntPtr.Zero, IntPtr.Zero).ToInt32());
                rect = new Rectangle(r.Left, r.Top + idx * itemH, r.Right - r.Left, itemH);

                if (idx == 0)
                {
                    // ".." 父目录；驱动器根 / UNC 服务器根无父路径，跳过
                    string parent = Path.GetDirectoryName(info.Dir.TrimEnd('\\'));
                    if (string.IsNullOrEmpty(parent)) { LogFail("dotdot-no-parent"); return null; }
                    isDir = true;
                    LogFail(null);
                    return parent;
                }
                int i = idx - 1;
                if (i >= info.Items.Count) { LogFail("index-overflow"); return null; }
                var e2 = info.Items[i];
                LogFail(null);
                isDir = e2.IsDir;
                return Path.Combine(info.Dir, e2.Name);
            }
            LogFail("mouse-not-in-listbox n=" + _lbCache.Count + " @" + mx + "," + my);
            return null;
        }

        // LCL 文件列表窗口类名不稳定（实测同版本两个实例分别是 "L" 与 "LCLListBox"，
        // 类名冲突时 LCL 会换名注册），不能用类名匹配。改用消息探测：
        // 几何过滤（宽高 >200 的可见子窗口）后发 LB_GETCOUNT / LB_GETITEMHEIGHT，
        // 响应且值合理的就是 ListBox（容器/Tab/路径栏等对 LB_ 消息返回 0）
        private static List<IntPtr> GetListBoxes(IntPtr tcMain)
        {
            if (_lbCacheMain == tcMain && _lbCache.Count > 0
                && (DateTime.UtcNow - _lbCacheTime).TotalMilliseconds < 10000)
                return _lbCache;
            var found = new List<IntPtr>();
            NativeMethods.EnumChildProc cb = (h, l) =>
            {
                NativeMethods.RECT r;
                if (!NativeMethods.GetWindowRect(h, out r)) return true;
                if (r.Right - r.Left < 200 || r.Bottom - r.Top < 200) return true;
                int n = NativeMethods.SendMsg(h, LB_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt32();
                if (n <= 0 || n > 500000) return true;
                int ih = NativeMethods.SendMsg(h, LB_GETITEMHEIGHT, IntPtr.Zero, IntPtr.Zero).ToInt32();
                if (ih < 8 || ih > 64) return true;
                found.Add(h);
                return true;
            };
            NativeMethods.EnumChildWindows(tcMain, cb, IntPtr.Zero);
            if (found.Count > 0)
            {
                _lbCacheMain = tcMain;
                _lbCacheTime = DateTime.UtcNow;
                _lbCache.Clear();
                _lbCache.AddRange(found);
            }
            return found;
        }

        private static PanelInfo GetPanelInfo(IntPtr lb, IntPtr tcMain)
        {
            PanelInfo info;
            bool fresh = false;
            if (!_panels.TryGetValue(lb, out info)
                || (DateTime.UtcNow - info.Time).TotalMilliseconds > 2000)
            {
                info = RefreshPanel(lb, tcMain);
                if (info == null) { _panels.Remove(lb); return null; }
                _panels[lb] = info;
                fresh = true;
            }
            // GETCOUNT 变化说明列表内容变了（切目录/刷新/滚动外的重排）→ 重刷
            if (!fresh)
            {
                int count = NativeMethods.SendMsg(lb, LB_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt32();
                if (count != info.LastCount)
                {
                    info = RefreshPanel(lb, tcMain);
                    if (info == null) { _panels.Remove(lb); return null; }
                    _panels[lb] = info;
                }
            }
            return info;
        }

        private static PanelInfo RefreshPanel(IntPtr lb, IntPtr tcMain)
        {
            string dir = ReadPanelDir(tcMain, lb);
            if (string.IsNullOrEmpty(dir)) return null;
            int sortMode; bool neg;
            GetTcSort(lb, tcMain, out sortMode, out neg);
            var info = new PanelInfo { Dir = dir, Time = DateTime.UtcNow };
            var entries = new List<Entry>();
            try
            {
                foreach (var e in Directory.GetFileSystemEntries(dir))
                {
                    try
                    {
                        var fi = new FileInfo(e);
                        bool isDir = fi.Attributes.HasFlag(FileAttributes.Directory);
                        entries.Add(new Entry
                        {
                            Name = Path.GetFileName(e),
                            IsDir = isDir,
                            Ext = Path.GetExtension(e) ?? "",
                            Size = isDir ? -1 : fi.Length,
                            Time = fi.LastWriteTimeUtc.Ticks,
                        });
                    }
                    catch { }
                }
            }
            catch { return null; }

            // 按 TC 排序模式复现：0=名称 1=扩展名 2=大小 3=日期；降序反转；目录组前置（TC 默认）
            Comparison<Entry> cmp;
            switch (sortMode)
            {
                case 1:
                    cmp = (a, b) => { int c = NatCompare(a.Ext, b.Ext); return c != 0 ? c : NatCompare(a.Name, b.Name); };
                    break;
                case 2:
                    // 大小排序：目录无大小，目录间按名称；文件按大小
                    cmp = (a, b) => (a.IsDir || b.IsDir) ? NatCompare(a.Name, b.Name) : a.Size.CompareTo(b.Size);
                    break;
                case 3:
                    cmp = (a, b) => a.Time.CompareTo(b.Time);
                    break;
                default:
                    cmp = (a, b) => NatCompare(a.Name, b.Name);
                    break;
            }
            if (neg) { var orig = cmp; cmp = (a, b) => orig(b, a); }
            var dirs = entries.FindAll(x => x.IsDir);
            dirs.Sort(cmp);
            var files = entries.FindAll(x => !x.IsDir);
            files.Sort(cmp);
            info.Items.AddRange(dirs);
            info.Items.AddRange(files);

            info.LastCount = NativeMethods.SendMsg(lb, LB_GETCOUNT, IntPtr.Zero, IntPtr.Zero).ToInt32();
            // 数量对不上：搜索/过滤/自定义排序/虚拟面板 → 无法对齐，放弃
            if (info.LastCount != info.Items.Count + 1) return null;

            // 名称列宽估算：最长文件名的像素宽 + 图标 16 + 边距。TC 列是自绘的拿不到精确边界，
            // 字体差异加 15% 余量。clamp：最小 120（短名目录），最大不超过列表宽
            try
            {
                NativeMethods.RECT lr;
                NativeMethods.GetWindowRect(lb, out lr);
                int listW = lr.Right - lr.Left;
                int maxTextW = 0;
                using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero))
                {
                    using (var f = new Font("Microsoft YaHei", 9f, FontStyle.Regular, GraphicsUnit.Point))
                    {
                        foreach (var n in info.Items) { int w = (int)(g.MeasureString(n.Name, f).Width * 1.15); if (w > maxTextW) maxTextW = w; }
                    }
                }
                info.NameColW = Math.Min(Math.Max(maxTextW + 16 + 12, 120), listW);
            }
            catch { info.NameColW = 200; }
            return info;
        }

        // 读 TC 排序：Config.TcSortMode >= 0（用户显式声明，最可靠，改 TC 排序后同步改这里）时直接用；
        // 否则自动从 TC ini 推断——优先 tabs 配置（[lefttabs]/[righttabs] 的 n_path+n_options，
        // 多 tab 各自排序，路径匹配当前面板；n_options 第2位=sortorder 第3位=降序标志），
        // fallback [Left]/[Right] 主节。悬停列表在主窗口左半 → [lefttabs]，右半 → [righttabs]。30s 缓存。
        // 局限：TC 退出时才写 ini；运行中临时点列头改排序无法实时感知 → 错位。
        private static void GetTcSort(IntPtr lb, IntPtr tcMain, out int sortMode, out bool neg)
        {
            sortMode = 0; neg = false;
            bool isLeft = IsLeftPanel(lb, tcMain);
            if (Config.TcSortMode >= 0)
            {
                sortMode = Config.TcSortMode / 2;
                neg = (Config.TcSortMode % 2) == 1;
                return;
            }
            if (_sortL >= -1 && (DateTime.UtcNow - _sortTime).TotalSeconds < 30)
            {
                sortMode = isLeft ? _sortL : _sortR;
                neg = isLeft ? _negL : _negR;
                return;
            }
            try
            {
                uint pid;
                NativeMethods.GetWindowThreadProcessId(tcMain, out pid);
                var h = NativeMethods.OpenProcess(0x1000, false, pid);   // PROCESS_QUERY_LIMITED_INFORMATION
                if (h == IntPtr.Zero) return;
                string exePath;
                try
                {
                    var sb = new StringBuilder(1024); uint sz = 1024;
                    if (!NativeMethods.QueryFullProcessImageNameW(h, 0, sb, ref sz)) return;
                    exePath = sb.ToString();
                }
                finally { NativeMethods.CloseHandle(h); }
                string exeDir = Path.GetDirectoryName(exePath);
                if (string.IsNullOrEmpty(exeDir)) return;
                string mainIni = Path.Combine(exeDir, "wincmd.ini");
                if (!File.Exists(mainIni)) return;
                // RedirectSection=%COMMANDER_PATH%\User\User.ini
                string effIni = mainIni;
                string rs = ReadIni(mainIni, "Left", "RedirectSection");
                if (!string.IsNullOrEmpty(rs))
                {
                    rs = rs.Replace("%COMMANDER_PATH%", exeDir);
                    if (File.Exists(rs)) effIni = rs;
                }

                // 当前面板目录（用于 tab 路径匹配）
                string curDir = ReadPanelDir(tcMain, lb);

                foreach (var side in new[] { true, false })   // 左、右
                {
                    string sec = side ? "lefttabs" : "righttabs";
                    int mode = -1; bool n = false;
                    // activetab（1-based）→ 索引 fallback
                    int active = 0;
                    int.TryParse(ReadIni(effIni, sec, "activetab"), out active);
                    if (active > 0) active--;
                    for (int t = 0; t < 32; t++)
                    {
                        string tpath = ReadIni(effIni, sec, t + "_path");
                        if (string.IsNullOrEmpty(tpath)) break;
                        string topts = ReadIni(effIni, sec, t + "_options");
                        bool match = !string.IsNullOrEmpty(curDir)
                            && string.Equals(tpath.TrimEnd('\\'), curDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                        if (match || t == active)
                        {
                            // n_options=a|b|c|d|... 第2位=sortorder 第3位=降序
                            var parts = topts.Split('|');
                            if (parts.Length > 3)
                            {
                                int m; int.TryParse(parts[1], out m);
                                if (m >= 0 && m <= 3) mode = m;
                                n = parts[2] == "1";
                            }
                            if (match) break;   // 路径命中优先，不再看 activetab
                        }
                    }
                    if (mode < 0)
                    {
                        // fallback：主节 [Left]/[Right] 的 sortorder + negativeSortorder（两种拼法）
                        string s = side ? "Left" : "Right";
                        mode = ParseSort(ReadIni(effIni, s, "sortorder"));
                        n = ParseNeg(ReadIni(effIni, s, "negativeSortorder"), ReadIni(effIni, s, "negative Sortorder"));
                    }
                    if (side) { _sortL = mode; _negL = n; }
                    else { _sortR = mode; _negR = n; }
                }
                _sortTime = DateTime.UtcNow;
                Program.DebugLog("[tc] ini sort L=" + _sortL + (_negL ? " desc" : " asc") + " R=" + _sortR + (_negR ? " desc" : " asc") + (curDir != null ? " curDir=" + curDir : "") + " ini=" + effIni);
                sortMode = isLeft ? _sortL : _sortR;
                neg = isLeft ? _negL : _negR;
            }
            catch { }
        }

        private static bool IsLeftPanel(IntPtr lb, IntPtr tcMain)
        {
            NativeMethods.RECT lr, mr;
            if (!NativeMethods.GetWindowRect(lb, out lr) || !NativeMethods.GetWindowRect(tcMain, out mr))
                return true;
            return (lr.Left + lr.Right) / 2 < (mr.Left + mr.Right) / 2;
        }

        private static int ParseSort(string v)
        {
            int n;
            if (int.TryParse(v, out n) && n >= 0 && n <= 3) return n;
            return 0;
        }

        private static bool ParseNeg(string v1, string v2)
        {
            string v = !string.IsNullOrEmpty(v1) ? v1 : v2;
            return v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadIni(string ini, string section, string key)
        {
            try
            {
                var sb = new StringBuilder(256);
                NativeMethods.GetPrivateProfileString(section, key, "", sb, 256, ini);
                return sb.ToString();
            }
            catch { return ""; }
        }

        // UIA 读 TC 路径栏（name 形如 "D:\dir\*.*"），与列表窗口按屏幕 x 中心距离配对；
        // 非活动 tab 的路径栏 BoundingRectangle 宽度为 0，过滤掉
        private static string ReadPanelDir(IntPtr tcMain, IntPtr lb)
        {
            try
            {
                var root = System.Windows.Automation.AutomationElement.FromHandle(tcMain);
                var cond = new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.ClassNameProperty, "Window");
                var panes = root.FindAll(System.Windows.Automation.TreeScope.Descendants, cond);
                NativeMethods.RECT lr;
                if (!NativeMethods.GetWindowRect(lb, out lr)) return null;
                int lbCenter = (lr.Left + lr.Right) / 2;
                string best = null;
                long bestDist = long.MaxValue;
                foreach (System.Windows.Automation.AutomationElement p in panes)
                {
                    string name;
                    try { name = p.Current.Name; } catch { continue; }
                    if (string.IsNullOrEmpty(name) || !name.EndsWith("*.*")) continue;
                    var r = p.Current.BoundingRectangle;
                    if (r.Width <= 0) continue;
                    long dist = Math.Abs((long)(r.X + r.Width / 2) - lbCenter);
                    if (dist < bestDist) { bestDist = dist; best = name; }
                }
                if (best == null || bestDist > 2000) return null;
                return best.Substring(0, best.Length - 3);
            }
            catch { return null; }
        }
    }
}