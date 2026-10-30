using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using WRect = System.Windows.Rect;

namespace HoverExplorer
{
    // perch (ClipPad) 文本块悬停检测。perch 是 Tauri+WebView2 应用，窗口 title="ClipPad"，
    // alwaysOnTop+skipTaskbar。文本块 snippet-text 是纯文本，不渲染 markdown 链接，
    // [text](路径) 以字面文本显示。本模块用 UIAutomation TextPattern.GetRangeFromPoint
    // 取鼠标下段落，正则找 [text](本地路径)，FindText 定位屏幕矩形判断鼠标命中哪个链接，
    // File/Directory.Exists 校验后返回路径，复用 he 现有 ScheduleFilePreview/OpenSub。
    internal static class PerchSupport
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        // [text](路径)  路径以盘符 X:\ 或 UNC \\ 开头，路径字符不含 ) 和换行
        private static readonly Regex _linkRe = new Regex(
            @"\[([^\]\n]*)\]\((((?:[A-Za-z]:\\|\\\\)[^\)\n]*))\)", RegexOptions.Compiled);

        // perch_hover.json 解析：{"path":"..."|"null","editing":bool,"l":n,"t":n,"r":n,"b":n,"ts":ms}
        private static readonly Regex _hoverRe = new Regex(
            @"""path"":(?:""((?:[^""\\]|\\.)*)""|null),""editing"":(true|false),""l"":(-?\d+),""t"":(-?\d+),""r"":(-?\d+),""b"":(-?\d+),""ts"":(\d+)",
            RegexOptions.Compiled);
        private static readonly DateTime _epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static string _lastLog;
        private static void Log(string s)
        {
            if (s == _lastLog) return;
            _lastLog = s;
            Program.DebugLog("[perch] " + s);
        }

        // 命中缓存：鼠标微动出链接矩形时 UIA FromPoint 命中不稳定（反复 null），
        // 导致 he 反复弹/关面板。缓存上次命中的 path+rect，鼠标在 rect+24px 容差内直接返回缓存。
        private static string _cachedPath;
        private static Rectangle _cachedRect;
        private static bool _cachedIsDir;

        private static string GetTitle(IntPtr h)
        {
            var sb = new StringBuilder(256);
            GetWindowTextW(h, sb, 256);
            return sb.ToString();
        }

        // 读 perch_hover.json。返回：null=无效/过期→UIA fallback；"noop"=无命中；""=编辑态；path=命中。
        private static string ReadHoverFile(int mx, int my, out Rectangle rect, out bool isDir)
        {
            rect = Rectangle.Empty;
            isDir = false;
            // 依次读通用 hover_link.json（Obsidian 插件）和 perch_hover.json（perch），
            // 鼠标只在一个应用，只有一个文件新鲜，任一有效即返回。
            foreach (var fn in new[] { "hover_link.json", "perch_hover.json" })
            {
                var r = ReadOneHoverFile(fn, mx, my, out rect, out isDir);
                if (r != null) return r;
            }
            return null;
        }

        private static string ReadOneHoverFile(string fn, int mx, int my, out Rectangle rect, out bool isDir)
        {
            rect = Rectangle.Empty;
            isDir = false;
            try
            {
                var p = Path.Combine(Path.GetTempPath(), fn);
                if (!File.Exists(p)) return null;
                var content = File.ReadAllText(p);
                var m = _hoverRe.Match(content);
                if (!m.Success) return null;
                ulong ts = ulong.Parse(m.Groups[7].Value);
                ulong now = (ulong)(DateTime.UtcNow - _epoch).TotalMilliseconds;
                if (now - ts > 1500) return null;
                if (m.Groups[2].Value == "true") return "";
                if (m.Groups[1].Value == "") return "noop";
                string path = m.Groups[1].Value.Replace("\\\\", "\\").Replace("\\\"", "\"");
                int l = int.Parse(m.Groups[3].Value), t = int.Parse(m.Groups[4].Value);
                int r = int.Parse(m.Groups[5].Value), b = int.Parse(m.Groups[6].Value);
                if (mx < l - 6 || mx > r + 6 || my < t - 4 || my > b + 4) return "noop";
                if (Directory.Exists(path)) isDir = true;
                else if (!File.Exists(path)) return "noop";
                rect = Rectangle.FromLTRB(l, t, r, b);
                return path;
            }
            catch { return null; }
        }

        public static string DetectHoverItem(int mx, int my, out Rectangle rect, out bool isDir)
        {
            rect = Rectangle.Empty;
            isDir = false;

            // 文件协议（perch 主动告知）最优先：perch 悬停链接/编辑态时写 %TEMP%\perch_hover.json。
            // 必须在缓存检查之前——否则编辑态下鼠标仍在旧链接矩形内，缓存命中直接返回旧 path，
            // 根本不读文件，editing=true 的抑制不生效。
            // null=文件无效/过期→走缓存+UIA fallback；"noop"=无命中→不弹窗；""=编辑态→抑制；path=命中。
            var fh = ReadHoverFile(mx, my, out rect, out isDir);
            if (fh != null)
            {
                if (fh.Length > 0 && fh != "noop")
                { _cachedPath = fh; _cachedRect = rect; _cachedIsDir = isDir; Log("file:" + fh); return fh; }
                _cachedPath = null;
                if (fh == "") Log("editing-skip:file");
                return null;
            }

            // 缓存命中：鼠标在上次链接矩形 +24px 容差内，直接返回缓存（避免抖动反复 null 致面板弹/关）
            if (_cachedPath != null && _cachedRect != Rectangle.Empty)
            {
                var exp = Rectangle.Inflate(_cachedRect, 24, 24);
                if (exp.Contains(mx, my))
                {
                    rect = _cachedRect;
                    isDir = _cachedIsDir;
                    return _cachedPath;
                }
                _cachedPath = null;
            }

            var pt = new NativeMethods.POINT { X = mx, Y = my };
            var hwnd = NativeMethods.WindowFromPoint(pt);
            if (hwnd == IntPtr.Zero) return null;

            IntPtr perchWnd = IntPtr.Zero;
            var cur = hwnd;
            while (cur != IntPtr.Zero)
            {
                string t = GetTitle(cur);
                if (t.IndexOf("ClipPad", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.IndexOf("Perch", StringComparison.OrdinalIgnoreCase) >= 0)
                { perchWnd = cur; break; }
                cur = NativeMethods.GetParent(cur);
            }
            if (perchWnd == IntPtr.Zero) { Log("not-perch"); return null; }

            AutomationElement hit;
            try { hit = AutomationElement.FromPoint(new System.Windows.Point(mx, my)); }
            catch (Exception ex) { Log("frompoint-err:" + ex.Message); return null; }
            if (hit == null) { Log("frompoint-null"); return null; }

            // 主窗口 UIA FromPoint 常命中行容器/chip（HelpText="编辑这条..."/"关闭"等 UI 文案），
            // 链接 <span title=路径> 是其子元素。先在命中点子树找 HelpText 像本地路径且鼠标在其矩形内的 span。
            var subHit = FindPathInSubtree(hit, mx, my, 3, ref rect, out isDir);
            if (subHit != null) { Log("subtree:" + subHit); return subHit; }

            // 向上遍历父链：perch (note.js:697) 渲染 [文字](路径) 成 <span title="路径">文字</span>，
            // UIA FromPoint 常命中其子文本节点"文字"（HelpText 空），需向上找到 <span> 取 HelpText(=title=路径)。
            // 同时每个有 TextPattern 的元素取 DocumentRange 全文找 [text](path) 源码（编辑态 textarea 场景）。
            var c = hit;
            for (int i = 0; i < 14 && c != null; i++)
            {
                // HelpText = HTML title = 路径（渲染态 <span title=路径>）
                try
                {
                    string ht = c.Current.HelpText;
                    if (!string.IsNullOrEmpty(ht) && LooksLikePath(ht))
                    {
                        var rp = Resolve(ht, mx, my, ref rect, out isDir, Rectangle.Empty);
                        if (rp != null) { Log("href:" + ht); return rp; }
                    }
                }
                catch { }

                // TextPattern.DocumentRange 全文找 [text](path) 源码（编辑态 textarea / 纯文本 div）
                TextPattern tp = null;
                object pat;
                try { if (c.TryGetCurrentPattern(TextPattern.Pattern, out pat) && pat != null) tp = (TextPattern)pat; }
                catch { }
                if (tp != null)
                {
                    var found = TryTextPatternLink(tp, mx, my, ref rect, out isDir);
                    if (found != null) return found;
                }
                try { c = TreeWalker.RawViewWalker.GetParent(c); } catch { break; }
            }

            // fallback：元素 Name 里找单个链接
            string name;
            try { name = hit.Current.Name; } catch { name = null; }
            if (!string.IsNullOrEmpty(name))
            {
                var m = _linkRe.Match(name);
                if (m.Success) return Resolve(m.Groups[2].Value, mx, my, ref rect, out isDir, Rectangle.Empty);
            }

            // 兜底：主窗口 DOM 深（.sec>.sec-cards>.card>...），FromPoint 命中上层容器时
            // 命中点子树+父链都够不到链接 span。从 perch 窗口根全子树扫描（带矩形剪枝），
            // 找 HelpText 像本地路径且鼠标在其矩形内的链接 span。
            try
            {
                var root = AutomationElement.FromHandle(perchWnd);
                if (root != null)
                {
                    var rootHit = FindPathInSubtree(root, mx, my, 15, ref rect, out isDir);
                    if (rootHit != null) { Log("root-subtree:" + rootHit); return rootHit; }
                }
            }
            catch (Exception ex) { Log("root-subtree-err:" + ex.Message); }
            Log("no-link-any");
            return null;
        }

        // 在某元素的 TextPattern.DocumentRange 全文里找 [text](path) 链接，FindText 定位屏幕矩形判断鼠标命中
        private static string TryTextPatternLink(TextPattern tp, int mx, int my, ref Rectangle rect, out bool isDir)
        {
            isDir = false;
            string full;
            TextPatternRange docRange;
            try { docRange = tp.DocumentRange; full = docRange.GetText(-1); }
            catch { return null; }
            if (string.IsNullOrEmpty(full)) return null;
            if (full.Length > 8000) full = full.Substring(0, 8000);

            var matches = _linkRe.Matches(full);
            if (matches.Count == 0) return null;

            foreach (Match m in matches)
            {
                string whole = m.Value;
                TextPatternRange sub = null;
                try { sub = docRange.FindText(whole, false, false); } catch { }
                if (sub == null) continue;
                WRect[] br;
                try { br = sub.GetBoundingRectangles(); } catch { continue; }
                if (br == null) continue;
                foreach (var r in br)
                {
                    if (r.Width <= 0 || r.Height <= 0) continue;
                    var rr = Rectangle.FromLTRB((int)Math.Floor(r.Left), (int)Math.Floor(r.Top),
                                                (int)Math.Ceiling(r.Right), (int)Math.Ceiling(r.Bottom));
                    if (mx >= rr.Left && mx <= rr.Right && my >= rr.Top && my <= rr.Bottom)
                    {
                        Log("hit:" + m.Groups[2].Value);
                        return Resolve(m.Groups[2].Value, mx, my, ref rect, out isDir, rr);
                    }
                }
            }
            if (matches.Count == 1)
            {
                Log("single-link-fallback:" + matches[0].Groups[2].Value);
                return Resolve(matches[0].Groups[2].Value, mx, my, ref rect, out isDir, Rectangle.Empty);
            }
            return null;
        }


        // 粗筛是否像本地路径（盘符 X:\ 或 UNC \\）
        private static bool LooksLikePath(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length < 3) return false;
            if (s.Length >= 3 && char.IsLetter(s[0]) && s[1] == ':' && (s[2] == '\\' || s[2] == '/')) return true;
            if (s.StartsWith("\\\\") || s.StartsWith("//")) return true;
            return false;
        }

        // 在元素子树里找 HelpText 像本地路径且鼠标在其 BoundingRectangle 内的链接 span
        private static string FindPathInSubtree(AutomationElement root, int mx, int my, int maxDepth, ref Rectangle rect, out bool isDir)
        {
            isDir = false;
            int count = 0;
            return FindPathInSubtreeCore(root, mx, my, maxDepth, ref count, ref rect, out isDir);
        }

        private static string FindPathInSubtreeCore(AutomationElement el, int mx, int my, int depth, ref int count, ref Rectangle rect, out bool isDir)
        {
            isDir = false;
            if (depth < 0 || count > 500) return null;
            count++;
            AutomationElement child;
            try { child = TreeWalker.RawViewWalker.GetFirstChild(el); } catch { return null; }
            while (child != null)
            {
                WRect cr = default(WRect);
                try { cr = child.Current.BoundingRectangle; } catch { }
                // 矩形剪枝：child 有非 0 矩形且不含鼠标点（带容差）则不递进其子树，
                // 兄弟分支（其他卡片/分组）整棵跳过，主窗口全树扫描仍快。
                bool mayContain = cr.Width <= 0 || cr.Height <= 0
                    || (mx >= cr.Left - 24 && mx <= cr.Right + 24 && my >= cr.Top - 24 && my <= cr.Bottom + 24);
                string ht = null;
                try { ht = child.Current.HelpText; } catch { }
                if (!string.IsNullOrEmpty(ht) && LooksLikePath(ht))
                {
                    if (cr.Width > 0 && cr.Height > 0 && mx >= cr.Left - 6 && mx <= cr.Right + 6 && my >= cr.Top - 4 && my <= cr.Bottom + 4)
                    {
                        Rectangle rr = Rectangle.FromLTRB((int)cr.Left, (int)cr.Top, (int)cr.Right, (int)cr.Bottom);
                        var rp = Resolve(ht, mx, my, ref rr, out isDir, rr);
                        if (rp != null) { rect = rr; return rp; }
                    }
                }
                if (mayContain)
                {
                    var sub = FindPathInSubtreeCore(child, mx, my, depth - 1, ref count, ref rect, out isDir);
                    if (sub != null) return sub;
                }
                try { child = TreeWalker.RawViewWalker.GetNextSibling(child); } catch { break; }
            }
            return null;
        }

        // 渲染态 <span title="路径">文字</span>（perch note.js:697）：UIA HelpText = title = 路径
        private static string TryGetHrefValue(AutomationElement el)
        {
            // HelpText（HTML title 属性）—— perch 渲染链接的路径存放位置
            try
            {
                string ht = el.Current.HelpText;
                if (!string.IsNullOrEmpty(ht)) return ht;
            }
            catch { }
            // ValuePattern.Value（<a href> 场景备用）
            try
            {
                object pat;
                if (el.TryGetCurrentPattern(ValuePattern.Pattern, out pat) && pat != null)
                {
                    string v = ((ValuePattern)pat).Current.Value;
                    if (!string.IsNullOrEmpty(v)) return NormalizeHref(v);
                }
            }
            catch { }
            return null;
        }

        private static string NormalizeHref(string v)
        {
            if (v.StartsWith("file:///")) v = v.Substring(8).Replace('/', '\\');
            else if (v.StartsWith("file://")) v = v.Substring(7).Replace('/', '\\');
            return v;
        }

        private static string Resolve(string path, int mx, int my, ref Rectangle rect, out bool isDir, Rectangle linkRect)
        {
            isDir = false;
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                if (Directory.Exists(path)) { isDir = true; }
                else if (!File.Exists(path)) { Log("path-not-exist:" + path); return null; }
            }
            catch { return null; }
            rect = linkRect.IsEmpty ? Rectangle.FromLTRB(mx - 4, my - 4, mx + 4, my + 4) : linkRect;
            // 缓存命中，避免鼠标微动导致反复 null → 面板弹/关
            _cachedPath = path; _cachedRect = rect; _cachedIsDir = isDir;
            return path;
        }
    }
}