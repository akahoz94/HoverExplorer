using System;
using System.Drawing;

namespace HoverExplorer
{
    // 音频/视频播放会话的统一接口：WPF MediaElement 实现（MediaSession，m4a 等索引友好的格式走它）
    // 与 MCI 实现（MciAudioSession，MP3 走它——Media Foundation 对无索引 MP3 打开要全文件扫描
    // 求时长、大文件数秒，MCI 只读头部毫秒级打开）
    internal interface IMediaSession
    {
        string Path { get; }
        bool IsVideo { get; }
        event Action Opened;
        event Action Failed;
        event Action Ended;
        event Action<double?> EstimateReady;
        System.Windows.Forms.Control HostView { get; }
        bool IsLoaded { get; }
        bool IsPlaying { get; }
        bool HasFailed { get; }
        TimeSpan Duration { get; }
        double? EstimatedMs { get; }
        int NaturalWidth { get; }
        int NaturalHeight { get; }
        TimeSpan Position { get; }
        void Play();
        void Pause();
        void Toggle();
        void SeekRatio(double ratio);
        void Reload(string path);
        void Close();
    }
}
