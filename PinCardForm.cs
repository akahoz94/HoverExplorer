using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace HoverExplorer
{
    // 桌面卡片·标签带样式（原型方案 D）：28px 高标签条 + 名字 + 类型色扩展名徽章 + 左 6px 色条。
    // 宽度恒定（名字+徽章），悬停提亮一档，sub 与操作按钮组覆盖显示在卡片右端文字上（不右扩）。
    // text 卡片：主操作=粘贴（MA_NOACTIVATE 不抢焦点 + 模拟 Ctrl+V 贴回原窗口），
    // 悬停 800ms 弹可编辑预览（复用 PreviewForm），关闭时写回内容。
    internal class PinCardForm : Form
    {
        private readonly PinnedItem _item;
        private readonly string _path;   // = _item.Path，兼容外部按路径定位
        private readonly bool _isFile, _isUrl, _isText, _isDir;
        private readonly Color _typeColor;   // 按类型着色的色条/徽章色
        private readonly bool _isToday;      // 今天修改过的文件 → 名字左侧状态点
        private string _name, _ext, _sub;

        private Label _nameLbl, _extBadge, _subLbl;

        private Point _dragStart, _formStart;
        private bool _moved, _hover;

        // 标签带配色：随主题的深/浅两版
        private static readonly Color BgDark = Color.FromArgb(0x1b, 0x1b, 0x1f);
        private static readonly Color BgHoverDark = Color.FromArgb(0x2e, 0x2e, 0x36);
        private static readonly Color BgLight = Color.FromArgb(0xf4, 0xf3, 0xf7);
        private static readonly Color BgHoverLight = Color.FromArgb(0xe4, 0xe3, 0xec);
        private Color _bg, _bgHover, _fg, _fgSub, _fgBtn;   // 主题切换时由 ApplyTheme() 重算
        private Color _btnHoverBg;

        private Timer _textPreviewTimer;
        private Timer _textHoverTimer;   // 预览开着时轮询：鼠标离开卡片∪预览且未在编辑 → 关（与文件预览行为一致）
        private PreviewForm _textPreview;
        private bool _textDirty;

        public PinCardForm(PinnedItem item)
        {
            _item = item;
            _path = item.Path;
            _isFile = item.Kind == "file";
            _isUrl = item.Kind == "url";
            _isText = item.Kind == "text";
            _typeColor = _fnTypeColor(item);
            _fnApplyThemeColors();

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(item.X, item.Y);
            TopMost = true;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;   // 高度按像素固定，禁止 DPI/字体缩放

            BackColor = _bg;
            ForeColor = _fg;
            DoubleBuffered = true;
            Height = 28;

            // ----- 内容提取 -----
            _isDir = _isFile && Directory.Exists(item.Path);
            long size = 0;
            _isToday = false;
            if (_isFile)
            {
                _name = Path.GetFileName(item.Path);
                _ext = _isDir ? "DIR" : (Path.HasExtension(item.Path) ? Path.GetExtension(item.Path).TrimStart('.').ToUpperInvariant() : "FILE");
                try { var fi = new FileInfo(item.Path); size = fi.Length; _isToday = fi.LastWriteTime.Date == DateTime.Today; } catch { }
                _sub = _isDir ? _fnCountSub(item.Path) : Config.FmtSize(size);
            }
            else if (_isUrl)
            {
                string dom = Ingest.DomainOf(item.Path);
                _name = !string.IsNullOrEmpty(item.Title) ? item.Title : dom;
                _ext = "URL";
                _sub = dom;
            }
            else
            {
                _name = !string.IsNullOrEmpty(item.Title) ? item.Title : Ingest.Ellipsis(Ingest.FirstLine(item.Path), 18);
                _ext = "TXT";
                _sub = item.Path.Length + " 字";
            }

            // ----- 布局：宽度恒定 = 名字 + 徽章 + 右 padding（悬停不改变宽度） -----
            int nameW, subW, extW;
            using (var g = CreateGraphics())
            {
                using (var fN = new Font("Microsoft YaHei", 9f))
                using (var fS = new Font("Microsoft YaHei", 8f))
                using (var fE = new Font("Microsoft YaHei", 8f, FontStyle.Bold))
                {
                    // 用 TextRenderer(GDI) 测量——Label 渲染走 GDI，GDI+ MeasureString 偏窄导致徽章截断
                    nameW = Math.Min(TextRenderer.MeasureText(_name, fN).Width + 2, 260);
                    subW = TextRenderer.MeasureText(_sub, fS).Width + 2;
                    extW = TextRenderer.MeasureText(_ext, fE, new Size(200, 20), TextFormatFlags.NoPadding).Width + 8;
                }
            }
            int x = _isToday ? 21 : 14;
            _nameLbl = new Label
            {
                Location = new Point(x, 6),
                Size = new Size(nameW, 16),
                Text = _name,
                Font = new Font("Microsoft YaHei", 9f),
                ForeColor = _fg,
                BackColor = _bg,
                AutoEllipsis = true,
            };
            Controls.Add(_nameLbl);

            _extBadge = new Label
            {
                Location = new Point(x + nameW + 5, 4),
                Size = new Size(extW, 20),
                Text = _ext,
                Font = new Font("Microsoft YaHei", 8f, FontStyle.Bold),
                BackColor = _typeColor,
                ForeColor = Color.FromArgb(0x11, 0x11, 0x11),
                TextAlign = ContentAlignment.MiddleCenter,
            };
            Controls.Add(_extBadge);

            Width = _extBadge.Right + 8;

            // sub：悬停时显示在徽章右侧，空间不足截断（不改变卡片宽度）
            _subLbl = new Label
            {
                Location = new Point(_extBadge.Right + 5, 8),
                Size = new Size(Math.Max(subW, 30), 13),
                Text = _sub,
                Font = new Font("Microsoft YaHei", 8f),
                ForeColor = _fgSub,
                BackColor = _bg,
                AutoEllipsis = true,
                Visible = false,
            };
            Controls.Add(_subLbl);

            // 徽章 = 执行入口：点击徽章执行主操作（file 打开 / url 打开 / text 粘贴）。
            // hover 提亮提示可点
            _extBadge.Click += (s, e) => { if (_isText) _fnPaste(); else Run(); };
            _extBadge.Cursor = Cursors.Hand;
            _extBadge.MouseEnter += (s, e) => { _extBadge.BackColor = _fnLighten(_typeColor, 0.25f); };
            _extBadge.MouseLeave += (s, e) => { _extBadge.BackColor = _typeColor; };

            // ----- 拖动 + 右键/中键 -----
            MouseEventHandler down = (s, e) =>
            {
                if (e.Button == MouseButtons.Left) { _dragStart = Cursor.Position; _formStart = Location; _moved = false; Capture = true; }
            };
            MouseEventHandler cardMouse = (s, e) =>
            {
                if (e.Button == MouseButtons.Middle) Program.Unpin(_item.Kind, _item.Path);
                else if (e.Button == MouseButtons.Right)
                {
                    if (_isFile) ShellContextMenu.Show(_item.Path, Handle, () => Program.Unpin(_item.Kind, _item.Path));
                    else ShowCardMenu();
                }
            };
            MouseDown += down; MouseDown += cardMouse;
            // 徽章不挂 down：Capture=true 会把鼠标消息路由到 Form，Label 收不到 MouseUp →
            // Click 永不触发（实测徽章点击无反应的根因）。徽章专用于点击执行，拖动从名字区开始
            foreach (Control c in new Control[] { _nameLbl, _subLbl })
            {
                c.MouseDown += down;
                c.MouseDown += cardMouse;
            }
            _extBadge.MouseDown += cardMouse;
            // 双击卡片 = 复制内容（file 复制路径 / url 复制链接 / text 复制全文）
            EventHandler dblCopy = (s, e) =>
            {
                try { Clipboard.SetText(_item.Path); Program.ShowBalloon("HoverExplorer", I18n.T("card_copied")); } catch { }
            };
            DoubleClick += dblCopy;
            foreach (Control c in new Control[] { _nameLbl, _subLbl }) c.DoubleClick += dblCopy;
            MouseMove += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                if (!_moved)
                {
                    var d = Cursor.Position;
                    if (Math.Abs(d.X - _dragStart.X) > 6 || Math.Abs(d.Y - _dragStart.Y) > 6) _moved = true;
                }
                if (_moved)
                {
                    var d = Cursor.Position;
                    Location = new Point(_formStart.X + d.X - _dragStart.X, _formStart.Y + d.Y - _dragStart.Y);

                }
            };
            MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    Capture = false;
                    if (_moved) Program.UpdatePinPos(_item.Kind, _item.Path, Left, Top);
                    _moved = false;
                }
            };

            // ----- 悬停：提亮 + sub 显示；text 卡 800ms 后弹可编辑预览 -----
            // 关闭入口：中键点击卡片 / 右键菜单"取消固定"（外置关闭窗已移除）
            MouseEnter += (s, e) => _fnSetHover(true);
            MouseLeave += (s, e) => { if (!_fnCardContainsCursor()) _fnSetHover(false); };

            // 卡片关闭（中键/Unpin/退出）时清理：预览窗口关掉、计时器停掉。
            // 否则悬停期间关卡片：800ms 后预览照样弹出，且轮询对已 Dispose 的
            // this 调 PointToClient 每 Tick 抛异常 → 预览永不自动关闭
            FormClosed += (s, e) =>
            {
                if (_textPreviewTimer != null) { _textPreviewTimer.Stop(); _textPreviewTimer.Dispose(); _textPreviewTimer = null; }
                if (_textHoverTimer != null) { _textHoverTimer.Stop(); _textHoverTimer.Dispose(); _textHoverTimer = null; }
                _fnCloseTextPreview();
            };

            // 文件夹卡片：接受拖入的文件/目录，复制(默认)或移动(Shift)进该文件夹
            // 子控件会挡住 form 的拖放事件，需对每个子控件单独启用 AllowDrop
            if (_isDir)
            {
                _fnEnableDrop(this);
                foreach (Control c in Controls) _fnEnableDrop(c);
            }
        }

        // 主题切换时重算颜色并刷新已建控件（Program.cs themeDark/themeLight 调用）
        public void ApplyTheme()
        {
            _fnApplyThemeColors();
            Color bg = _hover ? _bgHover : _bg;
            BackColor = bg;
            _nameLbl.ForeColor = _fg;
            _subLbl.ForeColor = _fgSub;
            foreach (Control c in Controls)
            {
                if (c == _extBadge) continue;   // 徽章类型色不随主题
                if (c is Label && c.BackColor == _btnHoverBg) c.BackColor = bg;
                else if (c is Label && (c.BackColor == BgDark || c.BackColor == BgLight || c.BackColor == BgHoverDark || c.BackColor == BgHoverLight)) c.BackColor = bg;
            }
            _nameLbl.BackColor = bg;
            _subLbl.BackColor = bg;

            Invalidate();
        }

        private void _fnApplyThemeColors()
        {
            bool light = Config.Theme == "light";
            _bg = light ? BgLight : BgDark;
            _bgHover = light ? BgHoverLight : BgHoverDark;
            _fg = light ? Color.FromArgb(0x26, 0x26, 0x2b) : Color.White;
            _fgSub = light ? Color.FromArgb(0x8a, 0x8a, 0x96) : Color.FromArgb(0x9a, 0x9a, 0xa6);
            _fgBtn = light ? Color.FromArgb(0x5a, 0x5a, 0x66) : Color.FromArgb(0xbb, 0xbb, 0xc6);
            _btnHoverBg = light ? Color.FromArgb(0xd6, 0xd5, 0xe0) : Color.FromArgb(0x3a, 0x3a, 0x44);
        }

        private static Color _fnLighten(Color c, float k)
        {
            return Color.FromArgb(c.R + (int)((255 - c.R) * k), c.G + (int)((255 - c.G) * k), c.B + (int)((255 - c.B) * k));
        }

        private bool _fnCardContainsCursor()
        {
            // 卡片已 Dispose（中键关闭后轮询还在跑）：PointToClient 会抛 ObjectDisposedException，
            // 异常中断 Tick → 预览永不自动关闭。判定"不在卡片上"让轮询走正常关闭路径
            if (IsDisposed) return false;
            var p = PointToClient(Cursor.Position);
            return p.X >= 0 && p.X < ClientSize.Width && p.Y >= 0 && p.Y < ClientSize.Height;
        }

        private bool _fnPreviewContainsCursor()
        {
            return _textPreview != null && !_textPreview.IsDisposed
                && _textPreview.ClientRectangle.Contains(_textPreview.PointToClient(Cursor.Position));
        }

        private void _fnSetHover(bool on)
        {
            if (_hover == on) return;
            _hover = on;
            Color bg = on ? _bgHover : _bg;
            BackColor = bg;
            foreach (Control c in Controls) if (c.BackColor == _bg || c.BackColor == _bgHover) c.BackColor = bg;
            // sub 放徽章右侧到卡片右缘的剩余空间（按钮组已在徽章左侧），放不下就不显示
            if (on)
            {
                int left = _extBadge.Right + 5;
                int right = Width - 6;
                if (right - left >= 24)
                {
                    _subLbl.Location = new Point(left, 8);
                    _subLbl.Size = new Size(right - left, 13);
                    _subLbl.Visible = true;
                }
                else _subLbl.Visible = false;
            }
            else _subLbl.Visible = false;

            if (_isText)
            {
                if (on)
                {
                    if (_textPreviewTimer == null)
                    {
                        _textPreviewTimer = new Timer { Interval = 800 };
                        _textPreviewTimer.Tick += (s, e) => { _textPreviewTimer.Stop(); if (IsDisposed) return; if (_hover) _fnShowTextPreview(); };
                    }
                    _textPreviewTimer.Start();
                }
                else
                {
                    if (_textPreviewTimer != null) _textPreviewTimer.Stop();
                    // 关闭交给 _textHoverTimer 统一轮询（离开卡片∪预览 → 延迟关闭）
                }
            }
        }

        // text 卡可编辑预览：复用 PreviewForm 的 Control 构造，关闭时写回内容
        private void _fnShowTextPreview()
        {
            if (_textPreview != null && !_textPreview.IsDisposed) return;
            var tb = new RichTextBox
            {
                Font = new Font("Microsoft YaHei", 9),
                BackColor = Config.BG,
                ForeColor = Config.FG,
                BorderStyle = BorderStyle.None,
                ReadOnly = false,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                WordWrap = true,
                Dock = DockStyle.Fill,
                Text = _item.Path,
            };
            tb.TextChanged += (s, e) => _textDirty = true;

            int w = 300, h = 120;
            try
            {
                using (var g = CreateGraphics())
                {
                    var sz = g.MeasureString("W", tb.Font);
                    int lineH = (int)sz.Height + 1;
                    var lines = _item.Path.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    h = Math.Min(Math.Max(lineH * lines.Length + 8, 60), 300);
                    int maxW = 0;
                    foreach (var line in lines) { var lw = g.MeasureString(line, tb.Font).Width; if (lw > maxW) maxW = (int)lw; }
                    w = (int)Math.Max(Math.Min(maxW + 20, 480), 240);
                }
            }
            catch { }

            _textPreview = new PreviewForm(tb, w, h, "textcard");
            _textPreview.FilePath = _path;
            // textcard：隐藏复制按钮（复制文件/复制路径对文本无意义），同位显示粘贴按钮
            _textPreview.HideCopyForTextCard();
            // 记录预览打开前的前台窗口：预览的粘贴按钮把（可能已编辑的）文本贴回那里
            IntPtr pasteTarget = NativeMethods.GetForegroundWindow();
            _textPreview.PasteAction = () =>
            {
                try
                {
                    string txt = tb.Text;
                    if (!string.IsNullOrEmpty(txt)) Clipboard.SetText(txt);
                    if (pasteTarget != IntPtr.Zero && pasteTarget != _textPreview.Handle)
                        NativeMethods.SetForegroundWindow(pasteTarget);
                    var t = new Timer { Interval = 60 };
                    t.Tick += (s2, e2) =>
                    {
                        t.Stop(); t.Dispose();
                        NativeMethods.keybd_event(0x11, 0, 0, UIntPtr.Zero);
                        NativeMethods.keybd_event(0x56, 0, 0, UIntPtr.Zero);
                        NativeMethods.keybd_event(0x56, 0, 2, UIntPtr.Zero);
                        NativeMethods.keybd_event(0x11, 0, 2, UIntPtr.Zero);
                    };
                    t.Start();
                }
                catch (Exception ex) { Program.DebugLog("[pincard] preview-paste fail " + ex.Message); }
            };
            // 位置：卡片下方，工作区内钳制
            var wa = Screen.GetWorkingArea(new Rectangle(Location, Size));
            int px = Math.Min(Right + 4, wa.Right - w - 8);
            int py = Math.Min(Bottom + 6, wa.Bottom - h - 8);
            _textPreview.Location = new Point(Math.Max(px, wa.Left + 4), Math.Max(py, wa.Top + 4));
            _textPreview.FormClosing += (s, e) =>
            {
                if (_textDirty)
                {
                    _item.Path = tb.Text;
                    _textDirty = false;
                    Config.Save();
                    _fnRefreshTextCard();
                }
            };
            _textPreview.Show();
            // 轮询关闭（与文件预览的纯几何行为一致）：鼠标离开 卡片∪预览 400ms → 关，
            // FormClosing 里 dirty 写回内容（编辑中移开鼠标不丢字）。不看焦点——
            // 点击编辑后 ContainsFocus 恒 true 会导致永不自动关闭
            if (_textHoverTimer == null)
            {
                _textHoverTimer = new Timer { Interval = 400 };
                _textHoverTimer.Tick += (s, e) =>
                {
                    if (_textPreview == null || _textPreview.IsDisposed) { _textHoverTimer.Stop(); return; }
                    if (_textPreview.Pinned) return;   // 手动拖动固定后不自动关
                    if (_fnCardContainsCursor() || _fnPreviewContainsCursor()) return;
                    _textHoverTimer.Stop();
                    _fnCloseTextPreview();
                };
            }
            _textHoverTimer.Start();
            Program.DebugLog("[pincard] text-preview open " + _item.Path.Length + " 字");
        }

        private void _fnCloseTextPreview()
        {
            if (_textHoverTimer != null) { _textHoverTimer.Stop(); }
            if (_textPreview == null) return;
            try { _textPreview.Close(); _textPreview.Dispose(); } catch { }
            _textPreview = null;
        }

        // 文本写回后刷新卡片显示（名字摘要/副标题）
        private void _fnRefreshTextCard()
        {
            _name = !string.IsNullOrEmpty(_item.Title) ? _item.Title : Ingest.Ellipsis(Ingest.FirstLine(_item.Path), 18);
            _sub = _item.Path.Length + " 字";
            _nameLbl.Text = _name;
            _subLbl.Text = _sub;
            Invalidate();
        }

        // 粘贴：卡片 MA_NOACTIVATE 不抢焦点，SetClipboard 后模拟 Ctrl+V 贴回原焦点窗口
        private void _fnPaste()
        {
            try
            {
                Clipboard.SetText(_item.Path);
                var t = new Timer { Interval = 40 };
                t.Tick += (s, e) =>
                {
                    t.Stop(); t.Dispose();
                    NativeMethods.keybd_event(0x11, 0, 0, UIntPtr.Zero);
                    NativeMethods.keybd_event(0x56, 0, 0, UIntPtr.Zero);
                    NativeMethods.keybd_event(0x56, 0, 2, UIntPtr.Zero);
                    NativeMethods.keybd_event(0x11, 0, 2, UIntPtr.Zero);
                };
                t.Start();
            }
            catch (Exception ex) { Program.DebugLog("[pincard] paste fail " + ex.Message); }
        }

        // 文件便捷构造
        public PinCardForm(string path, int x, int y)
            : this(new PinnedItem { Kind = "file", Path = path, X = x, Y = y })
        {
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        // 高度锁死 28px：AutoScale 在句柄创建/布局阶段会改写 bounds（实测 28→39），
        // 在 SetBoundsCore 统一钳制。不能用 MaximumSize=(0,28)：0 宽分量被当真实上限，
        // 宽度全被压到 MinimumSize.Width → 所有卡片同宽、徽章截断（实测踩坑）
        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            height = 28;
            base.SetBoundsCore(x, y, width, height, specified);
        }

        // 点击不激活（MA_NOACTIVATE）：粘贴到原焦点窗口的前提；text 编辑预览是独立窗口正常激活
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; }
            base.WndProc(ref m);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Win11.Apply(Handle, Config.Theme != "light", Config.Border);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // 左 6px 类型色条（标签带头）
            using (var b = new SolidBrush(_typeColor))
                e.Graphics.FillRectangle(b, 0, 0, 6, Height);
            // 今天修改过：名字左侧状态点
            if (_isToday)
                using (var b = new SolidBrush(Color.FromArgb(0x2f, 0xb3, 0x6b)))
                    e.Graphics.FillEllipse(b, 10, 12, 5, 5);
        }

        private static string _fnCountSub(string dir)
        {
            try
            {
                int n = Directory.GetFileSystemEntries(dir).Length;
                return n + " 项";
            }
            catch { return ""; }
        }

        // 按卡片类型 + 文件扩展名映射颜色（folder 黄/image 绿/doc 蓝/script 橙/link 紫/pdf 红/text 灰/lnk 青）
        private static Color _fnTypeColor(PinnedItem item)
        {
            if (item.Kind == "url") return Color.FromArgb(0x8b, 0x5c, 0xf6);
            if (item.Kind == "text") return Color.FromArgb(0x64, 0x74, 0x8b);
            if (Directory.Exists(item.Path)) return Color.FromArgb(0xf2, 0xa9, 0x00);
            string ext = Path.GetExtension(item.Path).ToLowerInvariant();
            switch (ext)
            {
                case ".png": case ".jpg": case ".jpeg": case ".gif": case ".bmp": case ".webp": return Color.FromArgb(0x2f, 0xae, 0x66);
                case ".md": case ".markdown": return Color.FromArgb(0x3b, 0x82, 0xf6);
                case ".bat": case ".cmd": case ".ps1": case ".sh": return Color.FromArgb(0xf0, 0x7f, 0x1c);
                case ".pdf": return Color.FromArgb(0xe5, 0x48, 0x4d);
                case ".lnk": return Color.FromArgb(0x14, 0xb8, 0xa6);
                default: return Color.FromArgb(0x64, 0x74, 0x8b);
            }
        }

        private void Run()
        {
            try
            {
                if (_item.Kind == "text")
                {
                    try { Clipboard.SetText(_item.Path); } catch { }
                    Program.ShowBalloon("HoverExplorer", I18n.T("card_copied"));
                    return;
                }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_item.Path) { UseShellExecute = true });
            }
            catch (Exception ex) { Program.DebugLog("[pincard] run fail " + _item.Kind + " " + _item.Path + " " + ex.Message); }
        }

        // 对一个控件启用文件拖放（复制/移动进 _item 文件夹）
        private void _fnEnableDrop(Control c)
        {
            c.AllowDrop = true;
            c.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effect = (e.KeyState & 4) != 0 ? DragDropEffects.Move : DragDropEffects.Copy; BackColor = Config.DropHighlight; } };
            c.DragOver += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = (e.KeyState & 4) != 0 ? DragDropEffects.Move : DragDropEffects.Copy; };
            c.DragLeave += (s, e) => { BackColor = _bg; };
            c.DragDrop += (s, e) => _fnHandleDrop(e);
        }

        private void _fnHandleDrop(DragEventArgs e)
        {
            BackColor = _bg;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return;
            bool move = e.Effect == DragDropEffects.Move;
            int ok = 0;
            foreach (var f in files)
            {
                try
                {
                    string src = f.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string dst = Path.Combine(_item.Path, Path.GetFileName(src));
                    if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase)) continue;
                    if (move)
                    {
                        if (Directory.Exists(src)) Directory.Move(src, dst);
                        else File.Move(src, dst);
                    }
                    else _fnCopyEntry(src, dst);
                    ok++;
                }
                catch (Exception ex) { Program.DebugLog("[pincard] drop fail " + f + " " + ex.Message); }
            }
            if (ok > 0) Program.ShowBalloon("HoverExplorer", (move ? "已移动 " : "已复制 ") + ok + " 项 → " + Path.GetFileName(_item.Path));
        }

        // 递归复制文件或目录到 dst
        private static void _fnCopyEntry(string src, string dst)
        {
            if (File.Exists(src)) { File.Copy(src, dst, true); return; }
            if (!Directory.Exists(src)) return;
            Directory.CreateDirectory(dst);
            foreach (var d in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            {
                string rel = d.Substring(src.Length).TrimStart(Path.DirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(dst, rel));
            }
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(src.Length).TrimStart(Path.DirectorySeparatorChar);
                File.Copy(f, Path.Combine(dst, rel), true);
            }
        }

        private void ShowCardMenu()
        {
            var m = new ContextMenuStrip { BackColor = Config.BG, ForeColor = Config.FG, Font = new Font(Config.FontName, Config.FontSize) };
            if (_item.Kind == "url")
            {
                m.Items.Add(I18n.T("open"), null, (s, e) => Run());
                m.Items.Add(I18n.T("copy_link"), null, (s, e) => { try { Clipboard.SetText(_item.Path); } catch { } });
            }
            else if (_item.Kind == "text")
            {
                m.Items.Add(I18n.T("copy_text"), null, (s, e) => { try { Clipboard.SetText(_item.Path); } catch { } });
            }
            else
            {
                m.Items.Add(I18n.T("copy_path"), null, (s, e) => { try { Clipboard.SetText(_item.Path); } catch { } });
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(I18n.T("unpin"), null, (s, e) => Program.Unpin(_item.Kind, _item.Path));
            m.Show(Cursor.Position);
        }
    }

}