using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MediaElement = System.Windows.Controls.MediaElement;
using MediaState = System.Windows.Controls.MediaState;
using WpfStretch = System.Windows.Media.Stretch;
using ExceptionRoutedEventArgs = System.Windows.ExceptionRoutedEventArgs;

using WinPanel = System.Windows.Forms.Panel;
using WinControl = System.Windows.Forms.Control;
using WinImage = System.Drawing.Image;
using DrawingSize = System.Drawing.Size;
using Application = System.Windows.Application;
using ElementHost = System.Windows.Forms.Integration.ElementHost;

namespace HoverExplorer
{
    internal static class MediaPreview
    {
        // ===== Shell 缩略图（SVG 渲染图，零依赖）=====
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

        private static readonly Guid IID_ShellItemImageFactory = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx; public int cy; }

        [Flags]
        private enum SIIGBF { ResizeToFit = 0, BiggerSizeOk = 1, MemoryOnly = 2, IconOnly = 4, ThumbnailOnly = 8, InCacheOnly = 0x10 }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        // thumbnailOnly=true：仅接受真缩略图（无扩展时返回 null，避免拿到通用图标误导）
        internal static WinImage ShellThumb(string path, int maxPx, bool thumbnailOnly)
        {
            try
            {
                object obj;
                var iid = IID_ShellItemImageFactory;
                SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out obj);
                var factory = (IShellItemImageFactory)obj;
                IntPtr hbm;
                int hr = factory.GetImage(new SIZE { cx = maxPx, cy = maxPx },
                    thumbnailOnly ? SIIGBF.ThumbnailOnly : SIIGBF.ResizeToFit, out hbm);
                if (hr != 0 || hbm == IntPtr.Zero) return null;
                WinImage img;
                using (var src = Image.FromHbitmap(hbm)) img = new Bitmap(src);
                DeleteObject(hbm);
                return img;
            }
            catch { return null; }
        }

        // ===== WPF MediaElement 播放器（音频+视频统一，Media Foundation 解码，零部署）=====
        private static bool _wpfInited;
        private static void EnsureWpf()
        {
            if (_wpfInited) return;
            if (Application.Current == null) new Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            _wpfInited = true;
        }

        private static string Fmt(TimeSpan t)
        {
            if (t.TotalHours >= 1) return ((int)t.TotalHours).ToString("00") + t.ToString(@"\:mm\:ss");
            return t.ToString(@"mm\:ss");
        }

        internal static WinControl MediaPlayer(string path, bool isVideo, int w, int h)
        {
            EnsureWpf();
            var panel = new WinPanel { Size = new DrawingSize(w, h), BackColor = Config.BG };
            var bar = new WinPanel { Dock = DockStyle.Bottom, Height = 30, BackColor = Config.BG };
            var btn = new Button { Text = "❚❚", Width = 36, Dock = DockStyle.Left, FlatStyle = FlatStyle.Flat, ForeColor = Config.FG, BackColor = Config.BG, Font = new Font("Microsoft YaHei", 9) };
            // 去掉 Flat 按钮的反色边框与悬停反色，保持与主题一致
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Config.BgHover;
            btn.FlatAppearance.MouseDownBackColor = Config.BgHover;
            var lblTime = new Label { Width = 110, Dock = DockStyle.Right, Text = "00:00 / --:--", TextAlign = ContentAlignment.MiddleRight, ForeColor = Config.FgDir, BackColor = Config.BG, AutoEllipsis = true, Font = new Font("Microsoft YaHei", 8) };
            var prog = new SeekBar { Dock = DockStyle.Fill };
            bar.Controls.Add(prog);
            bar.Controls.Add(btn);
            bar.Controls.Add(lblTime);

            var host = new ElementHost { Dock = DockStyle.Fill, BackColorTransparent = true };
            var me = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Close,
                ScrubbingEnabled = true,
                Stretch = WpfStretch.Uniform
            };
            host.Child = me;

            if (isVideo)
            {
                panel.Controls.Add(host);
                panel.Controls.Add(bar);
            }
            else
            {
                var lblName = new Label { Dock = DockStyle.Top, Height = 20, Text = Path.GetFileName(path), AutoEllipsis = true, ForeColor = Config.FG, BackColor = Config.BG, Font = new Font("Microsoft YaHei", 9) };
                panel.Controls.Add(host);
                panel.Controls.Add(lblName);
                panel.Controls.Add(bar);
            }

            bool playing = true;
            bool loaded = false;
            TimeSpan total = TimeSpan.Zero;
            me.MediaOpened += delegate
            {
                loaded = true;
                Program.DebugLog("[mp] media-opened " + (isVideo ? "video" : "audio") + " " + path);
                if (me.NaturalDuration.HasTimeSpan) total = me.NaturalDuration.TimeSpan;
                // 视频拿到真实分辨率后，让预览窗按该宽高比调整（音频没有画面，不动）
                // WPF 宿主（ElementHost/MediaElement）初始化完成后再保一次顶：
                // 媒体面板加载期间窗口易被刚激活的资源管理器压到下面
                try
                {
                    var owner = panel.FindForm();
                    if (owner != null && owner.IsHandleCreated)
                        PopupForm.EnsureWinTopmost(owner.Handle, "media-opened " + (isVideo ? "video" : "audio"));
                }
                catch { }
                if (isVideo && me.NaturalVideoWidth > 0 && me.NaturalVideoHeight > 0)
                {
                    try
                    {
                        var form = panel.FindForm() as PreviewForm;
                        if (form != null) form.FitVideoAspect(me.NaturalVideoWidth, me.NaturalVideoHeight);
                    }
                    catch { }
                }
            };
            me.MediaFailed += delegate(object s2, ExceptionRoutedEventArgs e2)
            {
                if (isVideo)
                {
                    // 解码失败才回退缩略图：按面板实际宽度取图，避免放大后是拉糊的小图
                    var thumb = ShellThumb(path, Math.Max(Config.PreviewMax * 2, panel.Width), false);
                    if (thumb != null)
                    {
                        host.Hide();
                        var pb = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = thumb, BackColor = Color.Black };
                        panel.Controls.Add(pb);
                    }
                }
                btn.Enabled = false;
                lblTime.Text = "解码失败";
            };
            me.MediaEnded += delegate
            {
                playing = false;
                btn.Text = "▶";
                me.Position = TimeSpan.Zero;
            };
            btn.Click += delegate
            {
                if (playing)
                {
                    me.Pause();
                    btn.Text = "▶";
                    playing = false;
                }
                else
                {
                    me.Play();
                    btn.Text = "❚❚";
                    playing = true;
                }
            };
            var tick = new Timer { Interval = 500 };
            tick.Tick += delegate
            {
                if (!loaded || total <= TimeSpan.Zero) return;
                lblTime.Text = Fmt(me.Position) + " / " + Fmt(total);
                prog.Ratio = me.Position.TotalMilliseconds / total.TotalMilliseconds;
                if (playing && me.Position >= total) { playing = false; btn.Text = "▶"; }
            };
            tick.Start();
            prog.Seek += delegate(double ratio)
            {
                if (total <= TimeSpan.Zero) return;
                me.Position = TimeSpan.FromTicks((long)(total.Ticks * ratio));
                prog.Ratio = ratio;
            };
            panel.Disposed += delegate
            {
                try { tick.Stop(); tick.Dispose(); } catch { }
                try { me.Close(); } catch { }
            };
            panel.Tag = me;   // 改名时需要拿到它 Close/重放
            me.Source = new Uri(path, UriKind.Absolute);
            me.Play();
            return panel;
        }

        // 播放中的 MediaElement 占着文件句柄，改名/移动前必须 Close，否则 File.Move 失败
        internal static void StopMedia(WinControl root)
        {
            var me = root.Tag as MediaElement;
            if (me != null) { try { me.Close(); } catch { } }
        }

        // 改名后按新路径重新播放（音频面板顶部的文件名标签一并更新）
        internal static void PlayMedia(WinControl root, string path)
        {
            var me = root.Tag as MediaElement;
            if (me == null) return;
            foreach (WinControl c in root.Controls)
            {
                var lbl = c as Label;
                if (lbl != null && lbl.Dock == DockStyle.Top) lbl.Text = Path.GetFileName(path);
            }
            me.Source = new Uri(path, UriKind.Absolute);
            me.Play();
        }

        // ===== SVG：IE 引擎回退渲染（UI 线程，Shell 无 SVG 缩略图扩展时）=====
        private static bool _emulationSet;
        private static void EnsureBrowserEmulation()
        {
            if (_emulationSet) return;
            _emulationSet = true;
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION"))
                {
                    string exe = AppDomain.CurrentDomain.FriendlyName;
                    if (k.GetValue(exe) == null) k.SetValue(exe, 11001, Microsoft.Win32.RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        // 深色模式下图形垫浅色底，避免透明底深色图形看不清
        internal static WinControl SvgBrowserPanel(string path)
        {
            EnsureBrowserEmulation();
            bool dark = Config.Theme != "light";
            var imgBg = dark ? Color.White : Config.BG;
            var panel = new WinPanel { Size = new DrawingSize(320, 380), BackColor = Config.BG };

            var wb = new WebBrowser { Dock = DockStyle.Fill, ScrollBarsEnabled = false, AllowWebBrowserDrop = false };
            string url = Path.GetFullPath(path).Replace('\\', '/');
            wb.DocumentText = "<html><body style='margin:0;padding:0;background:" + ColorTranslator.ToHtml(imgBg) + ";text-align:center;overflow:hidden'>"
                + "<img src='file:///" + url + "' style='max-width:100%;max-height:100%;vertical-align:middle'/></body></html>";

            string code;
            try
            {
                code = File.ReadAllText(path);
                const int maxLen = 65536;
                if (code.Length > maxLen) code = code.Substring(0, maxLen) + "\r\n... (truncated)";
            }
            catch (Exception ex) { code = ex.Message; }
            var tb = new RichTextBox
            {
                Dock = DockStyle.Bottom,
                Height = 140,
                ReadOnly = true,
                BackColor = Config.BG,
                ForeColor = Config.FG,
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 8),
                Text = code,
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.Both,
            };

            panel.Controls.Add(wb);
            panel.Controls.Add(tb);
            Win11.ApplyScrollTheme(tb, dark);
            return panel;
        }

        // 自绘进度条：系统 ProgressBar 深色模式下无法着色；轨道用主题描边色，已播放部分用强调色
        private class SeekBar : WinControl
        {
            private double _ratio;
            public double Ratio
            {
                get { return _ratio; }
                set { _ratio = value < 0 ? 0 : (value > 1 ? 1 : value); Invalidate(); }
            }
            public event Action<double> Seek;

            public SeekBar()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = Config.BG;
                Cursor = Cursors.Hand;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(BackColor);
                const int trackH = 6;
                int y = (Height - trackH) / 2;
                using (var b = new SolidBrush(Config.Border))
                    g.FillRectangle(b, 4, y, Width - 8, trackH);
                int fw = (int)((Width - 8) * _ratio);
                if (fw > 0)
                    using (var b = new SolidBrush(Config.Accent))
                        g.FillRectangle(b, 4, y, fw, trackH);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left || Seek == null || Width <= 8) return;
                Seek((double)(e.X - 4) / (Width - 8));
            }
        }
    }
}