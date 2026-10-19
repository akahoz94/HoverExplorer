using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace HoverExplorer
{
    // 方案 C 迷你卡片：固定文件为桌面独立浮动卡片，点击执行
    internal class PinCardForm : Form
    {
        private readonly string _path;
        private Label _closeBtn;
        private Point _dragBefore;

        public PinCardForm(string path, int x, int y)
        {
            _path = path;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(x, y);
            TopMost = true;
            ShowInTaskbar = false;
            Width = 200;
            Height = 52;
            BackColor = Config.BG;
            ForeColor = Config.FG;
            DoubleBuffered = true;

            string name = Path.GetFileName(path);
            string sub = "";
            try { var fi = new FileInfo(path); sub = Config.FmtSize(fi.Length); } catch { }

            Image iconImg = null;
            try { var ic = Icon.ExtractAssociatedIcon(path); if (ic != null) iconImg = ic.ToBitmap(); } catch { }
            if (iconImg != null) iconImg = new Bitmap(iconImg, 28, 28);

            var tile = new Panel
            {
                Location = new Point(14, 8),
                Size = new Size(36, 36),
                BackColor = Config.Border,
            };
            var iconLbl = new Label
            {
                Dock = DockStyle.Fill,
                ImageAlign = ContentAlignment.MiddleCenter,
                Image = iconImg,
                BackColor = Config.Border,
            };
            tile.Controls.Add(iconLbl);
            Controls.Add(tile);

            var nameLbl = new Label
            {
                Location = new Point(60, 9),
                Size = new Size(104, 18),
                Text = name,
                Font = new Font(Config.FontName, 9f),
                ForeColor = Config.FG,
                BackColor = Config.BG,
                AutoEllipsis = true,
            };
            Controls.Add(nameLbl);

            var subLbl = new Label
            {
                Location = new Point(60, 28),
                Size = new Size(104, 14),
                Text = sub,
                Font = new Font(Config.FontName, 8f),
                ForeColor = Config.PlaceholderFg,
                BackColor = Config.BG,
                AutoEllipsis = true,
            };
            Controls.Add(subLbl);

            var runBtn = new Label
            {
                Location = new Point(168, 13),
                Size = new Size(26, 26),
                BackColor = Config.Border,
                Image = RunIcon(false),
                ImageAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
            };
            runBtn.MouseEnter += (s, e) => { runBtn.BackColor = Config.Accent; runBtn.Image = RunIcon(true); };
            runBtn.MouseLeave += (s, e) => { runBtn.BackColor = Config.Border; runBtn.Image = RunIcon(false); };
            Controls.Add(runBtn);

            _closeBtn = new Label
            {
                Location = new Point(Width - 20, -4),
                Size = new Size(20, 20),
                BackColor = Config.BG,
                Text = "×",
                Font = new Font(Config.FontName, 12f),
                ForeColor = Config.PlaceholderFg,
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
                Visible = false,
            };
            _closeBtn.MouseEnter += (s, e) => { _closeBtn.ForeColor = Color.FromArgb(209, 52, 56); };
            _closeBtn.MouseLeave += (s, e) => { _closeBtn.ForeColor = Config.PlaceholderFg; };
            _closeBtn.Click += (s, e) => Program.Unpin(_path);
            Controls.Add(_closeBtn);

            MouseEventHandler drag = (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                _dragBefore = Location;
                NativeMethods.ReleaseCapture();
                NativeMethods.SendMessage(Handle, 0xA1, 0x2, 0);
            };
            MouseEventHandler cardMouse = (s, e) =>
            {
                if (e.Button == MouseButtons.Middle) Program.Unpin(_path);
                else if (e.Button == MouseButtons.Right) ShellContextMenu.Show(_path, Handle, () => Program.Unpin(_path));
            };
            MouseUp += (s, e) =>
            {
                if (Location != _dragBefore) Program.UpdatePinPos(_path, Left, Top);
            };
            foreach (Control c in new Control[] { tile, iconLbl, nameLbl, subLbl, runBtn })
            {
                c.MouseDown += drag;
                c.MouseDown += cardMouse;
                c.MouseUp += (s, e) => { if (Location != _dragBefore) Program.UpdatePinPos(_path, Left, Top); };
            }
            tile.Click += (s, e) => Run();
            iconLbl.Click += (s, e) => Run();
            nameLbl.Click += (s, e) => Run();
            subLbl.Click += (s, e) => Run();
            runBtn.Click += (s, e) => Run();
            Click += (s, e) => Run();
            MouseDown += cardMouse;

            MouseEnter += (s, e) => { _closeBtn.Visible = true; _closeBtn.BringToFront(); };
            MouseLeave += (s, e) => { if (!ClientRectangle.Contains(PointToClient(Cursor.Position))) _closeBtn.Visible = false; };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Win11.Apply(Handle, Config.Theme != "light", Config.Border);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var b = new SolidBrush(Config.Accent))
                e.Graphics.FillRectangle(b, 0, 7, 3, Height - 14);
        }

        private void Run()
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_path) { UseShellExecute = true }); }
            catch (Exception ex) { Program.DebugLog("[pincard] run fail " + _path + " " + ex.Message); }
        }

        private void ShowCardMenu()
        {
            var m = new ContextMenuStrip { BackColor = Config.BG, ForeColor = Config.FG, Font = new Font(Config.FontName, Config.FontSize) };
            m.Items.Add(I18n.T("open"), null, (s, e) => Run());
            m.Items.Add(I18n.T("new_window"), null, (s, e) => { try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + _path + "\""); } catch { } });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(I18n.T("copy_path"), null, (s, e) => { try { Clipboard.SetText(_path); } catch { } });
            m.Items.Add(I18n.T("unpin"), null, (s, e) => Program.Unpin(_path));
            m.Show(Cursor.Position);
        }

        private static Image RunIcon(bool active)
        {
            var bmp = new Bitmap(14, 14);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var c = active ? Color.White : Config.Accent;
                using (var b = new SolidBrush(c))
                {
                    var pts = new PointF[] { new PointF(3, 2), new PointF(11, 7), new PointF(3, 12) };
                    g.FillPolygon(b, pts);
                }
            }
            return bmp;
        }
    }
}