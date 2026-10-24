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
        internal static void EnsureWpf()
        {
            if (_wpfInited) return;
            if (Application.Current == null) new Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            _wpfInited = true;
        }

        internal static string Fmt(TimeSpan t)
        {
            if (t.TotalHours >= 1) return ((int)t.TotalHours).ToString("00") + t.ToString(@"\:mm\:ss");
            return t.ToString(@"mm\:ss");
        }


        // 媒体面板：视频带底部控制条；音频只返回一个 1×1 的 WPF 宿主，
        // 操作栏（播放键/进度覆盖层/频谱）由 PreviewForm 自绘，播放状态通过 MediaSession 读写
        internal static WinControl MediaPlayer(string path, bool isVideo, int w, int h)
        {
            var panel = new WinPanel { Size = new DrawingSize(w, h), BackColor = Config.BG };
            var session = new MediaSession(path, isVideo);
            panel.Tag = session;
            var host = session.HostView;
            host.Dock = DockStyle.Fill;
            panel.Controls.Add(host);
            // Dock 按 z 序反序计算：宿主置底（索引 0）让它 Fill 残余区
            panel.Controls.SetChildIndex(host, 0);
            panel.Disposed += delegate { session.Close(); };

            if (!isVideo) return panel;   // 音频：画面交给 PreviewForm 的操作栏

            var bar = new WinPanel { Dock = DockStyle.Bottom, Height = 30, BackColor = Config.BG };
            var btn = new Button { Text = "❚❚", Width = 36, Dock = DockStyle.Left, FlatStyle = FlatStyle.Flat, ForeColor = Config.FG, BackColor = Config.BG, Font = new Font("Microsoft YaHei", 9) };
            // 去掉 Flat 按钮的反色边框与悬停反色，保持与主题一致
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Config.BgHover;
            btn.FlatAppearance.MouseDownBackColor = Config.BgHover;
            var lblTime = new Label { Width = 110, Dock = DockStyle.Right, Text = I18n.T("media_loading"), TextAlign = ContentAlignment.MiddleRight, ForeColor = Config.FgDir, BackColor = Config.BG, AutoEllipsis = true, Font = new Font("Microsoft YaHei", 8) };
            var prog = new SeekBar { Dock = DockStyle.Fill };
            bar.Controls.Add(prog);
            bar.Controls.Add(btn);
            bar.Controls.Add(lblTime);
            panel.Controls.Add(bar);

            session.Opened += delegate
            {
                Program.DebugLog("[mp] media-opened video " + path);
                // WPF 宿主初始化完成后保一次顶：加载期间窗口易被刚激活的资源管理器压到下面
                try
                {
                    var owner = panel.FindForm();
                    if (owner != null && owner.IsHandleCreated)
                        PopupForm.EnsureWinTopmost(owner.Handle, "media-opened video");
                }
                catch { }
                // 视频拿到真实分辨率后按该宽高比调整预览窗（音频没有画面，不动）
                if (session.NaturalWidth > 0 && session.NaturalHeight > 0)
                {
                    try
                    {
                        var form = panel.FindForm() as PreviewForm;
                        if (form != null) form.FitVideoAspect(session.NaturalWidth, session.NaturalHeight);
                    }
                    catch { }
                }
            };
            session.Failed += delegate
            {
                // 解码失败才回退缩略图：按面板实际宽度取图，避免放大后是拉糊的小图
                var thumb = ShellThumb(path, Math.Max(Config.PreviewMax * 2, panel.Width), false);
                if (thumb != null)
                {
                    host.Hide();
                    var pb = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = thumb, BackColor = Color.Black };
                    panel.Controls.Add(pb);
                }
                btn.Enabled = false;
                lblTime.Text = "解码失败";
            };
            session.Ended += delegate { btn.Text = "▶"; };

            btn.Click += delegate
            {
                session.Toggle();
                btn.Text = session.IsPlaying ? "❚❚" : "▶";
            };
            var tick = new Timer { Interval = 500 };
            tick.Tick += delegate
            {
                if (!session.IsLoaded || session.Duration <= TimeSpan.Zero) return;
                lblTime.Text = Fmt(session.Position) + " / " + Fmt(session.Duration);
                prog.Ratio = session.Position.TotalMilliseconds / session.Duration.TotalMilliseconds;
                btn.Text = session.IsPlaying ? "❚❚" : "▶";
            };
            tick.Start();
            prog.Seek += delegate(double ratio)
            {
                session.SeekRatio(ratio);
                prog.Ratio = ratio;
            };
            panel.Disposed += delegate
            {
                try { tick.Stop(); tick.Dispose(); } catch { }
            };
            return panel;
        }

        // 快速估算音频时长（不读全文件）：MP3 解析首个帧头（CBR 按比特率 / VBR 按 Xing 帧数），WAV 按头块字节率
        internal static double? EstimateDurationMs(string path)
        {
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    long len = fs.Length;
                    var head = new byte[10];
                    if (fs.Read(head, 0, 10) < 10) return null;
                    // WAV：RIFF....WAVE，fmt 块第 28-31 字节是 byteRate
                    if (head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F' && head[8] == 'W' && head[9] == 'A')
                    {
                        var fmt = new byte[32];
                        fs.Position = 0;
                        if (fs.Read(fmt, 0, 32) < 32) return null;
                        int byteRate = fmt[28] | (fmt[29] << 8) | (fmt[30] << 16) | (fmt[31] << 24);
                        if (byteRate > 0) return (len - 44) * 1000.0 / byteRate;
                        return null;
                    }
                    // MP3：跳过 ID3v2 标签
                    long dataStart = 0;
                    if (head[0] == 'I' && head[1] == 'D' && head[2] == '3')
                        dataStart = 10 + (((head[6] & 0x7f) << 21) | ((head[7] & 0x7f) << 14) | ((head[8] & 0x7f) << 7) | (head[9] & 0x7f));
                    fs.Position = dataStart;
                    var buf = new byte[65536];
                    int n = fs.Read(buf, 0, buf.Length);
                    for (int i = 0; i + 4 <= n; i++)
                    {
                        if (buf[i] != 0xFF || (buf[i + 1] & 0xE0) != 0xE0) continue;
                        int ver = (buf[i + 1] >> 3) & 3;   // 3=MPEG1, 2=MPEG2
                        int layer = (buf[i + 1] >> 1) & 3; // 1=Layer III
                        if (ver == 0 || layer != 1) continue;
                        int brIdx = (buf[i + 2] >> 4) & 0xF;
                        int srIdx = (buf[i + 2] >> 2) & 3;
                        if (brIdx == 0 || brIdx == 15 || srIdx == 3) continue;
                        bool mpeg1 = ver == 3;
                        int[] brT1 = { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0 };
                        int[] brT2 = { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0 };
                        int bitrate = (mpeg1 ? brT1[brIdx] : brT2[brIdx]) * 1000;
                        int[] srT1 = { 44100, 48000, 32000 };
                        int[] srT2 = { 22050, 24000, 16000 };
                        int srate = (mpeg1 ? srT1[srIdx] : srT2[srIdx]);
                        int samplesPerFrame = mpeg1 ? 1152 : 576;
                        // VBR：Xing/Info 标签带总帧数 → 精确
                        int xoff = i + 4 + (mpeg1 ? 32 : 17);
                        if (xoff + 16 <= n
                            && ((buf[xoff] == 'X' && buf[xoff + 1] == 'i' && buf[xoff + 2] == 'n' && buf[xoff + 3] == 'g')
                                || (buf[xoff] == 'I' && buf[xoff + 1] == 'n' && buf[xoff + 2] == 'f' && buf[xoff + 3] == 'o')))
                        {
                            int flags = (buf[xoff + 4] << 24) | (buf[xoff + 5] << 16) | (buf[xoff + 6] << 8) | buf[xoff + 7];
                            if ((flags & 1) != 0)
                            {
                                long frames = ((long)buf[xoff + 8] << 24) | ((long)buf[xoff + 9] << 16) | ((long)buf[xoff + 10] << 8) | (long)buf[xoff + 11];
                                if (frames > 0) return frames * samplesPerFrame * 1000.0 / srate;
                            }
                        }
                        // CBR：按比特率估算（去掉 ID3v1 尾标签 128 字节）
                        long tail = len >= 128 ? 128 : 0;
                        return (len - dataStart - tail) * 8.0 * 1000.0 / bitrate;
                    }
                }
            }
            catch { }
            return null;
        }

        // 播放中的 MediaElement 占着文件句柄，改名/移动前必须 Close，否则 File.Move 失败
        internal static void StopMedia(WinControl root)
        {
            var session = root.Tag as MediaSession;
            if (session != null) { session.Close(); return; }
            var me = root.Tag as MediaElement;
            if (me != null) { try { me.Close(); } catch { } }
        }

        // 改名后按新路径重新播放；标题栏的文件名/格式/图标由 PreviewForm.RestoreContentTo 刷新
        internal static void PlayMedia(WinControl root, string path)
        {
            var session = root.Tag as MediaSession;
            if (session != null) session.Reload(path);
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

    // 播放会话：把 WPF MediaElement 的加载/播放/进度抽出来，视频控制条与音频操作栏共用。
    // 音频窗口不显示画面，但 ElementHost 仍需挂在可见控件树里（取 1×1），否则 WPF 可能停掉播放。
    internal sealed class MediaSession
    {
        private readonly ElementHost _host;
        private readonly MediaElement _me;
        private readonly System.Threading.SynchronizationContext _ctx;
        private bool _loaded;
        private bool _playing;
        private bool _failed;
        private TimeSpan _total;
        private double? _estimateMs;

        internal string Path { get; private set; }
        internal bool IsVideo { get; private set; }

        internal event Action Opened;
        internal event Action Failed;
        internal event Action Ended;
        internal event Action<double?> EstimateReady;

        internal MediaSession(string path, bool isVideo)
        {
            Path = path;
            IsVideo = isVideo;
            MediaPreview.EnsureWpf();
            _ctx = System.Threading.SynchronizationContext.Current;
            _host = new ElementHost { BackColorTransparent = true };
            _me = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Close,
                // 音频关 scrub：MP3 无索引，MF 打开时全文件扫描求时长，scrub 会再拖慢；音频 seek 无需帧精确
                ScrubbingEnabled = isVideo,
                Stretch = WpfStretch.Uniform
            };
            _host.Child = _me;
            _me.MediaOpened += delegate
            {
                _loaded = true;
                if (_me.NaturalDuration.HasTimeSpan) _total = _me.NaturalDuration.TimeSpan;
                var a = Opened; if (a != null) { try { a(); } catch { } }
            };
            _me.MediaFailed += delegate(object s, ExceptionRoutedEventArgs e)
            {
                _failed = true;
                _playing = false;
                var a = Failed; if (a != null) { try { a(); } catch { } }
            };
            _me.MediaEnded += delegate
            {
                _playing = false;
                try { _me.Position = TimeSpan.Zero; } catch { }
                var a = Ended; if (a != null) { try { a(); } catch { } }
            };
            _me.Source = new Uri(path, UriKind.Absolute);
            _me.Play();            // 预览弹出即播放（音频同样自动播）
            _playing = true;
            // 后台快速估算时长：大 MP3 无索引时 MF 求时长较慢，先给个可读的近似值
            System.Threading.Tasks.Task.Run(delegate
            {
                double? est = MediaPreview.EstimateDurationMs(path);
                _estimateMs = est;
                if (_ctx != null) _ctx.Post(delegate
                {
                    var a = EstimateReady; if (a != null) { try { a(est); } catch { } }
                }, null);
            });
        }

        internal WinControl HostView { get { return _host; } }
        internal bool IsLoaded { get { return _loaded; } }
        internal bool IsPlaying { get { return _playing && !_failed; } }
        internal bool HasFailed { get { return _failed; } }
        internal TimeSpan Duration { get { return _total; } }
        internal double? EstimatedMs { get { return _estimateMs; } }
        internal int NaturalWidth { get { try { return _me.NaturalVideoWidth; } catch { return 0; } } }
        internal int NaturalHeight { get { try { return _me.NaturalVideoHeight; } catch { return 0; } } }

        internal TimeSpan Position
        {
            get { try { return _me.Position; } catch { return TimeSpan.Zero; } }
        }

        internal void Play()
        {
            if (_failed) return;
            try { _me.Play(); _playing = true; } catch { }
        }

        internal void Pause()
        {
            try { _me.Pause(); } catch { }
            _playing = false;
        }

        internal void Toggle()
        {
            if (_playing) Pause(); else Play();
        }

        internal void SeekRatio(double ratio)
        {
            if (_total <= TimeSpan.Zero) return;
            if (ratio < 0) ratio = 0;
            if (ratio > 1) ratio = 1;
            try { _me.Position = TimeSpan.FromTicks((long)(_total.Ticks * ratio)); } catch { }
        }

        internal void Reload(string path)
        {
            Path = path;
            _loaded = false;
            _failed = false;
            _total = TimeSpan.Zero;
            try
            {
                _me.Source = new Uri(path, UriKind.Absolute);
                _me.Play();
                _playing = true;
            }
            catch { _playing = false; }
        }

        internal void Close()
        {
            _playing = false;
            try { _me.Close(); } catch { }
        }
    }

    // WASAPI 环回采集 + 手写 FFT：零外部依赖的系统混音频谱。采集不可用时 UI 走包络降级
    internal sealed class AudioMeter : IDisposable
    {
        private const int FftSize = 1024;
        private const int BandCount = 32;

        private readonly object _lock = new object();
        private readonly float[] _bands = new float[BandCount];
        private readonly float[] _ring = new float[FftSize];
        private readonly float[] _re = new float[FftSize];
        private readonly float[] _im = new float[FftSize];
        private float[] _bufF;
        private short[] _bufS;
        private int[] _bufI;
        private int _ringPos;
        private volatile bool _running;
        private volatile bool _failed;
        private System.Threading.Thread _thread;
        private double _sqSum;   // 一个发布周期内的单声道能量累计（Rms 用）
        private int _sqN;
        private float _rms;

        internal bool Failed { get { return _failed; } }

        // 最近一个发布窗口（~33ms）的实测响度，波形图按进度写入包络桶
        internal float Rms { get { return _rms; } }

        internal float[] Levels
        {
            get
            {
                lock (_lock)
                {
                    var copy = new float[BandCount];
                    Array.Copy(_bands, copy, BandCount);
                    return copy;
                }
            }
        }

        // 失败过就不再重启：环回失败通常是持久原因（无默认设备/独占模式），反复重启只会刷日志
        internal void Start()
        {
            if (_failed || _running || _thread != null) return;
            _running = true;
            _thread = new System.Threading.Thread(Loop) { IsBackground = true, Name = "AudioMeter" };
            _thread.SetApartmentState(System.Threading.ApartmentState.MTA);
            _thread.Start();
        }

        // 暂停时停采：线程退出并释放 COM，恢复播放后 Start 重新采集
        internal void Stop()
        {
            _running = false;
            try { if (_thread != null) _thread.Join(300); } catch { }
            _thread = null;
        }

        public void Dispose()
        {
            Stop();
        }

        // ===== COM 互操作 =====
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            void EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
            void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
            void RegisterEndpointNotificationCallback(IntPtr client);
            void UnregisterEndpointNotificationCallback(IntPtr client);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            void Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
                [MarshalAs(UnmanagedType.IUnknown)] out object iface);
            void OpenPropertyStore(int stgmAccess, out IntPtr store);
            void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            void GetState(out int state);
        }

        [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioClient
        {
            void Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
            void GetBufferSize(out int frames);
            void GetStreamLatency(out long latency);
            void GetCurrentPadding(out int padding);
            void IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
            void GetMixFormat(out IntPtr format);
            void GetDevicePeriod(out long defaultPeriod, out long minPeriod);
            void Start();
            void Stop();
            void Reset();
            void SetEventHandle(IntPtr handle);
            [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
        }

        [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioCaptureClient
        {
            [PreserveSig] int GetBuffer(out IntPtr data, out int frames, out int flags, out long devicePosition, out long qpcPosition);
            [PreserveSig] int ReleaseBuffer(int frames);
            [PreserveSig] int GetNextPacketSize(out int frames);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEFORMATEX
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        private static readonly Guid IID_IAudioClient = new Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
        private static readonly Guid IID_IAudioCaptureClient = new Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
        private static readonly Guid KsFloat = new Guid("00000003-0000-0010-8000-00AA00389B71");

        private const int ShareShared = 0;
        private const int FlagLoopback = 0x00020000;
        private const int BufferSilent = 0x2;

        private void Loop()
        {
            IMMDeviceEnumerator en = null;
            IMMDevice dev = null;
            IAudioClient client = null;
            IAudioCaptureClient cap = null;
            IntPtr fmt = IntPtr.Zero;
            try
            {
                en = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();
                en.GetDefaultAudioEndpoint(0, 0, out dev);
                object obj;
                var iid = IID_IAudioClient;
                dev.Activate(ref iid, 23, IntPtr.Zero, out obj);
                client = (IAudioClient)obj;
                client.GetMixFormat(out fmt);
                var wf = (WAVEFORMATEX)Marshal.PtrToStructure(fmt, typeof(WAVEFORMATEX));
                bool isFloat = wf.wFormatTag == 3;
                if (wf.wFormatTag == 0xFFFE)
                {
                    var sub = (Guid)Marshal.PtrToStructure(new IntPtr(fmt.ToInt64() + 24), typeof(Guid));
                    isFloat = sub == KsFloat;
                }
                int channels = wf.nChannels < 1 ? 1 : wf.nChannels;
                int bits = wf.wBitsPerSample;
                uint rate = wf.nSamplesPerSec;
                // 共享模式 + 环回：bufferDuration/periodicity 传 0 由音频引擎决定
                client.Initialize(ShareShared, FlagLoopback, 0, 0, fmt, IntPtr.Zero);
                object capObj;
                var ciid = IID_IAudioCaptureClient;
                client.GetService(ref ciid, out capObj);
                cap = (IAudioCaptureClient)capObj;
                client.Start();
                Program.DebugLog("[meter] loopback " + rate + "Hz " + channels + "ch " + bits + "bit float=" + isFloat);

                int lastPublish = Environment.TickCount;
                while (_running)
                {
                    int packet;
                    if (cap.GetNextPacketSize(out packet) != 0 || packet <= 0)
                    {
                        System.Threading.Thread.Sleep(10);
                        continue;
                    }
                    while (packet > 0 && _running)
                    {
                        IntPtr data;
                        int frames, flags;
                        long devPos, qpc;
                        if (cap.GetBuffer(out data, out frames, out flags, out devPos, out qpc) != 0) break;
                        if (frames > 0) Feed(data, frames, channels, bits, isFloat, (flags & BufferSilent) != 0);
                        cap.ReleaseBuffer(frames);
                        if (cap.GetNextPacketSize(out packet) != 0) break;
                    }
                    int now = Environment.TickCount;
                    if (now - lastPublish >= 33)
                    {
                        lastPublish = now;
                        PublishBands(rate);
                    }
                }
            }
            catch (Exception ex)
            {
                _failed = true;
                Program.DebugLog("[meter] unavailable: " + ex.Message);
            }
            finally
            {
                try { if (client != null) client.Stop(); } catch { }
                try { if (fmt != IntPtr.Zero) Marshal.FreeCoTaskMem(fmt); } catch { }
                try { if (cap != null) Marshal.ReleaseComObject(cap); } catch { }
                try { if (client != null) Marshal.ReleaseComObject(client); } catch { }
                try { if (dev != null) Marshal.ReleaseComObject(dev); } catch { }
                try { if (en != null) Marshal.ReleaseComObject(en); } catch { }
            }
        }

        private void Feed(IntPtr data, int frames, int ch, int bits, bool isFloat, bool silent)
        {
            int n = frames * ch;
            if (silent || data == IntPtr.Zero)
            {
                for (int i = 0; i < frames; i++) Push(0f);
                return;
            }
            if (isFloat && bits == 32)
            {
                if (_bufF == null || _bufF.Length < n) _bufF = new float[n];
                Marshal.Copy(data, _bufF, 0, n);
                for (int i = 0; i < frames; i++)
                {
                    float s = 0f;
                    for (int c = 0; c < ch; c++) s += _bufF[i * ch + c];
                    Push(s / ch);
                }
            }
            else if (bits == 16)
            {
                if (_bufS == null || _bufS.Length < n) _bufS = new short[n];
                Marshal.Copy(data, _bufS, 0, n);
                for (int i = 0; i < frames; i++)
                {
                    float s = 0f;
                    for (int c = 0; c < ch; c++) s += _bufS[i * ch + c] / 32768f;
                    Push(s / ch);
                }
            }
            else if (bits == 32)
            {
                if (_bufI == null || _bufI.Length < n) _bufI = new int[n];
                Marshal.Copy(data, _bufI, 0, n);
                for (int i = 0; i < frames; i++)
                {
                    float s = 0f;
                    for (int c = 0; c < ch; c++) s += _bufI[i * ch + c] / 2147483648f;
                    Push(s / ch);
                }
            }
        }

        private void Push(float v)
        {
            _ring[_ringPos] = v;
            _ringPos = (_ringPos + 1) % FftSize;
            _sqSum += (double)v * v;
            _sqN++;
        }

        private void PublishBands(uint rate)
        {
            // RMS 滚动发布：无数据（静音/暂停）时按 0.6 衰减归零
            if (_sqN > 0) _rms = (float)Math.Sqrt(_sqSum / _sqN);
            else _rms *= 0.6f;
            _sqSum = 0;
            _sqN = 0;
            if (rate < 8000) return;
            // Hann 窗 + 循环缓冲顺序化
            int p = _ringPos;
            for (int i = 0; i < FftSize; i++)
            {
                float w = 0.5f - 0.5f * (float)Math.Cos(2 * Math.PI * i / (FftSize - 1));
                _re[i] = _ring[p] * w;
                _im[i] = 0f;
                p = (p + 1) % FftSize;
            }
            Fft(_re, _im, FftSize);
            double fLo = 55.0, fHi = Math.Min(16000.0, rate / 2.0);
            float half = FftSize / 2f;
            for (int b = 0; b < BandCount; b++)
            {
                double f0 = fLo * Math.Pow(fHi / fLo, (double)b / BandCount);
                double f1 = fLo * Math.Pow(fHi / fLo, (double)(b + 1) / BandCount);
                int k0 = (int)(f0 * FftSize / rate);
                int k1 = (int)(f1 * FftSize / rate);
                if (k0 < 1) k0 = 1;
                if (k1 <= k0) k1 = k0 + 1;
                if (k1 > FftSize / 2) k1 = FftSize / 2;
                float mag = 0f;
                for (int k = k0; k < k1; k++)
                {
                    float rr = _re[k], ii = _im[k];
                    float m = (float)Math.Sqrt(rr * rr + ii * ii) / half;
                    if (m > mag) mag = m;
                }
                float db = 20f * (float)Math.Log10(mag + 1e-6f);
                float v = (db + 62f) / 56f;   // -62dB..-6dB → 0..1
                if (v < 0f) v = 0f;
                if (v > 1f) v = 1f;
                v = (float)Math.Sqrt(v);
                lock (_lock) _bands[b] = Math.Max(v, _bands[b] * 0.78f);
            }
        }

        // 迭代基-2 FFT（原地）
        private static void Fft(float[] re, float[] im, int n)
        {
            int j = 0;
            for (int i = 1; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    float tr = re[i]; re[i] = re[j]; re[j] = tr;
                    float ti = im[i]; im[i] = im[j]; im[j] = ti;
                }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2 * Math.PI / len;
                float wr = (float)Math.Cos(ang), wi = (float)Math.Sin(ang);
                int halfLen = len >> 1;
                for (int i = 0; i < n; i += len)
                {
                    float cr = 1f, ci = 0f;
                    for (int k = 0; k < halfLen; k++)
                    {
                        float ur = re[i + k], ui = im[i + k];
                        float vr = re[i + k + halfLen] * cr - im[i + k + halfLen] * ci;
                        float vi = re[i + k + halfLen] * ci + im[i + k + halfLen] * cr;
                        re[i + k] = ur + vr; im[i + k] = ui + vi;
                        re[i + k + halfLen] = ur - vr; im[i + k + halfLen] = ui - vi;
                        float ncr = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = ncr;
                    }
                }
            }
        }
    }

    // 频谱条已由波形图（WaveView）取代，此控件移除

    // 波形图：外层峰值带（浅）+ 内层 RMS 带（深）中线镜像；已播=强调色、未播=弱化色、位置竖线。
    // 数据来自 WaveDecoder 全量摘要（失败时回退环回渐进包络，峰值=RMS）。
    // 交互：点击/横向拖动 = seek；竖向拖动 >4px = 移动窗口（MoveWindow 交给宿主）
    internal sealed class WaveView : WinControl
    {
        private static readonly float[] Empty = new float[0];
        private float[] _rms = Empty, _peak = Empty;
        private double _pos;
        private bool _down, _scrubbed, _winMoved;
        private Point _downPt;
        public event Action<double> Seek;
        public event Action MoveWindow;

        internal WaveView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Config.BG;
            Cursor = Cursors.Hand;
        }

        internal void SetData(float[] rms, float[] peak, double posRatio)
        {
            _rms = rms ?? Empty;
            _peak = peak ?? Empty;
            _pos = posRatio < 0 ? 0 : (posRatio > 1 ? 1 : posRatio);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            int mid = Height / 2;
            int half = Math.Max(2, Height / 2 - 1);
            int n = Math.Min(_rms.Length, _peak.Length);
            int pw = (int)Math.Round(Width * _pos);
            using (var pkPlayed = new SolidBrush(Color.FromArgb(110, Config.Accent)))
            using (var rmPlayed = new SolidBrush(Color.FromArgb(225, Config.Accent)))
            using (var pkRest = new SolidBrush(Color.FromArgb(50, Config.PlaceholderFg)))
            using (var rmRest = new SolidBrush(Color.FromArgb(120, Config.PlaceholderFg)))
            {
                for (int x = 0; x < Width; x++)
                {
                    float pk = 0f, rm = 0f;
                    if (n > 0)
                    {
                        int idx = x * n / Width;
                        if (idx >= n) idx = n - 1;
                        pk = _peak[idx];
                        rm = _rms[idx];
                        if (pk < 0f) pk = 0f; else if (pk > 1f) pk = 1f;
                        if (rm < 0f) rm = 0f; else if (rm > 1f) rm = 1f;
                    }
                    bool played = x < pw;
                    int ph = Math.Max(1, (int)Math.Round(pk * half));
                    int rh = Math.Max(1, (int)Math.Round(rm * half));
                    g.FillRectangle(played ? pkPlayed : pkRest, x, mid - ph, 1, ph * 2);
                    g.FillRectangle(played ? rmPlayed : rmRest, x, mid - rh, 1, rh * 2);
                }
            }
            if (pw > 0 && pw < Width)
                using (var p = new Pen(Config.Accent))
                    g.DrawLine(p, pw, 1, pw, Height - 2);
        }

        private void SeekAt(int x)
        {
            if (Width <= 0) return;
            double r = (double)x / Width;
            if (r < 0) r = 0;
            if (r > 1) r = 1;
            var s = Seek;
            if (s != null) s(r);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _down = true;
            _scrubbed = false;
            _winMoved = false;
            _downPt = e.Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_down || _winMoved) return;
            int dx = e.X - _downPt.X;
            int dy = e.Y - _downPt.Y;
            // 竖向位移先超阈值 → 拖动移动窗口（横向是 scrub 语义，不能用来移窗）
            if (Math.Abs(dy) > 4 && Math.Abs(dy) > Math.Abs(dx))
            {
                _winMoved = true;
                var m = MoveWindow;
                if (m != null) m();
                return;
            }
            if (Math.Abs(dx) > 3)
            {
                _scrubbed = true;
                SeekAt(e.X);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_down && !_scrubbed && !_winMoved) SeekAt(e.X);   // 原地点击 = 定点 seek
            _down = false;
        }
    }
}