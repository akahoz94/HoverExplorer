using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace HoverExplorer
{
    // 历史/收藏文件夹入口列表：表头（标题+⧉复制+图钉固定+⌄折叠）与文件列表行为一致；
    // 收藏分组在前、历史在后；点击行展开该文件夹级联列表，右键行管理。
    // 数据变更（RecordRecent/收藏增删）实时重建（NotifyChanged）；固定后不自动关闭
    internal class EntryListForm : Form
    {
        private readonly Action<string, Point> _onOpenFolder;
        private readonly List<Control> _rows = new List<Control>();   // 分组标签+行（重建时清除）
        private Timer _watchTimer;
        private bool _dirty;
        private static EntryListForm _current;   // 当前开着的实例（实时更新通知用）

        private Label _titleLbl;
        private Label _pinBtn;
        private Label _foldBtn;
        private Panel _headPanel;   // 表头容器：宽度须随 _listW 同步，否则按钮被裁切
        private bool _pinned;
        private int _listW = 340;
        private int _headerH = 26;
        private int _fullH = 60;
        // 行区/表头左右内缩与底部留白：与 PopupForm 一致，不顶到面板左右边缘与底边，
        // 与深色配色下圆角描边（同背景色）视觉协调
        private const int ListPadLr = 6;
        private const int ListPadBottom = 6;
        private Timer _rowHoverTimer;     // 行悬停 400ms → 右侧弹子列表
        private string _rowHoverPath;
        private EntryRow _rowHoverRow;

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // 与面板/预览一致：暗色标题区 + DWM 圆角（Win11）+ 投影
            Win11.Apply(Handle, Config.Theme != "light", Config.Border);
        }

        public EntryListForm(Action<string, Point> onOpenFolder)
        {
            _onOpenFolder = onOpenFolder;
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Config.ListCanvas;   // 配色跟随文件列表（画布底）
            DoubleBuffered = false;   // 双缓冲绘制在首次弹出时偶发 BufferedGraphics AV 崩溃（进程退出），改直绘
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Escape) return;
                // Esc 主动关闭后区域内不再重弹（否则鼠标停在触发区时 HoverDelay 后又弹回来）；
                // 移出区域再回来即恢复武装
                Program._entryArmed = false;
                Close();
            };
            _fnBuildHeader();
            _fnBuildRows();
        }

        // ---- 表头：拖动固定 + 图钉 + 折叠为卡片 ----
        private void _fnBuildHeader()
        {
            _headPanel = new Panel { BackColor = Config.ListHdbg, Location = new Point(ListPadLr, 0), Size = new Size(_listW - ListPadLr * 2, _headerH) };
            // 实际拖动后才固定；单纯点击不固定（与文件列表表头一致）
            MouseEventHandler drag = (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                var before = Location;
                NativeMethods.ReleaseCapture(); NativeMethods.SendMessage(Handle, 0xA1, 0x2, 0);
                if (Location != before && !_pinned) _fnTogglePin();
            };
            _headPanel.MouseDown += drag;

            _titleLbl = new Label
            {
                Text = I18n.T("entry_title"),
                Font = new Font(Config.FontName, Config.FontSize, FontStyle.Bold),
                ForeColor = Config.ListFg,
                BackColor = Config.ListHdbg,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(6, 3),
                Size = new Size(_headPanel.Width - 58, 20),
                Cursor = Cursors.Hand,
            };
            Tips.Set(_titleLbl, I18n.T("entry_tip"));
            _titleLbl.MouseDown += drag;
            _headPanel.Controls.Add(_titleLbl);

            // 图钉：固定后不随鼠标离开自动关闭
            _pinBtn = new Label
            {
                Size = new Size(20, 18),
                AutoSize = false,
                BackColor = Config.ListHdbg,
                Image = PreviewForm.PinIcon(false),
                Cursor = Cursors.Hand,
                Location = new Point(_headPanel.Width - 46, 4),
            };
            Tips.Set(_pinBtn, I18n.T("tip_pin_panel"));
            _pinBtn.Click += (s, e) => _fnTogglePin();
            _headPanel.Controls.Add(_pinBtn);

            // 折叠为桌面卡片：列表收起为常驻小卡片（悬停卡片再弹列表）
            _foldBtn = new Label
            {
                Size = new Size(20, 18),
                AutoSize = false,
                BackColor = Config.ListHdbg,
                Image = PreviewForm.PinLaunchIcon(false),
                Cursor = Cursors.Hand,
                Location = new Point(_headPanel.Width - 24, 4),
            };
            _foldBtn.Click += (s, e) => Program.CollapseEntryToCard(Left, Top);
            Tips.Set(_foldBtn, I18n.T("fold_card"));
            _headPanel.Controls.Add(_foldBtn);

            Controls.Add(_headPanel);
        }

        private void _fnTogglePin()
        {
            _pinned = !_pinned;
            if (_pinBtn != null) _pinBtn.Image = PreviewForm.PinIcon(_pinned);
        }

        // ---- 行区：收藏分组 / 历史分组。配色跟随文件列表调色板（List* 系列）----
        private void _fnBuildRows()
        {
            foreach (var r in _rows) { try { Controls.Remove(r); r.Dispose(); } catch { } }
            _rows.Clear();
            var fav = Config.FavoriteFolders.Where(Directory.Exists).ToList();
            var recent = Config.RecentFolders.Where(p => Directory.Exists(p) && !fav.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();

            // 宽度：用户拖宽优先；否则按内容自适应（只量文件夹名，路径尾已移除）
            if (Config.EntryListW >= 220) _listW = Config.EntryListW;
            else
            {
                var nameFont = new Font(Config.FontName, Config.FontSize);
                int w = 300;
                using (var g = CreateGraphics())
                {
                    foreach (var p in fav.Concat(recent))
                    {
                        var sw = g.MeasureString(Path.GetFileName(p), nameFont).Width;
                        if (sw + 64 > w) w = (int)sw + 64;
                    }
                }
                _listW = Math.Min(w, 520);
            }

            BackColor = Config.ListCanvas;
            var font = new Font(Config.FontName, Config.FontSize);
            var subFont = new Font(Config.FontName, Config.FontSize - 0.5f);
            int y = _headerH + 2;
            int zebra = 0;
            y = _fnAddSection(y, I18n.T("sec_fav"));
            if (fav.Count == 0 && recent.Count == 0)
            {
                var empty = new Label
                {
                    Text = I18n.T("entry_empty"),
                    Location = new Point(ListPadLr + 4, y),
                    AutoSize = true,
                    ForeColor = Config.ListMu,
                    Font = subFont,
                    BackColor = Config.ListCanvas,
                };
                Controls.Add(empty);
                _rows.Add(empty);
                y += 26;
            }
            foreach (var p in fav) y = _fnAddRow(y, p, true, font, subFont, zebra++);
            if (recent.Count > 0) y = _fnAddSection(y, I18n.T("sec_recent"));
            foreach (var p in recent) y = _fnAddRow(y, p, false, font, subFont, zebra++);

            _fullH = y + ListPadBottom;
            // 表头容器与按钮位置同步新宽度：Panel 裁切溢出子控件，
            // 只挪按钮不挪容器会把图钉/折叠裁没（长路径撑宽后按钮消失的根因）
            if (_headPanel != null)
            {
                _headPanel.Size = new Size(_listW - ListPadLr * 2, _headerH);
                _headPanel.BackColor = Config.ListHdbg;
            }
            if (_titleLbl != null)
            {
                _titleLbl.Width = _headPanel.Width - 58;
                _titleLbl.BackColor = Config.ListHdbg;
                _titleLbl.ForeColor = Config.ListFg;
            }
            if (_pinBtn != null) { _pinBtn.Location = new Point(_headPanel.Width - 46, 4); _pinBtn.BackColor = Config.ListHdbg; }
            if (_foldBtn != null) { _foldBtn.Location = new Point(_headPanel.Width - 24, 4); _foldBtn.BackColor = Config.ListHdbg; }
            ClientSize = new Size(_listW, _fullH);
        }

        private int _fnAddSection(int y, string title)
        {
            var l = new Label
            {
                Text = title,
                Location = new Point(ListPadLr + 2, y),
                Size = new Size(_listW - ListPadLr * 2 - 4, 16),
                ForeColor = Config.ListMu,
                Font = new Font(Config.FontName, Config.FontSize - 1f, FontStyle.Bold),
                BackColor = Config.ListCanvas,
            };
            Controls.Add(l);
            _rows.Add(l);
            return y + 20;
        }

        private int _fnAddRow(int y, string path, bool isFav, Font font, Font subFont, int zebra)
        {
            var baseColor = zebra % 2 == 1 ? Config.ListZb : Config.ListBg;
            var row = new EntryRow
            {
                Full = path,
                IsFav = isFav,
                Location = new Point(ListPadLr, y),
                Size = new Size(_listW - ListPadLr * 2, 24),
                Font = font,
                BackColor = baseColor,
                BaseColor = baseColor,
                ForeColor = Config.ListFg,
                Cursor = Cursors.Hand,
                Name = Path.GetFileName(path),
                SubFont = subFont,
            };
            row.MouseEnter += delegate
            {
                row.BackColor = Config.ListHv;
                _rowHoverPath = path;
                _fnStartRowHover(row);
            };
            row.MouseLeave += delegate
            {
                if (!_fnRowContains(row)) row.BackColor = row.BaseColor;
                if (_rowHoverPath == path) { _rowHoverPath = null; _fnStopRowHover(); }
            };
            row.StarHover += delegate(object s, bool inStar)
            {
                // 星标上悬停不触发行级联子列表（400ms 弹出会盖住星标本意）
                if (inStar) _fnStopRowHover();
                else if (_rowHoverPath == path) _fnStartRowHover(row);
            };
            row.StarToggled += delegate { _fnToggleFav(row); };
            row.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                RecordRecent(path);
                _onOpenFolder(path, row.PointToScreen(Point.Empty));
                Close();
            };
            row.MouseUp += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Right) return;
                _fnRowMenu(row);
            };
            // 拖放目标：拖文件到行上 = 复制（默认）/移动（Shift），与文件列表行语义一致
            row.AllowDrop = true;
            row.DragEnter += delegate(object s, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effect = (e.KeyState & 4) != 0 ? DragDropEffects.Move : DragDropEffects.Copy;
                    row.BackColor = Config.ListDrop;
                }
            };
            row.DragOver += delegate(object s, DragEventArgs e)
            {
                e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop)
                    ? ((e.KeyState & 4) != 0 ? DragDropEffects.Move : DragDropEffects.Copy)
                    : DragDropEffects.None;
            };
            row.DragLeave += delegate { if (!_fnRowContains(row)) row.BackColor = row.BaseColor; };
            row.DragDrop += delegate(object s, DragEventArgs e)
            {
                row.BackColor = row.BaseColor;
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                Program.DropFilesIntoEntryFolder(path, files, (e.KeyState & 4) != 0);
            };
            Controls.Add(row);
            _rows.Add(row);
            return y + 26;
        }

        // 星标点击：历史行点亮 → 移入收藏区顶部；收藏行熄灭 → 移回历史顶部（不在历史则补进去，行不消失）
        private void _fnToggleFav(EntryRow row)
        {
            if (row.IsFav)
            {
                Config.FavoriteFolders.RemoveAll(p => string.Equals(p, row.Full, StringComparison.OrdinalIgnoreCase));
                if (!Config.RecentFolders.Contains(row.Full, StringComparer.OrdinalIgnoreCase))
                    Config.RecentFolders.Insert(0, row.Full);
            }
            else
                Config.FavoriteFolders.Insert(0, row.Full);
            _dirty = true;
            _fnSave();
            _fnBuildRows();
        }

        private void _fnRowMenu(EntryRow row)
        {
            var m = new ContextMenuStrip { ShowImageMargin = false, BackColor = Config.BG, ForeColor = Config.FG, Font = new Font(Config.FontName, Config.FontSize) };
            if (!row.IsFav)
                m.Items.Add(I18n.T("add_fav"), null, (s, e) =>
                {
                    Config.FavoriteFolders.Insert(0, row.Full);
                    _dirty = true; _fnSave(); _fnBuildRows();
                });
            else
                m.Items.Add(I18n.T("fav_remove"), null, (s, e) =>
                {
                    Config.FavoriteFolders.RemoveAll(p => string.Equals(p, row.Full, StringComparison.OrdinalIgnoreCase));
                    _dirty = true; _fnSave(); _fnBuildRows();
                });
            if (!row.IsFav)
                m.Items.Add(I18n.T("recent_remove"), null, (s, e) =>
                {
                    Config.RecentFolders.RemoveAll(p => string.Equals(p, row.Full, StringComparison.OrdinalIgnoreCase));
                    _dirty = true; _fnSave(); _fnBuildRows();
                });
            m.Show(row, row.PointToClient(Cursor.Position));
        }

        private void _fnSave()
        {
            if (!_dirty) return;
            _dirty = false;
            Config.Save();
        }

        private bool _fnRowContains(Control c)
        {
            var pt = c.PointToClient(Cursor.Position);
            return pt.X >= 0 && pt.X < c.Width && pt.Y >= 0 && pt.Y < c.Height;
        }

        // ---- 行悬停 → 右侧级联子列表（与文件列表行为一致）----
        private void _fnStartRowHover(EntryRow row)
        {
            if (_rowHoverTimer == null)
            {
                _rowHoverTimer = new Timer { Interval = 400 };
                _rowHoverTimer.Tick += (s, e) =>
                {
                    _rowHoverTimer.Stop();
                    if (IsDisposed || _rowHoverPath == null) return;
                    var hr = _rowHoverRow;
                    if (hr == null || hr.IsDisposed) return;
                    var sp = hr.PointToScreen(Point.Empty);
                    Program.ShowEntrySubList(new Point(sp.X + hr.Width, sp.Y), _rowHoverPath);
                };
            }
            _rowHoverRow = row;
            _rowHoverTimer.Stop();
            _rowHoverTimer.Start();
        }

        private void _fnStopRowHover()
        {
            if (_rowHoverTimer != null) _rowHoverTimer.Stop();
            _rowHoverRow = null;
        }

        // 数据变更/语言/主题切换 → 开着的列表实时重建（行重排+表头文本图标全刷新）
        internal static void NotifyChanged()
        {
            if (_current == null || _current.IsDisposed) return;
            var f = _current;
            f._fnBuildRows();
            // 配色/主题切换后窗口已存在，主动重算并重画非客户区描边，否则边框卡在旧色
            try { Win11.RefreshNc(f.Handle); } catch { }
            try
            {
                // 表头文本/提示随语言刷新；图标重取——主题切换 ClearUiIconCache 后
                // 旧 Image 引用已 Dispose，不重赋按钮会变空白
                f._titleLbl.Text = I18n.T("entry_title");
                Tips.Set(f._titleLbl, I18n.T("entry_tip"));
                f._pinBtn.Image = PreviewForm.PinIcon(f._pinned);
                f._foldBtn.Image = PreviewForm.PinLaunchIcon(false);
                Tips.Set(f._pinBtn, I18n.T("tip_pin_panel"));
                Tips.Set(f._foldBtn, I18n.T("fold_card"));
            }
            catch { }
        }

        // 右缘拖宽：NCHITTEST 返回 HTRIGHT 交给系统原生改宽；拖完按新宽重建行并记忆
        // 圆角模式（WindowCorners=1）下 WndNcBorder 的 WM_NCCALCSIZE 把客户区四周内缩
        // FramePad 像素，WinForms 的 ClientSize 并不知情：写入补描边占位、读取返回真实客户区，
        // 让四边留白与圆角描边协调（与 PopupForm 一致）；直角模式 FramePad=0 无影响
        public new Size ClientSize
        {
            get
            {
                var s = IsHandleCreated ? Win11.RealClient(Handle) : Size.Empty;
                return s.IsEmpty ? base.ClientSize : s;
            }
            set { base.ClientSize = new Size(value.Width + Win11.FramePad * 2, value.Height + Win11.FramePad * 2); }
        }

        protected override void WndProc(ref Message m)
        {
            // 圆角描边与自身背景同色：亮色描边压在深色配色上很突兀，同色后只剩 DWM 圆角外形
            if (Win11.WndNcBorder(Handle, ref m, () => BackColor)) return;
            if (m.Msg == 0x84)   // WM_NCHITTEST
            {
                var lp = m.LParam.ToInt64();
                var cp = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                if (cp.X >= ClientSize.Width - 6 && cp.Y >= 0 && cp.Y < ClientSize.Height)
                {
                    m.Result = (IntPtr)11;   // HTRIGHT
                    return;
                }
            }
            else if (m.Msg == 0x232)   // WM_EXITSIZEMOVE
            {
                int w = ClientSize.Width;
                if (w >= 220 && w != _listW)
                {
                    Config.EntryListW = w;
                    _dirty = true;
                    _fnBuildRows();
                }
            }
            base.WndProc(ref m);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _current = this;
            if (_watchTimer == null)
            {
                // 离开时长 ≥ 关闭延迟即收（时间阈值而非两拍采样：采样法实际关闭会多出 0.5~1 倍延迟）。
                // 40ms 轮询保证阈值精确；触发区→列表间 4px 间隙的穿越 <30ms，被 ≥60ms 的阈值兜住
                int missSince = 0;
                _watchTimer = new Timer { Interval = 40 };
                _watchTimer.Tick += (s, e2) =>
                {
                    if (!Visible) { _watchTimer.Stop(); return; }
                    if (_pinned) return;   // 固定后不自动关（与文件列表图钉一致）
                    // 拖拽会话中（左键按住，含 OLE 拖放途经）：列表是放置目标，鼠标不在列表上也保持
                    if ((NativeMethods.GetAsyncKeyState(0x01) & 0x8000) != 0) { missSince = 0; return; }
                    bool inside = _fnListContainsCursor();
                    if (!inside && Config.EntryRectW >= 20 && Config.EntryRectH >= 20)
                    {
                        // 鼠标仍在触发区域内也保持：列表弹在区域下方，区内鼠标不在列表上是常态，
                        // 不保持会形成“弹出→关闭→再弹出”的循环
                        var rp = Cursor.Position;
                        inside = rp.X >= Config.EntryRectX && rp.X < Config.EntryRectX + Config.EntryRectW
                            && rp.Y >= Config.EntryRectY && rp.Y < Config.EntryRectY + Config.EntryRectH;
                    }
                    if (!inside) inside = PopupForm.InAnyHoverPanelOrPreview(Cursor.Position.X, Cursor.Position.Y);
                    if (!inside && Program.EntryCardContainsCursor()) inside = true;   // 折叠卡片在 → 列表由它承接
                    if (inside) { missSince = 0; return; }
                    int now = Environment.TickCount;
                    if (missSince == 0) { missSince = now; return; }
                    // 收藏·历史定位是快速访问入口：关闭快于文件面板（上限 200ms），
                    // 即使全局关闭延迟设得较大也不在这里拖泥带水
                    if (now - missSince >= Math.Max(60, Math.Min(Config.CloseDelayMs, 200)))
                    {
                        _watchTimer.Stop();
                        _fnSave();
                        Close();
                    }
                };
            }
            _watchTimer.Stop();
            _watchTimer.Start();
        }

        private bool _fnListContainsCursor()
        {
            var pt = PointToClient(Cursor.Position);
            return pt.X >= 0 && pt.X < Width && pt.Y >= 0 && pt.Y < Height;
        }

        public static void RecordRecent(string path) { RecordRecent(path, true); }

        // reorder=false（Explorer 轮询每 2.5s 调用）：路径已在历史中就完全跳过——
        // 之前每个已存在的路径都被移到顶部并触发整表重建+写盘，多窗口时每轮轮询
        // 连续重建数次，列表无缓冲直接画 → “每隔几秒闪烁几次”的根因。
        // 真正访问（点击行打开）仍走 reorder=true 置顶
        public static void RecordRecent(string path, bool reorder)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            bool exists = Config.RecentFolders.Contains(path, StringComparer.OrdinalIgnoreCase);
            if (exists && !reorder) return;
            if (!exists || !string.Equals(Config.RecentFolders[0], path, StringComparison.OrdinalIgnoreCase))
            {
                Config.RecentFolders.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
                Config.RecentFolders.Insert(0, path);
            }
            if (Config.RecentFolders.Count > Config.EntryRecentMax)
                Config.RecentFolders.RemoveRange(Config.EntryRecentMax, Config.RecentFolders.Count - Config.EntryRecentMax);
            Config.Save();
            NotifyChanged();   // 开着的收藏·历史列表实时刷新
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_watchTimer != null) { _watchTimer.Stop(); _watchTimer.Dispose(); _watchTimer = null; }
            if (_rowHoverTimer != null) { _rowHoverTimer.Stop(); _rowHoverTimer.Dispose(); _rowHoverTimer = null; }
            if (_current == this) _current = null;
            base.OnFormClosed(e);
        }
    }

    // 收藏·历史折叠后的桌面常驻卡片（原型 D 标签带语言）：accent 色条 + 金色★徽章 + 标题；
    // 宽度随语言自适应（TextRenderer 测宽），悬停提亮一档；悬停 400ms / 点击 → 卡片下方左对齐
    // 弹出列表；拖动移动；中键关闭（彻底取消折叠态）
    internal class EntryCardForm : Form, IPaletteTarget
    {
        private Timer _hoverTimer;
        private bool _hover;
        private int _paletteIndex;   // 与桌面卡片同体系：滚轮循环切换，作为后续新卡默认
        private const int CardH = 28;
        protected override bool ShowWithoutActivation { get { return true; } }

        // 高度锁死 28（SetBoundsCore 层，AutoScale/DPI 绕不过）；宽度随语言自适应
        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            base.SetBoundsCore(x, y, width, CardH, specified);
        }

        public EntryCardForm(int x, int y)
        {
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(x, y);
            BackColor = Config.BG;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            _paletteIndex = (Config.CardPaletteIndex >= 0 && Config.CardPaletteIndex < CardPalettes.All.Count)
                ? Config.CardPaletteIndex : 0;
            _fnApplyPalette();
            _fnLayout();

            // 高度兜底：构造期设的 ClientSize 可能被 handle 创建时的 DPI/自动缩放改走
            // （首次 39px、Relayout 后 28px 的根因），Shown 时按物理像素再压一次
            Shown += (s, e) => _fnLayout();

            MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Middle) { Close(); return; }
                if (e.Button == MouseButtons.Left)
                {
                    NativeMethods.ReleaseCapture();
                    NativeMethods.SendMessage(Handle, 0xA1, 0x2, 0);
                }
            };
            MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) _fnOpenList(false);
            };
            MouseEnter += (s, e) =>
            {
                _hover = true;
                _fnApplyPalette();
                Invalidate();
                if (_hoverTimer == null)
                {
                    _hoverTimer = new Timer { Interval = 400 };
                    _hoverTimer.Tick += (s2, e2) => { _hoverTimer.Stop(); if (!IsDisposed) _fnOpenList(true); };
                }
                _hoverTimer.Stop();
                _hoverTimer.Start();
            };
            MouseLeave += (s, e) =>
            {
                _hover = false;
                _fnApplyPalette();
                Invalidate();
                if (_hoverTimer != null) _hoverTimer.Stop();
            };
            Tips.Set(this, I18n.T("entry_tip"));
        }

        // 宽度按当前语言标题实测（TextRenderer=GDI 与绘制一致；MeasureString 偏窄会截字）
        private void _fnLayout()
        {
            using (var f = new Font(Config.FontName, 9f))
            {
                var tw = TextRenderer.MeasureText(I18n.T("entry_title"), f, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                ClientSize = new Size(12 + 16 + 8 + tw.Width + 10, CardH);
            }
        }

        // 语言/主题切换：重测宽度并重绘（Program 侧调用）
        public void Relayout()
        {
            if (IsDisposed) return;
            _fnLayout();
            _fnApplyPalette();
            Tips.Set(this, I18n.T("entry_tip"));
            Invalidate();
        }

        // ---- 配色（与桌面卡片同体系，滚轮循环切换）----
        private void _fnApplyPalette()
        {
            var pal = CardPalettes.Get(_paletteIndex);
            if (pal == null) return;
            BackColor = _hover ? pal.Hover : pal.Bg;
        }

        public void SetPalette(int index, bool persist)
        {
            int n = CardPalettes.All.Count;
            if (n == 0) return;
            index = ((index % n) + n) % n;
            _paletteIndex = index;
            Config.CardPaletteIndex = index;   // 作为后续新卡片的默认配色
            if (persist) Program.SaveConfigDebounced();
            _fnApplyPalette();
            Invalidate();
        }

        public int CyclePalette(int dir)
        {
            int n = CardPalettes.All.Count;
            if (n == 0) return _paletteIndex;
            SetPalette(_paletteIndex + dir, true);
            return _paletteIndex;
        }

        private void _fnOpenList(bool auto)
        {
            if (IsDisposed) return;
            // auto=true 悬停触发：刚点行打开过 Explorer（armed=false）时不弹，
            // 避免列表关掉后鼠标还停在卡片/触发区域上又被自动弹回
            if (auto && !Program._entryArmed) return;
            Program.ShowEntryList(Location.X, Bottom + 4);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            int h = ClientSize.Height;
            var pal = CardPalettes.Get(_paletteIndex);
            var tape = pal != null ? pal.Folder : Config.Accent;   // 左色条显示当前配色（palette 的文件夹类型色）
            var fg = pal != null ? pal.Fg : Config.FG;
            var border = pal != null ? pal.Border : Config.Border;
            // 左侧 6px 色条（palette 色，与桌面卡片同语言）
            using (var b = new SolidBrush(tape)) g.FillRectangle(b, 0, 0, 6, h);
            // 金色★徽章：16×16 色块 + 白星（收藏语义恒金色）
            var badge = new Rectangle(12, (h - 16) / 2, 16, 16);
            using (var b = new SolidBrush(ColorTranslator.FromHtml("#f0b429"))) g.FillRectangle(b, badge);
            using (var f = new Font(Config.FontName, 8.5f))
            using (var b = new SolidBrush(Color.White))
                g.DrawString("★", f, b, badge.X + (badge.Width - 10) / 2, badge.Y + (badge.Height - 12) / 2);
            // 标题：徽章右侧，垂直居中
            using (var f = new Font(Config.FontName, 9f))
                TextRenderer.DrawText(g, I18n.T("entry_title"), f, new Point(36, (h - f.Height) / 2), fg);
            // 边框收尾（palette 自带宽度）
            if (pal != null && pal.BorderWidth > 0)
                using (var p = new Pen(border, pal.BorderWidth)) g.DrawRectangle(p, 0, 0, ClientSize.Width - 1, h - 1);
            else if (pal == null)
                using (var p = new Pen(border)) g.DrawRectangle(p, 0, 0, ClientSize.Width - 1, h - 1);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_hoverTimer != null) { _hoverTimer.Stop(); _hoverTimer.Dispose(); _hoverTimer = null; }
            base.OnFormClosed(e);
        }
    }

    // 列表行：图标 | ★收藏星标（恒在最前，可点击）| 文件夹名；路径尾已移除。
    // 斑马底色存 BaseColor（悬停/拖放还原用），配色取文件列表调色板
    internal class EntryRow : Control
    {
        public string Full;
        public bool IsFav;
        public new string Name;
        public Font SubFont;
        public Color BaseColor;
        public event EventHandler StarToggled;
        public event EventHandler<bool> StarHover;

        private bool _inStar;
        private static readonly Color StarLit = ColorTranslator.FromHtml("#f0b429");

        public EntryRow()
        {
            // 不用 OptimizedDoubleBuffer：同上，BufferedGraphics 在首次弹出有 AV 崩溃记录，行面积小直绘无碍
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        }

        private Rectangle StarRect { get { return new Rectangle(24, 0, 20, Height); } }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool inStar = StarRect.Contains(e.Location);
            if (inStar == _inStar) return;
            _inStar = inStar;
            var h = StarHover;
            if (h != null) h(this, inStar);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!_inStar) return;
            _inStar = false;
            var h = StarHover;
            if (h != null) h(this, false);
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            // 点星标 = 收藏/取消收藏，不触发行点击（打开文件夹）
            if (e.Button == MouseButtons.Left && StarRect.Contains(e.Location))
            {
                var t = StarToggled;
                if (t != null) t(this, EventArgs.Empty);
                return;
            }
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var icon = PopupForm.GetEntryIcon();
            if (icon != null) g.DrawImage(icon, 6, (Height - icon.Height) / 2);
            // 星标：收藏=实心金（与桌面卡片★徽章同色），历史=描边弱化；悬停提亮
            var sf = SubFont ?? Font;
            var starColor = IsFav ? StarLit : (_inStar ? Config.ListFg : Config.ListMu);
            TextRenderer.DrawText(g, IsFav ? "★" : "☆", sf, StarRect, starColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            // 名称限制在行宽内，过长中间省略（与文件列表一致），不再溢出/撑宽
            float availW = Width - 46 - 6;
            using (var b = new SolidBrush(ForeColor))
            {
                if (availW > 20)
                    g.DrawString(RowLabel.MidEllipsis(g, Name, Font, availW), Font, b, 46, (Height - Font.Height) / 2);
                else
                    g.DrawString(Name, Font, b, 46, (Height - Font.Height) / 2);
            }
        }
    }

    // 触发区域桌面指示：半透明色块常驻标示入口位置（RGBA=RGB 底色 + 窗体 Opacity=alpha）。
    // WS_EX_TRANSPARENT 鼠标全穿透——纯视觉层，悬停触发仍由 Program.Tick 按矩形判定
    internal class EntryIndicatorForm : Form
    {
        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x20;   // WS_EX_TRANSPARENT：点击/悬停穿透
                return cp;
            }
        }

        public EntryIndicatorForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
        }

        public void Apply(Rectangle rect, int argb)
        {
            var c = Color.FromArgb(argb);
            BackColor = Color.FromArgb(255, c.R, c.G, c.B);
            Opacity = Math.Max(0.04, c.A / 255.0);
            Bounds = rect;
        }
    }

    // 桌面框选遮罩：盖住鼠标所在屏（FromPoint），Opacity 真半透明，
    // 拖拽画矩形（实时描边），松开确定并回调，Esc 取消
    internal class AreaPickForm : Form
    {
        private readonly Action<Rectangle> _onPicked;
        private Point _start;
        private Rectangle _rect;
        private bool _dragging;

        public AreaPickForm(Action<Rectangle> onPicked)
        {
            _onPicked = onPicked;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            Cursor = Cursors.Cross;
            // 鼠标所在屏（框选按钮在哪个屏就盖哪个屏；PrimaryScreen 会盖错屏）
            Bounds = Screen.FromPoint(Cursor.Position).Bounds;
            DoubleBuffered = true;
            // WinForms 不支持 ARGB BackColor（会截断成纯黑全屏窗）→ 用 Opacity 做真半透明
            BackColor = Color.Black;
            Opacity = 0.25;
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) { _start = e.Location; _dragging = true; _rect = new Rectangle(e.Location, Size.Empty); Invalidate(); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;
            int x = Math.Min(_start.X, e.X), y = Math.Min(_start.Y, e.Y);
            _rect = new Rectangle(x, y, Math.Abs(e.X - _start.X) + 1, Math.Abs(e.Y - _start.Y) + 1);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging) return;
            _dragging = false;
            if (_rect.Width >= 10 && _rect.Height >= 10)
            {
                var picked = new Rectangle(RectangleToScreen(_rect).Location, _rect.Size);
                DialogResult = DialogResult.OK;
                Close();
                Program.DebugLog("[entry] area picked " + picked);
                if (_onPicked != null) _onPicked(picked);
            }
            else
            {
                // 太小的框视为误触，继续选
                _rect = Rectangle.Empty;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_rect.Width > 0)
            {
                using (var p = new Pen(Config.Accent, 2))
                {
                    e.Graphics.DrawRectangle(p, _rect);
                    using (var b = new SolidBrush(Color.FromArgb(60, Config.Accent)))
                        e.Graphics.FillRectangle(b, _rect);
                }
            }
            using (var b = new SolidBrush(Color.White))
            {
                var tip = I18n.T("area_pick") + "  ·  Esc";
                var f = new Font(Config.FontName, 11);
                var sz = e.Graphics.MeasureString(tip, f);
                e.Graphics.DrawString(tip, f, b, (Width - sz.Width) / 2, Height / 2 - sz.Height);
            }
        }
    }
}