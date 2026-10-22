using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace HoverExplorer
{
    // 参考原型（PixPin_2026-10-22）重排：单栏分组 + 行式设置项（左标题描述/右控件）+ 底部按钮
    internal class SettingsForm : Form
    {
        private ToggleSwitch _hoverToggle;
        private ToggleSwitch _cardToggle;
        private HotkeyPill _hoverPill;
        private HotkeyPill _cardPill;
        private DelayStepper _delayStepper;
        private ComboBox _tcSortCombo;
        private Button _okBtn;
        private Button _cancelBtn;
        private Label _closeBtn;
        private Label _verLbl;
        private Label _hoverHintLbl;
        private Label _cardHintLbl;

        private int _hoverMods;
        private int _hoverKey;
        private int _cardMods;
        private int _cardKey;
        private bool _hoverRecording;
        private bool _cardRecording;

        private const int PadX = 24;
        private const int RowH = 48;
        private const int TitleBarH = 60;


        public bool HotkeyEnabled { get; private set; }
        public int HotkeyModifiers { get; private set; }
        public int HotkeyKey { get; private set; }
        public double HoverDelaySetting { get; private set; }
        public int TcSortSetting { get; private set; }
        public bool CardHotkeyEnabledSetting { get; private set; }
        public int CardHotkeyModifiersSetting { get; private set; }
        public int CardHotkeyKeySetting { get; private set; }

        private static LinkLabel MakeLink(string text, int x, int y, string url)
        {
            var link = new LinkLabel
            {
                Text = text,
                Location = new Point(x, y),
                AutoSize = true,
                Font = new Font(Config.FontName, 8.5f),
                Tag = "sub",
                LinkArea = new LinkArea(0, text.Length)
            };
            link.Click += (s, e) => OpenUrl(url);
            return link;
        }

        private static void OpenUrl(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }

        public SettingsForm()
        {
            Text = I18n.T("settings");
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            Width = 500;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            KeyPreview = true;

            HotkeyEnabled = Config.HotkeyEnabled;

            _hoverMods = Config.HotkeyModifiers;
            _hoverKey = Config.HotkeyKey;
            _cardMods = Config.CardHotkeyModifiers;
            _cardKey = Config.CardHotkeyKey;

            int contentRight = Width - PadX;

            // ---- 标题区 ----
            var titleLbl = new Label
            {
                Text = I18n.T("settings"),
                Location = new Point(PadX, 18),
                AutoSize = true,
                Font = new Font(Config.FontName, 13f, FontStyle.Bold)
            };
            titleLbl.MouseDown += (s, e) => _fnDrag(e);
            Controls.Add(titleLbl);

            var subLbl = new Label
            {
                Text = I18n.T("settings_subtitle"),
                Location = new Point(PadX, 46),
                AutoSize = true,
                ForeColor = Config.PlaceholderFg,
                Font = new Font(Config.FontName, 8.5f),
                Tag = "sub"
            };
            subLbl.MouseDown += (s, e) => _fnDrag(e);
            Controls.Add(subLbl);

            // ---- 分组 1：悬停预览 ----
            int y = _fnAddGroup(70, I18n.T("group_hover"));

            y = 92;
            _hoverToggle = new ToggleSwitch { On = Config.HotkeyEnabled };
            _hoverToggle.Toggled += (s, e) => _fnUpdateHoverState();
            y = _fnAddRow(y, I18n.T("hotkey_enabled"), I18n.T("hotkey_desc"), _hoverToggle, contentRight);

            _hoverPill = new HotkeyPill();
            _hoverPill.SetHotkey(_hoverMods, _hoverKey);
            _hoverPill.RecordClick += (s, e) => _fnStartHoverCapture();
            _hoverHintLbl = _fnMakeHint();
            int hoverKeyRowY = y;
            y = _fnAddRow(y, I18n.T("hotkey_row"), null, null, contentRight);
            _fnPlacePill(_hoverPill, hoverKeyRowY);
            Controls.Add(_hoverPill);
            _hoverHintLbl.Location = new Point(PadX, hoverKeyRowY + 19);
            Controls.Add(_hoverHintLbl);

            _delayStepper = new DelayStepper { Value = Config.HoverDelay };
            y = _fnAddRow(y, I18n.T("delay_row"), I18n.T("hover_delay_desc"), _delayStepper, contentRight);

            _fnAddSep(230);

            // ---- 分组 2：桌面卡片 ----
            _fnAddGroup(244, I18n.T("group_card"));

            y = 266;
            _cardToggle = new ToggleSwitch { On = Config.CardHotkeyEnabled };
            _cardToggle.Toggled += (s, e) => _fnUpdateCardState();
            y = _fnAddRow(y, I18n.T("hotkey_enabled"), I18n.T("card_hotkey_desc"), _cardToggle, contentRight);

            _cardPill = new HotkeyPill();
            _cardPill.SetHotkey(_cardMods, _cardKey);
            _cardPill.RecordClick += (s, e) => _fnStartCardCapture();
            _cardHintLbl = _fnMakeHint();
            int cardKeyRowY = y;
            _fnAddRow(y, I18n.T("hotkey_row"), null, null, contentRight);
            _fnPlacePill(_cardPill, cardKeyRowY);
            Controls.Add(_cardPill);
            _cardHintLbl.Location = new Point(PadX, cardKeyRowY + 19);
            Controls.Add(_cardHintLbl);

            _fnAddSep(348);

            // ---- 分组 3：排序 ----
            _fnAddGroup(362, I18n.T("group_tc"));

            y = 384;
            var pausedTag = new TagLabel(I18n.T("tag_paused"));
            y = _fnAddRow(y, I18n.T("group_sort"), null, pausedTag, contentRight);

            _tcSortCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font(Config.FontName, 8.5f),
                Enabled = false,
                Width = 170,
                Height = 24
            };
            _tcSortCombo.Items.AddRange(new object[] { "自动 (读 TC ini)", "名称 ↑", "名称 ↓", "日期 ↑", "日期 ↓", "大小 ↑", "大小 ↓", "扩展名 ↑", "扩展名 ↓" });
            _tcSortCombo.SelectedIndex = Config.TcSortMode + 1;
            y = _fnAddRow(y, I18n.T("tc_sort_row"), I18n.T("tc_sort_disabled"), _tcSortCombo, contentRight);

            _fnAddSep(470);

            // ---- 底部按钮区（确定在左、取消在右）----
            int btnY = 482;
            _okBtn = new Button
            {
                Text = I18n.T("settings_ok"),
                Location = new Point(contentRight - 86 - 10 - 86, btnY),
                Size = new Size(86, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Config.Accent,
                ForeColor = Color.White,
                Font = new Font(Config.FontName, 9f)
            };
            _okBtn.FlatAppearance.BorderSize = 0;
            _okBtn.FlatAppearance.MouseOverBackColor = Config.Accent;
            _okBtn.Click += (s, e) =>
            {
                HotkeyEnabled = _hoverToggle.On;
                HotkeyModifiers = _hoverMods;
                HotkeyKey = _hoverKey;
                HoverDelaySetting = _delayStepper.Value;
                // TC 功能暂停：下拉禁用，保持原值不变（防御式直取 Config 值）
                TcSortSetting = Config.TcSortMode;
                CardHotkeyEnabledSetting = _cardToggle.On;
                CardHotkeyModifiersSetting = _cardMods;
                CardHotkeyKeySetting = _cardKey;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(_okBtn);

            _cancelBtn = new Button
            {
                Text = I18n.T("settings_cancel"),
                Location = new Point(contentRight - 86, btnY),
                Size = new Size(86, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Config.BG,
                ForeColor = Config.FG,
                Font = new Font(Config.FontName, 9f)
            };
            _cancelBtn.FlatAppearance.BorderColor = Config.Border;
            _cancelBtn.FlatAppearance.MouseOverBackColor = Config.BgHover;
            _cancelBtn.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(_cancelBtn);

            // ---- 赞赏区（保留）----
            EventHandler copyWx = (s, e) =>
            {
                try { Clipboard.SetText("jtugqivi"); MessageBox.Show(I18n.T("donated"), I18n.T("donate"), MessageBoxButtons.OK, MessageBoxIcon.Information); } catch { }
            };
            int dy = 524;
            Image qrImg = null;
            try
            {
                var s = typeof(SettingsForm).Assembly.GetManifestResourceStream("HoverExplorer.donate_qr.png");
                if (s != null) { qrImg = Image.FromStream(s); }
            }
            catch { }
            if (qrImg != null)
            {
                var qr = new PictureBox
                {
                    Location = new Point(PadX, dy),
                    Size = new Size(110, 110),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Image = qrImg,
                    Cursor = Cursors.Hand,
                };
                qr.Click += copyWx;
                Controls.Add(qr);
            }
            var donateLbl = new LinkLabel
            {
                Text = "vx:jtugqivi",
                Location = new Point(146, dy + 4),
                AutoSize = true,
                Font = new Font(Config.FontName, 8.5f),
                Tag = "sub"
            };
            donateLbl.Click += copyWx;
            Controls.Add(donateLbl);
            Controls.Add(MakeLink("GitHub Releases", 146, dy + 28, "https://github.com/dlsdgj/HoverExplorer/releases"));
            Controls.Add(MakeLink("百度网盘 (pwd: hbf8)", 146, dy + 52, "https://pan.baidu.com/s/12rp6LTe7Y8oSErvP8wTLgA?pwd=hbf8"));

            // 版本号：右下角弱化
            _verLbl = new Label
            {
                Text = "v" + Program.Version,
                AutoSize = true,
                ForeColor = Config.PlaceholderFg,
                Font = new Font(Config.FontName, 8f),
                Tag = "sub"
            };
            _verLbl.Location = new Point(contentRight - _verLbl.PreferredWidth, dy + 110 - _verLbl.PreferredHeight);
            Controls.Add(_verLbl);

            // 自绘标题栏关闭按钮
            _closeBtn = new Label
            {
                Text = "×",
                Size = new Size(28, 24),
                Location = new Point(Width - 32, 8),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(Config.FontName, 11),
                Cursor = Cursors.Hand
            };
            _closeBtn.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            _closeBtn.MouseEnter += (s, e) => _closeBtn.BackColor = Config.BgHover;
            _closeBtn.MouseLeave += (s, e) => _closeBtn.BackColor = Config.BG;
            Controls.Add(_closeBtn);

            Height = dy + 110 + 24;

            AcceptButton = _okBtn;

            ApplyThemeColors();
            _fnUpdateHoverState();
            _fnUpdateCardState();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Config.Border, 1))
                e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _fnDrag(e);
        }

        private void _fnDrag(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.Y <= TitleBarH)
            {
                NativeMethods.ReleaseCapture();
                NativeMethods.SendMessage(Handle, 0xA1, 0x2, 0);
            }
        }

        // ================= 布局辅助 =================

        private int _fnAddGroup(int y, string text)
        {
            var l = new Label
            {
                Text = text,
                Location = new Point(PadX, y),
                AutoSize = true,
                Font = new Font(Config.FontName, 10f, FontStyle.Bold)
            };
            Controls.Add(l);
            return y + 22;
        }

        private int _fnAddRow(int y, string title, string desc, Control right, int contentRight)
        {
            var t = new Label
            {
                Text = title,
                Location = new Point(PadX, y),
                AutoSize = true,
                Font = new Font(Config.FontName, 9.5f)
            };
            Controls.Add(t);
            if (!string.IsNullOrEmpty(desc))
            {
                var d = new Label
                {
                    Text = desc,
                    Location = new Point(PadX, y + 19),
                    AutoSize = true,
                    ForeColor = Config.PlaceholderFg,
                    Font = new Font(Config.FontName, 8f),
                    Tag = "sub"
                };
                Controls.Add(d);
            }
            if (right != null)
            {
                int dy = right.Height <= 22 ? 0 : -2;
                right.Location = new Point(contentRight - right.Width, y + dy);
                Controls.Add(right);
            }
            return y + RowH;
        }

        private void _fnAddSep(int y)
        {
            Controls.Add(new Separator
            {
                Location = new Point(PadX, y),
                Size = new Size(Width - PadX * 2, 1),
                BackColor = Config.Border
            });
        }

        private Label _fnMakeHint()
        {
            return new Label
            {
                AutoSize = true,
                ForeColor = Config.PlaceholderFg,
                Font = new Font(Config.FontName, 8f),
                Text = I18n.T("hotkey_click_hint"),
                Tag = "sub"
            };
        }

        private void _fnPlacePill(HotkeyPill pill, int rowY)
        {
            pill.Location = new Point(Width - PadX - pill.Width, rowY - 2);
        }

        // ================= 快捷键录制 =================

        private void _fnStartHoverCapture()
        {
            _fnStopCapture();
            _hoverRecording = true;
            _hoverPill.Recording = true;
            _hoverHintLbl.Text = I18n.T("hotkey_esc_cancel");
        }

        private void _fnStartCardCapture()
        {
            _fnStopCapture();
            _cardRecording = true;
            _cardPill.Recording = true;
            _cardHintLbl.Text = I18n.T("hotkey_esc_cancel");
        }

        private void _fnStopCapture()
        {

            if (_hoverRecording)
            {
                _hoverRecording = false;
                _hoverPill.Recording = false;
                _fnPlacePill(_hoverPill, _hoverPill.Location.Y + 2);
                _hoverHintLbl.Text = I18n.T("hotkey_click_hint");
            }
            if (_cardRecording)
            {
                _cardRecording = false;
                _cardPill.Recording = false;
                _fnPlacePill(_cardPill, _cardPill.Location.Y + 2);
                _cardHintLbl.Text = I18n.T("hotkey_click_hint");
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_hoverRecording || _cardRecording)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                if (e.KeyCode == Keys.Escape) { _fnStopCapture(); return; }
                if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.Menu || e.KeyCode == Keys.ShiftKey) return;
                int mods = 0;
                if ((e.Modifiers & Keys.Control) == Keys.Control) mods |= NativeMethods.MOD_CONTROL;
                if ((e.Modifiers & Keys.Alt) == Keys.Alt) mods |= NativeMethods.MOD_ALT;
                if ((e.Modifiers & Keys.Shift) == Keys.Shift) mods |= NativeMethods.MOD_SHIFT;
                if (_hoverRecording) { _hoverMods = mods; _hoverKey = (int)e.KeyCode; _hoverPill.SetHotkey(mods, (int)e.KeyCode); }
                else { _cardMods = mods; _cardKey = (int)e.KeyCode; _cardPill.SetHotkey(mods, (int)e.KeyCode); }
                _fnStopCapture();
                return;
            }
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
        }

        private void _fnUpdateHoverState()
        {
            bool en = _hoverToggle.On;
            _hoverPill.Enabled = en;
            _hoverHintLbl.Text = en ? (_hoverRecording ? I18n.T("hotkey_esc_cancel") : I18n.T("hotkey_click_hint")) : I18n.T("hotkey_disabled");
        }

        private void _fnUpdateCardState()
        {
            bool en = _cardToggle.On;
            _cardPill.Enabled = en;
            _cardHintLbl.Text = en ? (_cardRecording ? I18n.T("hotkey_esc_cancel") : I18n.T("hotkey_click_hint")) : I18n.T("hotkey_disabled");
        }

        // ================= 主题 =================

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Win11.ApplyRounded(Handle, Config.Theme != "light");
        }

        private void ApplyThemeColors()
        {
            BackColor = Config.BG;
            ForeColor = Config.FG;
            foreach (Control c in Controls)
            {
                if (c is Separator) { c.BackColor = Config.Border; continue; }
                if (c is ToggleSwitch || c is HotkeyPill || c is DelayStepper || c is TagLabel) { c.Invalidate(); continue; }
                if (c is ComboBox || c is PictureBox) continue;
                if ((c.Tag as string) == "sub") { c.ForeColor = Config.PlaceholderFg; continue; }
                c.BackColor = Config.BG;
                c.ForeColor = Config.FG;
                var link = c as LinkLabel;
                if (link != null) link.LinkColor = Config.Accent;
            }
            if (_okBtn != null) { _okBtn.BackColor = Config.Accent; _okBtn.FlatAppearance.MouseOverBackColor = Config.Accent; }
            if (_cancelBtn != null) { _cancelBtn.FlatAppearance.BorderColor = Config.Border; }
            if (_verLbl != null) _verLbl.ForeColor = Config.PlaceholderFg;
            if (_closeBtn != null) { _closeBtn.ForeColor = Config.PlaceholderFg; _closeBtn.BackColor = Config.BG; }
        }

        // ================= 静态展示工具（Program.cs 依赖） =================

        public static string ModsToString(int mods)
        {
            var parts = new List<string>();
            if ((mods & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((mods & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
            if ((mods & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
            return parts.Count > 0 ? string.Join("+", parts) + "+" : "";
        }

        public static string KeyToString(int key)
        {
            if (key == 0) return I18n.T("hotkey_none");
            return ((Keys)key).ToString();
        }

        public static string HotkeyDisplay(bool enabled, int mods, int key)
        {
            if (!enabled) return I18n.T("hotkey_disabled");
            return ModsToString(mods) + KeyToString(key);
        }

        // ================= 自绘控件 =================

        private class Separator : Panel { }

        // 圆角开关：激活时 Accent 底 + 白色圆滑块
        private class ToggleSwitch : Control
        {
            private bool _on;
            public event EventHandler Toggled;

            public bool On
            {
                get { return _on; }
                set { if (_on == value) return; _on = value; Invalidate(); }
            }

            public ToggleSwitch()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                SetStyle(ControlStyles.Selectable, false);
                Size = new Size(44, 22);
                Cursor = Cursors.Hand;
                Click += (s, e) => { On = !On; if (Toggled != null) Toggled(this, EventArgs.Empty); };
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Color track;
                if (!Enabled) track = Config.Border;
                else if (_on) track = Config.Accent;
                else track = Config.Theme != "light" ? ColorTranslator.FromHtml("#3a3a42") : ColorTranslator.FromHtml("#d4d4da");
                using (var path = _fnRoundRect(0.5f, 0.5f, Width - 1, Height - 1, (Height - 1) / 2f))
                using (var b = new SolidBrush(track))
                    g.FillPath(b, path);
                int d = Height - 4;
                int x = _on ? Width - d - 2 : 2;
                using (var b = new SolidBrush(Color.White))
                    g.FillEllipse(b, x, 2, d, d);
            }
        }

        // 键位胶囊：[Ctrl]+[Alt]+[Q]，点击进入录制
        private class HotkeyPill : Control
        {
            private int _mods;
            private int _key;
            private bool _rec;
            public event EventHandler RecordClick;

            public bool Recording
            {
                get { return _rec; }
                set { _rec = value; _fnRelayout(); Invalidate(); }
            }

            public HotkeyPill()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
                SetStyle(ControlStyles.Selectable, false);
                Cursor = Cursors.Hand;
                Size = new Size(60, 24);
                Click += (s, e) => { if (Enabled && RecordClick != null) RecordClick(this, EventArgs.Empty); };
            }

            public void SetHotkey(int mods, int key)
            {
                _mods = mods;
                _key = key;
                _fnRelayout();
                Invalidate();
            }

            private List<string> _fnParts()
            {
                if (_rec) return new List<string> { I18n.T("hotkey_press_key") };
                var parts = new List<string>();
                if ((_mods & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
                if ((_mods & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
                if ((_mods & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
                parts.Add(SettingsForm.KeyToString(_key));
                return parts;
            }

            private void _fnRelayout()
            {
                var parts = _fnParts();
                int w = 0;
                using (var f = new Font(Config.FontName, 8.5f))
                {
                    foreach (var p in parts)
                    {
                        var sz = TextRenderer.MeasureText(p, f, Size.Empty, TextFormatFlags.NoPadding);
                        w += sz.Width + 16;
                    }
                }
                w += Math.Max(0, parts.Count - 1) * 16;
                Size = new Size(Math.Max(w, 60), 24);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var parts = _fnParts();
                using (var f = new Font(Config.FontName, 8.5f))
                {
                    int x = 0;
                    for (int i = 0; i < parts.Count; i++)
                    {
                        int w = TextRenderer.MeasureText(parts[i], f, Size.Empty, TextFormatFlags.NoPadding).Width + 16;
                        Color bg = !Enabled ? Config.BG : (_rec ? Config.Accent : Config.BgHover);
                        Color border = _rec ? Config.Accent : Config.Border;
                        Color fg = !Enabled ? Config.PlaceholderFg : (_rec ? Color.White : Config.FG);
                        using (var path = _fnRoundRect(x + 0.5f, 1.5f, w - 1, Height - 3, 4))
                        {
                            using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                            using (var p = new Pen(border)) g.DrawPath(p, path);
                        }
                        TextRenderer.DrawText(g, parts[i], f, new Rectangle(x, 0, w, Height), fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                        x += w;
                        if (i < parts.Count - 1)
                        {
                            TextRenderer.DrawText(g, "+", f, new Rectangle(x, 0, 16, Height), Enabled ? Config.PlaceholderFg : Config.Border, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                            x += 16;
                        }
                    }
                }
            }
        }

        // 步进器：[-] 0.50 秒 [+]
        private class DelayStepper : Control
        {
            private double _val = 0.30;

            public double Value
            {
                get { return _val; }
                set { _val = Math.Max(0.10, Math.Min(3.00, Math.Round(value, 2))); Invalidate(); }
            }

            public DelayStepper()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
                SetStyle(ControlStyles.Selectable, false);
                Size = new Size(116, 26);
                Cursor = Cursors.Hand;
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.X < 22) Value = _val - 0.05;
                else if (e.X > Width - 22) Value = _val + 0.05;
                else return;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = _fnRoundRect(0.5f, 0.5f, Width - 1, Height - 1, 4))
                {
                    using (var b = new SolidBrush(Config.BgHover)) g.FillPath(b, path);
                    using (var p = new Pen(Config.Border)) g.DrawPath(p, path);
                }
                using (var f = new Font(Config.FontName, 8.5f))
                {
                    TextRenderer.DrawText(g, "−", f, new Rectangle(0, 0, 22, Height), Config.FG, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    TextRenderer.DrawText(g, "+", f, new Rectangle(Width - 22, 0, 22, Height), Config.FG, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    string mid = string.Format("{0:0.00} {1}", _val, I18n.T("delay_unit"));
                    TextRenderer.DrawText(g, mid, f, new Rectangle(22, 0, Width - 44, Height), Config.FG, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                using (var p = new Pen(Config.Border))
                {
                    g.DrawLine(p, 22, 4, 22, Height - 4);
                    g.DrawLine(p, Width - 22, 4, Width - 22, Height - 4);
                }
            }
        }

        // 状态标签：「暂停开发」
        private class TagLabel : Control
        {
            public TagLabel(string text)
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
                SetStyle(ControlStyles.Selectable, false);
                using (var f = new Font(Config.FontName, 8f))
                {
                    var sz = TextRenderer.MeasureText(text, f, Size.Empty, TextFormatFlags.NoPadding);
                    Size = new Size(sz.Width + 14, 22);
                }
                _text = text;
            }

            private readonly string _text;

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                bool dark = Config.Theme != "light";
                Color bg = dark ? ColorTranslator.FromHtml("#453a15") : ColorTranslator.FromHtml("#fdf0cd");
                Color fg = dark ? ColorTranslator.FromHtml("#e5b84f") : ColorTranslator.FromHtml("#a8720a");
                using (var path = _fnRoundRect(0.5f, 0.5f, Width - 1, Height - 1, 4))
                using (var b = new SolidBrush(bg))
                    g.FillPath(b, path);
                using (var f = new Font(Config.FontName, 8f))
                {
                    TextRenderer.DrawText(g, _text, f, new Rectangle(0, 0, Width, Height), fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
        }

        private static GraphicsPath _fnRoundRect(float x, float y, float w, float h, float r)
        {
            var path = new GraphicsPath();
            float d = Math.Min(r * 2, Math.Min(w, h));
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}