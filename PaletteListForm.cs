using System;
using System.Drawing;
using System.Windows.Forms;

namespace HoverExplorer
{
    // 卡片配色速查列表：滚轮在某张卡片上切换配色时弹出，选中项随滚动实时高亮；
    // 鼠标离开卡片（且不在列表上）后自动关闭，停在列表上可点行选定。
    // 每行一条迷你标签带（配色底色 + 左侧类型色条 + 右端徽章块）作单列预览。
    // 配色应用目标：桌面卡片（PinCardForm）与收藏·历史卡片（EntryCardForm）共用调色列表。
    // Bounds/IsDisposed 由 Control 基类隐式实现
    internal interface IPaletteTarget
    {
        void SetPalette(int index, bool persist);
        Rectangle Bounds { get; }
        bool IsDisposed { get; }
    }

    // 不抢焦点（ShowWithoutActivation + MA_NOACTIVATE），否则会打断用户正在操作的窗口。
    internal class PaletteListForm : Form
    {
        private const int RowH = 24;
        private const int PadX = 8;
        private const int PadY = 5;
        private const int TapeW = 46;
        private const int TapeH = 12;
        private const int LeaveDelay = 350;   // 离开后延迟关闭：卡片与列表之间 4px 缝隙要放行
        private const int PollInterval = 120;

        private static PaletteListForm _inst;

        private int _index;
        private IPaletteTarget _owner;   // Form 基类带 Bounds/IsDisposed，接口只补 SetPalette
        private readonly Timer _timer;      // 轮询：鼠标是否还在卡片/列表上
        private DateTime _leaveSince = DateTime.MinValue;

        public static PaletteListForm Instance
        {
            get { return (_inst != null && !_inst.IsDisposed && _inst.IsHandleCreated) ? _inst : null; }
        }

        private PaletteListForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            BackColor = Config.BG;
            Font = new Font(Config.FontName, 9f);

            int n = CardPalettes.All.Count;
            Width = 232;
            Height = PadY * 2 + n * RowH;

            _timer = new Timer { Interval = PollInterval };
            _timer.Tick += (s, e) => _fnCheckHover();

            MouseClick += (s, e) =>
            {
                int i = HitRow(e.Location);
                if (i < 0) return;
                if (_owner != null && !_owner.IsDisposed) _owner.SetPalette(i, true);
                _index = i;
                Invalidate();
                _fnHide();
            };
        }

        // 滚轮触发：列表已存在就只更新选中项（实时高亮），否则新建并定位到卡片下方
        public static void ShowFor(int index, Rectangle anchor, IPaletteTarget card)
        {
            var f = Instance;
            if (f == null)
            {
                f = new PaletteListForm();
                _inst = f;
            }
            f._owner = card;
            f._index = index;
            f._leaveSince = DateTime.MinValue;
            f._fnPlace(anchor);
            if (!f.Visible) f.Show();
            f.Invalidate();
            f._timer.Start();
        }

        public static void HideNow()
        {
            var f = Instance;
            if (f == null) return;
            f._fnHide();
        }

        private void _fnHide()
        {
            _timer.Stop();
            _leaveSince = DateTime.MinValue;
            if (IsHandleCreated && Visible) Hide();
        }

        private static int HitRow(Point p)
        {
            int i = (p.Y - PadY) / RowH;
            if (i < 0 || i >= CardPalettes.All.Count) return -1;
            return i;
        }

        // 鼠标离开卡片（也不在列表上）→ 延迟一小会儿关闭；卡片没了也立刻关
        private void _fnCheckHover()
        {
            if (_owner == null || _owner.IsDisposed) { _fnHide(); return; }
            var p = Cursor.Position;
            bool on = _owner.Bounds.Contains(p) || Bounds.Contains(p);
            if (on)
            {
                _leaveSince = DateTime.MinValue;
                return;
            }
            if (_leaveSince == DateTime.MinValue) { _leaveSince = DateTime.UtcNow; return; }
            if ((DateTime.UtcNow - _leaveSince).TotalMilliseconds >= LeaveDelay) _fnHide();
        }

        // 定位：优先放卡片下方，放不下翻到上方，整体钳在工作区内
        private void _fnPlace(Rectangle anchor)
        {
            var wa = Screen.GetWorkingArea(new Point(anchor.Left, anchor.Top));
            int x = Math.Min(anchor.Left, wa.Right - Width - 4);
            int y = anchor.Bottom + 4;
            if (y + Height > wa.Bottom) y = anchor.Top - Height - 4;
            Location = new Point(Math.Max(wa.Left + 4, x), Math.Max(wa.Top + 4, y));
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; }   // MA_NOACTIVATE
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            var list = CardPalettes.All;
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                int top = PadY + i * RowH;
                bool sel = i == _index;
                if (sel)
                {
                    using (var b = new SolidBrush(Config.BgHover))
                        g.FillRectangle(b, 0, top, Width, RowH);
                    using (var b = new SolidBrush(Config.Accent))
                        g.FillRectangle(b, 0, top, 3, RowH);
                }
                // 单列预览：一条迷你标签带（底色 + 左色条 + 右端徽章块）
                int tx = PadX, ty = top + (RowH - TapeH) / 2;
                using (var b = new SolidBrush(p.Bg))
                    g.FillRectangle(b, tx, ty, TapeW, TapeH);
                using (var b = new SolidBrush(p.Folder))
                    g.FillRectangle(b, tx, ty, 4, TapeH);
                using (var b = new SolidBrush(p.Folder))
                    g.FillRectangle(b, tx + TapeW - 11, ty + 2, 9, TapeH - 4);
                if (p.BorderWidth > 0 && p.Border.A > 0)
                {
                    using (var pen = new Pen(p.Border, p.BorderWidth))
                        g.DrawRectangle(pen, tx, ty, TapeW, TapeH);
                }
                else
                {
                    using (var pen = new Pen(Color.FromArgb(60, 128, 128, 128)))
                        g.DrawRectangle(pen, tx, ty, TapeW, TapeH);
                }

                string text = (i + 1) + ". " + p.Name;
                Color fg = sel ? Config.FG : Config.PlaceholderFg;
                TextRenderer.DrawText(g, text, Font, new Point(tx + TapeW + 8, top + 5), fg,
                    TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
            using (var pen = new Pen(Config.Border))
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }
}
