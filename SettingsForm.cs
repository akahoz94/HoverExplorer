using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace HoverExplorer
{
    internal class SettingsForm : Form
    {
        private CheckBox _enabledChk;
        private CheckBox _ctrlChk;
        private CheckBox _altChk;
        private CheckBox _shiftChk;
        private TextBox _keyBox;
        private Label _currentLbl;
        private Button _okBtn;
        private Button _cancelBtn;
        private Label _hintLbl;
        private NumericUpDown _hoverDelayBox;

        private int _modifiers;
        private int _key;
        private bool _capturing;

        public bool HotkeyEnabled { get; private set; }
        public int HotkeyModifiers { get; private set; }
        public int HotkeyKey { get; private set; }
        public double HoverDelaySetting { get; private set; }

        public SettingsForm()
        {
            Text = I18n.T("settings");
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            Width = 380;
            Height = 500;
            ShowInTaskbar = false;

            HotkeyEnabled = Config.HotkeyEnabled;
            _modifiers = Config.HotkeyModifiers;
            _key = Config.HotkeyKey;

            int y = 15;

            var titleLbl = new Label
            {
                Text = I18n.T("settings_hotkey"),
                Location = new Point(20, y),
                AutoSize = true,
                Font = new Font(Config.FontName, 11, FontStyle.Bold)
            };
            Controls.Add(titleLbl);
            y += 28;

            var descLbl = new Label
            {
                Text = I18n.T("hotkey_desc"),
                Location = new Point(20, y),
                AutoSize = true,
                ForeColor = Config.PlaceholderFg,
                Font = new Font(Config.FontName, 9)
            };
            Controls.Add(descLbl);
            y += 22;

            _enabledChk = new CheckBox
            {
                Text = I18n.T("hotkey_enabled"),
                Location = new Point(20, y),
                AutoSize = true,
                Checked = HotkeyEnabled
            };
            _enabledChk.CheckedChanged += (s, e) => UpdateEnabledState();
            Controls.Add(_enabledChk);
            y += 28;

            var modLbl = new Label { Text = I18n.T("hotkey_modifiers") + ":", Location = new Point(20, y + 4), AutoSize = true };
            Controls.Add(modLbl);

            _ctrlChk = new CheckBox { Text = "Ctrl", Location = new Point(110, y), AutoSize = true, Checked = (_modifiers & NativeMethods.MOD_CONTROL) != 0 };
            _altChk = new CheckBox { Text = "Alt", Location = new Point(170, y), AutoSize = true, Checked = (_modifiers & NativeMethods.MOD_ALT) != 0 };
            _shiftChk = new CheckBox { Text = "Shift", Location = new Point(220, y), AutoSize = true, Checked = (_modifiers & NativeMethods.MOD_SHIFT) != 0 };
            _ctrlChk.CheckedChanged += (s, e) => UpdateCurrent();
            _altChk.CheckedChanged += (s, e) => UpdateCurrent();
            _shiftChk.CheckedChanged += (s, e) => UpdateCurrent();
            Controls.Add(_ctrlChk);
            Controls.Add(_altChk);
            Controls.Add(_shiftChk);
            y += 30;

            var keyLbl = new Label { Text = I18n.T("hotkey_key") + ":", Location = new Point(20, y + 4), AutoSize = true };
            Controls.Add(keyLbl);

            _keyBox = new TextBox
            {
                Location = new Point(110, y),
                Width = 90,
                ReadOnly = true,
                Text = KeyToString(_key),
                TextAlign = HorizontalAlignment.Center
            };
            _keyBox.Click += (s, e) => StartCapture();
            _keyBox.KeyDown += (s, e) =>
            {
                if (!_capturing) return;
                if (e.KeyCode == Keys.Escape)
                {
                    StopCapture();
                    return;
                }
                if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.Menu || e.KeyCode == Keys.ShiftKey) return;
                _key = (int)e.KeyCode;
                StopCapture();
                UpdateCurrent();
            };
            Controls.Add(_keyBox);

            _hintLbl = new Label { Location = new Point(210, y + 4), AutoSize = true, ForeColor = Config.PlaceholderFg };
            Controls.Add(_hintLbl);
            y += 30;

            var curTitleLbl = new Label { Text = I18n.T("hotkey_current") + ":", Location = new Point(20, y + 4), AutoSize = true };
            Controls.Add(curTitleLbl);
            _currentLbl = new Label { Location = new Point(110, y + 4), AutoSize = true, Font = new Font(Config.FontName, 10, FontStyle.Bold) };
            Controls.Add(_currentLbl);
            UpdateCurrent();
            y += 35;

            var hoverTitleLbl = new Label
            {
                Text = I18n.T("hover_delay"),
                Location = new Point(20, y + 4),
                AutoSize = true,
                Font = new Font(Config.FontName, 11, FontStyle.Bold)
            };
            Controls.Add(hoverTitleLbl);
            y += 28;

            _hoverDelayBox = new NumericUpDown
            {
                Location = new Point(20, y),
                Width = 80,
                Minimum = 0.1m,
                Maximum = 3.0m,
                Increment = 0.05m,
                DecimalPlaces = 2,
                Value = (decimal)Config.HoverDelay,
            };
            Controls.Add(_hoverDelayBox);
            var hoverHintLbl = new Label { Text = I18n.T("hover_delay_desc"), Location = new Point(110, y + 4), AutoSize = true, ForeColor = Config.PlaceholderFg, Font = new Font(Config.FontName, 9) };
            Controls.Add(hoverHintLbl);
            y += 35;

            _okBtn = new Button { Text = I18n.T("settings_ok"), Location = new Point(170, y), Size = new Size(85, 28) };
            _okBtn.Click += (s, e) =>
            {
                HotkeyEnabled = _enabledChk.Checked;
                HotkeyModifiers = (_ctrlChk.Checked ? NativeMethods.MOD_CONTROL : 0)
                                | (_altChk.Checked ? NativeMethods.MOD_ALT : 0)
                                | (_shiftChk.Checked ? NativeMethods.MOD_SHIFT : 0);
                HotkeyKey = _key;
                HoverDelaySetting = (double)_hoverDelayBox.Value;
                DialogResult = DialogResult.OK;
                Close();
            };
            _cancelBtn = new Button { Text = I18n.T("settings_cancel"), Location = new Point(265, y), Size = new Size(85, 28) };
            _cancelBtn.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(_okBtn);
            Controls.Add(_cancelBtn);

            // 赞赏：二维码 + 微信号（点击复制）
            EventHandler copyWx = (s, e) =>
            {
                try { Clipboard.SetText("jtugqivi"); MessageBox.Show(I18n.T("donated"), I18n.T("donate"), MessageBoxButtons.OK, MessageBoxIcon.Information); } catch { }
            };
            int dy = y + 38;
            int linkX = 20, linkY = dy;
            // 二维码为嵌入资源，不再依赖外部文件
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
                    Location = new Point(20, dy),
                    Size = new Size(150, 150),
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Image = qrImg,
                    Cursor = Cursors.Hand,
                };
                qr.Click += copyWx;
                Controls.Add(qr);
                linkX = 180; linkY = dy + 64;
            }
            var donateLbl = new LinkLabel
            {
                Text = "vx:jtugqivi",
                Location = new Point(linkX, linkY),
                AutoSize = true,
                Font = new Font(Config.FontName, 9)
            };
            donateLbl.Click += copyWx;
            Controls.Add(donateLbl);

            AcceptButton = _okBtn;
            CancelButton = _cancelBtn;

            ApplyThemeColors();
            UpdateEnabledState();
        }

        private void StartCapture()
        {
            _capturing = true;
            _keyBox.Text = I18n.T("hotkey_press_key");
            _keyBox.BackColor = Color.LightYellow;
            _hintLbl.Text = I18n.T("hotkey_esc_cancel");
        }

        private void StopCapture()
        {
            _capturing = false;
            _keyBox.Text = KeyToString(_key);
            _keyBox.BackColor = Config.BgHover;
            _hintLbl.Text = I18n.T("hotkey_click_hint");
        }

        private void UpdateCurrent()
        {
            int mods = (_ctrlChk.Checked ? NativeMethods.MOD_CONTROL : 0)
                     | (_altChk.Checked ? NativeMethods.MOD_ALT : 0)
                     | (_shiftChk.Checked ? NativeMethods.MOD_SHIFT : 0);
            _currentLbl.Text = ModsToString(mods) + KeyToString(_key);
        }

        private void UpdateEnabledState()
        {
            bool en = _enabledChk.Checked;
            _ctrlChk.Enabled = en;
            _altChk.Enabled = en;
            _shiftChk.Enabled = en;
            _keyBox.Enabled = en;
            if (en)
            {
                _hintLbl.Text = _capturing ? I18n.T("hotkey_esc_cancel") : I18n.T("hotkey_click_hint");
            }
            else
            {
                _hintLbl.Text = I18n.T("hotkey_disabled");
            }
        }

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

        private void ApplyThemeColors()
        {
            BackColor = Config.BG;
            ForeColor = Config.FG;
            foreach (Control c in Controls)
            {
                c.BackColor = Config.BG;
                c.ForeColor = Config.FG;
                var link = c as LinkLabel;
                if (link != null) link.LinkColor = Config.Accent;
            }
            _keyBox.BackColor = Config.BgHover;
            _keyBox.ForeColor = Config.FG;
            _keyBox.BorderStyle = BorderStyle.FixedSingle;
            _currentLbl.ForeColor = Config.FgDir;
            _hintLbl.ForeColor = Config.PlaceholderFg;
        }
    }
}