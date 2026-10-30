using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace HoverExplorer
{
    // MP3 的 MCI 播放会话：Windows 内置 MPEG 解码，open 只读头部毫秒级返回
    // （Media Foundation 对无索引 MP3 打开要全文件扫描求时长，大文件数秒）。
    // 接口与 MediaSession 一致（IMediaSession），由 PreviewForm.SetupAudio 驱动。
    // 结束事件靠轮询（MCI 无回调），由会话自带定时器完成。
    internal sealed class MciAudioSession : IMediaSession
    {
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern int mciSendString(string command, StringBuilder ret, int retLen, IntPtr hwndCallback);

        private static int _aliasSeq;
        private readonly string _alias;
        private string _path;
        private bool _opened, _failed, _playing, _paused, _endedFired;
        private double _speed = 1.0;
        private TimeSpan _total;
        private Timer _poll;

        public event Action Opened;
        public event Action Failed;
        public event Action Ended;
        public event Action<double?> EstimateReady;

        public string Path { get { return _path; } }
        public bool IsVideo { get { return false; } }
        public System.Windows.Forms.Control HostView { get { return null; } }
        public bool IsLoaded { get { return _opened; } }
        public bool HasFailed { get { return _failed; } }
        public bool IsPlaying { get { return _playing && !_endedFired; } }
        public TimeSpan Duration { get { return _total; } }
        public double? EstimatedMs { get { return _total.TotalMilliseconds > 0 ? _total.TotalMilliseconds : (double?)null; } }
        public int NaturalWidth { get { return 0; } }
        public int NaturalHeight { get { return 0; } }

        public MciAudioSession(string path)
        {
            _path = path;
            _alias = "hfa" + System.Threading.Interlocked.Increment(ref _aliasSeq);
            var sb = new StringBuilder(64);
            if (mciSendString("open \"" + path + "\" type mpegvideo alias " + _alias, sb, 64, IntPtr.Zero) != 0)
            {
                _failed = true;
                var f = Failed; if (f != null) { try { f(); } catch { } }
                return;
            }
            mciSendString("set " + _alias + " time format milliseconds", null, 0, IntPtr.Zero);
            _total = ProbeLength(path, SendInt("status " + _alias + " length"));
            _opened = true;
            var o = Opened; if (o != null) { try { o(); } catch { } }
            var e = EstimateReady; if (e != null) { try { e(_total.TotalMilliseconds); } catch { } }
            Play();
            // 结束轮询：MCI 无回调，position ≥ duration 即触发一次 Ended
            _poll = new Timer { Interval = 200 };
            _poll.Tick += (s, ev) =>
            {
                if (_endedFired || !_playing) return;
                var tot = _total.TotalMilliseconds;
                if (tot > 0 && Position.TotalMilliseconds >= tot - 40)
                {
                    _endedFired = true;
                    _playing = false;
                    var en = Ended; if (en != null) { try { en(); } catch { } }
                }
            };
            _poll.Start();
        }

        private int SendInt(string cmd)
        {
            var sb = new StringBuilder(64);
            return mciSendString(cmd, sb, 64, IntPtr.Zero) == 0 ? ToInt(sb.ToString()) : 0;
        }

        // MCI 的 mpegvideo 驱动不读 Xing VBR 头，按"首帧码率×文件大小"估算时长——
        // LAME 的 Xing 帧是 64k 静默帧，VBR 文件时长会被高估数倍（如 5 分钟报成 16 分钟）。
        // 用 EstimateDurationMs（Xing 精确帧数 / CBR 按码率）交叉校验，偏差 >8% 采信解析值
        private static TimeSpan ProbeLength(string path, int mciMs)
        {
            try
            {
                double? est = MediaPreview.EstimateDurationMs(path);
                if (est.HasValue && est.Value > 0)
                {
                    if (mciMs <= 0) return TimeSpan.FromMilliseconds(est.Value);
                    if (Math.Abs(mciMs - est.Value) / est.Value > 0.08)
                    {
                        Program.DebugLog("[audio] mci length " + mciMs + "ms 不可信，改用解析值 " + est.Value + "ms " + MediaPreview.TruncPath(path));
                        return TimeSpan.FromMilliseconds(est.Value);
                    }
                }
            }
            catch { }
            return TimeSpan.FromMilliseconds(Math.Max(0, mciMs));
        }

        private static int ToInt(string s)
        {
            int v;
            return int.TryParse(s.Trim(), out v) ? v : 0;
        }

        public TimeSpan Position
        {
            get
            {
                if (!_opened || _failed) return TimeSpan.Zero;
                return TimeSpan.FromMilliseconds(Math.Max(0, SendInt("status " + _alias + " position")));
            }
        }

        public void Play()
        {
            if (!_opened || _failed) return;
            _endedFired = false;
            mciSendString("play " + _alias, null, 0, IntPtr.Zero);
            _playing = true; _paused = false;
            Program.DebugLog("[audio] play(mci) " + MediaPreview.TruncPath(_path));
        }

        public void Pause()
        {
            if (!_opened || _failed) return;
            mciSendString("pause " + _alias, null, 0, IntPtr.Zero);
            _playing = false; _paused = true;
        }

        public void Toggle() { if (IsPlaying) Pause(); else Play(); }

        // 倍速：MCI 的 speed 以"正常速"为 1000（百分比），与 WPF 的 SpeedRatio 换算
        public double SpeedRatio
        {
            get { return _speed; }
            set
            {
                if (value < 0.25) value = 0.25;
                if (value > 4.0) value = 4.0;
                _speed = value;
                if (!_opened || _failed) return;
                mciSendString("set " + _alias + " speed " + (int)(value * 1000), null, 0, IntPtr.Zero);
            }
        }

        public void SeekRatio(double ratio)
        {
            if (!_opened || _failed || _total <= TimeSpan.Zero) return;
            if (ratio < 0) ratio = 0;
            if (ratio > 1) ratio = 1;
            var ms = (long)(_total.Ticks * ratio) / TimeSpan.TicksPerMillisecond;
            mciSendString("seek " + _alias + " to " + ms, null, 0, IntPtr.Zero);
            if (_playing || !_paused) mciSendString("play " + _alias, null, 0, IntPtr.Zero);
        }

        public void Reload(string path)
        {
            Close();
            _path = path;
            _endedFired = false; _failed = false; _total = TimeSpan.Zero;
            var sb = new StringBuilder(64);
            if (mciSendString("open \"" + path + "\" type mpegvideo alias " + _alias, sb, 64, IntPtr.Zero) == 0)
            {
                mciSendString("set " + _alias + " time format milliseconds", null, 0, IntPtr.Zero);
                _total = ProbeLength(path, SendInt("status " + _alias + " length"));
                _opened = true;
                var o = Opened; if (o != null) { try { o(); } catch { } }
                Play();
                StartEndPoll();
            }
            else _failed = true;
        }

        private void StartEndPoll()
        {
            if (_poll != null) { try { _poll.Stop(); _poll.Dispose(); } catch { } }
            _poll = new Timer { Interval = 200 };
            _poll.Tick += (s, ev) =>
            {
                if (_endedFired || !_playing) return;
                var tot = _total.TotalMilliseconds;
                if (tot > 0 && Position.TotalMilliseconds >= tot - 40)
                {
                    _endedFired = true;
                    _playing = false;
                    var en = Ended; if (en != null) { try { en(); } catch { } }
                }
            };
            _poll.Start();
        }

        public void Close()
        {
            if (_poll != null) { try { _poll.Stop(); _poll.Dispose(); } catch { } _poll = null; }
            if (_opened)
            {
                _playing = false;
                mciSendString("stop " + _alias, null, 0, IntPtr.Zero);
                mciSendString("close " + _alias, null, 0, IntPtr.Zero);
                Program.DebugLog("[audio] stop+close(mci) " + MediaPreview.TruncPath(_path));
                _opened = false;
            }
        }
    }
}
