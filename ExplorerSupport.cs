using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace HoverExplorer
{
    internal static class ExplorerSupport
    {
        private static IntPtr _cachedHwnd;
        private static System.Collections.Generic.List<string> _cachedTabs;
        private static DateTime _cacheTime;

        private static string _lastDetectLog;
        private static void LogDetect(string state, string detail)
        {
            string key = state + "|" + detail;
            if (key == _lastDetectLog) return;
            _lastDetectLog = key;
            Program.DebugLog("[detect] " + state + (string.IsNullOrEmpty(detail) ? "" : " | " + detail));
        }

        private static string SafeCls(AutomationElement el)
        {
            try { return el.Current.ClassName; } catch { return "?"; }
        }

        private static string SafeName(AutomationElement el)
        {
            try { return el.Current.Name; } catch { return null; }
        }

        // Win11 详细信息视图：行内名称单元格是 Name 为"名称"的子元素（其 Value 才是文件名）
        private static AutomationElement FindNameCell(AutomationElement item, int depth)
        {
            if (depth > 3) return null;
            var walker = TreeWalker.RawViewWalker;
            var child = walker.GetFirstChild(item);
            while (child != null)
            {
                string n = SafeName(child);
                if (n == "名称" || n == "Name") return child;
                var sub = FindNameCell(child, depth + 1);
                if (sub != null) return sub;
                child = walker.GetNextSibling(child);
            }
            return null;
        }

        private static IntPtr _cachedHeaderHwnd;
        private static double _cachedHeaderLeft = double.NaN, _cachedHeaderRight;
        private static DateTime _headerCacheTime;

        // 检测是否存在列头"名称"——有列头才是详细信息视图；图标/平铺视图无列头
        private static bool HasNameHeader(IntPtr explorerHwnd)
        {
            if (explorerHwnd == _cachedHeaderHwnd && !double.IsNaN(_cachedHeaderLeft)
                && (DateTime.UtcNow - _headerCacheTime).TotalSeconds < 3)
                return true;
            try
            {
                var root = AutomationElement.FromHandle(explorerHwnd);
                if (root == null) return false;
                var cond = new OrCondition(
                    new PropertyCondition(AutomationElement.NameProperty, "名称"),
                    new PropertyCondition(AutomationElement.NameProperty, "Name"));
                var headers = root.FindAll(TreeScope.Descendants, cond);
                for (int i = 0; i < headers.Count; i++)
                {
                    try
                    {
                        var hr = headers[i].Current.BoundingRectangle;
                        if (hr.Width > 10 && hr.Height > 0)
                        {
                            _cachedHeaderHwnd = explorerHwnd;
                            _cachedHeaderLeft = hr.X; _cachedHeaderRight = hr.Right;
                            _headerCacheTime = DateTime.UtcNow;
                            return true;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// 计算行内"名称区域"（图标+名称）：优先用行内名称子元素，
        /// 找不到则用列头"名称"矩形裁剪整行，最后回退整行。
        /// 高度始终取整行高度（垂直覆盖整行），只做水平裁剪。
        /// </summary>
        private static bool FindNameArea(IntPtr explorerHwnd, AutomationElement item,
            System.Windows.Rect full, out double nameLeft, out double nameRight, out double nameTop, out double nameHeight)
        {
            nameLeft = full.X; nameRight = full.Right; nameTop = full.Y; nameHeight = full.Height;

            // 1) 在 ListItem 子树中找名称单元格（Name 为"名称"），取其水平范围、整行垂直范围
            try
            {
                var cell = FindNameCell(item, 0);
                if (cell != null)
                {
                    var nr = cell.Current.BoundingRectangle;
                    if (nr.Width > 0 && nr.Height > 0 && nr.Right <= full.Right + 2 && nr.Left >= full.X - 2)
                    {
                        // 仅详细信息视图（有列头）才用名称列裁剪；图标/平铺视图用整行 full
                        if (!HasNameHeader(explorerHwnd)) return false;
                        nameLeft = nr.Left;
                        nameRight = nr.Right;
                        nameTop = full.Y;
                        nameHeight = full.Height;
                        return true;
                    }
                }
            }
            catch { }

            // 2) 用列头"名称"矩形裁剪整行（结果缓存 3 秒）
            try
            {
                double colLeft = double.NaN, colRight = 0;
                if (explorerHwnd == _cachedHeaderHwnd && !double.IsNaN(_cachedHeaderLeft)
                    && (DateTime.UtcNow - _headerCacheTime).TotalSeconds < 3)
                {
                    colLeft = _cachedHeaderLeft; colRight = _cachedHeaderRight;
                }
                else
                {
                    var root = AutomationElement.FromHandle(explorerHwnd);
                    if (root != null)
                    {
                        var cond = new PropertyCondition(AutomationElement.NameProperty, "名称");
                        var headers = root.FindAll(TreeScope.Descendants, cond);
                        AutomationElement best = null;
                        double bestTop = double.MaxValue;
                        for (int i = 0; i < headers.Count; i++)
                        {
                            var h = headers[i];
                            try
                            {
                                var hr = h.Current.BoundingRectangle;
                                if (hr.Width <= 10 || hr.Height <= 0) continue;
                                if (hr.Top < bestTop) { bestTop = hr.Top; best = h; }
                            }
                            catch { }
                        }
                        if (best == null)
                        {
                            var cond2 = new PropertyCondition(AutomationElement.NameProperty, "Name");
                            headers = root.FindAll(TreeScope.Descendants, cond2);
                            for (int i = 0; i < headers.Count; i++)
                            {
                                var h = headers[i];
                                try
                                {
                                    var hr = h.Current.BoundingRectangle;
                                    if (hr.Width <= 10 || hr.Height <= 0) continue;
                                    if (hr.Top < bestTop) { bestTop = hr.Top; best = h; }
                                }
                                catch { }
                            }
                        }
                        if (best != null)
                        {
                            var col = best.Current.BoundingRectangle;
                            colLeft = col.X; colRight = col.Right;
                            _cachedHeaderHwnd = explorerHwnd;
                            _cachedHeaderLeft = colLeft; _cachedHeaderRight = colRight;
                            _headerCacheTime = DateTime.UtcNow;
                        }
                    }
                }
                if (!double.IsNaN(colLeft))
                {
                    double left = Math.Max(full.X, colLeft);
                    double right = Math.Min(full.Right, colRight - 1);
                    if (right > left + 40)
                    {
                        nameLeft = left; nameRight = right;
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        // 检测悬停的条目（目录或文件）；isDir 标识类型，主循环据此分流弹面板或文件预览
        public static string DetectHoverItem(int mx, int my, out System.Drawing.Rectangle rect, out bool isDir)
        {
            rect = System.Drawing.Rectangle.Empty;
            isDir = false;
            var pt = new NativeMethods.POINT { X = mx, Y = my };
            var hwnd = NativeMethods.WindowFromPoint(pt);
            if (hwnd == IntPtr.Zero) return null;

            IntPtr explorerHwnd = IntPtr.Zero;
            var cur = hwnd;
            while (cur != IntPtr.Zero)
            {
                var cls = NativeMethods.GetClassOf(cur);
                if (cls == "CabinetWClass") { explorerHwnd = cur; break; }
                cur = NativeMethods.GetParent(cur);
            }
            if (explorerHwnd == IntPtr.Zero) { LogDetect("no-explorer-hwnd", null); return null; }

            var apt = new System.Windows.Point(mx, my);
            AutomationElement item = null;
            LogDetect("step:enter-uia", null);
            try
            {
                // 方法1: 从鼠标点元素向上查找 ListItem/DataItem（兼容部分系统）
                AutomationElement hitEl = AutomationElement.FromPoint(apt);
                LogDetect("step:frompoint-done", hitEl == null ? "null" : "ok");
                if (hitEl != null)
                {
                    string _hitCt = "?", _hitCls = "?";
                    try { _hitCt = hitEl.Current.ControlType.ProgrammaticName; } catch { }
                    try { _hitCls = hitEl.Current.ClassName; } catch { }
                    LogDetect("frompoint-el", "ct=" + _hitCt + " cls=" + _hitCls);
                    var walker = TreeWalker.RawViewWalker;
                    var c = hitEl;
                    int depth = 0;
                    while (c != null && depth < 30)
                    {
                        string cls = SafeCls(c);
                        if (cls == "CabinetWClass") break;
                        try
                        {
                            var ct = c.Current.ControlType;
                            if (ct == ControlType.ListItem || ct == ControlType.DataItem)
                            { item = c; LogDetect("m1-hit", ct.ProgrammaticName); break; }
                        }
                        catch { }
                        c = walker.GetParent(c);
                        depth++;
                    }
                }
            }
            catch { }

            // 方法2: 枚举所有 ListItem/DataItem 并命中测试（更可靠）
            if (item == null)
            {
                LogDetect("step:m2-start", null);
                try
                {
                    var root = AutomationElement.FromHandle(explorerHwnd);
                    if (root != null)
                    {
                        var cond = new OrCondition(
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem));
                        var items = root.FindAll(TreeScope.Descendants, cond);
                        LogDetect("step:m2-found", items.Count + " items");
                        for (int i = 0; i < items.Count; i++)
                        {
                            var it = items[i];
                            try
                            {
                                var r = it.Current.BoundingRectangle;
                                if (mx >= r.X && mx <= r.Right && my >= r.Y && my <= r.Bottom)
                                { item = it; LogDetect("m2-hit", it.Current.ControlType.ProgrammaticName); break; }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
            if (item == null)
            {
                // 方法3: 导航窗格 TreeView 命中测试
                string navPath = DetectNavPaneFolder(explorerHwnd, mx, my);
                if (navPath != null) { LogDetect("hit-navpane", navPath); return navPath; }
                LogDetect("no-item", "mouse=" + mx + "," + my);
                return null;
            }

            string name = SafeName(item);

            System.Windows.Rect full;
            try { full = item.Current.BoundingRectangle; }
            catch { return null; }

            // 计算名称区域（图标 + 名称文本），仅高亮该区域而非整条
            double nameLeft, nameRight, nameTop, nameHeight;
            FindNameArea(explorerHwnd, item, full, out nameLeft, out nameRight, out nameTop, out nameHeight);

            // 用相邻行行距约束高度，避免 UIA 行矩形含内边距导致高亮框偏高
            try
            {
                var walker = TreeWalker.RawViewWalker;
                double pitch = 0;
                var sib = walker.GetNextSibling(item);
                if (sib != null)
                {
                    try { if (sib.Current.ControlType == ControlType.ListItem) pitch = sib.Current.BoundingRectangle.Y - full.Y; } catch { }
                }
                if (pitch <= 0)
                {
                    var prev = walker.GetPreviousSibling(item);
                    if (prev != null)
                    {
                        try { if (prev.Current.ControlType == ControlType.ListItem) pitch = full.Y - prev.Current.BoundingRectangle.Y; } catch { }
                    }
                }
                if (pitch > 0 && nameHeight > pitch) nameHeight = pitch;
            }
            catch { }

            // 触发区域限定为名称区域（图标 + 名称），悬停在其他列不触发
            if (mx < nameLeft || mx > nameRight || my < nameTop || my > nameTop + nameHeight)
            {
                LogDetect("rejected-region", string.Format("mouse={0},{1} nameArea=({2:F0},{3:F0})-({4:F0},{5:F0}) row=({6:F0},{7:F0})-({8:F0},{9:F0})",
                    mx, my, nameLeft, nameTop, nameRight, nameTop + nameHeight, full.X, full.Y, full.Right, full.Bottom));
                return null;
            }

            rect = new System.Drawing.Rectangle((int)nameLeft, (int)nameTop, (int)(nameRight - nameLeft), (int)nameHeight);

            string dirPath = GetExplorerPath(explorerHwnd, name);
            if (dirPath == null) { LogDetect("no-path", "name=" + name); return null; }

            string fullPath = Path.Combine(dirPath, name);
            isDir = Directory.Exists(fullPath);
            LogDetect("hit", (isDir ? "DIR " : "FILE ") + fullPath);
            return fullPath;
        }

        /// <summary>
        /// 检测导航窗格中鼠标悬停的文件夹路径
        /// </summary>
        private static string DetectNavPaneFolder(IntPtr explorerHwnd, int mx, int my)
        {
            IntPtr treeHwnd = IntPtr.Zero;
            NativeMethods.EnumChildProc cb = (h, l) =>
            {
                if (NativeMethods.GetClassOf(h) == "SysTreeView32") { treeHwnd = h; return false; }
                return true;
            };
            NativeMethods.EnumChildWindows(explorerHwnd, cb, IntPtr.Zero);
            if (treeHwnd == IntPtr.Zero) return null;

            NativeMethods.RECT tr;
            if (!NativeMethods.GetWindowRect(treeHwnd, out tr)) return null;
            if (mx < tr.Left || mx > tr.Right || my < tr.Top || my > tr.Bottom) return null;

            uint pid;
            NativeMethods.GetWindowThreadProcessId(treeHwnd, out pid);
            IntPtr proc = NativeMethods.OpenProcess(
                NativeMethods.PROCESS_VM_OPERATION | NativeMethods.PROCESS_VM_READ | NativeMethods.PROCESS_VM_WRITE,
                false, pid);
            if (proc == IntPtr.Zero) return null;

            try
            {
                int htSize = Marshal.SizeOf(typeof(NativeMethods.TVHITTESTINFO));
                IntPtr remoteHt = NativeMethods.VirtualAllocEx(proc, IntPtr.Zero, (uint)htSize,
                    NativeMethods.MEM_COMMIT, NativeMethods.PAGE_READWRITE);
                if (remoteHt == IntPtr.Zero) return null;

                NativeMethods.POINT clientPt = new NativeMethods.POINT { X = mx, Y = my };
                NativeMethods.ScreenToClient(treeHwnd, ref clientPt);

                NativeMethods.TVHITTESTINFO ht = new NativeMethods.TVHITTESTINFO();
                ht.pt = clientPt;
                IntPtr localHt = Marshal.AllocHGlobal(htSize);
                Marshal.StructureToPtr(ht, localHt, false);
                uint written;
                NativeMethods.WriteProcessMemory(proc, remoteHt, localHt, (uint)htSize, out written);

                IntPtr res;
                NativeMethods.SendMessageTimeoutW(treeHwnd, (uint)NativeMethods.TVM_HITTEST,
                    IntPtr.Zero, remoteHt, NativeMethods.SMTO_ABORTIFHUNG, 1000, out res);

                NativeMethods.TVHITTESTINFO outHt = new NativeMethods.TVHITTESTINFO();
                uint read;
                NativeMethods.ReadProcessMemory(proc, remoteHt, localHt, (uint)htSize, out read);
                outHt = (NativeMethods.TVHITTESTINFO)Marshal.PtrToStructure(localHt, typeof(NativeMethods.TVHITTESTINFO));
                Marshal.FreeHGlobal(localHt);
                NativeMethods.VirtualFreeEx(proc, remoteHt, 0, NativeMethods.MEM_RELEASE);

                if (outHt.hItem == IntPtr.Zero) return null;

                // 获取 item 文本并向上构造路径
                int itemSize = Marshal.SizeOf(typeof(NativeMethods.TVITEMW));
                IntPtr remoteItem = NativeMethods.VirtualAllocEx(proc, IntPtr.Zero, (uint)itemSize,
                    NativeMethods.MEM_COMMIT, NativeMethods.PAGE_READWRITE);
                int textSize = 520 * 2;
                IntPtr remoteText = NativeMethods.VirtualAllocEx(proc, IntPtr.Zero, (uint)textSize,
                    NativeMethods.MEM_COMMIT, NativeMethods.PAGE_READWRITE);
                if (remoteItem == IntPtr.Zero || remoteText == IntPtr.Zero) return null;

                try
                {
                    IntPtr localItem = Marshal.AllocHGlobal(itemSize);
                    IntPtr localText = Marshal.AllocHGlobal(textSize);

                    string path = GetTreeItemText(treeHwnd, proc, remoteItem, remoteText,
                        localItem, localText, itemSize, textSize, outHt.hItem);
                    if (string.IsNullOrEmpty(path)) return null;

                    IntPtr cur = outHt.hItem;
                    for (int i = 0; i < 10; i++)
                    {
                        IntPtr parentRes;
                        NativeMethods.SendMessageTimeoutW(treeHwnd, (uint)NativeMethods.TVM_GETNEXTITEM,
                            (IntPtr)NativeMethods.TVGN_PARENT, cur, NativeMethods.SMTO_ABORTIFHUNG, 1000, out parentRes);
                        IntPtr parent = parentRes;
                        if (parent == IntPtr.Zero) break;
                        string parentText = GetTreeItemText(treeHwnd, proc, remoteItem, remoteText,
                            localItem, localText, itemSize, textSize, parent);
                        if (string.IsNullOrEmpty(parentText)) break;
                        // 跳过根节点（如"此电脑"、"快速访问"）
                        if (parentText == "此电脑" || parentText == "快速访问" || parentText == "网络" ||
                            parentText == "This PC" || parentText == "Quick access" || parentText == "Network")
                            break;
                        path = parentText + "\\" + path;
                        cur = parent;
                    }

                    Marshal.FreeHGlobal(localItem);
                    Marshal.FreeHGlobal(localText);

                    // 验证路径是否存在
                    if (Directory.Exists(path)) return path;
                    return null;
                }
                finally
                {
                    NativeMethods.VirtualFreeEx(proc, remoteItem, 0, NativeMethods.MEM_RELEASE);
                    NativeMethods.VirtualFreeEx(proc, remoteText, 0, NativeMethods.MEM_RELEASE);
                }
            }
            finally
            {
                NativeMethods.CloseHandle(proc);
            }
        }

        private static string GetTreeItemText(IntPtr treeHwnd, IntPtr proc, IntPtr remoteItem, IntPtr remoteText,
            IntPtr localItem, IntPtr localText, int itemSize, int textSize, IntPtr hItem)
        {
            NativeMethods.TVITEMW item = new NativeMethods.TVITEMW();
            item.mask = NativeMethods.TVIF_HANDLE | NativeMethods.TVIF_TEXT;
            item.hItem = hItem;
            item.pszText = remoteText;
            item.cchTextMax = 520;
            Marshal.StructureToPtr(item, localItem, false);
            uint written;
            NativeMethods.WriteProcessMemory(proc, remoteItem, localItem, (uint)itemSize, out written);
            IntPtr res;
            NativeMethods.SendMessageTimeoutW(treeHwnd, (uint)NativeMethods.TVM_GETITEMW,
                IntPtr.Zero, remoteItem, NativeMethods.SMTO_ABORTIFHUNG, 1000, out res);
            uint read;
            NativeMethods.ReadProcessMemory(proc, remoteText, localText, (uint)textSize, out read);
            return Marshal.PtrToStringUni(localText);
        }

        private static string GetExplorerPath(IntPtr hwnd, string itemName)
        {
            var tabs = GetExplorerTabPaths(hwnd);
            if (tabs == null || tabs.Count == 0) return null;
            // 多标签窗口：选择包含目标项的标签页路径
            if (!string.IsNullOrEmpty(itemName))
            {
                foreach (var p in tabs)
                {
                    try { if (Directory.Exists(Path.Combine(p, itemName)) || File.Exists(Path.Combine(p, itemName))) return p; }
                    catch { }
                }
            }
            return tabs[0];
        }

        private static System.Collections.Generic.List<string> GetExplorerTabPaths(IntPtr hwnd)
        {
            if (hwnd == _cachedHwnd && _cachedTabs != null && (DateTime.UtcNow - _cacheTime).TotalSeconds < 2)
                return _cachedTabs;
            _cachedHwnd = hwnd;
            _cachedTabs = new System.Collections.Generic.List<string>();
            _cacheTime = DateTime.UtcNow;

            try
            {
                // 用 Shell.Application COM 取该窗口所有标签页的路径（每个标签一个条目，HWND 相同）
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType != null)
                {
                    object shell = Activator.CreateInstance(shellType);
                    object windows = shellType.InvokeMember("Windows",
                        System.Reflection.BindingFlags.InvokeMethod, null, shell, null);
                    if (windows != null)
                    {
                        int count = (int)windows.GetType().InvokeMember("Count",
                            System.Reflection.BindingFlags.GetProperty, null, windows, null);
                        long targetHwnd = hwnd.ToInt64();
                        for (int i = 0; i < count; i++)
                        {
                            object win = windows.GetType().InvokeMember("Item",
                                System.Reflection.BindingFlags.InvokeMethod, null,
                                windows, new object[] { i });
                            if (win == null) continue;
                            try
                            {
                                long wh = (long)win.GetType().InvokeMember("HWND",
                                    System.Reflection.BindingFlags.GetProperty, null, win, null);
                                if (wh != targetHwnd) continue;
                                object doc = win.GetType().InvokeMember("Document",
                                    System.Reflection.BindingFlags.GetProperty, null, win, null);
                                if (doc == null) continue;
                                object folder = doc.GetType().InvokeMember("Folder",
                                    System.Reflection.BindingFlags.GetProperty, null, doc, null);
                                if (folder == null) continue;
                                object self = folder.GetType().InvokeMember("Self",
                                    System.Reflection.BindingFlags.GetProperty, null, folder, null);
                                if (self == null) continue;
                                string path = (string)self.GetType().InvokeMember("Path",
                                    System.Reflection.BindingFlags.GetProperty, null, self, null);
                                if (!string.IsNullOrEmpty(path) && Directory.Exists(path)
                                    && !_cachedTabs.Contains(path))
                                    _cachedTabs.Add(path);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }

            // 兜底：面包屑解析（部分系统 COM 不可用时使用）
            if (_cachedTabs.Count == 0)
            {
                try
                {
                    var el = AutomationElement.FromHandle(hwnd);
                    if (el != null)
                    {
                        var cond = new PropertyCondition(AutomationElement.ClassNameProperty, "FileExplorerExtensions.BreadcrumbBarItemControl");
                        var crumbs = el.FindAll(TreeScope.Descendants, cond);
                        if (crumbs.Count > 0)
                        {
                            string path = null;
                            foreach (AutomationElement crumb in crumbs)
                            {
                                string name = crumb.Current.Name;
                                if (string.IsNullOrEmpty(name)) continue;
                                try { if (crumb.Current.IsOffscreen) continue; } catch { }
                                var match = Regex.Match(name, @"\(([A-Za-z]:)\)");
                                if (match.Success)
                                    path = match.Groups[1].Value + "\\";
                                else if (path != null)
                                {
                                    string next = Path.Combine(path, name);
                                    if (Directory.Exists(next)) path = next;
                                }
                            }
                            if (path != null && Directory.Exists(path)) _cachedTabs.Add(path);
                        }
                    }
                }
                catch { }
            }

            return _cachedTabs;
        }
    }
}