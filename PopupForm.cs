using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace HoverExplorer
{
    // 按钮/图标提示窗口：无边框、不抢前台、不在任务栏，位置固定在鼠标指针上方
    internal class TipForm : Form
    {
        private Label _lbl;

        public TipForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Config.BG;
            _lbl = new Label
            {
                AutoSize = false,
                ForeColor = Config.FG,
                BackColor = Config.BG,
                Location = new Point(5, 3),
                Font = new Font(Config.FontName, Config.FontSize),
                UseCompatibleTextRendering = true,
            };
            Controls.Add(_lbl);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 /*WS_EX_NOACTIVATE*/ | 0x00000080 /*WS_EX_TOOLWINDOW*/;
                return cp;
            }
        }

        public void SetText(string t)
        {
            _lbl.Text = t;
            // 不支持换行的 MeasureText，逐行量出来再加总（标题提示是“路径 + 改名说明”两行）
            var g = CreateGraphics();
            int w = 0, h = 0;
            foreach (var ln in t.Split('\n'))
            {
                var s = TextRenderer.MeasureText(g, ln, _lbl.Font);
                if (s.Width > w) w = s.Width;
                h += s.Height;
            }
            g.Dispose();
            _lbl.Size = new Size(w + 2, h + 1);
            ClientSize = new Size(_lbl.Width + 10, _lbl.Height + 6);
        }

        // 指针上方 18px；上方放不下就落到指针下方，并始终留在工作区内
        public void Follow()
        {
            var p = Cursor.Position;
            var wa = Screen.FromPoint(p).WorkingArea;
            int x = p.X - Width / 2, y = p.Y - Height - 18;
            if (x + Width > wa.Right) x = wa.Right - Width - 2;
            if (x < wa.Left) x = wa.Left + 2;
            if (y < wa.Top) y = p.Y + 22;
            if (Location.X != x || Location.Y != y) Location = new Point(x, y);
        }

        public void ShowTip() { if (!Visible) Visible = true; }
        public void HideTip() { if (Visible) Visible = false; }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Config.Border)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    // 列表/预览窗口的按钮提示统一管理：受 Config.ShowTips 开关控制，位置由 TipForm 控制
    internal static class Tips
    {
        private static readonly Dictionary<Control, string> _map = new Dictionary<Control, string>();
        private static readonly Timer _timer = new Timer { Interval = 300 };
        private static TipForm _form;
        private static Control _cur;

        static Tips()
        {
            _timer.Tick += (s, e) => { _timer.Stop(); if (_cur != null) ShowAt(_cur); };
        }

        public static void Set(Control c, string text)
        {
            if (c == null || c.IsDisposed) return;
            if (!Config.ShowTips || string.IsNullOrEmpty(text))
            {
                if (_map.ContainsKey(c)) { Unbind(c); _map.Remove(c); }
                return;
            }
            if (!_map.ContainsKey(c)) Bind(c);
            _map[c] = text;
        }

        // 开关关闭 / 面板销毁时立刻摘掉全部提示，不依赖控件重建
        public static void ApplyConfig()
        {
            if (Config.ShowTips) return;
            foreach (var c in _map.Keys.ToArray()) { Unbind(c); _map.Remove(c); }
            Hide();
        }

        private static void Bind(Control c)
        {
            c.MouseEnter += OnEnter;
            c.MouseMove += OnMove;
            c.MouseLeave += OnLeave;
            c.MouseDown += OnDown;
            c.Disposed += OnDisposed;
        }

        private static void Unbind(Control c)
        {
            c.MouseEnter -= OnEnter;
            c.MouseMove -= OnMove;
            c.MouseLeave -= OnLeave;
            c.MouseDown -= OnDown;
            c.Disposed -= OnDisposed;
        }

        private static void OnEnter(object s, EventArgs e)
        {
            _cur = s as Control;
            _timer.Stop(); _timer.Start();
        }

        private static void OnMove(object s, EventArgs e)
        {
            if (_cur == s && _form != null && _form.Visible) _form.Follow();
        }

        private static void OnLeave(object s, EventArgs e) { if (_cur == s) Hide(); }

        // 点了按钮就不再显示提示（比如点了固定按钮，提示不该一直挂着）
        private static void OnDown(object s, EventArgs e) { if (_cur == s) Hide(); }

        private static void OnDisposed(object s, EventArgs e)
        {
            var c = s as Control;
            if (c == null) return;
            _map.Remove(c);
            if (_cur == c) Hide();
        }

        private static void ShowAt(Control c)
        {
            // 菜单打开期间不弹提示：提示窗口是 TopMost，会盖到菜单项上（复制按钮悬停即弹菜单）
            if (Program.ShellMenuOpen) { _cur = null; return; }
            string t;
            if (!_map.TryGetValue(c, out t)) return;
            if (_form == null) _form = new TipForm();
            _form.SetText(t);
            _form.Follow();
            _form.ShowTip();
        }

        private static void Hide()
        {
            _timer.Stop();
            _cur = null;
            if (_form != null) _form.HideTip();
        }

        // 提示窗口也要参与每 Tick 提顶：面板/预览会被强制提到 TopMost 最前，否则它反而被盖住
        public static void EnsureTopmost()
        {
            if (_form == null || !_form.Visible || !_form.IsHandleCreated) return;
            NativeMethods.SetWindowPos(_form.Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE);
        }
    }

    internal class RowLabel : Label
    {
        public string Full;
        public new string Name;
        public bool IsDir;
        public bool Selected;
        public Color OrigBg;
        public Color OrigFg;
        public Image Icon;
        public string SizeText;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Icon != null)
                e.Graphics.DrawImage(Icon, 4, (Height - Icon.Height) / 2);
            if (!string.IsNullOrEmpty(SizeText))
            {
                bool multi = Height > 30;
                var ts = e.Graphics.MeasureString(SizeText, Font);
                float x = Width - 4 - ts.Width + 2;
                float y = multi ? 3 : (Height - ts.Height) / 2f;
                using (var b = new SolidBrush(Config.PlaceholderFg))
                    e.Graphics.DrawString(SizeText, Font, b, x, y);
            }
        }
    }

    internal class BorderLabel : Label
    {
        public Color BorderColor = Config.Border;
        // 排序箭头：0 无 / 1 升 / 2 降。画在右侧固定预留区，Text 恒定 → 按钮宽度恒定无横向位移
        public int SortArrow;
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(BorderColor, 1))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            if (SortArrow > 0)
            {
                int cx = Width - 8, cy = Height / 2;
                var pts = SortArrow == 1
                    ? new Point[] { new Point(cx - 3, cy + 2), new Point(cx + 3, cy + 2), new Point(cx, cy - 3) }
                    : new Point[] { new Point(cx - 3, cy - 2), new Point(cx + 3, cy - 2), new Point(cx, cy + 3) };
                using (var b = new SolidBrush(ForeColor))
                    e.Graphics.FillPolygon(b, pts);
            }
        }
    }

    internal class SizeGripCtl : Control
    {
        private bool _hover;

        public SizeGripCtl()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor
                | ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Color.Transparent;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            // 空闲时很淡，鼠标移到角上才加亮：避免常显手柄遮挡右下角信息
            int a = _hover ? 220 : 80;
            int len = Math.Min(Width, Height) - 8;
            if (len < 4) len = 4;
            int m = 3;
            int x = Width - 1 - m, y = Height - 1 - m;   // 角的顶点（右下）
            using (var pen = new Pen(Color.FromArgb(a, Config.PlaceholderFg), _hover ? 1.6f : 1.2f))
            {
                // 反向 "L"：沿右边缘的短线 + 沿下边缘的短线，构成右下角标
                g.DrawLine(pen, x, y, x, y - len);
                g.DrawLine(pen, x, y, x - len, y);
            }
        }
    }

    internal class PreviewForm : Form
    {
        // 不抢前台：预览仅为悬停展示，激活会打断当前窗口的焦点
        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Win11.Apply(Handle, Config.Theme != "light", Config.Border);

        }
        private Image _img;
        private bool _pinned;
        private Panel _toolbar;
        private Label _iconLbl;
        private Label _nameLbl;
        private Label _metaLbl;
        private Label _pinBtn;

        private Label _copyBtn;
        private Label _saveBtn;
        private Label _pasteBtn;   // textcard 专用：粘贴到原焦点窗口（PinCardForm 注入 PasteAction）
        public Action PasteAction;
        private Panel _scrollPanel;
        private Control _content;
        private SizeGripCtl _grip;
        public string FilePath;
        public Action<string> OnFileSwitched;
        private Label _pinLaunchBtn;
        private double _zoom = 1.0;
        private WheelFilter _wheelFilter;

        private bool _textDirty;

        private Timer _unpinCloseTimer;
        private Timer _gripTimer;
        private Timer _sizeSaveTimer;
        // 自定义内容类型：video / audio / svg（决定用哪套尺寸记忆）
        private string _mediaKind = "svg";
        internal string MediaKind { get { return _mediaKind; } }
        // 复制按钮悬停弹出的菜单（每次新建，留引用才能在鼠标移开后收起）
        private ContextMenuStrip _copyMenu;

        private int _toolbarH = 34;
        // 区分预览窗的“挪位置”与“改尺寸”，避免挪动时把当前尺寸写进记忆
        private Size _pvSizeMoveStart;

        // 所在目录改名后，同步预览窗里的文件路径
        internal void RebaseFilePath(string oldPrefix, string newPrefix)
        {
            if (string.IsNullOrEmpty(FilePath)) return;
            if (string.Equals(FilePath, oldPrefix, StringComparison.OrdinalIgnoreCase)) FilePath = newPrefix;
            else if (FilePath.StartsWith(oldPrefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                FilePath = newPrefix + FilePath.Substring(oldPrefix.Length);
        }



        public bool Pinned { get { return _pinned; } }

        public PreviewForm(Image img)
            : this()
        {
            _img = img;
            var pb = new PictureBox { Image = img, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill };
            _content = pb;
            _scrollPanel.Controls.Add(pb);
            BindResize(pb);
            ClientSize = new Size(img.Width, img.Height);
            ApplyImageSize(img.Width, img.Height);

        }

        public PreviewForm(Image img, int displayW, int displayH)
            : this()
        {
            _img = img;
            var pb = new PictureBox { Image = img, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill };
            _content = pb;
            _scrollPanel.Controls.Add(pb);
            BindResize(pb);
            ClientSize = new Size(displayW, displayH);
            ApplyImageSize(displayW, displayH);

        }

        public PreviewForm(string text, long size)
            : this()
        {
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
            };
            tb.Text = text;
            tb.SelectionStart = 0;
            tb.SelectionLength = 0;
            _content = tb;
            _scrollPanel.Controls.Add(tb);
            BindResize(tb);
            Win11.ApplyScrollTheme(tb, Config.Theme != "light");
            tb.TextChanged += (s, e) =>
            {
                if (!_textDirty) { _textDirty = true; _saveBtn.Visible = true; UpdateToolbarBtnPos(); }
            };

            using (var g = CreateGraphics())
            {
                var sz = g.MeasureString("W", tb.Font);
                int lineH = (int)sz.Height + 1;
                int initH = lineH * Config.TextPreviewLines + 4;
                int maxW = 0;
                foreach (var line in text.Split(new[] { "\r\n" }, StringSplitOptions.None))
                {
                    var w = g.MeasureString(line, tb.Font);
                    if (w.Width > maxW) maxW = (int)w.Width;
                }
                ClientSize = new Size(Math.Max(Math.Min(maxW + 20, 480), 240), Math.Min(initH, 320));
            }

        }

        // 自定义内容面板（视频/音频播放器 / SVG 浏览器）
        // kind: video / audio / svg —— 视频与音频使用各自独立的尺寸记忆
        public PreviewForm(Control content, int w, int h, string kind)
            : this()
        {
            _content = content;
            _mediaKind = string.IsNullOrEmpty(kind) ? "svg" : kind;
            // 关键：填充滚动面板，窗口调整大小时内容（视频画面）才会跟着变
            content.Dock = DockStyle.Fill;
            _scrollPanel.Controls.Add(content);
            BindResize(content);
            ApplyContentSize(w, h);
        }

        private PreviewForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Config.BG;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);

            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            _wheelFilter = new WheelFilter(this);
            Application.AddMessageFilter(_wheelFilter);
            FormClosed += (s, e) =>
            {
                try { Application.RemoveMessageFilter(_wheelFilter); } catch { }
                if (_sizeSaveTimer != null)
                {
                    _sizeSaveTimer.Stop(); _sizeSaveTimer.Dispose(); _sizeSaveTimer = null;
                    try { Config.Save(); } catch { }
                }
                // 预览窗关了，悬停弹出的复制菜单不能还挂在屏幕上
                if (_copyMenu != null) { try { _copyMenu.Close(); } catch { } _copyMenu = null; }
            };

            _toolbar = new Panel { BackColor = Config.BG, Height = 34 };
            Controls.Add(_toolbar);

            _scrollPanel = new Panel { AutoScroll = true, BackColor = Config.BG };
            Controls.Add(_scrollPanel);
            Win11.ApplyScrollTheme(_scrollPanel, Config.Theme != "light");

            // 右下角半透明拖拽手柄：平时隐藏，光标靠近右下角时浮现，避免遮挡内容
            _grip = new SizeGripCtl { Size = new Size(18, 18), Cursor = Cursors.SizeNWSE, Visible = false };
            _grip.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                NativeMethods.ReleaseCapture();
                NativeMethods.SendMessage(Handle, 0xA1, 0x11, 0);
            };
            Controls.Add(_grip);
            _grip.BringToFront();
            Tips.Set(_grip, I18n.T("tip_resize"));

            // 轮询光标控制手柄显隐：内容控件（WPF 播放器/滚动条）不上报鼠标事件，只能靠全局光标位置判断
            _gripTimer = new Timer { Interval = 50 };
            _gripTimer.Tick += (s, e) =>
            {
                NativeMethods.POINT pt;
                if (!NativeMethods.GetCursorPos(out pt)) return;
                bool inCorner = pt.X >= Right - 20 && pt.X <= Right && pt.Y >= Bottom - 20 && pt.Y <= Bottom;
                bool lDown = (NativeMethods.GetAsyncKeyState(0x01) & 0x8000) != 0;
                if (inCorner || (_grip.Visible && lDown))
                {
                    if (!_grip.Visible) { _grip.Show(); _grip.BringToFront(); }
                }
                else if (_grip.Visible) _grip.Hide();
            };
            _gripTimer.Start();

            _iconLbl = new Label
            {
                Size = new Size(22, 16),
                Location = new Point(8, 9),
                Font = new Font(Config.FontName, 7.5f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Config.PlaceholderFg,
                TextAlign = ContentAlignment.MiddleCenter,
            };
            _toolbar.Controls.Add(_iconLbl);

            _nameLbl = new Label
            {
                Location = new Point(34, 9),
                Font = new Font(Config.FontName, 9f, FontStyle.Bold),
                ForeColor = Config.FG,
                BackColor = Config.BG,
                AutoEllipsis = true,
                Cursor = Cursors.Hand,
            };
            _toolbar.Controls.Add(_nameLbl);

            _metaLbl = new Label
            {
                Font = new Font(Config.FontName, 8f),
                ForeColor = Config.PlaceholderFg,
                BackColor = Config.BG,
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true,
            };
            _toolbar.Controls.Add(_metaLbl);

            _pinBtn = new Label
            {
                Size = new Size(20, 18),
                AutoSize = false,
                BackColor = Config.BG,
                Image = PinIcon(false),
                Cursor = Cursors.Hand,
            };
            _pinBtn.Click += (s, e) => TogglePin();
            Tips.Set(_pinBtn, I18n.T("tip_pin_panel"));
            _toolbar.Controls.Add(_pinBtn);

            _pinLaunchBtn = new Label
            {
                Size = new Size(20, 18),
                AutoSize = false,
                BackColor = Config.BG,
                Image = PinLaunchIcon(false),
                Cursor = Cursors.Hand,
            };
            _pinLaunchBtn.Click += (s, e) => { if (!string.IsNullOrEmpty(FilePath)) { Program.TogglePin(FilePath); RefreshPinLaunchIcon(); } };
            Tips.Set(_pinLaunchBtn, I18n.T("tip_pin_card"));
            _toolbar.Controls.Add(_pinLaunchBtn);


            _copyBtn = new Label
            {
                Size = new Size(20, 18),
                AutoSize = false,
                BackColor = Config.BG,
                Image = CopyIcon(),
                Cursor = Cursors.Hand,
            };
            // 复制按钮是悬停即弹菜单，不再挂提示（提示会盖到弹出的菜单项上）
            // 鼠标移开按钮后短延时收起：期间鼠标要是移到了菜单上就不关，否则菜单没法点
            var copyLeave = new Timer { Interval = 200 };
            copyLeave.Tick += (s, e) =>
            {
                copyLeave.Stop();
                if (_copyMenu == null) return;
                NativeMethods.POINT p; NativeMethods.GetCursorPos(out p);
                if (!_copyMenu.Bounds.Contains(p.X, p.Y)) _copyMenu.Close();
            };
            _copyBtn.MouseEnter += (s, e) => { copyLeave.Stop(); ShowCopyMenu(); };
            _copyBtn.MouseLeave += (s, e) => { if (_copyMenu != null) copyLeave.Start(); };
            _toolbar.Controls.Add(_copyBtn);

            // textcard：复制文件/复制路径无意义 → 隐藏 _copyBtn，同位换成粘贴按钮
            _pasteBtn = new Label
            {
                Size = new Size(20, 18),
                AutoSize = false,
                BackColor = Config.BG,
                Text = "⇥",
                Font = new Font(Config.FontName, 10f, FontStyle.Bold),
                ForeColor = Config.FG,
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
                Visible = false,
            };
            _pasteBtn.Click += (s, e) => { if (PasteAction != null) PasteAction(); };
            _toolbar.Controls.Add(_pasteBtn);

            _saveBtn = new Label
            {
                Size = new Size(20, 18),
                AutoSize = false,
                BackColor = Config.BG,
                Image = SaveIcon(),
                Cursor = Cursors.Hand,
                Visible = false,
            };
            _saveBtn.Click += (s, e) => SaveClick();
            Tips.Set(_saveBtn, I18n.T("tip_save"));
            _toolbar.Controls.Add(_saveBtn);

            MouseEventHandler drag = (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                var sp = ((Control)s).PointToScreen(e.Location);
                int hit = GetResizeHit(sp.X, sp.Y);
                if (hit != 0) { NativeMethods.ReleaseCapture(); NativeMethods.SendMessage(Handle, 0xA1, hit, 0); return; }
                // 实际拖动后才固定；单纯点击标题不固定
                var before = Location;
                NativeMethods.ReleaseCapture(); NativeMethods.SendMessage(Handle, 0xA1, 0x2, 0);
                if (Location != before && !_pinned) { _pinned = true; _pinBtn.Image = PinIcon(true); Padding = new Padding(1); Invalidate(); }
            };
            _toolbar.MouseDown += drag;
            _iconLbl.MouseDown += drag;
            _nameLbl.MouseDown += drag;
            _metaLbl.MouseDown += drag;
            foreach (var ctl in new Control[] { _toolbar, _iconLbl, _nameLbl, _metaLbl })
            {
                ctl.MouseMove += (s, e) => { var c = (Control)s; var sp = c.PointToScreen(e.Location); UpdateResizeCursor(c, sp.X, sp.Y); };
                ctl.MouseLeave += (s, e) => { ((Control)s).Cursor = null; };
            }
            BindResize(_scrollPanel);
            Resize += (s, e) => ApplyLayout();
            UpdateToolbarBtnPos();
        }

        private void BindResize(Control c)
        {
            c.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                var sp = ((Control)s).PointToScreen(e.Location);
                int hit = GetResizeHit(sp.X, sp.Y);
                if (hit != 0) { Program.DebugLog("[pv-resize] down hit=0x" + hit.ToString("X") + " at " + sp.X + "," + sp.Y); NativeMethods.ReleaseCapture(); NativeMethods.SendMessage(Handle, 0xA1, hit, 0); }
            };
            c.MouseMove += (s, e) =>
            {
                var sp = ((Control)s).PointToScreen(e.Location);
                UpdateResizeCursor((Control)s, sp.X, sp.Y);
            };
            c.MouseLeave += (s, e) => { ((Control)s).Cursor = null; };
        }

        private void UpdateResizeCursor(Control c, int x, int y)
        {
            int b = 6, br = 20;
            Cursor cur;
            if (x <= Left + b && y <= Top + b) cur = Cursors.SizeNWSE;
            else if (x >= Right - b && y <= Top + b) cur = Cursors.SizeNESW;
            else if (x <= Left + b && y >= Bottom - b) cur = Cursors.SizeNESW;
            else if (x >= Right - br && y >= Bottom - br) cur = Cursors.SizeNWSE;
            else if (x <= Left + b) cur = Cursors.SizeWE;
            else if (x >= Right - b) cur = Cursors.SizeWE;
            else if (y <= Top + b) cur = Cursors.SizeNS;
            else if (y >= Bottom - b) cur = Cursors.SizeNS;
            else cur = null;
            Program.DebugLog("[pv-resize] move at " + x + "," + y + " -> " + (cur == null ? "null" : cur.ToString()));
            c.Cursor = cur;
        }

        private int GetResizeHit(int x, int y)
        {
            int b = 6, br = 20;
            if (x <= Left + b && y <= Top + b) return 0x0D;
            if (x >= Right - b && y <= Top + b) return 0x0E;
            if (x <= Left + b && y >= Bottom - b) return 0x10;
            if (x >= Right - br && y >= Bottom - br) return 0x11;
            if (x <= Left + b) return 0x0A;
            if (x >= Right - b) return 0x0B;
            if (y <= Top + b) return 0x0C;
            if (y >= Bottom - b) return 0x0F;
            return 0;
        }

        protected override void WndProc(ref Message m)
        {
            // WM_EXITSIZEMOVE：右下角手柄/边缘拖拽结束后记住尺寸（图片与媒体面板都记）。
            // 拖动窗口位置也走这个消息，所以只有尺寸真的变了才记，否则挪一下窗口就把尺寸写死。
            if (m.Msg == 0x231) _pvSizeMoveStart = ClientSize;
            else if (m.Msg == 0x232)
            {
                if (ClientSize != _pvSizeMoveStart && (_content is PictureBox || _content is Panel))
                {
                    if (_content is PictureBox && ClientSize.Width >= 120) Config.PreviewMinWidth = ClientSize.Width;
                    RememberSize(ClientSize.Width, ClientSize.Height);
                    Program.DebugLog("[pv-size] resize-end " + _mediaKind + " " + ClientSize.Width + "x" + ClientSize.Height);
                }
            }
            base.WndProc(ref m);
        }

        // 图片预览尺寸入口：优先套用记住的尺寸，否则走自动尺寸
        private void ApplyImageSize(int w, int h)
        {
            if (Config.PreviewImgW > 0 && Config.PreviewImgH > 0)
            {
                var wa0 = Screen.GetWorkingArea(this);
                int rw = Math.Max(120, Math.Min(Config.PreviewImgW, wa0.Width - 20));
                int rh = Math.Max(90, Math.Min(Config.PreviewImgH, wa0.Height - 20));
                ClientSize = new Size(rw, rh);
                Program.DebugLog("[pv-size] remembered " + rw + "x" + rh);
                return;
            }
            AutoFitSize(w, h);
        }

        // 自动尺寸：不小于 PreviewMinWidth，超限时等比收缩到最大占屏比例以内
        // （原图模式下超大图不再铺满整屏）
        private void AutoFitSize(int w, int h)
        {
            if (w <= 0) w = Config.PreviewMinWidth;
            if (h <= 0) h = (int)(w * 0.75);
            int minW = Config.PreviewMinWidth;
            if (w < minW && w > 0) { h = (int)Math.Round(h * (double)minW / w); w = minW; }
            var wa = Screen.GetWorkingArea(this);
            double ratio = Math.Min(1, Math.Max(0.3, Config.PreviewMaxRatio));
            int maxW = Math.Max(240, (int)(wa.Width * ratio));
            int maxH = Math.Max(180, (int)(wa.Height * ratio));
            if (w > maxW || h > maxH)
            {
                double k = Math.Min((double)maxW / w, (double)maxH / h);
                w = (int)(w * k); h = (int)(h * k);
            }
            if (w > wa.Width - 20) w = wa.Width - 20;
            if (h > wa.Height - 20) h = wa.Height - 20;
            ClientSize = new Size(Math.Max(120, w), Math.Max(90, h));
            Program.DebugLog("[pv-size] auto " + w + "x" + h + " -> " + ClientSize.Width + "x" + ClientSize.Height + " max=" + maxW + "x" + maxH);
        }

        // 取当前内容类型对应的记忆尺寸（视频/音频/SVG 各自独立）
        private void GetRememberedMediaSize(out int w, out int h)
        {
            if (_mediaKind == "video") { w = Config.PreviewVideoW; h = Config.PreviewVideoH; }
            else if (_mediaKind == "audio") { w = Config.PreviewAudioW; h = Config.PreviewAudioH; }
            else { w = Config.PreviewMediaW; h = Config.PreviewMediaH; }
        }

        private void SetRememberedMediaSize(int w, int h)
        {
            if (_mediaKind == "video") { Config.PreviewVideoW = w; Config.PreviewVideoH = h; }
            else if (_mediaKind == "audio") { Config.PreviewAudioW = w; Config.PreviewAudioH = h; }
            else { Config.PreviewMediaW = w; Config.PreviewMediaH = h; }
        }

        // 视频/音频/SVG 面板：优先套用记住的尺寸，否则按内容尺寸（含工具栏 34px）并限制占屏
        private void ApplyContentSize(int w, int h)
        {
            var wa = Screen.GetWorkingArea(this);
            int mw, mh;
            GetRememberedMediaSize(out mw, out mh);
            if (mw > 0 && mh > 0)
            {
                int rw = Math.Max(160, Math.Min(mw, wa.Width - 20));
                int rh = Math.Max(120, Math.Min(mh, wa.Height - 20));
                ClientSize = new Size(rw, rh);
                Program.DebugLog("[pv-size] media-remembered " + _mediaKind + " " + rw + "x" + rh);
                return;
            }
            double ratio = Math.Min(1, Math.Max(0.3, Config.PreviewMaxRatio));
            int maxW = Math.Max(240, (int)(wa.Width * ratio));
            int maxH = Math.Max(180, (int)(wa.Height * ratio));
            int cw = w, ch = h + 34;
            if (cw > maxW || ch > maxH)
            {
                double k = Math.Min((double)maxW / cw, (double)maxH / ch);
                cw = (int)(cw * k); ch = (int)(ch * k);
            }
            if (cw > wa.Width - 20) cw = wa.Width - 20;
            if (ch > wa.Height - 20) ch = wa.Height - 20;
            ClientSize = new Size(Math.Max(160, cw), Math.Max(120, ch));
            Program.DebugLog("[pv-size] media-auto " + w + "x" + h + " -> " + ClientSize.Width + "x" + ClientSize.Height);
        }

        // 记住用户调整后的尺寸（右下角手柄拖拽 / Ctrl+滚轮），延迟落盘避免频繁写配置
        private void RememberSize(int w, int h)
        {
            if (_content is PictureBox)
            {
                Config.PreviewImgW = w;
                Config.PreviewImgH = h;
            }
            else
            {
                SetRememberedMediaSize(w, h);
            }
            if (_sizeSaveTimer == null)
            {
                _sizeSaveTimer = new Timer { Interval = 500 };
                _sizeSaveTimer.Tick += (s, e) => { _sizeSaveTimer.Stop(); try { Config.Save(); } catch { } };
            }
            _sizeSaveTimer.Stop();
            _sizeSaveTimer.Start();
        }

        // 视频解码完成后按真实宽高比调整窗口（避免固定 4:3 造成上下黑边）
        // 用户已记住视频尺寸时不改动，尊重其手动调整
        internal void FitVideoAspect(int vw, int vh)
        {
            if (vw <= 0 || vh <= 0) return;
            int mw, mh;
            GetRememberedMediaSize(out mw, out mh);
            if (mw > 0 && mh > 0) return;
            var wa = Screen.GetWorkingArea(this);
            double ratio = Math.Min(1, Math.Max(0.3, Config.PreviewMaxRatio));
            int maxW = Math.Max(240, (int)(wa.Width * ratio));
            int maxH = Math.Max(180, (int)(wa.Height * ratio));
            int w = Math.Max(200, Math.Min(ClientSize.Width, maxW));
            int h = 64 + (int)Math.Round(w * (double)vh / vw);   // 工具栏 34 + 播放条 30 + 画面
            if (h > maxH)
            {
                h = maxH;
                w = Math.Max(200, (int)Math.Round((h - 64) * (double)vw / vh));
            }
            if (h > wa.Height - 20) h = wa.Height - 20;
            ClientSize = new Size(w, Math.Max(120, h));
            // 尺寸变化后别越出屏幕
            if (Top + Height > wa.Bottom) Top = Math.Max(wa.Top, wa.Bottom - Height - 4);
            if (Left + Width > wa.Right) Left = Math.Max(wa.Left, wa.Right - Width - 4);
            Program.DebugLog("[pv-size] video-aspect " + vw + "x" + vh + " -> " + ClientSize.Width + "x" + ClientSize.Height);
        }


        private void HandleWheel(int delta)
        {
            SwitchFile(delta > 0 ? -1 : 1);
        }

        private void ZoomBy(int delta)
        {
            double factor = delta > 0 ? 1.15 : 0.87;
            int newW, newH;
            var pb = _content as PictureBox;
            if (pb != null)
            {
                if (pb.Image == null) return;
                _zoom *= factor;
                _zoom = Math.Max(0.2, Math.Min(6, _zoom));
                newW = (int)(pb.Image.Width * _zoom);
                newH = (int)(pb.Image.Height * _zoom);
            }
            else
            {
                // 视频/音频/SVG 面板：按当前窗口等比缩放，画面随之变大
                newW = (int)(ClientSize.Width * factor);
                newH = (int)(ClientSize.Height * factor);
            }
            var wa = Screen.GetWorkingArea(this);
            newW = Math.Max(120, Math.Min(wa.Width, newW));
            newH = Math.Max(90, Math.Min(wa.Height, newH));
            ClientSize = new Size(newW, newH);
            RememberSize(newW, newH);
        }

        private static List<string> GetImageFiles(string dir)
        {
            var result = new List<string>();
            string[] all;
            try { all = Directory.GetFiles(dir); } catch { return result; }
            foreach (var f in all)
            {
                string ext = Path.GetExtension(f);
                if (Config.ImgExts.Contains(ext) || Config.IcoExts.Contains(ext))
                    result.Add(f);
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        private void SwitchFile(int dir)
        {
            if (string.IsNullOrEmpty(FilePath)) return;
            string dir0 = Path.GetDirectoryName(FilePath);
            if (string.IsNullOrEmpty(dir0)) return;
            var imgs = GetImageFiles(dir0);
            if (imgs.Count == 0) return;
            int idx = imgs.FindIndex(f => string.Equals(f, FilePath, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) idx = 0;
            int next = idx + dir;
            if (next < 0) next = imgs.Count - 1;
            if (next >= imgs.Count) next = 0;
            string newPath = imgs[next];
            int pos = next + 1;
            int total = imgs.Count;
            Task.Run(() =>
            {
                try
                {
                    var result = PreviewProvider.Generate(newPath);
                    if (result == null || result.Kind != PreviewResult.KindEnum.Image) return;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (IsDisposed) { if (result.Image != null) result.Image.Dispose(); return; }
                        var oldImg = _img;
                        _img = result.Image;
                        var pb = _content as PictureBox;
                        if (pb == null) { result.Image.Dispose(); return; }
                        pb.Image = result.Image;
                        FilePath = newPath;
                        _zoom = 1.0;
                        pb.SizeMode = PictureBoxSizeMode.Zoom;
                        ApplyImageSize(result.DisplayW > 0 ? result.DisplayW : result.Image.Width, result.DisplayH > 0 ? result.DisplayH : result.Image.Height);
                        UpdateMeta(newPath, pos, total);
                        if (oldImg != null) try { oldImg.Dispose(); } catch { }
                        if (OnFileSwitched != null) OnFileSwitched(newPath);
                    });
                }
                catch { }
            });
        }

        private void UpdateMeta(string path, int pos, int total)
        {
            try
            {
                var fi = new FileInfo(path);
                string name = Path.GetFileName(path);
                string ext = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
                string meta = ext + " · " + Config.FmtSize(fi.Length);
                if (total > 0) meta += " · " + pos + "/" + total;
                if (_nameLbl != null) _nameLbl.Text = name;
                if (_metaLbl != null) _metaLbl.Text = meta;
                UpdateToolbarBtnPos();
            }
            catch { }
        }

        private class WheelFilter : IMessageFilter
        {
            private PreviewForm _form;
            public WheelFilter(PreviewForm f) { _form = f; }
            public bool PreFilterMessage(ref Message m)
            {
                if (_form == null || _form.IsDisposed) return false;
                if (m.Msg != 0x20A) return false;
                bool ctrl0 = (Control.ModifierKeys & Keys.Control) == Keys.Control;
                bool isPic = _form._content is PictureBox;
                // 媒体面板只在 Ctrl+滚轮（缩放窗口）时拦截，普通滚轮交还给控件
                if (!isPic && !(ctrl0 && _form._content is Panel)) return false;
                int x = (short)((m.LParam.ToInt64() >> 0) & 0xFFFF);
                int y = (short)((m.LParam.ToInt64() >> 16) & 0xFFFF);
                if (!_form.Bounds.Contains(x, y)) return false;
                int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                bool ctrl = (Control.ModifierKeys & Keys.Control) == Keys.Control;
                if (ctrl) _form.ZoomBy(delta);
                else _form.HandleWheel(delta);
                return true;
            }
        }


        private void UpdateToolbarBtnPos()
        {
            int btnY = 8;
            _pinBtn.Location = new Point(_toolbar.Width - _pinBtn.Width - 4, btnY);
            _pinLaunchBtn.Location = new Point(_pinBtn.Left - _pinLaunchBtn.Width - 2, btnY);
            _copyBtn.Location = new Point(_pinLaunchBtn.Left - _copyBtn.Width - 2, btnY);
            _pasteBtn.Location = _copyBtn.Location;   // textcard：粘贴按钮占复制按钮同位
            int anchor = _copyBtn.Left;
            if (_saveBtn.Visible)
            {
                _saveBtn.Location = new Point(_copyBtn.Left - _saveBtn.Width - 2, btnY);
                anchor = _saveBtn.Left;
            }
            using (var g = _metaLbl.CreateGraphics())
            {
                var sz = g.MeasureString(_metaLbl.Text, _metaLbl.Font);
                int metaW = Math.Min((int)sz.Width + 6, anchor - 34 - 4);
                if (metaW < 0) metaW = 0;
                _metaLbl.Size = new Size(metaW, 18);
                _metaLbl.Location = new Point(anchor - metaW - 4, btnY + 1);
            }
            int nameW = _metaLbl.Left - 34 - 2;
            if (nameW < 0) nameW = 0;
            _nameLbl.Location = new Point(34, btnY + 1);
            _nameLbl.Size = new Size(nameW, 18);
            RefreshPinLaunchIcon();
        }

        // textcard 预览：复制按钮换粘贴按钮（PasteAction 由 PinCardForm 随后注入）
        public void HideCopyForTextCard()
        {
            _copyBtn.Visible = false;
            _pasteBtn.Visible = true;
            UpdateToolbarBtnPos();
        }

        public void SetHeader(string name, string meta, string iconText, Color iconColor)        {
            _nameLbl.Text = name;
            _metaLbl.Text = meta;
            _iconLbl.Text = iconText;
            _iconLbl.BackColor = iconColor;
            // 标题常被右侧按钮挤成省略号，悬停给出完整路径
            Tips.Set(_nameLbl, string.IsNullOrEmpty(FilePath) ? name : FilePath);
            // 设置 meta 文本后必须重算 _metaLbl 的宽高与位置，
            // 否则沿用构造期(空文本)算出的 6px 宽，内容被 AutoEllipsis 截断不可见
            UpdateToolbarBtnPos();
        }

        private void ApplyLayout()
        {
            _grip.Location = new Point(ClientSize.Width - _grip.Width - 2, ClientSize.Height - _grip.Height - 2);
            if (!IsHandleCreated) return;
            _toolbar.Location = new Point(0, 0);
            _toolbar.Size = new Size(ClientSize.Width, _toolbarH);
            _scrollPanel.Location = new Point(0, _toolbarH);
            _scrollPanel.Size = new Size(ClientSize.Width, Math.Max(0, ClientSize.Height - _toolbarH));
            UpdateToolbarBtnPos();
        }


        // 预览内容正占着文件句柄：图片是 Image.FromFile 锁的，媒体是播放中的 MediaElement。
        // 不释放直接 File.Move / File.Delete 必定失败，所以改名/删除前先释放，之后按原路径恢复。
        internal void ReleaseContent()
        {
            if (IsDisposed) return;
            var pb = _content as PictureBox;
            if (pb != null)
            {
                pb.Image = null;
                if (_img != null) { _img.Dispose(); _img = null; }
                return;
            }
            var p = _content as Panel;
            if (p != null) MediaPreview.StopMedia(p);
        }

        internal void ReloadContent()
        {
            var pb = _content as PictureBox;
            if (pb != null)
            {
                try
                {
                    var r = PreviewProvider.Generate(FilePath);
                    if (r != null && r.Image != null) { _img = r.Image; pb.Image = r.Image; }
                }
                catch (Exception ex) { Program.DebugLog("[pv-reload] fail " + ex.Message); }
                return;
            }
            var p = _content as Panel;
            if (p != null) MediaPreview.PlayMedia(p, FilePath);
        }

        internal void ReleaseContentIf(string path)
        {
            if (string.Equals(FilePath, path, StringComparison.OrdinalIgnoreCase)) ReleaseContent();
        }

        internal void RestoreContentTo(string oldPath, string newPath)
        {
            if (!string.Equals(FilePath, oldPath, StringComparison.OrdinalIgnoreCase)) return;
            FilePath = newPath;
            ReloadContent();
        }




        internal static bool RecycleFile(string path)
        {
            try
            {
                var op = new NativeMethods.SHFILEOPSTRUCTW();
                op.hwnd = IntPtr.Zero;
                op.wFunc = NativeMethods.FO_DELETE;
                op.pFrom = path + "\0\0";
                op.pTo = null;
                op.fFlags = NativeMethods.FOF_ALLOWUNDO | NativeMethods.FOF_NOCONFIRMATION | NativeMethods.FOF_SILENT | NativeMethods.FOF_NOERRORUI;
                return NativeMethods.SHFileOperationW(ref op) == 0;
            }
            catch { return false; }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {

            if (_unpinCloseTimer != null) { _unpinCloseTimer.Stop(); _unpinCloseTimer.Dispose(); _unpinCloseTimer = null; }
            if (_gripTimer != null) { _gripTimer.Stop(); _gripTimer.Dispose(); _gripTimer = null; }
            base.OnFormClosed(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {

            base.OnPaint(e);
        }

        private void TogglePin()
        {
            _pinned = !_pinned;
            _pinBtn.Image = PinIcon(_pinned);
            if (_pinned)
            {
                Padding = new Padding(1);
                Invalidate();
            }
            else
            {
                Padding = new Padding(0);
                Invalidate();
                if (_unpinCloseTimer == null)
                {
                    _unpinCloseTimer = new Timer { Interval = 200 };
                    _unpinCloseTimer.Tick += (s, e) =>
                    {
                        NativeMethods.POINT pt;
                        NativeMethods.GetCursorPos(out pt);
                        if (!ContainsPoint(pt.X, pt.Y))
                        {
                            _unpinCloseTimer.Stop();
                            try { Close(); } catch { }
                        }
                    };
                }
                _unpinCloseTimer.Start();
            }
        }

        public bool ContainsPoint(int x, int y)
        {
            if (!IsHandleCreated || !Visible) return false;
            return Left <= x && x <= Left + Width && Top <= y && y <= Top + Height;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _img != null) _img.Dispose();
            base.Dispose(disposing);
        }

        internal static Image PinIcon(bool pinned)
        {
            return PopupForm.UiIcon("pin_" + pinned, () =>
            {
                var bmp = new Bitmap(16, 16);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    var c = Config.FG;
                    using (var p = new Pen(c, 1.4f))
                    using (var b = new SolidBrush(c))
                    {
                        g.DrawEllipse(p, 3, 2, 8, 8);
                        if (pinned) g.FillEllipse(b, 3, 2, 8, 8);
                        g.DrawLine(p, 7, 10, 7, 14);
                        g.DrawLine(p, 6, 14, 8, 14);
                    }
                }
                return bmp;
            });
        }

        internal static Image PinLaunchIcon(bool active)
        {
            return PopupForm.UiIcon("pinlaunch_" + active, () =>
            {
                var bmp = new Bitmap(16, 16);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    var c = active ? Config.Accent : Config.FG;
                    using (var p = new Pen(c, 1.4f))
                    using (var b = new SolidBrush(c))
                    {
                        g.DrawRectangle(p, 3, 3, 10, 10);
                        g.FillRectangle(b, 3, 3, 2, 10);
                    }
                }
                return bmp;
            });
        }

        internal void RefreshPinLaunchIcon()
        {
            if (_pinLaunchBtn == null) return;
            bool active = !string.IsNullOrEmpty(FilePath) && Program.IsPinned(FilePath);
            _pinLaunchBtn.Image = PinLaunchIcon(active);
            _pinLaunchBtn.Invalidate();
        }

        internal static Image TrashIcon()
        {
            return PopupForm.UiIcon("trash", () =>
            {
                var bmp = new Bitmap(16, 16);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (var p = new Pen(Config.FG, 1.4f))
                    {
                        g.DrawLine(p, 2, 4, 14, 4);
                        g.DrawLine(p, 4, 2, 12, 2);
                        g.DrawLine(p, 4, 2, 4, 4);
                        g.DrawLine(p, 12, 2, 12, 4);
                        g.DrawLine(p, 3.5f, 4, 4.5f, 14);
                        g.DrawLine(p, 12.5f, 4, 11.5f, 14);
                        g.DrawLine(p, 4.5f, 14, 11.5f, 14);
                        g.DrawLine(p, 7, 6, 7, 12);
                        g.DrawLine(p, 10, 6, 10, 12);
                    }
                }
                return bmp;
            });
        }

        internal static Image CopyIcon()
        {
            return PopupForm.UiIcon("copy", () =>
            {
                var bmp = new Bitmap(16, 16);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (var p = new Pen(Config.FG, 1.4f))
                    {
                        g.DrawRectangle(p, 2, 2, 8, 10);
                        g.DrawRectangle(p, 5, 4, 8, 10);
                    }
                }
                return bmp;
            });
        }

        private static Image SaveIcon()
        {
            return PopupForm.UiIcon("save", () =>
            {
                var bmp = new Bitmap(16, 16);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (var p = new Pen(Config.FG, 1.4f))
                    using (var b = new SolidBrush(Config.FG))
                    {
                        g.DrawRectangle(p, 2, 2, 12, 12);
                        g.FillRectangle(b, 4, 3, 8, 3);
                        g.DrawRectangle(p, 5, 9, 6, 4);
                    }
                }
                return bmp;
            });
        }

        private void ShowCopyMenu()
        {
            if (string.IsNullOrEmpty(FilePath) || Program.ShellMenuOpen) return;
            var m = new ContextMenuStrip { BackColor = Config.BG, ForeColor = Config.FG, Font = new Font(Config.FontName, Config.FontSize) };
            m.Renderer = new ToolStripProfessionalRenderer(new DarkColorTable());
            var rtb = _content as RichTextBox;
            if (rtb != null && rtb.SelectionLength > 0)
                m.Items.Add(new ToolStripMenuItem(I18n.T("copy_selected"), null, (EventHandler)((s, e) => { try { Clipboard.SetText(rtb.SelectedText); ShowToast(I18n.T("copied")); } catch { } })));
            else if (rtb != null)
                m.Items.Add(new ToolStripMenuItem(I18n.T("copy_all"), null, (EventHandler)((s, e) => { try { Clipboard.SetText(rtb.Text); ShowToast(I18n.T("copied")); } catch { } })));
            m.Items.Add(new ToolStripMenuItem(I18n.T("copy_file"), null, (EventHandler)((s, e) => { try { var f = new System.Collections.Specialized.StringCollection(); f.Add(FilePath); Clipboard.SetFileDropList(f); ShowToast(I18n.T("copied")); } catch { } })));
            m.Items.Add(new ToolStripMenuItem(I18n.T("copy_path"), null, (EventHandler)((s, e) => { try { Clipboard.SetText(FilePath); ShowToast(I18n.T("copied")); } catch { } })));
            Program.ShellMenuOpen = true;
            m.Closed += (s, ev) => { Program.ShellMenuOpen = false; if (_copyMenu == m) _copyMenu = null; };
            // 鼠标移出菜单就直接收起：悬停菜单不该一直挂着
            m.MouseLeave += (s, ev) => m.Close();
            _copyMenu = m;
            m.Show(_copyBtn.PointToScreen(new Point(0, _copyBtn.Height + 2)));
        }

        private void CopyClick()
        {
            try
            {
                var rtb = _content as RichTextBox;
                if (rtb != null && rtb.SelectionLength > 0)
                {
                    Clipboard.SetText(rtb.SelectedText);
                    ShowToast("已复制文本");
                    return;
                }
                if (!string.IsNullOrEmpty(FilePath))
                {
                    var files = new System.Collections.Specialized.StringCollection();
                    files.Add(FilePath);
                    Clipboard.SetFileDropList(files);
                    ShowToast("已复制文件");
                }
            }
            catch { }
        }

        private void ShowToast(string msg)
        {
            var t = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                BackColor = Config.BG,
                ShowInTaskbar = false,
                TopMost = true,
                Size = new Size(60, 22),
            };
            var lbl = new Label { Text = msg, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Config.FG, BackColor = Config.BG, Font = new Font("Microsoft YaHei", 9) };
            t.Controls.Add(lbl);
            t.Location = new Point(Left + (Width - t.Width) / 2, Top + 34);
            t.Show();
            var tm = new Timer { Interval = 800 };
            tm.Tick += (s, e) => { tm.Stop(); tm.Dispose(); t.Close(); t.Dispose(); };
            tm.Start();
        }

        private void SaveClick()
        {
            try
            {
                var rtb = _content as RichTextBox;
                if (rtb != null && !string.IsNullOrEmpty(FilePath))
                {
                    System.IO.File.WriteAllText(FilePath, rtb.Text);
                    _textDirty = false;
                    _saveBtn.Visible = false;
                    UpdateToolbarBtnPos();
                }
            }
            catch { }
        }
    }

    internal class RenameDialog : Form
    {
        public string NewName;
        private TextBox _entry;

        public RenameDialog(string oldName, int x, int y)
        {
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(Math.Max(x - 60, 0), Math.Max(y - 10, 0));
            var border = new Panel { BackColor = Config.Border, Padding = new Padding(1) };
            _entry = new TextBox
            {
                Text = oldName,
                Font = new Font(Config.FontName, Config.FontSize),
                BackColor = Config.BG,
                ForeColor = Config.FG,
                BorderStyle = BorderStyle.None,
                Width = 200,
            };
            _entry.SelectAll();
            _entry.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Return) { NewName = _entry.Text.Trim(); DialogResult = DialogResult.OK; Close(); }
                else if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            };
            border.Controls.Add(_entry);
            Controls.Add(border);
            ClientSize = new Size(202, _entry.PreferredHeight + 2);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _entry.Focus();
        }
    }

    internal class PopupForm : Form
    {
        public static string SortMode = "name";
        public static bool SortDesc = false;
        public static Action OnSortChange;

        private readonly int _level;
        private readonly Action<PopupForm, int, string, int> _onOpenSub;
        private readonly Func<int, int, string> _dropResolver;
        private readonly Action<int> _onScheduleCloseAfter;
        private readonly List<RowLabel> _rows = new List<RowLabel>();
        private readonly Dictionary<string, BorderLabel> _sortBtns = new Dictionary<string, BorderLabel>();
        private TextBox _searchBox;
        private Panel _searchBorder;
        // 顶部名称行：当前文件夹名 + 编辑图标（点击就地改名）
        private Panel _nameRow;
        private Label _titleLbl;
        private Label _editBtn;
        private TextBox _nameEdit;
        private FlowLayoutPanel _sortFlow;
        private Panel _listPanel;
        private string _path;
        private int _headH;
        private int _panelW = 260;
        // 固定时记下自身尺寸：后续刷新沿用，避免条目少时被内容宽度或全局默认值改写
        private int _pinnedW, _pinnedH;
        // 进入移动/调整大小时的尺寸，用于区分“只是挪了位置”和“真的改了尺寸”
        private Size _sizeMoveStart;

        private Timer _hoverTimer;
        private string _hoverPath;
        private int _hoverRowY;
        private Timer _previewTimer;
        private string _previewPath;
        private PreviewForm _previewForm;
        private Timer _previewCloseTimer;
        private bool _menuOpen;
        private bool _dragging;
        private int? _lastClickIdx;
        private bool _dragged;
        private Point _dragStart;
        private RowLabel _dragRow;
        private RowLabel _activeDirRow;
        private RowLabel _hlRow;
        private RowLabel _contextRow;
        private bool _pinned;
        private Label _pinBtn;
        private Label _pinCardBtn;   // 面板顶部"钉为桌面卡片"按钮
        private Label _rowDelBtn;
        private Label _rowCopyBtn;  // 行悬停时显示的"复制路径"按钮
        private Label _rowPinBtn;   // 行悬停时显示的"钉为桌面卡片"按钮
        private SizeGripCtl _grip;
        private Timer _gripTimer;

        public bool Pinned { get { return _pinned; } }

        public PopupForm(int level, Action<PopupForm, int, string, int> onOpenSub, Func<int, int, string> dropResolver, Action<int> onScheduleCloseAfter)
        {
            _level = level;
            _onOpenSub = onOpenSub;
            _dropResolver = dropResolver;
            _onScheduleCloseAfter = onScheduleCloseAfter;
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            // 最小宽度：排序栏 + ⧉ + 右上两按钮不互相覆盖；拖动时即受限，与持久化一致
            MinimumSize = new Size(280, 60);
            BackColor = LevelBgColor(_level);
            DoubleBuffered = true;
            AllowDrop = true;
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    // 正在就地改名 → 取消改名；搜索展开中 → 先收起搜索；否则关闭面板
                    if (_nameEdit != null && _nameEdit.Visible) EndTitleRename(false);
                    else if (_searchBorder != null && _searchBorder.Visible) ToggleSearch();
                    else Close();
                }
            };
            DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = (e.KeyState & 4) != 0 ? DragDropEffects.Move : DragDropEffects.Copy; };
            DragOver += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = (e.KeyState & 4) != 0 ? DragDropEffects.Move : DragDropEffects.Copy; };
            DragDrop += (s, e) =>
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files == null || string.IsNullOrEmpty(_path)) return;
                bool move = e.Effect == DragDropEffects.Move;
                Program.DebugLog("[drop-form] effect=" + e.Effect + " keyState=" + e.KeyState + " move=" + move + " dstDir=" + _path + " files=" + string.Join(";", files));
                foreach (var f in files)
                {
                    if (!string.IsNullOrEmpty(f) && (File.Exists(f) || Directory.Exists(f)))
                    {
                        if (move) DoMove(f, _path);
                        else DoCopy(f, _path);
                    }
                }
            };
        }

        private static readonly Dictionary<string, Image> _iconCache = new Dictionary<string, Image>();

        private static readonly Dictionary<string, Image> _uiIconCache = new Dictionary<string, Image>();
        internal static Image UiIcon(string key, Func<Image> create)
        {
            string fullKey = key + "|" + Config.Theme;
            Image img;
            if (_uiIconCache.TryGetValue(fullKey, out img)) return img;
            img = create();
            _uiIconCache[fullKey] = img;
            return img;
        }
        internal static void ClearUiIconCache()
        {
            foreach (var kv in _uiIconCache) { try { kv.Value.Dispose(); } catch { } }
            _uiIconCache.Clear();
        }

        // 一律不抢前台：面板只是悬停展示，抢前台会打断资源管理器的输入/重命名。
        // 搜索框需要输入时由用户点击激活（点击属于真实输入，允许切换前台）
        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Win11.Apply(Handle, Config.Theme != "light", Config.Border);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateGripPos();
            // 拖动右下角手柄改宽度后同步顶部与行宽度（只同步宽度，不重排行高）
            if (Width != _panelW) SyncWidth(ClientSize.Width);
            if (_pinBtn != null) _pinBtn.BringToFront();
            if (_pinCardBtn != null) _pinCardBtn.BringToFront();
        }

        private PictureBox _resizeFreeze;
        private Image _resizeFreezeImg;

        // 拖动尺寸时冻结列表：快照盖住客户区 + 隐藏几百行 Label（AutoScroll 容器每次 WM_SIZE
        // 都要对全部子行重算布局+重绘，文件多时每帧都卡）。拖动全程系统只画这张静态图。
        private void _fnBeginResizeFreeze()
        {
            try
            {
                _fnEndResizeFreeze();
                if (_listPanel == null || _listPanel.Controls.Count <= 40) return;
                if (!Visible || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
                var src = PointToScreen(Point.Empty);
                var img = new Bitmap(ClientSize.Width, ClientSize.Height);
                using (var g = Graphics.FromImage(img)) g.CopyFromScreen(src.X, src.Y, 0, 0, img.Size);
                _resizeFreezeImg = img;
                _resizeFreeze = new PictureBox { Image = img, SizeMode = PictureBoxSizeMode.Normal, BackColor = LevelBgColor(_level), Bounds = new Rectangle(Point.Empty, ClientSize) };
                Controls.Add(_resizeFreeze);
                _resizeFreeze.BringToFront();
                _listPanel.Visible = false;
            }
            catch { _fnEndResizeFreeze(); }
        }

        private void _fnEndResizeFreeze()
        {
            if (_resizeFreeze != null) { try { Controls.Remove(_resizeFreeze); _resizeFreeze.Dispose(); } catch { } _resizeFreeze = null; }
            if (_resizeFreezeImg != null) { try { _resizeFreezeImg.Dispose(); } catch { } _resizeFreezeImg = null; }
            if (_listPanel != null && !_listPanel.Visible) _listPanel.Visible = true;
        }

        protected override void WndProc(ref Message m)
        {
            // 拖动面板用的是 WM_NCLBUTTONDOWN+HTCAPTION，它同样会发 WM_EXITSIZEMOVE。
            // 因此必须区分“挪位置”和“改尺寸”：否则挪一下只有一个文件的小面板，
            // 它的高度就被记成全局默认的 PanelHeight，之后文件多的列表也被压成这么高。
            if (m.Msg == 0x231) { _sizeMoveStart = ClientSize; _fnBeginResizeFreeze(); }
            else if (m.Msg == 0x232)
            {
                _fnEndResizeFreeze();
                if (ClientSize != _sizeMoveStart)
                {
                    Config.PanelWidth = ClientSize.Width;
                    Config.PanelHeight = ClientSize.Height;
                    // 固定面板手动改过尺寸后，后续刷新沿用新尺寸
                    if (_pinned) { _pinnedW = ClientSize.Width; _pinnedH = ClientSize.Height; }
                    Config.Save();
                    Program.DebugLog("[panel-size] resize-end " + ClientSize.Width + "x" + ClientSize.Height);
                }
                else Program.DebugLog("[panel-size] move-end, size unchanged -> keep defaults " + Config.PanelWidth + "x" + Config.PanelHeight);
            }
            base.WndProc(ref m);
        }

        private void UpdateGripPos()
        {
            if (_grip == null || _grip.IsDisposed) return;
            _grip.Location = new Point(ClientSize.Width - _grip.Width, ClientSize.Height - _grip.Height);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_gripTimer != null) { _gripTimer.Stop(); _gripTimer.Dispose(); _gripTimer = null; }
            base.OnFormClosed(e);
        }

        private static Image GetIcon(string full, bool isDir)
        {
            string key = isDir ? "<dir>" : Path.GetExtension(full);
            Image cached;
            if (_iconCache.TryGetValue(key, out cached)) return cached;
            var shfi = new NativeMethods.SHFILEINFOW();
            // 用真实路径提取图标（比 "x"+ext + USEFILEATTRIBUTES 更准确，避免某些扩展名误判为文件夹图标）
            NativeMethods.SHGetFileInfo(full, 0, ref shfi, (uint)Marshal.SizeOf(typeof(NativeMethods.SHFILEINFOW)), 0x100 | 0x1);
            Image img = null;
            if (shfi.hIcon != IntPtr.Zero)
            {
                try { using (var icon = Icon.FromHandle(shfi.hIcon)) img = icon.ToBitmap(); }
                catch { }
                NativeMethods.DestroyIcon(shfi.hIcon);
            }
            _iconCache[key] = img;
            return img;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024.0).ToString("F1") + " MB";
            return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("F2") + " GB";
        }

        // 顶部控件与行宽度按面板宽度同步：拖动手柄改宽后必须刷新，
        // 否则排序行/名称行停在旧宽度、固定按钮位置不对
        private void SyncWidth(int w)
        {
            if (w < 40) return;
            _panelW = w;
            if (_level == 0)
            {
                foreach (Control c in Controls)
                {
                    if (c is Panel && c.BackColor == Config.Border) c.Width = w - 12;
                }
                if (_searchBox != null) _searchBox.Width = w - 14;
                if (_nameRow != null)
                {
                    _nameRow.Width = w - 12;
                    if (_titleLbl != null) _titleLbl.Width = Math.Max(40, w - 12 - 24);
                    if (_editBtn != null) _editBtn.Left = w - 12 - 22;
                    if (_nameEdit != null) _nameEdit.Width = Math.Max(60, w - 12 - 24);
                }
                if (_sortFlow != null) _sortFlow.Width = w - 12;
                if (_pinBtn != null) _pinBtn.Location = new Point(w - 26, 7);
                if (_pinCardBtn != null) _pinCardBtn.Location = new Point(w - 26 - 22, 7);
            }
            if (_listPanel != null)
            {
                int sw = _listPanel.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0;
                _listPanel.Width = w;
                foreach (var r in _rows) r.Width = w - sw;
            }
        }

        public bool ShowPanel(string path, int x, int y)
        {
            CancelHover();
            CancelPreview();
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(path).Select(e => Path.GetFileName(e)).ToArray(); }
            catch { return false; }
            Array.Sort(entries, StringComparer.Ordinal);
            int originalCount = entries.Length;
            if (entries.Length > Config.MaxItems)
                entries = entries.Take(Config.MaxItems).ToArray();
            try { SortEntries(path, ref entries); } catch (Exception ex) { Program.DebugLog("[panel] SortEntries fail: " + ex.Message); }
            Program.DebugLog("[panel] ShowPanel level=" + _level + " lang=" + I18n.Lang + " path=" + path + " entries=" + entries.Length);
            _path = path;

            SuspendLayout();
            // 视觉冻结：刷新前截屏，旧控件销毁后盖住客户区，重建期间不露白底（文件多时重建耗时长）
            Image freezeImg = null;
            try
            {
                if (Visible && ClientSize.Width > 0 && ClientSize.Height > 0)
                {
                    var src = PointToScreen(Point.Empty);
                    freezeImg = new Bitmap(ClientSize.Width, ClientSize.Height);
                    using (var g = Graphics.FromImage(freezeImg)) g.CopyFromScreen(src.X, src.Y, 0, 0, freezeImg.Size);
                }
            }
            catch { if (freezeImg != null) { freezeImg.Dispose(); freezeImg = null; } }
            while (Controls.Count > 0) Controls[0].Dispose();
            Controls.Clear();
            PictureBox freeze = null;
            if (freezeImg != null)
            {
                // 不用 Dock=Fill：SuspendLayout 挂起期间 Dock 不生效（PerformLayout 在移除本层之后才跑），
                // 必须直接设 Bounds 立即覆盖客户区，否则重建期间泵消息时新控件白底被绘制出来
                freeze = new PictureBox { Image = freezeImg, SizeMode = PictureBoxSizeMode.Normal, BackColor = LevelBgColor(_level), Bounds = new Rectangle(Point.Empty, ClientSize) };
                Controls.Add(freeze);
                freeze.BringToFront();
            }
            if (_gripTimer != null) { _gripTimer.Stop(); _gripTimer.Dispose(); _gripTimer = null; }
            _grip = null;
            _hlRow = null;
            _rows.Clear();
            _sortBtns.Clear();

            int curY = 5;

            if (_level == 0)
            {
                // 右侧留 48px 给 _pinCardBtn/_pinBtn：FlowLayoutPanel 不裁切溢出子控件，
                // 占满宽度时 ⧉ 会钻到按钮下面被覆盖；收窄后行尾空白也是拖拽入口
                var sortFlow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Location = new Point(6, curY), AutoSize = false, Height = 26, Width = _panelW - 60, BackColor = LevelBgColor(_level) };
                _sortFlow = sortFlow;
                foreach (var kv in Config.SortLabels)
                {
                var btn = new BorderLabel
                {
                    Text = kv.Value,
                    Font = new Font(Config.FontName, Config.FontSize),
                    Padding = new Padding(5, 1, 13, 1),
                    AutoSize = true,
                    BackColor = Config.BG,
                    ForeColor = Config.FG,
                    Cursor = Cursors.Hand,
                    Margin = new Padding(0, 0, 2, 0),
                };
                    var mode = kv.Key;
                    btn.Click += (s, e) => SetSort(mode);
                    Tips.Set(btn, I18n.T("tip_sort"));
                    _sortBtns[mode] = btn;
                    sortFlow.Controls.Add(btn);
                }
                var pathBtn = new Label
                {
                    Text = "⧉",
                    Font = new Font(Config.FontName, Config.FontSize),
                    AutoSize = true,
                    BackColor = Config.BG,
                    ForeColor = Config.FG,
                    Cursor = Cursors.Hand,
                    Margin = new Padding(0, 0, 2, 0),
                    Padding = new Padding(5, 1, 5, 1),
                };
                pathBtn.Click += (s, e) =>
                {
                    try { Clipboard.SetText(_path); } catch { }
                    var origText = pathBtn.Text;
                    pathBtn.Text = "✓";
                    var t = new Timer { Interval = 600 };
                    t.Tick += (s2, e2) => { ((Timer)s2).Stop(); ((Timer)s2).Dispose(); if (!pathBtn.IsDisposed) pathBtn.Text = origText; };
                    t.Start();
                };
                Tips.Set(pathBtn, I18n.T("copy_path"));
                sortFlow.Controls.Add(pathBtn);
                Controls.Add(sortFlow);
                curY += 28;

                _pinBtn = new Label
                {
                    Size = new Size(20, 18),
                    AutoSize = false,
                    Location = new Point(_panelW - 26, 7),
                    BackColor = Config.BG,
                    Image = PreviewForm.PinIcon(_pinned),
                    Cursor = Cursors.Hand,
                };
                Tips.Set(_pinBtn, I18n.T("tip_pin_panel"));
                _pinBtn.Click += (s, e) => TogglePinPopup();
                Controls.Add(_pinBtn);

                _pinCardBtn = new Label
                {
                    Size = new Size(20, 18),
                    AutoSize = false,
                    Location = new Point(_panelW - 26 - 22, 7),
                    BackColor = Config.BG,
                    Image = PreviewForm.PinLaunchIcon(false),
                    Cursor = Cursors.Hand,
                };
                Tips.Set(_pinCardBtn, I18n.T("tip_pin_card"));
                _pinCardBtn.Click += (s, e) => { if (!string.IsNullOrEmpty(_path)) Program.TogglePin(_path); };
                Controls.Add(_pinCardBtn);

                // 名称行：当前文件夹名（过长省略，悬停 tooltip 显示完整路径）+ 编辑图标（点击就地改名）
                _nameRow = new Panel { BackColor = Config.BG, Location = new Point(6, curY), Size = new Size(_panelW - 12, 22) };
                _titleLbl = new Label
                {
                    Text = ShowName(path),
                    Font = new Font(Config.FontName, Config.FontSize, FontStyle.Bold),
                    ForeColor = Config.FG,
                    BackColor = Config.BG,
                    AutoEllipsis = true,
                    UseCompatibleTextRendering = true,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Cursor = Cursors.Hand,
                    Location = new Point(0, 2),
                    Size = new Size(Math.Max(40, _panelW - 12 - 24), 18),
                };
                var titleDown = new Point(int.MinValue, int.MinValue);
                _titleLbl.MouseDown += (s, e) => titleDown = e.Location;
                // 用 MouseUp 而非 Click：拖动面板后不应误触发改名
                _titleLbl.MouseUp += (s, e) =>
                {
                    if (e.Button != MouseButtons.Left) return;
                    if (Math.Abs(e.Location.X - titleDown.X) > 3 || Math.Abs(e.Location.Y - titleDown.Y) > 3) return;
                    BeginTitleRename();
                };
                // 标题提示：完整路径 + 可改名说明（统一走 Tips，受开关控制）
                Tips.Set(_titleLbl, path + "\n" + I18n.T("tip_rename_dir"));
                _editBtn = new Label
                {
                    Text = "✎",
                    Font = new Font(Config.FontName, Config.FontSize),
                    ForeColor = Config.PlaceholderFg,
                    BackColor = Config.BG,
                    Size = new Size(20, 18),
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Cursor = Cursors.Hand,
                    Location = new Point(_panelW - 12 - 22, 2),
                };
                Tips.Set(_editBtn, I18n.T("tip_rename_dir"));
                _editBtn.Click += (s, e) => BeginTitleRename();
                _nameEdit = new TextBox
                {
                    Font = new Font(Config.FontName, Config.FontSize),
                    BackColor = Config.BG,
                    ForeColor = Config.FG,
                    BorderStyle = BorderStyle.FixedSingle,
                    Location = new Point(0, 1),
                    Width = Math.Max(60, _panelW - 12 - 24),
                    Visible = false,
                };
                _nameEdit.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Return) { e.SuppressKeyPress = true; EndTitleRename(true); }
                    else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; EndTitleRename(false); }
                };
                // 点别处 = 提交（与资源管理器重命名一致）
                _nameEdit.LostFocus += (s, e) => { if (_nameEdit.Visible) EndTitleRename(true); };
                _nameRow.Controls.Add(_titleLbl);
                _nameRow.Controls.Add(_editBtn);
                _nameRow.Controls.Add(_nameEdit);
                Controls.Add(_nameRow);
                curY += 22 + 2;

                // 搜索框：默认隐藏，Ctrl+F 才展开（常驻输入框会带来假焦点/IME 遮挡问题）
                _searchBorder = new Panel { BackColor = Config.Border, Location = new Point(6, curY), Size = new Size(_panelW - 12, 22), Visible = false };
                _searchBox = new TextBox
                {
                    Text = I18n.T("search"),
                    Font = new Font(Config.FontName, Config.FontSize),
                    BackColor = Config.BG,
                    ForeColor = Config.PlaceholderFg,
                    BorderStyle = BorderStyle.None,
                    Location = new Point(1, 2),
                    Width = _searchBorder.Width - 2,
                };
                _searchBox.GotFocus += (s, e) => { if (_searchBox.Text == I18n.T("search")) { _searchBox.Text = ""; _searchBox.ForeColor = Config.FG; } };
                _searchBox.LostFocus += (s, e) => { if (string.IsNullOrEmpty(_searchBox.Text)) { _searchBox.Text = I18n.T("search"); _searchBox.ForeColor = Config.PlaceholderFg; } };
                _searchBox.TextChanged += (s, e) => FilterRows(_searchBox.Text);
                Tips.Set(_searchBox, I18n.T("tip_search"));
                _searchBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; ToggleSearch(); } };
                _searchBorder.Controls.Add(_searchBox);
                Controls.Add(_searchBorder);
                _headH = curY;

                // 实际拖动后才固定；单纯点击不固定
                MouseEventHandler drag = (s, e) =>
                {
                    if (e.Button != MouseButtons.Left) return;
                    var before = Location;
                    NativeMethods.ReleaseCapture(); NativeMethods.SendMessage(Handle, 0xA1, 0x2, 0);
                    if (Location != before && !_pinned) TogglePinPopup();
                };
                sortFlow.MouseDown += drag;
                _nameRow.MouseDown += drag;
                _searchBorder.MouseDown += drag;
                // 顶部区域空白处右键 = 选项菜单
                MouseEventHandler blankMenu = (s, e) => { if (e.Button == MouseButtons.Right && Program.TrayMenu != null) { _menuOpen = true; Program.TrayMenu.Show(Cursor.Position); } };
                sortFlow.MouseUp += blankMenu;
                _nameRow.MouseUp += blankMenu;
                _searchBorder.MouseUp += blankMenu;
            }
            else
            {
                _headH = 0;
            }

            _listPanel = new Panel { Location = new Point(0, curY), BackColor = Config.BG };
            // 列表空白处右键弹出选项菜单（托盘图标隐藏时的入口）
            _listPanel.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Right && Program.TrayMenu != null)
                { _menuOpen = true; Program.TrayMenu.Show(Cursor.Position); }
            };
            Controls.Add(_listPanel);
            Win11.ApplyScrollTheme(_listPanel, Config.Theme != "light");

            _rowDelBtn = new Label
            {
                Size = new Size(18, 18),
                AutoSize = false,
                BackColor = Config.BgHover,
                Image = PreviewForm.TrashIcon(),
                Cursor = Cursors.Hand,
                Visible = false,
                TextAlign = ContentAlignment.MiddleCenter,
            };
            _rowDelBtn.Click += (s, e) =>
            {
                var p = _rowDelBtn.Tag as string;
                if (string.IsNullOrEmpty(p)) return;
                if (PreviewForm.RecycleFile(p)) ShowPanel(_path, Left, Top);
            };

            _listPanel.Controls.Add(_rowDelBtn);
            _rowDelBtn.BringToFront();
            Tips.Set(_rowDelBtn, I18n.T("delete"));

            _rowCopyBtn = new Label
            {
                Size = new Size(18, 18),
                AutoSize = false,
                BackColor = Config.BgHover,
                Image = PreviewForm.CopyIcon(),
                Cursor = Cursors.Hand,
                Visible = false,
                TextAlign = ContentAlignment.MiddleCenter,
            };
            _rowCopyBtn.Click += (s, e) =>
            {
                var p = _rowCopyBtn.Tag as string;
                if (string.IsNullOrEmpty(p)) return;
                try { Clipboard.SetText(p); ShowToast(I18n.T("copied")); } catch { }
            };
            _listPanel.Controls.Add(_rowCopyBtn);
            _rowCopyBtn.BringToFront();
            Tips.Set(_rowCopyBtn, I18n.T("copy_path"));

            _rowPinBtn = new Label
            {
                Size = new Size(18, 18),
                AutoSize = false,
                BackColor = Config.BgHover,
                Image = PreviewForm.PinLaunchIcon(false),
                Cursor = Cursors.Hand,
                Visible = false,
                TextAlign = ContentAlignment.MiddleCenter,
            };
            _rowPinBtn.Click += (s, e) =>
            {
                var p = _rowPinBtn.Tag as string;
                if (string.IsNullOrEmpty(p)) return;
                Program.TogglePin(p);
            };
            _listPanel.Controls.Add(_rowPinBtn);
            _rowPinBtn.BringToFront();
            Tips.Set(_rowPinBtn, I18n.T("tip_pin_card"));

            var font = new Font(Config.FontName, Config.FontSize);
            int maxW = 200;
            var sizeTexts = new string[entries.Length];
            var dirFlags = new bool[entries.Length];
            var sizeWs = new float[entries.Length];
            float maxSizeW = 0;
            using (var g = CreateGraphics())
            {
                foreach (var name in entries)
                {
                    var sz = g.MeasureString(name, font);
                    if (sz.Width + 36 > maxW) maxW = (int)sz.Width + 36;
                }
                // 预取文件大小 / 文件夹子项数（仅 NTFS 元数据，不读文件内容）
                for (int i = 0; i < entries.Length; i++)
                {
                    string full = Path.Combine(path, entries[i]);
                    bool isDir = Directory.Exists(full);
                    dirFlags[i] = isDir;
                    try
                    {
                        sizeTexts[i] = isDir
                            ? Directory.GetFileSystemEntries(full).Length + " " + I18n.T("items")
                            : FormatSize(new FileInfo(full).Length);
                    }
                    catch { }
                    if (sizeTexts[i] != null)
                    {
                        sizeWs[i] = g.MeasureString(sizeTexts[i], font).Width;
                        if (sizeWs[i] > maxSizeW) maxSizeW = sizeWs[i];
                    }
                }
                if (maxSizeW > 0) maxW += (int)maxSizeW + 10;
            }
            if (_level == 0)
            {
                foreach (var btn in _sortBtns.Values)
                {
                    int bw = btn.Width + btn.Margin.Horizontal + 6;
                    if (bw > maxW) maxW = bw;
                }
            }
            // 手动调整过的宽度强制沿用（拖窄也持久化，长文件名 AutoEllipsis 截断）
            if (Config.PanelWidth >= 200) maxW = Config.PanelWidth;
            if (maxW > Math.Max(400, Config.PanelWidth)) maxW = Math.Max(400, Config.PanelWidth);
            // 已固定的面板沿用固定时的宽度：条目少的目录刷新后不该被拉回默认宽度
            if (_pinned && _pinnedW > 0) maxW = _pinnedW;
            _panelW = maxW;

            if (_level == 0) SyncWidth(_panelW);

            int rowH = 24;
            // 长文件名会自动换行显示：按换行后实际行数计算行高（封顶 3 行）；
            // 名称可用宽度需扣除右侧大小文本保留区
            var rowHeights = new List<int>();
            using (var g = CreateGraphics())
            {
                float lineH = font.GetHeight(g);
                Action<int> measureHeights = delegate(int extraCut)
                {
                    rowHeights.Clear();
                    for (int i = 0; i < entries.Length; i++)
                    {
                        int padR = 4 + (sizeTexts[i] == null ? 0 : (int)sizeWs[i] + 6);
                        int availW = _panelW - 24 - padR - extraCut;
                        if (availW < 40) availW = 40;
                        int h = rowH;
                        var tsz = g.MeasureString(entries[i], font, availW);
                        if (tsz.Height > lineH + 2)
                        {
                            int lines = (int)Math.Ceiling(tsz.Height / lineH);
                            if (lines > 3) lines = 3;
                            h = (int)Math.Ceiling(lines * lineH) + 7;
                        }
                        rowHeights.Add(h);
                    }
                };
                measureHeights(0);
                var wa0 = Screen.GetWorkingArea(Cursor.Position);
                int totalH = curY;
                foreach (var h in rowHeights) totalH += h;
                if (totalH > wa0.Height - 8)
                    measureHeights(SystemInformation.VerticalScrollBarWidth);
            }
            int ry = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                string name = entries[i];
                string full = Path.Combine(path, name);
                bool isDir = dirFlags[i];
                var row = new RowLabel
                {
                    Full = full,
                    Name = name,
                    IsDir = isDir,
                    Text = name,
                    Font = font,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(24, 0, sizeTexts[i] == null ? 4 : (int)sizeWs[i] + 10, 0),
                    AutoSize = false,
                    UseCompatibleTextRendering = true,
                    BackColor = Config.BG,
                    ForeColor = Config.FG,
                    Location = new Point(0, ry),
                    Size = new Size(_panelW, rowHeights[i]),
                    Icon = GetIcon(full, isDir),
                    SizeText = sizeTexts[i],
                };
                row.OrigBg = row.BackColor;
                row.OrigFg = row.ForeColor;
                row.MouseEnter += (s, e) => OnRowEnter(row);
                row.MouseLeave += (s, e) => OnRowLeave(row);
                row.MouseDown += (s, e) => OnRowDown(row, e);
                row.MouseMove += (s, e) => OnRowMove(row, e);
                row.MouseUp += (s, e) => OnRowUp(row, e);
                row.GiveFeedback += (s, e) =>
                {
                    // 拖拽指针反馈：Shift=移动(纯箭头)，默认=复制(箭头+加号)
                    e.UseDefaultCursors = false;
                    bool move = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
                    Cursor.Current = move ? DropMoveCursor : DropCopyCursor;
                };
                row.MouseClick += (s, e) => { if (e.Button == MouseButtons.Right) PopupMenu(row, e); };
                if (isDir)
                {
                    row.AllowDrop = true;
                    row.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effect = (e.KeyState & 4) != 0 ? DragDropEffects.Move : DragDropEffects.Copy; row.BackColor = Config.DropHighlight; } };
                    row.DragOver += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = (e.KeyState & 4) != 0 ? DragDropEffects.Move : DragDropEffects.Copy; };
                    row.DragLeave += (s, e) => ApplySel(row);
                    row.DragDrop += (s, e) => { OnDrop(row, e); };
                }
                _listPanel.Controls.Add(row);
                _rows.Add(row);
                ry += rowHeights[i];
            }
            if (entries.Length == 0)
            {
                var emptyLbl = new Label
                {
                    Text = I18n.T("empty"),
                    Font = font,
                    ForeColor = Config.PlaceholderFg,
                    BackColor = Config.BG,
                    AutoSize = true,
                    Location = new Point(8, 0),
                };
                _listPanel.Controls.Add(emptyLbl);
            }
            var workArea = Screen.GetWorkingArea(Cursor.Position);
            int maxH = workArea.Height - 8;
            // 记忆高度只作上限；过小的值基本是拖动小面板时误记的历史配置，当作未设置
            if (Config.PanelHeight >= 150) maxH = Math.Min(Config.PanelHeight, maxH);
            int needH = curY + ry;
            // 同理：固定面板沿用固定时的高度（内容变多时仍可更高，但不超过工作区）
            if (_pinned && _pinnedH > 0 && needH < _pinnedH) needH = _pinnedH;
            if (needH > maxH)
            {
                int scrollW = SystemInformation.VerticalScrollBarWidth;
                needH = maxH;
                foreach (var r in _rows) r.Width = _panelW - scrollW;
                _listPanel.AutoScroll = true;
            }
            _listPanel.Size = new Size(_panelW, needH - curY);
            // 底部状态栏：占位防止 grip 挡住最后一行删除按钮，并显示限高/计数提示
            int statusH = 18;
            ClientSize = new Size(_panelW, needH + statusH);
            string statusText = originalCount > 0 ? I18n.T("panel_status_count", originalCount) : "";
            var statusLbl = new Label
            {
                Location = new Point(6, needH),
                Size = new Size(_panelW - 12, statusH),
                Text = statusText,
                Font = new Font(Config.FontName, Math.Max(7f, Config.FontSize - 1)),
                ForeColor = Config.PlaceholderFg,
                BackColor = LevelBgColor(_level),
                TextAlign = ContentAlignment.MiddleLeft,
            };
            Controls.Add(statusLbl);
            // 状态栏全宽可拖动固定：宽度收窄时顶部可拖区域被按钮挤占，底部是稳定拖拽入口
            statusLbl.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                var before = Location;
                NativeMethods.ReleaseCapture(); NativeMethods.SendMessage(Handle, 0xA1, 0x2, 0);
                if (Location != before && !_pinned) TogglePinPopup();
            };

            ResumeLayout(false);
            PerformLayout();

            ApplySortBtns();

            ClampPos(ref x, ref y);
            Location = new Point(x, y);

            // 整面板按 level 着色：递归把子控件 Config.BG 背景换成 LevelBgColor
            {
                Color lbg = LevelBgColor(_level);
                Action<Control> tint = null;
                tint = (ctl) => { if (ctl.BackColor == Config.BG) ctl.BackColor = lbg; foreach (Control cc in ctl.Controls) tint(cc); };
                tint(this);
            }

            // 移除冻结快照，显示重建后的内容；右上按钮此时才 BringToFront——
            // 重建全程按钮藏在遮罩的冻结图像里（视觉连续），移除后新按钮立即置顶出现，不闪不隐藏
            if (freeze != null)
            {
                Controls.Remove(freeze);
                freeze.Dispose();
            }
            if (freezeImg != null) freezeImg.Dispose();
            if (_pinBtn != null) _pinBtn.BringToFront();
            if (_pinCardBtn != null) _pinCardBtn.BringToFront();

            // 空白处右键 = 选项菜单（排序功能已由顶部排序按钮覆盖）
            MouseClick += (s, e) => { if (e.Button == MouseButtons.Right && Program.TrayMenu != null) { _menuOpen = true; Program.TrayMenu.Show(Cursor.Position); } };

            _grip = new SizeGripCtl { Size = new Size(16, 16), Cursor = Cursors.SizeNWSE, Visible = false };
            _grip.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                NativeMethods.ReleaseCapture();
                NativeMethods.SendMessage(Handle, 0xA1, 0x11, 0);
            };
            Controls.Add(_grip);
            _grip.BringToFront();
            UpdateGripPos();
            Tips.Set(_grip, I18n.T("tip_resize"));

            // 只在鼠标位于面板内（或正在拖拽）时显示，避免常显手柄遮住右下角的行信息
            if (_gripTimer != null) { _gripTimer.Stop(); _gripTimer.Dispose(); }
            _gripTimer = new Timer { Interval = 150 };
            _gripTimer.Tick += (s, e) =>
            {
                if (_grip == null || _grip.IsDisposed) return;
                NativeMethods.POINT pt;
                if (!NativeMethods.GetCursorPos(out pt)) return;
                bool inside = pt.X >= Left && pt.X < Right && pt.Y >= Top && pt.Y < Bottom;
                bool lDown = (NativeMethods.GetAsyncKeyState(0x01) & 0x8000) != 0;
                bool want = inside || (lDown && _grip.Visible);
                if (_grip.Visible != want) _grip.Visible = want;
            };
            _gripTimer.Start();

            // 不自动聚焦搜索框：后台进程在 Timer 里 Focus 只会得到"假焦点"
            // （光标闪但键盘消息仍归真正的前台窗口），需要点一下搜索框才能真正输入
            return true;
        }

        public void FocusSearch()
        {
            if (_searchBox != null) _searchBox.Focus();
        }

        private static string ShowName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string n = Path.GetFileName(path);
            return string.IsNullOrEmpty(n) ? path : n;
        }

        // Ctrl+F：展开/收起搜索框（默认隐藏，避免常驻输入框带来的假焦点与 IME 遮挡）
        public void ToggleSearch()
        {
            if (_searchBorder == null || _searchBox == null || _listPanel == null) return;
            bool on = !_searchBorder.Visible;
            _searchBorder.Visible = on;
            int dh = 24;
            _listPanel.Top += on ? dh : -dh;
            _listPanel.Height -= on ? dh : -dh;
            _headH += on ? dh : -dh;
            ClientSize = new Size(_panelW, ClientSize.Height + (on ? dh : -dh));
            if (_pinned) _pinnedH = ClientSize.Height;
            int lx = Left, ly = Top; ClampPos(ref lx, ref ly); Location = new Point(lx, ly);
            if (on)
            {
                _searchBox.ForeColor = Config.FG;
                _searchBox.Text = "";
                _searchBox.Focus();
            }
            else
            {
                _searchBox.Text = I18n.T("search");
                _searchBox.ForeColor = Config.PlaceholderFg;
                FilterRows("");
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F)) { ToggleSearch(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // 重命名文件/目录：非法字符与重名校验 + 固定项路径同步 + 相关面板刷新
        public static bool RenamePath(string src, string newName)
        {
            if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(newName)) return false;
            newName = newName.Trim();
            if (newName.Length == 0) return false;
            if (newName.IndexOfAny(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
            {
                Program.Log("[重命名失败] 名称不能包含 \\ / : * ? \" < > |");
                return false;
            }
            string dir = Path.GetDirectoryName(src);
            if (string.IsNullOrEmpty(dir)) return false;
            string dst = Path.Combine(dir, newName);
            if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase)) return false;
            // 关键：文件正被预览时就占着句柄（图片 Image.FromFile 锁文件、MediaElement 播放中），
            // 不先释放 File.Move 必定抛“文件正由另一进程使用”
            ReleasePreviewOf(src);
            bool moved = false;
            try
            {
                if (Directory.Exists(src)) { if (Directory.Exists(dst)) return false; Directory.Move(src, dst); }
                else { if (File.Exists(dst)) return false; File.Move(src, dst); }
                moved = true;
            }
            catch (Exception ex) { Program.Log("[重命名失败] " + ex.Message); }
            // 成功按新路径恢复画面，失败也要把空掉的画面放回去
            RestorePreviewOf(src, moved ? dst : src);
            if (!moved) return false;
            // 固定项存的是路径，改名后要同步，否则固定卡片/面板指向失效路径
            var pi = Config.PinnedItems.Find(p => string.Equals(p.Path, src, StringComparison.OrdinalIgnoreCase));
            if (pi != null) { pi.Path = dst; Config.Save(); }
            Program.RefreshPanelsShowing(dir, null);
            Program.DebugLog("[rename] " + src + " -> " + dst);
            return true;
        }

        // 顶部名称就地改名：改的是面板自己的根目录，成功后要更新 _path / 标题 / 子面板路径
        private void BeginTitleRename()
        {
            if (_nameEdit == null || _titleLbl == null || string.IsNullOrEmpty(_path)) return;
            _nameEdit.Text = ShowName(_path);
            _nameEdit.Visible = true;
            _nameEdit.BringToFront();
            _titleLbl.Visible = false;
            if (_editBtn != null) _editBtn.Visible = false;
            // 同上：面板默认不抢前台，改名时要主动激活才能收到键盘
            if (!Focused) Activate();
            _nameEdit.Focus();
            _nameEdit.SelectAll();
        }

        private void EndTitleRename(bool commit)
        {
            if (_nameEdit == null || !_nameEdit.Visible) return;
            string nn = commit ? _nameEdit.Text.Trim() : "";
            if (commit && !string.IsNullOrEmpty(nn))
            {
                string old = _path;
                string target = Path.Combine(Path.GetDirectoryName(old), nn);
                if (!string.Equals(old, target, StringComparison.OrdinalIgnoreCase) && RenamePath(old, nn))
                {
                    _path = target;
                    Program.RebasePaths(old, target);
                }
            }
            _nameEdit.Visible = false;
            _titleLbl.Visible = true;
            if (_editBtn != null) _editBtn.Visible = true;
            _titleLbl.Text = ShowName(_path);
            Tips.Set(_titleLbl, _path + "\n" + I18n.T("tip_rename_dir"));
        }

        // 目录改名后同步本面板路径（子面板路径以旧目录为前缀，一并改写）
        internal void RebasePath(string oldPrefix, string newPrefix)
        {
            if (string.IsNullOrEmpty(_path)) return;
            if (string.Equals(_path, oldPrefix, StringComparison.OrdinalIgnoreCase)) _path = newPrefix;
            else if (_path.StartsWith(oldPrefix + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                _path = newPrefix + _path.Substring(oldPrefix.Length);
        }

        public static void RebaseFilePreviewPath(string oldPrefix, string newPrefix)
        {
            if (_extPreviewForm != null && !_extPreviewForm.IsDisposed) _extPreviewForm.RebaseFilePath(oldPrefix, newPrefix);
        }

        // 所有改名都走这里：先把占用该文件的预览释放掉，移动完再恢复画面。
        // 覆盖悬停预览（_extPreviewForm）与各面板内的预览。
        internal static void ReleasePreviewOf(string path)
        {
            if (_extPreviewForm != null) _extPreviewForm.ReleaseContentIf(path);
            foreach (var p in Program.Panels) p.ReleaseInnerPreviewIf(path);
        }

        internal static void RestorePreviewOf(string oldPath, string newPath)
        {
            if (_extPreviewForm != null) _extPreviewForm.RestoreContentTo(oldPath, newPath);
            foreach (var p in Program.Panels) p.RestoreInnerPreview(oldPath, newPath);
        }

        // 面板内预览正显示该文件时，改名要先释放它的内容
        internal void ReleaseInnerPreviewIf(string path)
        {
            if (_previewForm != null) _previewForm.ReleaseContentIf(path);
        }

        internal void RestoreInnerPreview(string oldPath, string newPath)
        {
            if (_previewForm != null) _previewForm.RestoreContentTo(oldPath, newPath);
        }

        private void SortEntries(string path, ref string[] entries)
        {
            string mode = SortMode;
            bool desc = SortDesc;
            int n = entries.Length;
            var isDir = new bool[n];
            var sizes = new long[n];
            var times = new long[n];
            for (int i = 0; i < n; i++)
            {
                string full = Path.Combine(path, entries[i]);
                try
                {
                    isDir[i] = Directory.Exists(full);
                    if (mode == "size" && !isDir[i] && File.Exists(full)) sizes[i] = new FileInfo(full).Length;
                    if (mode == "date") times[i] = File.GetLastWriteTimeUtc(full).Ticks;
                }
                catch { }
            }
            var indices = new int[n];
            for (int i = 0; i < n; i++) indices[i] = i;
            var names = entries;
            Array.Sort<int>(indices, (ia, ib) =>
            {
                if (isDir[ia] != isDir[ib]) return isDir[ia] ? -1 : 1;
                int cmp;
                if (mode == "size") cmp = sizes[ia].CompareTo(sizes[ib]);
                else if (mode == "date") cmp = times[ia].CompareTo(times[ib]);
                else cmp = string.Compare(names[ia], names[ib], StringComparison.OrdinalIgnoreCase);
                if (cmp == 0) cmp = string.Compare(names[ia], names[ib], StringComparison.OrdinalIgnoreCase);
                return desc ? -cmp : cmp;
            });
            var sorted = new string[n];
            for (int i = 0; i < n; i++) sorted[i] = names[indices[i]];
            entries = sorted;
        }

        private void ApplySortBtns()
        {
            foreach (var kv in _sortBtns)
            {
                bool on = kv.Key == SortMode;
                kv.Value.Text = Config.SortLabels[kv.Key];
                kv.Value.SortArrow = on ? (SortDesc ? 2 : 1) : 0;
                kv.Value.BackColor = on ? Config.BgSel : LevelBgColor(_level);
                kv.Value.ForeColor = on ? Color.White : Config.FG;
                kv.Value.Invalidate();
            }
        }

        private void SetSort(string mode)
        {
            if (SortMode == mode) SortDesc = !SortDesc;
            else SortMode = mode;
            ApplySortBtns();
            if (OnSortChange != null) OnSortChange();
        }

        private void ToggleSortDesc()
        {
            SortDesc = !SortDesc;
            ApplySortBtns();
            if (OnSortChange != null) OnSortChange();
        }

        private void FilterRows(string q)
        {
            string raw = q;
            // 必须在 ToLowerInvariant 之前与占位符比较：
            // en/fr/es/ru 等语言占位符首字母大写（Search/Rechercher/Buscar/Поиск），
            // 小写化后再比较会失配，导致占位文本被当成搜索词、整表被过滤空
            q = q.Trim();
            if (q == I18n.T("search")) q = "";
            q = q.ToLowerInvariant();
            int hidden = 0;
            foreach (var row in _rows)
            {
                bool vis = string.IsNullOrEmpty(q) || row.Name.ToLowerInvariant().Contains(q);
                row.Visible = vis;
                if (!vis) hidden++;
            }
            Program.DebugLog("[filter] level=" + _level + " lang=" + I18n.Lang + " raw=\"" + raw + "\" placeholder=\"" + I18n.T("search") + "\" rows=" + _rows.Count + " hidden=" + hidden);
        }

        private void ClampPos(ref int x, ref int y)
        {
            NativeMethods.POINT pt;
            NativeMethods.GetCursorPos(out pt);
            var hmon = NativeMethods.MonitorFromPoint(pt, 2);
            var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO)) };
            NativeMethods.GetMonitorInfo(hmon, ref mi);
            int wl = mi.rcWork.Left, wt = mi.rcWork.Top, wr = mi.rcWork.Right, wb = mi.rcWork.Bottom;
            if (x + Width > wr) x = wr - Width - 4;
            if (y + Height > wb) y = wb - Height - 4;
            if (x < wl) x = wl + 4;
            if (y < wt) y = wt + 4;
        }

        private void OnRowEnter(RowLabel row)
        {
            row.BackColor = LevelHoverColor(_level);
            if (_rowDelBtn != null)
            {
                _rowDelBtn.Tag = row.Full;
                _rowDelBtn.Location = new Point(row.Width - 20, row.Top + (row.Height - 18) / 2);
                _rowDelBtn.Visible = true;
                _rowDelBtn.BringToFront();
            }
            if (_rowCopyBtn != null)
            {
                _rowCopyBtn.Tag = row.Full;
                _rowCopyBtn.Location = new Point(row.Width - 60, row.Top + (row.Height - 18) / 2);
                _rowCopyBtn.Visible = true;
                _rowCopyBtn.BringToFront();
            }
            if (_rowPinBtn != null)
            {
                _rowPinBtn.Tag = row.Full;
                _rowPinBtn.Location = new Point(row.Width - 40, row.Top + (row.Height - 18) / 2);
                _rowPinBtn.Visible = true;
                _rowPinBtn.BringToFront();
            }
            NativeMethods.POINT _ept; NativeMethods.GetCursorPos(out _ept);
            bool canPv = !row.IsDir && Config.PreviewFiles && PreviewProvider.CanPreview(row.Full);
            Program.DebugLog("[row] enter level=" + _level + " lang=" + I18n.Lang + " " + (row.IsDir ? "DIR " : "FILE ") + row.Name + " mouse=" + _ept.X + "," + _ept.Y + " previewFiles=" + Config.PreviewFiles + " canPreview=" + canPv);
            if (row.IsDir) ScheduleHover(row.Full, row.PointToScreen(Point.Empty).Y);
            else if (canPv) SchedulePreview(row);
        }

        private void OnRowLeave(RowLabel row)
        {
            bool onDel = false;
            if (_rowDelBtn != null && _rowDelBtn.Visible)
            {
                NativeMethods.POINT _dpt; NativeMethods.GetCursorPos(out _dpt);
                var dr = _rowDelBtn.RectangleToScreen(_rowDelBtn.ClientRectangle);
                onDel = _dpt.X >= dr.Left && _dpt.X <= dr.Right && _dpt.Y >= dr.Top && _dpt.Y <= dr.Bottom;
                if (!onDel) _rowDelBtn.Visible = false;
            }
            bool onCopy = false;
            if (_rowCopyBtn != null && _rowCopyBtn.Visible)
            {
                NativeMethods.POINT _cpt; NativeMethods.GetCursorPos(out _cpt);
                var cr = _rowCopyBtn.RectangleToScreen(_rowCopyBtn.ClientRectangle);
                onCopy = _cpt.X >= cr.Left && _cpt.X <= cr.Right && _cpt.Y >= cr.Top && _cpt.Y <= cr.Bottom;
                if (!onCopy) _rowCopyBtn.Visible = false;
            }
            bool onPin = false;
            if (_rowPinBtn != null && _rowPinBtn.Visible)
            {
                NativeMethods.POINT _ppt; NativeMethods.GetCursorPos(out _ppt);
                var pr = _rowPinBtn.RectangleToScreen(_rowPinBtn.ClientRectangle);
                onPin = _ppt.X >= pr.Left && _ppt.X <= pr.Right && _ppt.Y >= pr.Top && _ppt.Y <= pr.Bottom;
                if (!onPin) _rowPinBtn.Visible = false;
            }
            if (row == _activeDirRow) row.BackColor = LevelHoverColor(_level);
            else if (row == _contextRow) row.BackColor = LevelHoverColor(_level);  // 右键期间保持悬停高亮不被 OnRowLeave 擦除
            else row.BackColor = row.Selected ? Config.BgSel : LevelBgColor(_level);
            NativeMethods.POINT _lpt; NativeMethods.GetCursorPos(out _lpt);
            Program.DebugLog("[row] leave level=" + _level + " lang=" + I18n.Lang + " " + (row.IsDir ? "DIR " : "FILE ") + row.Name + " mouse=" + _lpt.X + "," + _lpt.Y + " onDel=" + onDel);
            if (onDel || onPin || onCopy) return;
            if (row == _contextRow) return;  // 右键模态期间不取消悬停状态、不关子面板/预览
            if (row.IsDir)
            {
                CancelHover();
                if (_onScheduleCloseAfter != null) _onScheduleCloseAfter(_level);
                if (row != _activeDirRow) ApplySel(row);
            }
            else
            {
                ApplySel(row);
            }
            SchedulePreviewClose();
        }

        public void SetActiveDir(string path)
        {
            if (_activeDirRow != null && _activeDirRow.Full != path) ApplySel(_activeDirRow);
            _activeDirRow = _rows.FirstOrDefault(r => r.Full == path);
            if (_activeDirRow != null) _activeDirRow.BackColor = LevelHoverColor(_level);
        }

        public void ClearActiveDir()
        {
            if (_activeDirRow != null) { ApplySel(_activeDirRow); _activeDirRow = null; }
        }

        // 滚轮切换图片时高亮列表中当前预览的文件
        public void HighlightPath(string path)
        {
            if (_hlRow != null && _hlRow.Full != path) { ApplySel(_hlRow); _hlRow = null; }
            if (string.IsNullOrEmpty(path)) return;
            var row = _rows.FirstOrDefault(r => string.Equals(r.Full, path, StringComparison.OrdinalIgnoreCase));
            if (row == null || row == _hlRow) return;
            row.BackColor = LevelHoverColor(_level);
            _hlRow = row;
        }

        private void ApplySel(RowLabel row)
        {
            if (row.Selected) { row.BackColor = Config.BgSel; row.ForeColor = Color.White; }
            else { row.BackColor = LevelBgColor(_level); row.ForeColor = row.OrigFg; }
        }

        private int RowIndex(RowLabel row) { return _rows.IndexOf(row); }

        private void ToggleSel(RowLabel row)
        {
            row.Selected = !row.Selected;
            ApplySel(row);
            _lastClickIdx = RowIndex(row);
        }

        private void RangeSel(RowLabel row)
        {
            int cur = RowIndex(row);
            if (cur < 0) return;
            int start = _lastClickIdx.HasValue ? _lastClickIdx.Value : 0;
            int lo = Math.Min(start, cur), hi = Math.Max(start, cur);
            for (int i = 0; i < _rows.Count; i++)
            {
                _rows[i].Selected = lo <= i && i <= hi;
                ApplySel(_rows[i]);
            }
            _lastClickIdx = cur;
        }

        private void ClearSel()
        {
            foreach (var r in _rows) if (r.Selected) { r.Selected = false; ApplySel(r); }
        }

        private List<RowLabel> SelRows() { return _rows.Where(r => r.Selected).ToList(); }

        private void ScheduleHover(string path, int rowScreenY)
        {
            CancelHover();
            _hoverPath = path;
            _hoverRowY = rowScreenY;
            _hoverTimer = new Timer { Interval = Config.SubHoverDelay };
            _hoverTimer.Tick += (s, e) => { _hoverTimer.Stop(); _hoverTimer.Dispose(); _hoverTimer = null; Program.DebugLog("[panel] 悬停计时触发 -> 请求打开子面板 level=" + _level + " path=" + _hoverPath); if (_hoverPath != null && _onOpenSub != null) _onOpenSub(this, _level, _hoverPath, _hoverRowY); };
            _hoverTimer.Start();
        }

        private void CancelHover()
        {
            if (_hoverTimer != null) { _hoverTimer.Stop(); _hoverTimer.Dispose(); _hoverTimer = null; }
            _hoverPath = null;
        }


        private void SchedulePreview(RowLabel row)
        {
            if (_previewForm != null && _previewPath == row.Full) { Program.DebugLog("[panel-preview] keep-existing level=" + _level + " " + row.Full); return; }
            Program.DebugLog("[panel-preview] schedule level=" + _level + " lang=" + I18n.Lang + " " + row.Full + " delay=" + Config.HoverDelayMs + " (hadTimer=" + (_previewTimer != null) + " hadForm=" + (_previewForm != null) + ")");
            CancelPreview();
            _previewPath = row.Full;
            _previewTimer = new Timer { Interval = Config.HoverDelayMs };
            _previewTimer.Tick += (s, e) => { _previewTimer.Stop(); _previewTimer.Dispose(); _previewTimer = null; Program.DebugLog("[panel-preview] timer-fired level=" + _level + " " + row.Full); ShowPreview(row); };
            _previewTimer.Start();
        }

        private void CancelPreview()
        {
            if (_previewTimer != null || _previewForm != null) Program.DebugLog("[panel-preview] cancel level=" + _level + " hadTimer=" + (_previewTimer != null) + " hadForm=" + (_previewForm != null));
            if (_previewTimer != null) { _previewTimer.Stop(); _previewTimer.Dispose(); _previewTimer = null; }
            _previewPath = null;
            CancelPreviewClose();
            ClosePreviewForm();
        }

        public void ClosePreviewIfAny() { CancelPreview(); }

        private void SchedulePreviewClose()
        {
            if (_previewForm == null)
            {
                Program.DebugLog("[panel-preview] schedule-close level=" + _level + " no form -> cleanup timer");
                if (_previewTimer != null) { _previewTimer.Stop(); _previewTimer.Dispose(); _previewTimer = null; }
                _previewPath = null;
                return;
            }
            Program.DebugLog("[panel-preview] schedule-close level=" + _level + " form at " + _previewForm.Left + "," + _previewForm.Top + " " + _previewForm.Width + "x" + _previewForm.Height + " pinned=" + _previewForm.Pinned);
            CancelPreviewClose();
            _previewCloseTimer = new Timer { Interval = 120 };
            _previewCloseTimer.Tick += (s, e) =>
            {
                _previewCloseTimer.Stop(); _previewCloseTimer.Dispose(); _previewCloseTimer = null;
                NativeMethods.POINT pt;
                NativeMethods.GetCursorPos(out pt);
                bool inPv = _previewForm != null && _previewForm.ContainsPoint(pt.X, pt.Y);
                bool pinned = _previewForm != null && _previewForm.Pinned;
                Program.DebugLog("[panel-preview] close-check level=" + _level + " mouse=" + pt.X + "," + pt.Y + " inPreview=" + inPv + " pinned=" + pinned + " formAlive=" + (_previewForm != null));
                if (pinned) return;
                if (_previewForm != null && inPv)
                    SchedulePreviewClose();
                else
                    ClosePreviewForm();
            };
            _previewCloseTimer.Start();
        }

        private void CancelPreviewClose()
        {
            if (_previewCloseTimer != null) { Program.DebugLog("[panel-preview] cancel-close level=" + _level); _previewCloseTimer.Stop(); _previewCloseTimer.Dispose(); _previewCloseTimer = null; }
        }

        private void ClosePreviewForm()
        {
            if (_previewForm == null) return;
            if (_previewForm.Pinned) { Program.DebugLog("[panel-preview] CLOSE detach (pinned) level=" + _level); _previewForm = null; return; }
            Program.DebugLog("[panel-preview] CLOSE level=" + _level + " form was at " + _previewForm.Left + "," + _previewForm.Top + " " + _previewForm.Width + "x" + _previewForm.Height); try { _previewForm.Close(); _previewForm.Dispose(); } catch { } _previewForm = null;
        }

        private void ShowPreview(RowLabel row)
        {
            string full = _previewPath;
            if (full == null || full != row.Full) { Program.DebugLog("[panel-preview] show-abort level=" + _level + " full=" + full + " row.Full=" + row.Full + " (path mismatch)"); return; }
            Program.DebugLog("[panel-preview] 开始生成 level=" + _level + " lang=" + I18n.Lang + " " + full);
            Task.Run(() =>
            {
                try
                {
                    var result = PreviewProvider.Generate(full);
                    if (result == null) { Program.DebugLog("[panel-preview] generate returned null level=" + _level + " " + full); return; }
                    BeginInvoke((MethodInvoker)delegate()
                    {
                        if (full != _previewPath) { Program.DebugLog("[panel-preview] show-stale level=" + _level + " full=" + full + " curPath=" + _previewPath + " (discarded)"); if (result.Image != null) result.Image.Dispose(); return; }
                        ClosePreviewForm();
                        if (result.Kind == PreviewResult.KindEnum.Image)
                        {
                            if (result.IsGif)
                                _previewForm = new PreviewForm(result.Image, result.DisplayW, result.DisplayH);
                            else
                                _previewForm = new PreviewForm(result.Image);
                        }
                        else if (result.Kind == PreviewResult.KindEnum.Media)
                        {
                            var ctl = MediaPreview.MediaPlayer(full, result.IsVideo, result.DisplayW, result.DisplayH);
                            _previewForm = new PreviewForm(ctl, ctl.Width, ctl.Height, result.IsVideo ? "video" : "audio");
                        }
                        else if (result.Kind == PreviewResult.KindEnum.SvgHtml)
                        {
                            var ctl = MediaPreview.SvgBrowserPanel(full);
                            _previewForm = new PreviewForm(ctl, ctl.Width, ctl.Height, "svg");
                        }
                        else
                        {
                            _previewForm = new PreviewForm(result.Text, result.Size);
                        }
                        PositionPreview(row);
                        _previewForm.FilePath = full;
                        ApplyHeader(_previewForm, full);
                        _previewForm.OnFileSwitched = p => HighlightPath(p);

                        _previewForm.Show();
                        // 同上：面板内预览也要在 Show 后立即置顶
                        NativeMethods.SetWindowPos(_previewForm.Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE);
                        NativeMethods.POINT _spt; NativeMethods.GetCursorPos(out _spt);
                        Program.DebugLog("[panel-preview] SHOW level=" + _level + " " + full + " at " + _previewForm.Left + "," + _previewForm.Top + " size " + _previewForm.Width + "x" + _previewForm.Height + " mouse=" + _spt.X + "," + _spt.Y);
                    });
                }
                catch (Exception ex) { Program.DebugLog("[panel-preview] generate-exception level=" + _level + " " + full + " " + ex.Message); }
            });
        }


        private void BindPreviewEvents()
        {

        }

        public bool PreviewContainsPoint(int x, int y)
        {
            return _previewForm != null && _previewForm.ContainsPoint(x, y);
        }

        // ===== 悬停文件直接预览（桌面/资源管理器，无面板打开时） =====
        private static Timer _extPreviewTimer;
        private static string _extPreviewPath;
        private static PreviewForm _extPreviewForm;
        private static Timer _extPreviewCloseTimer;
        private static int _extRetryCount;

        public static void ScheduleFilePreview(string full, Point mouse)
        {

            CancelFilePreview();
            if (!Config.PreviewFiles) return;
            if (!PreviewProvider.CanPreview(full)) return;
            _extRetryCount = 0;
            _extPreviewPath = full;
            Program.DebugLog("[preview] schedule " + full);
            _extPreviewTimer = new Timer { Interval = Config.HoverDelayMs };
            _extPreviewTimer.Tick += delegate(object s, EventArgs e)
            {
                ((Timer)s).Stop(); ((Timer)s).Dispose(); _extPreviewTimer = null;
                LoadFilePreview(_extPreviewPath, mouse);
            };
            _extPreviewTimer.Start();
        }

        public static void CancelFilePreview()
        {
            if (_extPreviewTimer != null) { _extPreviewTimer.Stop(); _extPreviewTimer.Dispose(); _extPreviewTimer = null; }
            _extPreviewPath = null;
            if (_extPreviewCloseTimer != null) { _extPreviewCloseTimer.Stop(); _extPreviewCloseTimer.Dispose(); _extPreviewCloseTimer = null; }
            if (_extPreviewForm != null && !_extPreviewForm.Pinned) { Program.DebugLog("[preview] cancel-existing-form"); CloseExtPreviewForm(); }
        }

        public static bool FilePreviewContainsPoint(int x, int y)
        {
            return _extPreviewForm != null && _extPreviewForm.ContainsPoint(x, y);
        }

        // 预览显示期间保持 TOPMOST（不激活）：防止被前台窗口盖住。
        // 仅在被外部进程窗口压住时才提顶，避免与其他叠加窗口每 Tick 互相翻转 z 序（闪烁）
        public static void EnsureExtPreviewTopmost()
        {
            if (_extPreviewForm == null || !_extPreviewForm.IsHandleCreated || !_extPreviewForm.Visible) return;
            EnsureWinTopmost(_extPreviewForm.Handle, "extpreview[" + _extPreviewForm.MediaKind + "] " + _extPreviewForm.FilePath);
        }

        public static void ScheduleExtPreviewClose(int mx, int my)
        {
            if (_extPreviewForm == null) { Program.DebugLog("[preview] schedule-close but no form"); CancelFilePreview(); return; }
            if (_extPreviewCloseTimer != null) return;
            Program.DebugLog("[preview] schedule-close");
            _extPreviewCloseTimer = new Timer { Interval = 120 };
            _extPreviewCloseTimer.Tick += delegate(object s, EventArgs e)
            {
                ((Timer)s).Stop(); ((Timer)s).Dispose(); _extPreviewCloseTimer = null;
                NativeMethods.POINT pt;
                NativeMethods.GetCursorPos(out pt);
                if (_extPreviewForm != null && !_extPreviewForm.Pinned && _extPreviewForm.ContainsPoint(pt.X, pt.Y))
                    ScheduleExtPreviewClose(pt.X, pt.Y);
                else if (_extPreviewForm != null && !_extPreviewForm.Pinned) CloseExtPreviewForm();
            };
            _extPreviewCloseTimer.Start();
        }

        private static void CloseExtPreviewForm()
        {
            if (_extPreviewForm == null) return;
            if (_extPreviewForm.Pinned) { Program.DebugLog("[preview] CLOSE detach (pinned)"); _extPreviewForm = null; return; }
            Program.DebugLog("[preview] CLOSE form"); try { _extPreviewForm.Close(); _extPreviewForm.Dispose(); } catch { } _extPreviewForm = null;
        }


        private static void ApplyHeader(PreviewForm form, string full)
        {
            string name = Path.GetFileName(full);
            string ext = Path.GetExtension(full).TrimStart('.').ToUpperInvariant();
            long size = 0;
            try { size = new FileInfo(full).Length; } catch { }
            string meta = ext + " · " + FormatSize(size);
            Color iconColor; string iconText;
            // lnk 头部显示指向目标：一眼看出快捷方式指向什么
            if (string.Equals(Path.GetExtension(full), ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                string t = PreviewProvider.GetLnkTarget(full);
                if (!string.IsNullOrEmpty(t))
                    meta = "LNK → " + (File.Exists(t) || Directory.Exists(t) ? Path.GetFileName(t) : t);
                else
                    meta = "LNK · " + FormatSize(size);
                iconColor = ColorTranslator.FromHtml("#14b8a6");
                iconText = "↗";
                form.SetHeader(name, meta, iconText, iconColor);
                return;
            }
            switch (ext)
            {
                case "MP3": case "WAV": case "WMA": case "M4A": iconColor = ColorTranslator.FromHtml("#e5567a"); iconText = "♪"; break;
                case "MP4": case "MKV": case "AVI": case "MOV": case "WMV": iconColor = ColorTranslator.FromHtml("#9b5de5"); iconText = "▶"; break;
                case "SVG": iconColor = ColorTranslator.FromHtml("#3b8eea"); iconText = "S"; break;
                case "PNG": case "JPG": case "JPEG": case "GIF": case "BMP": iconColor = ColorTranslator.FromHtml("#3b8eea"); iconText = "▣"; break;
                case "ZIP": case "RAR": case "7Z": iconColor = ColorTranslator.FromHtml("#c98a2b"); iconText = "▤"; break;
                case "JSON": iconColor = ColorTranslator.FromHtml("#d9a21b"); iconText = "{}"; break;
                case "CSS": iconColor = ColorTranslator.FromHtml("#2fa66a"); iconText = "#"; break;
                case "MD": iconColor = ColorTranslator.FromHtml("#4a6cf7"); iconText = "M"; break;
                default: iconColor = Config.PlaceholderFg; iconText = ext.Length > 3 ? ext.Substring(0, 3) : ext; break;
            }
            form.SetHeader(name, meta, iconText, iconColor);
        }

        private static void LoadFilePreview(string full, Point mouse)
        {
            if (full == null || _extPreviewPath != full) return;
            Program.DebugLog("[preview] timer-fired, generate start " + full);
            var ctx = System.Threading.SynchronizationContext.Current;
            Task.Run(delegate
            {
                try
                {
                    var result = PreviewProvider.Generate(full);
                    if (result == null) return;
                    ctx.Post(delegate
                    {
                        if (full != _extPreviewPath) { if (result.Image != null) result.Image.Dispose(); return; }
                        CloseExtPreviewForm();
                        if (result.Kind == PreviewResult.KindEnum.Media)
                        {
                            var ctl = MediaPreview.MediaPlayer(full, result.IsVideo, result.DisplayW, result.DisplayH);
                            _extPreviewForm = new PreviewForm(ctl, ctl.Width, ctl.Height, result.IsVideo ? "video" : "audio");
                        }
                        else if (result.Kind == PreviewResult.KindEnum.SvgHtml)
                        {
                            var ctl = MediaPreview.SvgBrowserPanel(full);
                            _extPreviewForm = new PreviewForm(ctl, ctl.Width, ctl.Height, "svg");
                        }
                        else if (result.Kind == PreviewResult.KindEnum.Image)
                        {
                            if (result.IsGif)
                                _extPreviewForm = new PreviewForm(result.Image, result.DisplayW, result.DisplayH);
                            else
                                _extPreviewForm = new PreviewForm(result.Image);
                        }
                        else
                        {
                            _extPreviewForm = new PreviewForm(result.Text, result.Size);
                        }
                        PositionExtPreview(mouse);
                        _extPreviewForm.FilePath = full;
                        ApplyHeader(_extPreviewForm, full);

                        _extPreviewForm.Show();
                        // 刚创建的窗口最容易被刚激活的资源管理器压住，Show 后立即补一次置顶
                        NativeMethods.SetWindowPos(_extPreviewForm.Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE);
                        Program.DebugLog("[preview] SHOW " + full + " at " + _extPreviewForm.Left + "," + _extPreviewForm.Top + " size " + _extPreviewForm.Width + "x" + _extPreviewForm.Height);
                    }, null);
                }
                catch
                {
                    // 失败自动重试（上限 3 次），避免一次性 IO 冲突导致预览永久消失
                    ctx.Post(delegate
                    {
                        if (_extPreviewPath == null || _extRetryCount >= 3 || !File.Exists(full)) return;
                        _extRetryCount++;
                        Program.DebugLog("[preview] generate-failed retry " + _extRetryCount);
                        var rt = new Timer { Interval = 400 };
                        rt.Tick += delegate(object s2, EventArgs e2)
                        {
                            ((Timer)s2).Stop(); ((Timer)s2).Dispose();
                            if (_extPreviewPath != null) LoadFilePreview(_extPreviewPath, mouse);
                        };
                        rt.Start();
                    }, null);
                }
            });
        }


        private static void PositionExtPreview(Point mouse)
        {
            if (_extPreviewForm == null) return;
            int x = mouse.X + 20, y = mouse.Y + 2;
            int pw = _extPreviewForm.Width, ph = _extPreviewForm.Height;
            var hpt = new NativeMethods.POINT { X = mouse.X, Y = mouse.Y };
            var hmon = NativeMethods.MonitorFromPoint(hpt, 2);
            var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO)) };
            NativeMethods.GetMonitorInfo(hmon, ref mi);
            int wl = mi.rcWork.Left, wt = mi.rcWork.Top, wr = mi.rcWork.Right, wb = mi.rcWork.Bottom;
            // 记住的尺寸/原图尺寸可能大于当前显示器，先钳到工作区内
            int maxW = wr - wl - 16, maxH = wb - wt - 16;
            if (maxW < 120) maxW = 120;
            if (maxH < 90) maxH = 90;
            if (pw > maxW) { pw = maxW; _extPreviewForm.ClientSize = new Size(pw, _extPreviewForm.ClientSize.Height); }
            if (ph > maxH) { ph = maxH; _extPreviewForm.ClientSize = new Size(_extPreviewForm.ClientSize.Width, ph); }
            if (x + pw > wr)
            {
                x = mouse.X - pw - 20;
                // 左侧放不下或会覆盖鼠标点：缩小宽度确保 x+pw <= mouse.X-4
                if (x < wl || x + pw > mouse.X - 4)
                {
                    int maxPw = mouse.X - wl - 24;
                    if (maxPw < 120) maxPw = 120;
                    if (pw > maxPw) { pw = maxPw; _extPreviewForm.ClientSize = new Size(pw, _extPreviewForm.ClientSize.Height); }
                    x = mouse.X - pw - 20;
                    if (x < wl) x = wl + 4;
                }
            }
            if (y + ph > wb) y = wb - ph - 4;
            if (y < wt) y = wt + 4;
            _extPreviewForm.Location = new Point(x, y);
        }

        private void PositionPreview(RowLabel row)
        {
            if (_previewForm == null) return;
            // 用面板自身边界定位：列表出现滚动条时行宽比面板窄（扣掉了滚动条宽度），
            // 按行右边界算会让预览窗压进面板里几个像素
            int lx = Left, rx = Right;
            int x = rx + 8, y = row.PointToScreen(Point.Empty).Y;
            int pw = _previewForm.Width, ph = _previewForm.Height;
            NativeMethods.POINT pt;
            NativeMethods.GetCursorPos(out pt);
            var hmon = NativeMethods.MonitorFromPoint(pt, 2);
            var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO)) };
            NativeMethods.GetMonitorInfo(hmon, ref mi);
            int wl = mi.rcWork.Left, wt = mi.rcWork.Top, wr = mi.rcWork.Right, wb = mi.rcWork.Bottom;
            // 记住的尺寸/原图尺寸可能大于当前显示器，先钳到工作区内
            int maxW = wr - wl - 16, maxH = wb - wt - 16;
            if (maxW < 120) maxW = 120;
            if (maxH < 90) maxH = 90;
            if (pw > maxW) { pw = maxW; _previewForm.ClientSize = new Size(pw, _previewForm.ClientSize.Height); }
            if (ph > maxH) { ph = maxH; _previewForm.ClientSize = new Size(_previewForm.ClientSize.Width, ph); }
            if (x + pw > wr)
            {
                x = lx - pw - 8;
                // 左侧放不下或会覆盖面板：缩小宽度确保 x+pw <= lx-4（不覆盖当前行）
                if (x < wl || x + pw > lx - 4)
                {
                    int maxPw = lx - wl - 12;
                    if (maxPw < 120) maxPw = 120;
                    if (pw > maxPw) { pw = maxPw; _previewForm.ClientSize = new Size(pw, _previewForm.ClientSize.Height); }
                    x = lx - pw - 8;
                    if (x < wl) x = wl + 4;
                }
            }
            if (y + ph > wb) y = wb - ph - 4;
            if (y < wt) y = wt + 4;
            _previewForm.Location = new Point(x, y);
            Program.DebugLog("[panel-preview] position level=" + _level + " panelL=" + lx + " panelR=" + rx + " rowY=" + row.PointToScreen(Point.Empty).Y + " pvSize=" + pw + "x" + ph + " -> pvAt=" + x + "," + y + " workArea=" + wl + "," + wt + "-" + wr + "," + wb + " mouse=" + pt.X + "," + pt.Y);
        }

        private void OnRowDown(RowLabel row, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragStart = e.Location;
                _dragRow = row;
                _dragged = false;
            }
        }

        private void OnRowMove(RowLabel row, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && _dragRow != null && !_dragged)
            {
                if (Math.Abs(e.X - _dragStart.X) > 3 || Math.Abs(e.Y - _dragStart.Y) > 3)
                {
                    var sel = SelRows();
                    string[] paths;
                    if (row.Selected && sel.Count > 1) paths = sel.Select(r => r.Full).ToArray();
                    else paths = new string[] { row.Full };
                    _dragged = true;
                    _dragging = true;
                    row.DoDragDrop(new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Copy | DragDropEffects.Move);
                    _dragging = false;
                    _dragRow = null;
                }
            }
        }

        private void OnRowUp(RowLabel row, MouseEventArgs e)
        {
            if (_dragged) { _dragged = false; return; }
            if (e.Button != MouseButtons.Left) return;
            if ((Control.ModifierKeys & Keys.Control) == Keys.Control) { ToggleSel(row); return; }
            if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift) { RangeSel(row); return; }
            ClearSel();
            OpenPath(row.Full);
        }

        private void OnDrop(RowLabel row, DragEventArgs e)
        {
            string dstDir = row.Full;
            if (!Directory.Exists(dstDir)) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            bool move = e.Effect == DragDropEffects.Move;
            Program.DebugLog("[drop-row] effect=" + e.Effect + " keyState=" + e.KeyState + " move=" + move + " dstDir=" + dstDir + " files=" + (files == null ? "null" : string.Join(";", files)));
            foreach (var src in files)
            {
                if (!string.IsNullOrEmpty(src) && (File.Exists(src) || Directory.Exists(src)) && src != dstDir)
                {
                    if (move) DoMove(src, dstDir);
                    else DoCopy(src, dstDir);
                }
            }
            ApplySel(row);
        }

        private void DoCopy(string src, string dstDir)
        {
            Task.Run(() =>
            {
                try
                {
                    string dst = UniqueDest(src, dstDir);
                    if (dst == src) { Program.DebugLog("[copy-skip] same path " + src); return; }
                    if (Directory.Exists(src)) CopyDir(src, dst);
                    else File.Copy(src, dst);
                    Program.DebugLog("[copy-ok] " + src + " -> " + dst);
                    BeginInvoke((MethodInvoker)delegate() { OnMoveDone(); Program.RefreshPanelsShowing(null, dstDir); });
                }
                catch (Exception ex) { Program.DebugLog("[copy-fail] " + src + " -> " + dstDir + " : " + ex.Message); }
            });
        }

        private static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
            foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        // 拖拽反馈光标：复制=箭头+加号，移动=纯箭头
        private static Cursor _dropCopyCursor, _dropMoveCursor;
        private static Cursor DropCopyCursor { get { if (_dropCopyCursor == null) _dropCopyCursor = MakeDropCursor(true); return _dropCopyCursor; } }
        private static Cursor DropMoveCursor { get { if (_dropMoveCursor == null) _dropMoveCursor = MakeDropCursor(false); return _dropMoveCursor; } }

        private static Cursor MakeDropCursor(bool copy)
        {
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                Cursors.Arrow.Draw(g, new Rectangle(0, 0, 32, 32));
                if (copy)
                {
                    g.FillRectangle(Brushes.White, 19, 19, 12, 12);
                    using (var pen = new Pen(Color.Black, 2))
                    {
                        g.DrawRectangle(pen, 19, 19, 11, 11);
                        g.DrawLine(pen, 25, 22, 25, 28);
                        g.DrawLine(pen, 22, 25, 28, 25);
                    }
                }
            }
            IntPtr mask = bmp.GetHbitmap();
            IntPtr color = bmp.GetHbitmap();
            var ii = new NativeMethods.ICONINFO { fIcon = false, xHotspot = 0, yHotspot = 0, hbmMask = mask, hbmColor = color };
            IntPtr hCur = NativeMethods.CreateIconIndirect(ref ii);
            NativeMethods.DeleteObject(mask);
            NativeMethods.DeleteObject(color);
            bmp.Dispose();
            return new Cursor(hCur);
        }

        private void DoMove(string src, string dstDir)
        {
            Task.Run(() =>
            {
                try
                {
                    string dst = UniqueDest(src, dstDir);
                    if (dst == src) { Program.DebugLog("[move-skip] same path " + src); return; }
                    if (Directory.Exists(src)) Directory.Move(src, dst);
                    else File.Move(src, dst);
                    Program.DebugLog("[move-ok] " + src + " -> " + dst);
                    BeginInvoke((MethodInvoker)delegate() { OnMoveDone(); Program.RefreshPanelsShowing(Path.GetDirectoryName(src), dstDir); });
                }
                catch (Exception ex) { Program.DebugLog("[move-fail] " + src + " -> " + dstDir + " : " + ex.Message); }
            });
        }

        private void OnMoveDone()
        {
            try { System.Media.SystemSounds.Beep.Play(); RefreshPanel(); } catch { }
        }

        private static string UniqueDest(string src, string dstDir)
        {
            string name = Path.GetFileName(src);
            string dst = Path.Combine(dstDir, name);
            if (!File.Exists(dst) && !Directory.Exists(dst)) return dst;
            string stem = Path.GetFileNameWithoutExtension(name);
            string ext = Path.GetExtension(name);
            int i = 1;
            while (true)
            {
                string cand = Path.Combine(dstDir, stem + "_" + i + ext);
                if (!File.Exists(cand) && !Directory.Exists(cand)) return cand;
                i++;
            }
        }

        public void RefreshPanel()
        {
            if (!IsHandleCreated || _path == null) return;
            BackColor = LevelBgColor(_level);
            Win11.Apply(Handle, Config.Theme != "light", Config.Border);
            int x = Location.X, y = Location.Y;
            ShowPanel(_path, x, y);
        }

        // 当前面板是否正在显示指定目录
        public bool ShowingPath(string dir)
        {
            return dir != null && _path != null && string.Equals(_path, dir, StringComparison.OrdinalIgnoreCase);
        }

        private static ToolStripMenuItem MI(string text, Action handler)
        {
            return new ToolStripMenuItem(text, null, (EventHandler)((s, e) => handler()));
        }

        private static void EditFile(string path)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(path) { Verb = "edit", UseShellExecute = true };
                System.Diagnostics.Process.Start(psi);
            }
            catch
            {
                try { System.Diagnostics.Process.Start("notepad.exe", "\"" + path + "\""); }
                catch (Exception ex) { Program.DebugLog("[edit] fail " + path + " " + ex.Message); }
            }
        }

        private void PopupMenu(RowLabel row, MouseEventArgs e)
        {
            // 右键高亮与悬停一致：直接用 LevelHoverColor，不占 Selected 状态（不影响 Ctrl/Shift 多选）
            if (_contextRow != null && _contextRow != row) ApplySel(_contextRow);
            _contextRow = row;
            row.BackColor = LevelHoverColor(_level);
            ShellContextMenu.Show(row.Full, Handle, null);
            // Shell 原生菜单是模态的，返回后恢复行底色
            if (_contextRow == row) { _contextRow = null; ApplySel(row); }
        }

        private void Rename(RowLabel row, MouseEventArgs e)
        {
            string oldName = Path.GetFileName(row.Full);
            int sx = row.PointToScreen(e.Location).X;
            int sy = row.PointToScreen(e.Location).Y;
            ModalOpen = true;
            try
            {
                using (var dlg = new RenameDialog(oldName, sx, sy))
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dlg.NewName) && dlg.NewName != oldName)
                    {
                        // 复用统一入口：校验非法字符/重名、同步固定项路径、刷新相关面板
                        if (RenamePath(row.Full, dlg.NewName))
                        {
                            if (row.IsDir) Program.RebasePaths(row.Full, Path.Combine(Path.GetDirectoryName(row.Full), dlg.NewName));
                            OnMoveDone();
                        }
                    }
                }
            }
            finally { ModalOpen = false; }
        }

        private void RecycleRows(List<RowLabel> rows)
        {
            var paths = rows.Where(r => File.Exists(r.Full) || Directory.Exists(r.Full)).Select(r => r.Full).ToList();
            if (paths.Count == 0) return;
            // 模态期间主循环暂停自动关闭（否则确认框会随面板被连带销毁）
            ModalOpen = true;
            bool yes;
            try
            {
                yes = MessageBox.Show(this, I18n.T("delete_confirm", paths.Count), I18n.T("delete_title"), MessageBoxButtons.YesNo) == DialogResult.Yes;
            }
            finally { ModalOpen = false; }
            if (!yes) return;
            ClearSel();
            if (RecyclePaths(paths.ToArray())) OnMoveDone();
            else Program.Log("[删除失败]");
        }

        private static bool RecyclePaths(string[] paths)
        {
            try
            {
                var op = new NativeMethods.SHFILEOPSTRUCTW();
                op.hwnd = IntPtr.Zero;
                op.wFunc = NativeMethods.FO_DELETE;
                op.pFrom = string.Join("\0", paths) + "\0";
                op.pTo = null;
                op.fFlags = NativeMethods.FOF_ALLOWUNDO | NativeMethods.FOF_NOCONFIRMATION | NativeMethods.FOF_SILENT | NativeMethods.FOF_NOERRORUI;
                return NativeMethods.SHFileOperationW(ref op) == 0;
            }
            catch { return false; }
        }

        private void CopyText(string text) { try { Clipboard.SetText(text); } catch { } }

        private void ShowToast(string msg)
        {
            var t = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                BackColor = Config.BG,
                ShowInTaskbar = false,
                TopMost = true,
                Size = new Size(60, 22),
            };
            var lbl = new Label { Text = msg, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Config.FG, BackColor = Config.BG, Font = new Font("Microsoft YaHei", 9) };
            t.Controls.Add(lbl);
            t.Location = new Point(Left + (Width - t.Width) / 2, Top + 34);
            t.Show();
            var tm = new Timer { Interval = 800 };
            tm.Tick += (s, e) => { tm.Stop(); tm.Dispose(); t.Close(); t.Dispose(); };
            tm.Start();
        }

        // 复制文件本身到剪贴板（FileDrop 格式，可粘贴到资源管理器）
        private void CopyFileToClipboard(string full)
        {
            try
            {
                if (!File.Exists(full) && !Directory.Exists(full)) return;
                var files = new System.Collections.Specialized.StringCollection();
                files.Add(full);
                Clipboard.SetFileDropList(files);
                ShowToast(I18n.T("copied"));
            }
            catch { }
        }

        private static void OpenPath(string path) { try { Process.Start(path); } catch { } }

        private static void OpenInExplorer(string full)
        {
            try
            {
                if (Directory.Exists(full)) Process.Start(full);
                else Process.Start("explorer.exe", "/select,\"" + full + "\"");
            }
            catch (Exception ex) { Program.Log("[打开失败] " + ex.Message); }
        }

        public int RightEdge() { return Left + Width; }
        public int Level { get { return _level; } }
        public int GeometryY() { return Top; }
        public int HeadHeight() { return _level == 0 ? _headH : 0; }

        public bool ContainsPoint(int x, int y)
        {
            if (!IsHandleCreated || !Visible) return false;
            return Left <= x && x <= Left + Width && Top <= y && y <= Top + Height;
        }

        public bool MenuOpen { get { return _menuOpen; } set { _menuOpen = value; } }
        // 模态对话框（删除确认/重命名等）显示期间置位，主循环应暂停自动关闭
        public bool ModalOpen;
        public bool IsDragging { get { return _dragging; } }
        public string CurrentPath { get { return _path; } }

        public void ClosePanel()
        {
            Program.DebugLog("[panel] ClosePanel level=" + _level + " path=" + _path + " pinned=" + _pinned);
            if (_pinned) return;
            CancelHover();

            CancelPreview();
            if (IsHandleCreated) try { Close(); Dispose(); } catch { }
        }

        private void TogglePinPopup()
        {
            _pinned = !_pinned;
            if (_pinBtn != null) _pinBtn.Image = PreviewForm.PinIcon(_pinned);
            // 固定瞬间锁住当前尺寸，取消固定后回到内容自适应
            if (_pinned) { _pinnedW = ClientSize.Width; _pinnedH = ClientSize.Height; }
            else { _pinnedW = 0; _pinnedH = 0; }
        }

        // 统一保顶：先恢复丢失的 WS_EX_TOPMOST（Show/Focus 激活会清），再按需提顶。
        // 每 Tick 无条件 SetWindowPos(TOPMOST) 会让叠加窗口互相翻转 z 序致闪烁，故仅在必要时提。
        // 面板、面板内预览、外部预览共用此方法，避免任一窗口 TopMost 丢失后陷入死锁
        // （TopMost 丢了 → CoveredByOther 首个上方窗口非 TopMost → return false → 永不提顶 → 永被盖住）
        private static DateTime _lastRestoreLog;
        private static DateTime _lastRaiseLog;
        private static DateTime _lastRaiseTime;

        // 命中测试：直接看 h 的位置上最顶层是谁。
        // 覆盖 CoveredByOther 漏判的情形——压住者不是 TopMost（说明 h 的 TopMost 已被系统重排/丢失），
        // 或 z 序被外部工具改动。外部进程窗口才计数，本进程窗口之间的相对顺序保持不动，避免互相翻转闪烁。
        private static bool CoveredByHitTest(IntPtr h)
        {
            try
            {
                int ex = NativeMethods.GetWindowLongW(h, NativeMethods.GWL_EXSTYLE);
                // 鼠标穿透窗口不会出现在命中测试里，跳过以免误判
                if ((ex & 0x20) != 0) return false;
                NativeMethods.RECT rc;
                if (!NativeMethods.GetWindowRect(h, out rc)) return false;
                if (rc.Right - rc.Left < 8 || rc.Bottom - rc.Top < 8) return false;
                int[] xs = { (rc.Left + rc.Right) / 2, rc.Left + 3, rc.Right - 3 };
                int[] ys = { (rc.Top + rc.Bottom) / 2, rc.Top + 3, rc.Bottom - 3 };
                int total = 0, covered = 0;
                foreach (int x in xs)
                {
                    foreach (int y in ys)
                    {
                        total++;
                        IntPtr top = NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = x, Y = y });
                        if (top == IntPtr.Zero || top == h) continue;
                        bool mine = false;
                        IntPtr cur = top;
                        for (int i = 0; i < 8 && cur != IntPtr.Zero; i++)
                        {
                            if (cur == h) { mine = true; break; }
                            cur = NativeMethods.GetParent(cur);
                        }
                        if (mine) continue;
                        uint pid;
                        NativeMethods.GetWindowThreadProcessId(top, out pid);
                        if (pid == Program.CurrentPid) continue;
                        covered++;
                    }
                }
                return covered * 2 >= total;
            }
            catch { return false; }
        }

        public static void EnsureWinTopmost(IntPtr h, string tag)
        {
            if (h == IntPtr.Zero) return;
            int exStyle = 0;
            try { exStyle = NativeMethods.GetWindowLongW(h, NativeMethods.GWL_EXSTYLE); }
            catch { }
            // 补 WS_EX_TOPMOST 样式必须无条件执行（ContainsFocus 也不能跳过）：
            // Windows 在 Show/Activate 时会清掉 WS_EX_TOPMOST，若此时窗口恰有焦点，
            // 旧逻辑因 ContainsFocus 提前 return → 样式丢失无人补 → 窗口掉出 TopMost band
            // → 被资源管理器等普通窗口盖住。SetWindowPos(TOPMOST, SWP_NOACTIVATE) 不会
            // 激活窗口、不会打断输入、IME 候选窗由系统管理不受影响，是安全的。
            if ((exStyle & (int)NativeMethods.WS_EX_TOPMOST) == 0)
            {
                NativeMethods.SetWindowPos(h, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                    NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE);
                if ((DateTime.UtcNow - _lastRestoreLog).TotalMilliseconds > 500)
                {
                    _lastRestoreLog = DateTime.UtcNow;
                    Program.DebugLog("[z] lost-topmost " + tag + " ex=0x" + exStyle.ToString("X"));
                }
            }
            // 窗口内控件正在输入（搜索框/文本框）：跳过条件提顶（会触发重绘、可能盖 IME），
            // 但上面样式补回已执行，TopMost band 位置不会丢
            try
            {
                var f = Control.FromHandle(h) as Form;
                if (f != null && f.ContainsFocus) return;
            }
            catch { }
            bool byOther = Program.CoveredByOther(h);
            bool byHit = !byOther && CoveredByHitTest(h);
            if (byOther || byHit)
            {
                // 节流：与外部置顶窗口竞争时不要每 Tick 反复提顶导致闪烁
                if ((DateTime.UtcNow - _lastRaiseTime).TotalMilliseconds < 120) return;
                _lastRaiseTime = DateTime.UtcNow;
                NativeMethods.SetWindowPos(h, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                    NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE);
                if ((DateTime.UtcNow - _lastRaiseLog).TotalMilliseconds > 300)
                {
                    _lastRaiseLog = DateTime.UtcNow;
                    Program.DebugLog("[z] raise " + tag + (byHit ? " (hit-test)" : ""));
                }
            }
        }

        public void EnsureTopmost()
        {
            if (!IsHandleCreated || !Visible) return;
            EnsureWinTopmost(Handle, "panel L" + _level + " " + _path);
            // 预览必须始终在面板之上：无条件提顶。预览每面板唯一，不与其他预览竞争，
            // 不会引入多面板叠加闪烁；条件提顶时面板重绘/子面板 Show 后预览会沉到面板下
            if (_previewForm != null && _previewForm.IsHandleCreated && _previewForm.Visible)
                NativeMethods.SetWindowPos(_previewForm.Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                    NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE);
        }

        // 级联层级色条：红橙黄绿青蓝紫循环，区分叠叠的面板
        private static readonly Color[] _levelColors = {
            Color.FromArgb(0xe5, 0x48, 0x4d), Color.FromArgb(0xf0, 0x7f, 0x1c), Color.FromArgb(0xf2, 0xa9, 0x00),
            Color.FromArgb(0x2f, 0xae, 0x66), Color.FromArgb(0x14, 0xb8, 0xa6), Color.FromArgb(0x3b, 0x82, 0xf6),
            Color.FromArgb(0x8b, 0x5c, 0xf6),
        };
        private static Color LevelColor(int level) { return _levelColors[level % _levelColors.Length]; }

        // 整面板背景按 level 轻微着色：LevelColor 低透明叠加底色，level 越大越浅（父级深）
        private static Color LevelBgColor(int level)
        {
            Color c = LevelColor(level), bg = Config.BG;
            int a = Math.Max(8, 28 - level * 6);  // L0 28, L1 22, L2 16, L3 10, L4+ 8
            return Color.FromArgb((c.R * a + bg.R * (255 - a)) / 255, (c.G * a + bg.G * (255 - a)) / 255, (c.B * a + bg.B * (255 - a)) / 255);
        }
        // 行悬停/展开高亮：比该级背景深一档
        private static Color LevelHoverColor(int level)
        {
            Color c = LevelColor(level), bg = LevelBgColor(level);
            int a = 48;
            return Color.FromArgb((c.R * a + bg.R * (255 - a)) / 255, (c.G * a + bg.G * (255 - a)) / 255, (c.B * a + bg.B * (255 - a)) / 255);
        }
    }

    internal class DarkColorTable : ProfessionalColorTable
    {
        public override Color MenuBorder { get { return Config.BgHover; } }
        public override Color MenuItemBorder { get { return Config.BgHover; } }
        public override Color MenuItemSelected { get { return Config.BgHover; } }
        public override Color MenuStripGradientBegin { get { return Config.BG; } }
        public override Color MenuStripGradientEnd { get { return Config.BG; } }
        public override Color ToolStripDropDownBackground { get { return Config.BG; } }
        public override Color ImageMarginGradientBegin { get { return Config.BG; } }
        public override Color ImageMarginGradientMiddle { get { return Config.BG; } }
        public override Color ImageMarginGradientEnd { get { return Config.BG; } }
        public override Color SeparatorDark { get { return Config.BgHover; } }
        public override Color SeparatorLight { get { return Config.BgHover; } }
    }

    internal class HighlightOverlay : Form
    {
        public HighlightOverlay(Rectangle r)
        {
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            // 关键：默认 MinimumSize 时系统会把窗口强制到最小跟踪高度(Win11 96DPI 下约 39px)，
            // 导致 21px 高的单行高亮框被撑成两行高。设为 (1,1) 解除该限制。
            MinimumSize = new Size(1, 1);
            Bounds = r;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            DoubleBuffered = true;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x20 | 0x80000;
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Color.FromArgb(230, 9, 71, 113), 2))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }
}