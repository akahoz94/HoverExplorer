using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace HoverExplorer
{
    internal static class Program
    {
        public const string Version = "1.0.0.6";

        private static string _desktopPath;
        private static IntPtr _listview = IntPtr.Zero;
        private static RemoteReader _reader;
        private static readonly List<PopupForm> _panels = new List<PopupForm>();

        // 刷新正在显示指定目录的所有面板（跨面板移动/复制后同步源和目标列表）
        public static void RefreshPanelsShowing(string dir1, string dir2)
        {
            foreach (var p in _panels)
            {
                if (p.IsDisposed) continue;
                if (p.ShowingPath(dir1) || (dir2 != null && p.ShowingPath(dir2))) p.RefreshPanel();
            }
        }
        private static string _hoverFolder;
        private static string _extHoverFile;
        private static string _lastHoverPath;
        private static DateTime _extNullSince = DateTime.MinValue;
        private static DateTime _hoverStart;
        private static DateTime _closeStart = DateTime.MinValue;
        private static System.Windows.Forms.Timer _subCloseTimer;
        private static int _subCloseLevel;
        private static System.Windows.Forms.Timer _timer;
        private static NotifyIcon _notifyIcon;
        internal static ContextMenuStrip TrayMenu;
        private static HotkeyWindow _hotkey;
        private static bool _hoverEnabled = true;
        private static bool _settingsOpen; // 设置窗口打开期间挂起面板自动关闭（面板可能是对话框属主）

        // 开机自启：写 HKCU Run 键，无需安装
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "HoverExplorer";
        private static bool IsAutoStart()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    var v = k == null ? null : k.GetValue(RunValueName) as string;
                    return !string.IsNullOrEmpty(v);
                }
            }
            catch { return false; }
        }
        private static void SetAutoStart(bool enable)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (k == null) return;
                    if (enable) k.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue(RunValueName, false);
                }
            }
            catch { }
        }

        public static void Log(string msg) { Trace.WriteLine(msg); }

        private static readonly object _dbgLock = new object();
        private static string _logPath;
        public static void DebugLog(string msg)
        {
            if (!Config.DebugLog) return;
            try
            {
                lock (_dbgLock)
                    File.AppendAllText(_logPath ?? (_logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hover_debug.log")),
                        DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + Environment.NewLine);
            }
            catch { }
        }

        private static void TruncateLogIfNeeded()
        {
            try
            {
                var path = _logPath ?? (_logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hover_debug.log"));
                if (!File.Exists(path)) return;
                if (new FileInfo(path).Length < 2000000) return;
                var lines = File.ReadAllLines(path);
                File.WriteAllLines(path, lines.Skip(Math.Max(0, lines.Length - 500)).ToArray());
            }
            catch { }
        }

        private static void LogBootInfo()
        {
            DebugLog("[boot] === HoverExplorer start ===");
            DebugLog("[boot] os=" + Environment.OSVersion.VersionString);
            try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) DebugLog("[boot] dpi=" + g.DpiX); } catch { }
            DebugLog("[boot] theme=" + Config.Theme + " lang=" + Config.Language + " explorer=" + Config.ExplorerEnabled + " preview=" + Config.PreviewFiles + " previewDelay=" + Config.PreviewDelay + " checkInterval=" + Config.CheckInterval + " debugLog=" + Config.DebugLog + " hotkey=" + Config.HotkeyEnabled + "/" + SettingsForm.HotkeyDisplay(Config.HotkeyEnabled, Config.HotkeyModifiers, Config.HotkeyKey));
        }

        private static DateTime _lastHideTooltip;
        private static void HideTooltips()
        {
            if ((DateTime.UtcNow - _lastHideTooltip).TotalMilliseconds < 80) return;
            _lastHideTooltip = DateTime.UtcNow;
            int n = 0;
            NativeMethods.EnumWindows((h, l) =>
            {
                if (NativeMethods.GetClassOf(h) == "tooltips_class32" && NativeMethods.IsWindowVisible(h))
                {
                    NativeMethods.SendMsg(h, 0x041E, IntPtr.Zero, IntPtr.Zero); // TTM_POP
                    // 移到屏幕外：移动窗口触发系统立即重绘原覆盖区域，避免 SW_HIDE 残影
                    NativeMethods.SetWindowPos(h, IntPtr.Zero, -32000, -32000, 0, 0, 0x0001 | 0x0004);
                    n++;
                }
                return true;
            }, IntPtr.Zero);
            if (n > 0) DebugLog("[tooltip] hidden " + n);
        }

        [STAThread]
        static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => DebugLog("[FATAL] UnhandledException: " + e.ExceptionObject);
            Application.ThreadException += (s, e) => DebugLog("[FATAL] ThreadException: " + e.Exception);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Config.Load();
            _desktopPath = GetDesktopPath();
            TruncateLogIfNeeded();
            LogBootInfo();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            _notifyIcon = new NotifyIcon
            {
                Icon = CreateTrayIcon(),
                Visible = !Config.HideTrayIcon,
                Text = "HoverExplorer",
            };
            var trayMenu = new ContextMenuStrip();
            var explorerItem = new ToolStripMenuItem(I18n.T("explorer_hover"));
            explorerItem.CheckOnClick = true;
            explorerItem.Checked = Config.ExplorerEnabled;
            explorerItem.CheckedChanged += (s, e) =>
            {
                Config.ExplorerEnabled = explorerItem.Checked;
                Config.Save();
            };
            trayMenu.Items.Add(explorerItem);
            var previewItem = new ToolStripMenuItem(I18n.T("file_preview"));
            previewItem.CheckOnClick = true;
            previewItem.Checked = Config.PreviewFiles;
            previewItem.CheckedChanged += (s, e) =>
            {
                Config.PreviewFiles = previewItem.Checked;
                Config.Save();
            };
            trayMenu.Items.Add(previewItem);
            var debugItem = new ToolStripMenuItem(I18n.T("debug_log"));
            debugItem.CheckOnClick = true;
            debugItem.Checked = Config.DebugLog;
            debugItem.CheckedChanged += (s, e) => { Config.DebugLog = debugItem.Checked; Config.Save(); if (Config.DebugLog) LogBootInfo(); };
            trayMenu.Items.Add(debugItem);
            var tooltipItem = new ToolStripMenuItem(I18n.T("hide_tooltip"));
            tooltipItem.CheckOnClick = true;
            tooltipItem.Checked = Config.HideTooltip;
            tooltipItem.CheckedChanged += (s, e) => { Config.HideTooltip = tooltipItem.Checked; Config.Save(); };
            trayMenu.Items.Add(tooltipItem);
            var autostartItem = new ToolStripMenuItem(I18n.T("autostart"));
            autostartItem.CheckOnClick = true;
            autostartItem.Checked = IsAutoStart();
            autostartItem.CheckedChanged += (s, e) => SetAutoStart(autostartItem.Checked);
            trayMenu.Items.Add(autostartItem);
            var hideTrayItem = new ToolStripMenuItem(I18n.T("hide_tray"));
            hideTrayItem.CheckOnClick = true;
            hideTrayItem.Checked = Config.HideTrayIcon;
            hideTrayItem.CheckedChanged += (s, e) =>
            {
                Config.HideTrayIcon = hideTrayItem.Checked;
                Config.Save();
                _notifyIcon.Visible = !Config.HideTrayIcon;
            };
            trayMenu.Items.Add(hideTrayItem);
            trayMenu.Items.Add(new ToolStripSeparator());
            var exitItem = new ToolStripMenuItem(I18n.T("exit"));

            Action refreshExitText = () =>
            {
                exitItem.Text = I18n.T("exit");
            };
            exitItem.Click += (s, e) => Application.Exit();
            refreshExitText();

            var themeItem = new ToolStripMenuItem(I18n.T("theme"));
            var themeDark = new ToolStripMenuItem(I18n.T("theme_dark")) { Checked = (Config.Theme == "dark") };
            var themeLight = new ToolStripMenuItem(I18n.T("theme_light")) { Checked = (Config.Theme == "light") };
            Action refreshThemeMenus = () => { themeDark.Checked = Config.Theme == "dark"; themeLight.Checked = Config.Theme == "light"; };
            themeDark.Click += (s, e) => { Config.Theme = "dark"; Config.ApplyTheme(); Config.Save(); refreshThemeMenus(); foreach (var p in _panels) p.RefreshPanel(); };
            themeLight.Click += (s, e) => { Config.Theme = "light"; Config.ApplyTheme(); Config.Save(); refreshThemeMenus(); foreach (var p in _panels) p.RefreshPanel(); };
            themeItem.DropDownItems.Add(themeDark);
            themeItem.DropDownItems.Add(themeLight);

            var settingsItem = new ToolStripMenuItem(I18n.T("settings"), null, (EventHandler)((s, e) =>
            {
                _settingsOpen = true;
                try
                {
                    using (var form = new SettingsForm())
                    {
                        if (form.ShowDialog() == DialogResult.OK)
                        {
                            Config.HotkeyEnabled = form.HotkeyEnabled;
                            Config.HotkeyModifiers = form.HotkeyModifiers;
                            Config.HotkeyKey = form.HotkeyKey;
                            Config.HoverDelay = form.HoverDelaySetting;
                            Config.Save();
                            bool ok = _hotkey.ReRegister();
                            if (!ok && Config.HotkeyEnabled)
                                MessageBox.Show(I18n.T("hotkey_reg_fail"), I18n.T("settings"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            else
                                MessageBox.Show(I18n.T("hotkey_saved"), I18n.T("settings"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                            refreshExitText();
                        }
                    }
                }
                finally { _settingsOpen = false; }
            }));

            var langItem = new ToolStripMenuItem(I18n.T("language"));
            for (int i = 0; i < I18n.LangCodes.Length; i++)
            {
                var code = I18n.LangCodes[i];
                var name = I18n.LangNames[i];
                var sub = new ToolStripMenuItem(name) { Checked = (code == Config.Language) };
                sub.Click += (s, e) =>
                {
                    Config.Language = code;
                    I18n.Lang = code;
                    Config.RefreshSortLabels();
                    Config.Save();
                    foreach (ToolStripMenuItem si in langItem.DropDownItems) si.Checked = false;
                    sub.Checked = true;
                    explorerItem.Text = I18n.T("explorer_hover");
                    previewItem.Text = I18n.T("file_preview");
                    debugItem.Text = I18n.T("debug_log");
                    tooltipItem.Text = I18n.T("hide_tooltip");
                    autostartItem.Text = I18n.T("autostart");
                    hideTrayItem.Text = I18n.T("hide_tray");

                    langItem.Text = I18n.T("language");
                    themeItem.Text = I18n.T("theme");
                    themeDark.Text = I18n.T("theme_dark");
                    themeLight.Text = I18n.T("theme_light");
                    settingsItem.Text = I18n.T("settings");
                    refreshExitText();
                };
                langItem.DropDownItems.Add(sub);
            }
            trayMenu.Items.Add(langItem);
            trayMenu.Items.Add(themeItem);

            trayMenu.Items.Add(settingsItem);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(exitItem);
            TrayMenu = trayMenu;
            // 选项菜单关闭后解除所有面板的菜单打开标记（避免面板被悬停丢失逻辑关闭后标记残留）
            trayMenu.Closed += (s, e) => { foreach (var p in _panels) p.MenuOpen = false; };
            _notifyIcon.ContextMenuStrip = trayMenu;

            _hotkey = new HotkeyWindow();
            _hotkey.HotkeyPressed += () =>
            {
                _hoverEnabled = !_hoverEnabled;
                if (!_hoverEnabled)
                {
                    CloseAll();
                    _hoverFolder = null;
                    _extHoverFile = null;
                    PopupForm.ScheduleExtPreviewClose(0, 0);
                }
                try { _notifyIcon.ShowBalloonTip(800, "HoverExplorer", _hoverEnabled ? I18n.T("hover_enabled") : I18n.T("hover_disabled"), ToolTipIcon.Info); } catch { }
                DebugLog("[hotkey] hover " + (_hoverEnabled ? "enabled" : "disabled"));
            };

            PopupForm.OnSortChange = () =>
            {
                foreach (var p in _panels) p.RefreshPanel();
            };

            _timer = new System.Windows.Forms.Timer { Interval = Config.CheckInterval };
            _timer.Tick += (s, e) => Tick();
            _timer.Start();

            Application.ApplicationExit += (s, e) =>
            {
                _timer.Stop();
                if (_notifyIcon != null) { _notifyIcon.Visible = false; _notifyIcon.Dispose(); }
                if (_hotkey != null) _hotkey.Unregister();
                if (_reader != null) _reader.Dispose();
            };

            Application.Run();
        }

        private static void Tick()
        {
            try { TickImpl(); }
            catch (Exception ex) { Log("[tick异常] " + ex.Message); }
        }

        private static void TickImpl()
        {
            if (!_hoverEnabled)
            {
                PopupForm.EnsureExtPreviewTopmost();
                return;
            }

            NativeMethods.POINT pt;
            NativeMethods.GetCursorPos(out pt);
            int mx = pt.X, my = pt.Y;

            if (Control.MouseButtons == MouseButtons.Left)
            {
                bool inExtPv = PopupForm.FilePreviewContainsPoint(mx, my);
                bool inPanelPv = _panels.Any(p => p.PreviewContainsPoint(mx, my));
                if (!inExtPv && !inPanelPv)
                {
                    PopupForm.CancelFilePreview();
                    foreach (var p in _panels) p.ClosePreviewIfAny();
                }
                PopupForm.EnsureExtPreviewTopmost();
                return;
            }

            // 预览显示期间每 Tick 维持 TOPMOST（不激活），防止被前台窗口盖住
            PopupForm.EnsureExtPreviewTopmost();
            // 面板同理：偶发弹出后被资源管理器窗口压住，每 Tick 重新置顶
            foreach (var p in _panels) p.EnsureTopmost();

            bool inPanel = _panels.Any(p => p.ContainsPoint(mx, my));
            bool inPreview = _panels.Any(p => p.PreviewContainsPoint(mx, my));
            bool inExtPreview = PopupForm.FilePreviewContainsPoint(mx, my);
            if (inPanel || _settingsOpen || _panels.Any(p => p.MenuOpen) || _panels.Any(p => p.ModalOpen) || _panels.Any(p => p.IsDragging) || inPreview || inExtPreview)
            {
                if (inPreview || inExtPreview) Program.DebugLog("[tick] mouse in preview, skip mx=" + mx + ",my=" + my + " inPanel=" + inPanel + " inPreview=" + inPreview + " inExtPreview=" + inExtPreview + " panels=" + _panels.Count);
                _closeStart = DateTime.MinValue;
                return;
            }

            string hoverPath = null;
            bool hoverIsDir = false;
            Rectangle hoverRect = Rectangle.Empty;

            if (NativeMethods.PointOnDesktop(mx, my))
            {
                hoverPath = DetectDesktopItem(mx, my, out hoverRect, out hoverIsDir);
            }
            else if (Config.ExplorerEnabled)
            {
                hoverPath = ExplorerSupport.DetectHoverItem(mx, my, out hoverRect, out hoverIsDir);
            }

            string hoverFolder = (hoverPath != null && hoverIsDir) ? hoverPath : null;
            // 无面板时悬停文件（图片/文本）直接预览，受"文件预览"开关控制
            string hoverFile = (hoverPath != null && !hoverIsDir && _panels.All(p => p.Pinned)) ? hoverPath : null;
            if (hoverPath != null && Config.HideTooltip) HideTooltips();
            if (hoverPath != _lastHoverPath)
            {
                _lastHoverPath = hoverPath;
                DebugLog("[hover] " + (hoverPath == null ? "null" : (hoverIsDir ? "DIR " : "FILE ") + hoverPath) + " @" + mx + "," + my);
            }
            if (hoverFile != null)
            {
                _extNullSince = DateTime.MinValue;
                if (hoverFile != _extHoverFile)
                {
                    _extHoverFile = hoverFile;
                    PopupForm.ScheduleFilePreview(hoverFile, new Point(mx, my));
                }
            }
            else if (_extHoverFile != null)
            {
                // UIA 偶发失效（如窗口激活瞬间检测不到行）不立即关预览：宽限 300ms
                if (_extNullSince == DateTime.MinValue) _extNullSince = DateTime.UtcNow;
                else if ((DateTime.UtcNow - _extNullSince).TotalMilliseconds > 300)
                {
                    _extHoverFile = null;
                    _extNullSince = DateTime.MinValue;
                    PopupForm.ScheduleExtPreviewClose(mx, my);
                }
            }

            if (hoverFolder == null)
            {
                MaybeClose();
                return;
            }

            _closeStart = DateTime.MinValue;
            if (hoverFolder != _hoverFolder)
            {
                DebugLog("[hover-dir] new target " + hoverFolder);
                _hoverFolder = hoverFolder;
                _hoverStart = DateTime.UtcNow;
                return;
            }

            if ((DateTime.UtcNow - _hoverStart).TotalSeconds >= Config.HoverDelay)
            {
                var nonPinned = _panels.Where(p => !p.Pinned).ToList();
                if (nonPinned.Count == 0 || nonPinned[0].CurrentPath != hoverFolder)
                {
                    DebugLog("[panel] opening " + hoverFolder);
                    foreach (var p in nonPinned) p.ClosePanel();
                    _panels.RemoveAll(p => !p.Pinned);
                    var p2 = new PopupForm(0, OpenSub, DropResolver, ScheduleCloseAfter);
                    if (p2.ShowPanel(hoverFolder, mx + Config.PopupOffset, my + Config.PopupOffset))
                    {
                        ShowHighlight(hoverRect);
                        _panels.Add(p2);
                        p2.Show();
                        p2.FocusSearch();
                        DebugLog("[panel] OPENED at " + p2.Left + "," + p2.Top + " size " + p2.Width + "x" + p2.Height);
                    }
                }
            }
        }

        // 检测桌面悬停的条目（目录或文件）；isDir 标识类型，主循环据此分流弹面板或文件预览
        private static string DetectDesktopItem(int mx, int my, out Rectangle rect, out bool isDir)
        {
            rect = Rectangle.Empty;
            isDir = false;
            var reader = EnsureReader();
            if (reader == null || _listview == IntPtr.Zero) return null;

            NativeMethods.RECT rc;
            NativeMethods.GetWindowRect(_listview, out rc);
            if (mx < rc.Left || mx > rc.Right || my < rc.Top || my > rc.Bottom) return null;

            int idx = reader.HitTest(mx, my);
            if (idx < 0) return null;

            string name = reader.GetItemText(idx);
            if (string.IsNullOrEmpty(name)) return null;

            string full = Path.Combine(_desktopPath, name);
            isDir = Directory.Exists(full);
            rect = reader.GetItemRect(idx);
            return full;
        }

        private static void MaybeClose()
        {
            if (_closeStart == DateTime.MinValue) _closeStart = DateTime.UtcNow;
            else if ((DateTime.UtcNow - _closeStart).TotalSeconds >= Config.CloseDelay)
            {
                DebugLog("[panel] close-delay fired (hover lost " + (DateTime.UtcNow - _closeStart).TotalMilliseconds.ToString("F0") + "ms ago)");
                CloseAll();
                _closeStart = DateTime.MinValue;
                _hoverFolder = null;
            }
        }

        private static RemoteReader EnsureReader()
        {
            var lv = GetDesktopListview();
            if (lv != _listview)
            {
                _listview = lv;
                if (_reader != null) _reader.Dispose();
                try { _reader = lv != IntPtr.Zero ? new RemoteReader(lv) : null; }
                catch (Exception ex) { _reader = null; Log("[错误] RemoteReader: " + ex.Message); }
            }
            return _reader;
        }

        private static void OpenSub(int level, string path, int rowScreenY)
        {
            DebugLog("[cascade] OpenSub 子面板 level=" + (level + 1) + " path=" + path + " 当前面板数=" + _panels.Count);
            if (_subCloseTimer != null) { _subCloseTimer.Stop(); _subCloseTimer.Dispose(); _subCloseTimer = null; }
            CloseAfter(level);
            if (_panels.Count == 0 || !_panels[level].IsHandleCreated) { DebugLog("[cascade] OpenSub 中止：父面板不存在 level=" + level); return; }
            int x = _panels[level].RightEdge() + Config.SubGap;
            int y = rowScreenY;
            var form = new PopupForm(level + 1, OpenSub, DropResolver, ScheduleCloseAfter);
            if (form.ShowPanel(path, x, y))
            {
                _panels.Add(form);
                form.Show();
                _panels[level].SetActiveDir(path);
                DebugLog("[cascade] 子面板已显示 level=" + (level + 1) + " at " + form.Left + "," + form.Top + " 面板总数=" + _panels.Count);
            }
            else
            {
                DebugLog("[cascade] ShowPanel 失败（目录读取失败？） path=" + path);
                form.Dispose();
            }
        }

        private static void CloseAfter(int level)
        {
            int before = _panels.Count;
            for (int i = _panels.Count - 1; i > level; i--)
            {
                var p = _panels[i];
                if (p.Pinned) continue;
                _panels.RemoveAt(i);
                p.ClosePanel();
            }
            if (before != _panels.Count) DebugLog("[cascade] CloseAfter level=" + level + " 面板数 " + before + " -> " + _panels.Count);
            if (level >= 0 && level < _panels.Count) _panels[level].ClearActiveDir();
        }

        private static void CloseAll()
        {
            var toClose = _panels.Where(p => !p.Pinned).ToList();
            var toKeep = _panels.Where(p => p.Pinned).ToList();
            DebugLog("[cascade] CloseAll 关闭面板 close=" + toClose.Count + " keep(pinned)=" + toKeep.Count);
            HideHighlight();
            if (_subCloseTimer != null) { _subCloseTimer.Stop(); _subCloseTimer.Dispose(); _subCloseTimer = null; }
            foreach (var p in toClose) p.ClosePanel();
            _panels.Clear();
            _panels.AddRange(toKeep);
        }

        private static HighlightOverlay _highlight;

        private static void ShowHighlight(Rectangle r)
        {
            if (r.IsEmpty) return;
            HideHighlight();
            try { _highlight = new HighlightOverlay(r); _highlight.Show(); } catch { }
        }

        private static void HideHighlight()
        {
            if (_highlight != null) { try { _highlight.Close(); _highlight.Dispose(); } catch { } _highlight = null; }
        }

        private static void ScheduleCloseAfter(int level)
        {
            if (_subCloseTimer != null) { _subCloseTimer.Stop(); _subCloseTimer.Dispose(); _subCloseTimer = null; }
            _subCloseLevel = level;
            _subCloseTimer = new System.Windows.Forms.Timer { Interval = 150 };
            _subCloseTimer.Tick += (s, e) =>
            {
                _subCloseTimer.Stop(); _subCloseTimer.Dispose(); _subCloseTimer = null;
                NativeMethods.POINT pt;
                NativeMethods.GetCursorPos(out pt);
                bool inSub = false;
                for (int i = _subCloseLevel + 1; i < _panels.Count; i++)
                    if (_panels[i].ContainsPoint(pt.X, pt.Y)) { inSub = true; break; }
                if (!inSub) CloseAfter(_subCloseLevel);
            };
            _subCloseTimer.Start();
        }

        private static string DropResolver(int x, int y)
        {
            var reader = EnsureReader();
            if (reader == null || _listview == IntPtr.Zero) return null;
            NativeMethods.RECT rect;
            NativeMethods.GetWindowRect(_listview, out rect);
            if (x < rect.Left || x > rect.Right || y < rect.Top || y > rect.Bottom) return null;
            int idx = reader.HitTest(x, y);
            if (idx < 0) return null;
            string name = reader.GetItemText(idx);
            if (string.IsNullOrEmpty(name)) return null;
            string full = Path.Combine(_desktopPath, name);
            return Directory.Exists(full) ? full : null;
        }

        private static IntPtr GetDesktopListview()
        {
            var progman = NativeMethods.FindWindow("Progman", null);
            var view = NativeMethods.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view == IntPtr.Zero)
            {
                IntPtr worker = IntPtr.Zero;
                while (true)
                {
                    worker = NativeMethods.FindWindowEx(IntPtr.Zero, worker, "WorkerW", null);
                    if (worker == IntPtr.Zero) break;
                    var v = NativeMethods.FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (v != IntPtr.Zero) { view = v; break; }
                }
            }
            if (view == IntPtr.Zero) return IntPtr.Zero;
            return NativeMethods.FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
        }

        private static string GetDesktopPath()
        {
            try { return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); }
            catch { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop"); }
        }

        private static Icon CreateTrayIcon()
        {
            try
            {
                var bmp = new Bitmap(32, 32);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    var folderColor = ColorTranslator.FromHtml("#2d3a5f");
                    var outline = ColorTranslator.FromHtml("#9cdcfe");
                    var dot = ColorTranslator.FromHtml("#2d7a3d");
                    using (var b = new SolidBrush(folderColor))
                    {
                        g.FillRectangle(b, 3, 5, 13, 6);
                        g.FillRectangle(b, 3, 9, 26, 18);
                    }
                    using (var p = new Pen(outline, 2))
                        g.DrawRectangle(p, 3, 9, 26, 18);
                    using (var b = new SolidBrush(dot))
                        g.FillEllipse(b, 21, 14, 6, 6);
                }
                var hicon = bmp.GetHicon();
                var icon = Icon.FromHandle(hicon);
                return icon;
            }
            catch { return SystemIcons.Application; }
        }
    }

    internal class HotkeyWindow : NativeWindow
    {
        public event Action HotkeyPressed;
        private const int HotkeyId = 0xB001;
        private bool _registered;

        public HotkeyWindow()
        {
            var cp = new CreateParams { Caption = "HE_Hotkey" };
            CreateHandle(cp);
            Register();
        }

        private void Register()
        {
            if (_registered) return;
            if (!Config.HotkeyEnabled) return;
            if (Config.HotkeyModifiers == 0 && Config.HotkeyKey == 0) return;
            _registered = NativeMethods.RegisterHotKey(Handle, HotkeyId, Config.HotkeyModifiers, Config.HotkeyKey);
        }

        public bool ReRegister()
        {
            UnregisterInternal();
            Register();
            return _registered || !Config.HotkeyEnabled;
        }

        private void UnregisterInternal()
        {
            if (_registered)
            {
                NativeMethods.UnregisterHotKey(Handle, HotkeyId);
                _registered = false;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                if (HotkeyPressed != null) HotkeyPressed();
            }
            base.WndProc(ref m);
        }

        public void Unregister()
        {
            UnregisterInternal();
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }
}