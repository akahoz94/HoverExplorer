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
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(BorderColor, 1))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
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
        private Label _pinBtn;
        private Panel _scrollPanel;
        private Control _content;



        public bool Pinned { get { return _pinned; } }

        public PreviewForm(Image img)
            : this()
        {
            _img = img;
            var pb = new PictureBox { Image = img, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill };
            _content = pb;
            _scrollPanel.Controls.Add(pb);
            ClientSize = new Size(img.Width, img.Height);
            UpdatePinPos();
        }

        public PreviewForm(Image img, int displayW, int displayH)
            : this()
        {
            _img = img;
            var pb = new PictureBox { Image = img, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill };
            _content = pb;
            _scrollPanel.Controls.Add(pb);
            ClientSize = new Size(displayW, displayH);
            UpdatePinPos();
        }

        public PreviewForm(string text, long size)
            : this()
        {
            var tb = new RichTextBox
            {
                Font = new Font("Consolas", 9),
                BackColor = Config.BG,
                ForeColor = Config.FG,
                BorderStyle = BorderStyle.None,
                ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                WordWrap = true,
                Dock = DockStyle.Fill,
            };
            tb.Text = text;
            tb.SelectionStart = 0;
            tb.SelectionLength = 0;
            _content = tb;
            _scrollPanel.Controls.Add(tb);

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
                ClientSize = new Size(Math.Min(maxW + 20, 480), Math.Min(initH, 320));
            }
            UpdatePinPos();
        }

        private PreviewForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Config.BG;
            DoubleBuffered = true;

            _scrollPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Config.BG };
            Controls.Add(_scrollPanel);

            _pinBtn = new Label
            {
                Text = I18n.T("pin"),
                Font = new Font(Config.FontName, 8f),
                ForeColor = Config.FG,
                BackColor = Config.BG,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(22, 18),
                Cursor = Cursors.Hand,
            };
            Controls.Add(_pinBtn);
            _pinBtn.BringToFront();
            _pinBtn.Click += (s, e) => TogglePin();


            Resize += (s, e) => UpdatePinPos();
        }

        private void UpdatePinPos()
        {
            _pinBtn.Location = new Point(ClientSize.Width - _pinBtn.Width - 2, 2);
        }

        private void TogglePin()
        {
            _pinned = !_pinned;
            if (_pinned)
            {
                FormBorderStyle = FormBorderStyle.SizableToolWindow;
                Text = I18n.T("preview");
                _pinBtn.Visible = false;

            }
            else
            {
                FormBorderStyle = FormBorderStyle.None;
                _pinBtn.Visible = true;

                UpdatePinPos();
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
        private readonly Action<int, string, int> _onOpenSub;
        private readonly Func<int, int, string> _dropResolver;
        private readonly Action<int> _onScheduleCloseAfter;
        private readonly List<RowLabel> _rows = new List<RowLabel>();
        private readonly Dictionary<string, BorderLabel> _sortBtns = new Dictionary<string, BorderLabel>();
        private TextBox _searchBox;
        private Panel _listPanel;
        private string _path;
        private int _headH;
        private int _panelW = 260;

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

        public PopupForm(int level, Action<int, string, int> onOpenSub, Func<int, int, string> dropResolver, Action<int> onScheduleCloseAfter)
        {
            _level = level;
            _onOpenSub = onOpenSub;
            _dropResolver = dropResolver;
            _onScheduleCloseAfter = onScheduleCloseAfter;
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Config.BG;
            DoubleBuffered = true;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Win11.Apply(Handle, Config.Theme != "light", Config.Border);
        }

        private static readonly Dictionary<string, Image> _iconCache = new Dictionary<string, Image>();

        private static Image GetIcon(bool isDir, string ext)
        {
            string key = isDir ? "<dir>" : ext;
            Image cached;
            if (_iconCache.TryGetValue(key, out cached)) return cached;
            var shfi = new NativeMethods.SHFILEINFOW();
            uint flags = 0x100 | 0x1 | 0x10;
            if (isDir)
                NativeMethods.SHGetFileInfo("x", 0x10, ref shfi, (uint)Marshal.SizeOf(typeof(NativeMethods.SHFILEINFOW)), flags);
            else
                NativeMethods.SHGetFileInfo("x" + ext, 0x80, ref shfi, (uint)Marshal.SizeOf(typeof(NativeMethods.SHFILEINFOW)), flags);
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

        public bool ShowPanel(string path, int x, int y)
        {
            CancelHover();
            CancelPreview();
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(path).Select(e => Path.GetFileName(e)).ToArray(); }
            catch { return false; }
            Array.Sort(entries, StringComparer.Ordinal);
            SortEntries(path, ref entries);
            if (entries.Length > Config.MaxItems)
                entries = entries.Take(Config.MaxItems).ToArray();
            Program.DebugLog("[panel] ShowPanel level=" + _level + " lang=" + I18n.Lang + " path=" + path + " entries=" + entries.Length);
            _path = path;

            SuspendLayout();
            while (Controls.Count > 0) Controls[0].Dispose();
            Controls.Clear();
            _rows.Clear();
            _sortBtns.Clear();

            int curY = 5;
            if (_level == 0)
            {
                var sortFlow = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Location = new Point(6, curY), AutoSize = false, Height = 26, Width = _panelW - 12 };
                foreach (var kv in Config.SortLabels)
                {
                    var btn = new BorderLabel
                    {
                        Text = kv.Value,
                        Font = new Font(Config.FontName, Config.FontSize),
                        Padding = new Padding(7, 1, 7, 1),
                        AutoSize = true,
                        BackColor = Config.BG,
                        ForeColor = Config.FG,
                        Cursor = Cursors.Hand,
                        Margin = new Padding(0, 0, 4, 0),
                    };
                    var mode = kv.Key;
                    btn.Click += (s, e) => SetSort(mode);
                    _sortBtns[mode] = btn;
                    sortFlow.Controls.Add(btn);
                }
                Controls.Add(sortFlow);
                curY += 28;

                var searchBorder = new Panel { BackColor = Config.Border, Location = new Point(6, curY), Size = new Size(_panelW - 12, 22) };
                _searchBox = new TextBox
                {
                    Text = I18n.T("search"),
                    Font = new Font(Config.FontName, Config.FontSize),
                    BackColor = Config.BG,
                    ForeColor = Config.PlaceholderFg,
                    BorderStyle = BorderStyle.None,
                    Location = new Point(1, 2),
                    Width = searchBorder.Width - 2,
                };
                _searchBox.GotFocus += (s, e) => { Program.DebugLog("[search] GotFocus level=" + _level + " text=\"" + _searchBox.Text + "\""); if (_searchBox.Text == I18n.T("search")) { _searchBox.Text = ""; _searchBox.ForeColor = Config.FG; } };
                _searchBox.LostFocus += (s, e) => { Program.DebugLog("[search] LostFocus level=" + _level + " text=\"" + _searchBox.Text + "\" -> 恢复占位符"); if (string.IsNullOrEmpty(_searchBox.Text)) { _searchBox.Text = I18n.T("search"); _searchBox.ForeColor = Config.PlaceholderFg; } };
                _searchBox.TextChanged += (s, e) => FilterRows(_searchBox.Text);
                _searchBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { _searchBox.Text = ""; FilterRows(""); } };
                searchBorder.Controls.Add(_searchBox);
                Controls.Add(searchBorder);
                curY += 22 + 2;
                _headH = curY;
            }
            else
            {
                _headH = 0;
            }

            _listPanel = new Panel { Location = new Point(0, curY), BackColor = Config.BG };
            Controls.Add(_listPanel);

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
            if (maxW > 400) maxW = 400;
            _panelW = maxW;

            if (_level == 0)
            {
                foreach (Control c in Controls)
                {

                    if (c is Panel && c.BackColor == Config.Border) c.Width = _panelW - 12;
                }
                if (_searchBox != null) _searchBox.Width = _panelW - 14;
            }

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
                    Icon = GetIcon(isDir, Path.GetExtension(name)),
                    SizeText = sizeTexts[i],
                };
                row.OrigBg = row.BackColor;
                row.OrigFg = row.ForeColor;
                row.MouseEnter += (s, e) => OnRowEnter(row);
                row.MouseLeave += (s, e) => OnRowLeave(row);
                row.MouseDown += (s, e) => OnRowDown(row, e);
                row.MouseMove += (s, e) => OnRowMove(row, e);
                row.MouseUp += (s, e) => OnRowUp(row, e);
                row.MouseClick += (s, e) => { if (e.Button == MouseButtons.Right) PopupMenu(row, e); };
                if (isDir)
                {
                    row.AllowDrop = true;
                    row.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effect = DragDropEffects.Move; row.BackColor = Config.DropHighlight; } };
                    row.DragLeave += (s, e) => ApplySel(row);
                    row.DragDrop += (s, e) => { OnDrop(row, e); };
                }
                _listPanel.Controls.Add(row);
                _rows.Add(row);
                ry += rowHeights[i];
            }
            var workArea = Screen.GetWorkingArea(Cursor.Position);
            int maxH = workArea.Height - 8;
            int needH = curY + ry;
            if (needH > maxH)
            {
                int scrollW = SystemInformation.VerticalScrollBarWidth;
                needH = maxH;
                foreach (var r in _rows) r.Width = _panelW - scrollW;
                _listPanel.AutoScroll = true;
            }
            _listPanel.Size = new Size(_panelW, needH - curY);
            ClientSize = new Size(_panelW, needH);
            ResumeLayout(false);
            PerformLayout();

            ApplySortBtns();

            ClampPos(ref x, ref y);
            Location = new Point(x, y);

            MouseClick += (s, e) => { if (e.Button == MouseButtons.Right) PopupSortMenu(e); };
            return true;
        }

        public void FocusSearch()
        {
            if (_searchBox != null) _searchBox.Focus();
        }

        private void SortEntries(string path, ref string[] entries)
        {
            string mode = SortMode;
            bool desc = SortDesc;
            var list = entries.ToList();
            list.Sort((a, b) =>
            {
                string fa = Path.Combine(path, a), fb = Path.Combine(path, b);
                bool da = Directory.Exists(fa), db = Directory.Exists(fb);
                if (da != db) return da ? -1 : 1;
                int cmp;
                if (mode == "size")
                {
                    long sa = File.Exists(fa) ? new FileInfo(fa).Length : 0;
                    long sb = File.Exists(fb) ? new FileInfo(fb).Length : 0;
                    cmp = sa.CompareTo(sb);
                }
                else if (mode == "date")
                {
                    long ta = File.GetLastWriteTimeUtc(fa).Ticks;
                    long tb = File.GetLastWriteTimeUtc(fb).Ticks;
                    cmp = ta.CompareTo(tb);
                }
                else
                {
                    cmp = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                }
                if (cmp == 0) cmp = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                return desc ? -cmp : cmp;
            });
            entries = list.ToArray();
        }

        private void ApplySortBtns()
        {
            string arrow = SortDesc ? " ▼" : " ▲";
            foreach (var kv in _sortBtns)
            {
                bool on = kv.Key == SortMode;
                kv.Value.Text = Config.SortLabels[kv.Key] + (on ? arrow : "");
                kv.Value.BackColor = on ? Config.BgSel : Config.BG;
                kv.Value.ForeColor = on ? Color.White : Config.FG;
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
            row.BackColor = Config.BgHover;
            NativeMethods.POINT _ept; NativeMethods.GetCursorPos(out _ept);
            bool canPv = !row.IsDir && Config.PreviewFiles && PreviewProvider.CanPreview(row.Full);
            Program.DebugLog("[row] enter level=" + _level + " lang=" + I18n.Lang + " " + (row.IsDir ? "DIR " : "FILE ") + row.Name + " mouse=" + _ept.X + "," + _ept.Y + " previewFiles=" + Config.PreviewFiles + " canPreview=" + canPv);
            if (row.IsDir) ScheduleHover(row.Full, row.PointToScreen(Point.Empty).Y);
            else if (canPv) SchedulePreview(row);
        }

        private void OnRowLeave(RowLabel row)
        {
            NativeMethods.POINT _lpt; NativeMethods.GetCursorPos(out _lpt);
            Program.DebugLog("[row] leave level=" + _level + " lang=" + I18n.Lang + " " + (row.IsDir ? "DIR " : "FILE ") + row.Name + " mouse=" + _lpt.X + "," + _lpt.Y);
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
            if (_activeDirRow != null) _activeDirRow.BackColor = Config.BgHover;
        }

        public void ClearActiveDir()
        {
            if (_activeDirRow != null) { ApplySel(_activeDirRow); _activeDirRow = null; }
        }

        private void ApplySel(RowLabel row)
        {
            if (row.Selected) { row.BackColor = Config.BgSel; row.ForeColor = Color.White; }
            else { row.BackColor = row.OrigBg; row.ForeColor = row.OrigFg; }
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
            _hoverTimer.Tick += (s, e) => { _hoverTimer.Stop(); _hoverTimer.Dispose(); _hoverTimer = null; Program.DebugLog("[panel] 悬停计时触发 -> 请求打开子面板 level=" + _level + " path=" + _hoverPath); if (_hoverPath != null && _onOpenSub != null) _onOpenSub(_level, _hoverPath, _hoverRowY); };
            _hoverTimer.Start();
        }

        private void CancelHover()
        {
            if (_hoverTimer != null) { _hoverTimer.Stop(); _hoverTimer.Dispose(); _hoverTimer = null; }
            _hoverPath = null;
        }


        private void SchedulePreview(RowLabel row)
        {
            Program.DebugLog("[panel-preview] schedule level=" + _level + " lang=" + I18n.Lang + " " + row.Full + " delay=" + Config.PreviewDelay + " (hadTimer=" + (_previewTimer != null) + " hadForm=" + (_previewForm != null) + ")");
            CancelPreview();
            _previewPath = row.Full;
            _previewTimer = new Timer { Interval = Config.PreviewDelay };
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
                if (_previewForm != null && !pinned && inPv)
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
            if (_previewForm != null && _previewForm.Pinned) { Program.DebugLog("[panel-preview] CLOSE skipped (pinned) level=" + _level); return; }
            if (_previewForm != null) { Program.DebugLog("[panel-preview] CLOSE level=" + _level + " form was at " + _previewForm.Left + "," + _previewForm.Top + " " + _previewForm.Width + "x" + _previewForm.Height); try { _previewForm.Close(); _previewForm.Dispose(); } catch { } _previewForm = null; }
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
                        else
                        {
                            _previewForm = new PreviewForm(result.Text, result.Size);
                        }
                        PositionPreview(row);
                        _previewForm.Show();
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
            _extPreviewTimer = new Timer { Interval = Config.PreviewDelay };
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

        // 预览显示期间保持 TOPMOST（不激活）：防止被前台窗口盖住
        public static void EnsureExtPreviewTopmost()
        {
            if (_extPreviewForm == null || !_extPreviewForm.IsHandleCreated || !_extPreviewForm.Visible) return;
            NativeMethods.SetWindowPos(_extPreviewForm.Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOACTIVATE);
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
            if (_extPreviewForm != null) { Program.DebugLog("[preview] CLOSE form"); try { _extPreviewForm.Close(); _extPreviewForm.Dispose(); } catch { } _extPreviewForm = null; }
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
                        if (result.Kind == PreviewResult.KindEnum.Image)
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
                        _extPreviewForm.Show();
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
            int x = mouse.X + 20, y = mouse.Y + 16;
            int pw = _extPreviewForm.Width, ph = _extPreviewForm.Height;
            var hpt = new NativeMethods.POINT { X = mouse.X, Y = mouse.Y };
            var hmon = NativeMethods.MonitorFromPoint(hpt, 2);
            var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO)) };
            NativeMethods.GetMonitorInfo(hmon, ref mi);
            int wl = mi.rcWork.Left, wt = mi.rcWork.Top, wr = mi.rcWork.Right, wb = mi.rcWork.Bottom;
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
            int lx = row.PointToScreen(Point.Empty).X;
            int rx = row.PointToScreen(new Point(row.Width, 0)).X;
            int x = rx + 8, y = row.PointToScreen(Point.Empty).Y;
            int pw = _previewForm.Width, ph = _previewForm.Height;
            NativeMethods.POINT pt;
            NativeMethods.GetCursorPos(out pt);
            var hmon = NativeMethods.MonitorFromPoint(pt, 2);
            var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO)) };
            NativeMethods.GetMonitorInfo(hmon, ref mi);
            int wl = mi.rcWork.Left, wt = mi.rcWork.Top, wr = mi.rcWork.Right, wb = mi.rcWork.Bottom;
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
            Program.DebugLog("[panel-preview] position level=" + _level + " rowL=" + lx + " rowR=" + rx + " rowY=" + row.PointToScreen(Point.Empty).Y + " pvSize=" + pw + "x" + ph + " -> pvAt=" + x + "," + y + " workArea=" + wl + "," + wt + "-" + wr + "," + wb + " mouse=" + pt.X + "," + pt.Y);
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
            foreach (var src in files)
            {
                if (!string.IsNullOrEmpty(src) && (File.Exists(src) || Directory.Exists(src)) && src != dstDir)
                    DoMove(src, dstDir);
            }
            ApplySel(row);
        }

        private void DoMove(string src, string dstDir)
        {
            Task.Run(() =>
            {
                try
                {
                    string dst = UniqueDest(src, dstDir);
                    if (dst == src) return;
                    if (Directory.Exists(src)) Directory.Move(src, dst);
                    else File.Move(src, dst);
                    BeginInvoke((MethodInvoker)delegate() { OnMoveDone(); });
                }
                catch (Exception ex) { Program.Log("[移动失败] " + ex.Message); }
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
            BackColor = Config.BG;
            Win11.Apply(Handle, Config.Theme != "light", Config.Border);
            int x = Location.X, y = Location.Y;
            ShowPanel(_path, x, y);
        }

        private static ToolStripMenuItem MI(string text, Action handler)
        {
            return new ToolStripMenuItem(text, null, (EventHandler)((s, e) => handler()));
        }

        private void PopupMenu(RowLabel row, MouseEventArgs e)
        {
            var m = new ContextMenuStrip { BackColor = Config.BG, ForeColor = Config.FG, Font = new Font(Config.FontName, Config.FontSize) };
            m.Renderer = new ToolStripProfessionalRenderer(new DarkColorTable());
            var sel = SelRows();
            if (sel.Count > 1)
            {
                m.Items.Add(MI(I18n.T("delete_n", sel.Count), () => RecycleRows(sel)));
                m.Items.Add(MI(I18n.T("copy_path_n", sel.Count), () => CopyText(string.Join("\n", sel.Select(r => r.Full)))));
                m.Items.Add(MI(I18n.T("cancel_sel"), () => ClearSel()));
            }
            else
            {
                ClearSel();
                m.Items.Add(MI(I18n.T("open"), () => OpenPath(row.Full)));
                m.Items.Add(MI(I18n.T("new_window"), () => OpenInExplorer(row.Full)));
                m.Items.Add(MI(I18n.T("rename"), () => Rename(row, e)));
                m.Items.Add(MI(I18n.T("delete"), () => RecycleRows(new List<RowLabel> { row })));
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add(MI(I18n.T("copy_path"), () => CopyText(row.Full)));
                m.Items.Add(MI(I18n.T("copy_name"), () => CopyText(row.Name)));
            }
            _menuOpen = true;
            m.Closed += (s, ev) => _menuOpen = false;
            m.Show(row, e.Location);
        }

        private void PopupSortMenu(MouseEventArgs e)
        {
            var m = new ContextMenuStrip { BackColor = Config.BG, ForeColor = Config.FG, Font = new Font(Config.FontName, Config.FontSize) };
            m.Renderer = new ToolStripProfessionalRenderer(new DarkColorTable());
            foreach (var kv in Config.SortLabels)
            {
                string mark = SortMode == kv.Key ? "● " : "   ";
                var item = new ToolStripMenuItem(mark + kv.Value);
                string mode = kv.Key;
                item.Click += (s, ev) => SetSort(mode);
                m.Items.Add(item);
            }
            m.Items.Add(new ToolStripSeparator());
            var descItem = new ToolStripMenuItem(SortDesc ? I18n.T("sort_asc") : I18n.T("sort_desc"));
            descItem.Click += (s, ev) => ToggleSortDesc();
            m.Items.Add(descItem);
            _menuOpen = true;
            m.Closed += (s, ev) => _menuOpen = false;
            m.Show(this, e.Location);
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
                        try
                        {
                            string newPath = Path.Combine(Path.GetDirectoryName(row.Full), dlg.NewName);
                            if (Directory.Exists(row.Full)) Directory.Move(row.Full, newPath);
                            else File.Move(row.Full, newPath);
                            OnMoveDone();
                        }
                        catch (Exception ex) { Program.Log("[重命名失败] " + ex.Message); }
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
        public int GeometryY() { return Top; }
        public int HeadHeight() { return _level == 0 ? _headH : 0; }

        public bool ContainsPoint(int x, int y)
        {
            if (!IsHandleCreated || !Visible) return false;
            return Left <= x && x <= Left + Width && Top <= y && y <= Top + Height;
        }

        public bool MenuOpen { get { return _menuOpen; } }
        // 模态对话框（删除确认/重命名等）显示期间置位，主循环应暂停自动关闭
        public bool ModalOpen;
        public bool IsDragging { get { return _dragging; } }
        public string CurrentPath { get { return _path; } }

        public void ClosePanel()
        {
            Program.DebugLog("[panel] ClosePanel level=" + _level + " path=" + _path);
            CancelHover();

            CancelPreview();
            if (IsHandleCreated) try { Close(); Dispose(); } catch { }
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