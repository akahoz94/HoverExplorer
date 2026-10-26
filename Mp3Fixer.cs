using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows.Forms;

// MP3 修复工具（独立程序，不依赖 HoverExplorer）：
// 视频站下载工具保存的"MP3"很多其实是 MP4 容器（文件头 ftyp），播放引擎打开要数秒。
// 本工具检测文件头，把容器型文件用 ffmpeg 转码成真 MP3（libmp3lame VBR 高音质）。
// 转换流程：输出到同目录 "名字_1.mp3" → 转换成功后删除旧文件 → _1 文件改名回原名。
// ffmpeg 自动探测（同目录/常见安装位置/PATH），找不到可一键联网下载官方构建（约 90MB，
// 解出 ffmpeg.exe 存到 %LOCALAPPDATA%\Mp3Fixer，不污染系统）。
internal static class Mp3Fixer
{
    [STAThread]
    static void Main(string[] args)
    {
        // 单实例互斥：两个实例并发处理同一文件夹会互相踩踏（临时文件被抢、改名竞态）
        bool createdNew;
        _single = new System.Threading.Mutex(true, @"Local\Mp3Fixer.SingleInstance", out createdNew);
        if (!createdNew)
        {
            MessageBox.Show("MP3 修复工具已在运行 · Already running.\n请使用已打开的窗口，避免两个实例同时处理同一文件夹。", "MP3 修复工具 / MP3 Fixer", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; } catch { }
        var f = new FixerForm();
        if (args.Length > 0) f.BeginWork(args);
        Application.Run(f);
        try { _single.ReleaseMutex(); } catch { }
    }

    private static System.Threading.Mutex _single;
}

internal class FixerForm : Form
{
    private ListBox _list;
    private Label _drop;
    private Label _status;
    private LinkLabel _dlLink;
    private Panel _barBg, _barFill;
    private Label _barText;
    private string _ffmpeg;
    private bool _busy;
    private int _total, _done;

    public FixerForm()
    {
        Text = "MP3 修复工具 / MP3 Fixer";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(760, 560);
        MinimumSize = new Size(560, 400);
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.FromArgb(30, 30, 36);
        ForeColor = Color.FromArgb(236, 236, 241);
        AllowDrop = true;

        _drop = new Label
        {
            Text = "把 MP3 文件或文件夹拖到本窗口 · Drag & drop MP3 files or folders here\r\n容器型假 MP3 转为真 MP3 · 真 MP3 自动跳过 · 原地替换\r\nFake-MP3 containers converted to real MP3 · real ones skipped · in-place",
            Dock = DockStyle.Top,
            Height = 70,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(154, 160, 180),
            BackColor = Color.FromArgb(40, 40, 48)
        };
        Controls.Add(_drop);

        // 进度条（自绘：底槽 + 填充 + 居中文字）
        _barBg = new Panel { Dock = DockStyle.Top, Height = 26, BackColor = Color.FromArgb(24, 24, 29), Padding = new Padding(2) };
        _barFill = new Panel { BackColor = Color.FromArgb(64, 130, 220), Location = new Point(2, 2), Size = new Size(0, 22) };
        _barText = new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(210, 214, 224), BackColor = Color.Transparent,
            Font = new Font("Microsoft YaHei UI", 8.5f), Text = ""
        };
        _barBg.Controls.Add(_barFill);
        _barBg.Controls.Add(_barText);
        _barText.BringToFront();
        Controls.Add(_barBg);

        _status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0),
            ForeColor = Color.FromArgb(140, 140, 150),
            BackColor = Color.FromArgb(36, 36, 42),
            Text = "就绪 · Ready"
        };
        Controls.Add(_status);

        _dlLink = new LinkLabel
        {
            Dock = DockStyle.Bottom,
            Height = 26,
            TextAlign = ContentAlignment.MiddleCenter,
            LinkColor = Color.FromArgb(120, 170, 255),
            BackColor = Color.FromArgb(36, 36, 42),
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Underline),
            Text = "未找到 ffmpeg —— 点此自动下载官方构建（约 110MB） · ffmpeg not found — click to download (~110MB)",
            Visible = false,
            TabStop = false
        };
        _dlLink.Click += (s, e) => DownloadFfmpeg();
        Controls.Add(_dlLink);

        _list = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(26, 26, 31),
            ForeColor = Color.FromArgb(220, 220, 228),
            BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 9f),
            IntegralHeight = false,
            HorizontalScrollbar = true
        };
        Controls.Add(_list);
        _list.BringToFront();

        DragEnter += (s, e) =>
        {
            if (_busy) return;
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        };
        DragDrop += (s, e) =>
        {
            if (_busy) return;
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (paths != null && paths.Length > 0) BeginWork(paths);
        };

        DetectFfmpeg();
    }

    private void DetectFfmpeg()
    {
        _ffmpeg = FindFfmpeg();
        if (_ffmpeg != null)
        {
            _dlLink.Visible = false;
            _status.Text = "ffmpeg: " + _ffmpeg;
        }
        else
        {
            _dlLink.Visible = true;
            _status.Text = "未找到 ffmpeg";
        }
    }

    // ffmpeg 探测：同目录 → 用户数据目录（本工具下载的）→ 常见安装位置 → PATH
    private static string FindFfmpeg()
    {
        var cands = new List<string>
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Mp3Fixer\ffmpeg.exe"),
            @"D:\portable\ffmpeg.exe",
            @"C:\portable\ffmpeg.exe",
            @"C:\Tools\ffmpeg.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"ffmpeg\bin\ffmpeg.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"ffmpeg\bin\ffmpeg.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"scoop\shims\ffmpeg.exe"),
            @"C:\ProgramData\chocolatey\bin\ffmpeg.exe"
        };
        foreach (var c in cands)
            if (File.Exists(c)) return c;
        try
        {
            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathVar.Split(';'))
            {
                var p = Path.Combine(dir.Trim(), "ffmpeg.exe");
                if (File.Exists(p)) return p;
            }
        }
        catch { }
        return null;
    }

    // 联网下载 ffmpeg 官方构建（gyan.dev Essentials，GitHub latest 固定链接），
    // 解出 bin/ffmpeg.exe 存到 %LOCALAPPDATA%\Mp3Fixer
    private void DownloadFfmpeg()
    {
        if (_busy) return;
        _busy = true;
        _dlLink.Visible = false;
        _status.Text = "正在下载 ffmpeg · Downloading ffmpeg…";
        // gyan.dev 官方永久链接（始终指向最新 Essentials 构建）。
        // GitHub latest/download 固定名会 404：该仓库发布资产名带版本号，无固定名资产
        const string url = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
        var zipPath = Path.Combine(Path.GetTempPath(), "mp3fixer_ffmpeg.zip");
        var destDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mp3Fixer");
        var destExe = Path.Combine(destDir, "ffmpeg.exe");
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                Directory.CreateDirectory(destDir);
                using (var wc = new WebClient())
                {
                    wc.DownloadProgressChanged += (s, e) =>
                        BeginInvoke((MethodInvoker)delegate
                        {
                            SetProgress(e.ProgressPercentage, 100,
                                "下载 ffmpeg · Downloading  " + (e.BytesReceived / 1024.0 / 1024.0).ToString("F1") + "/" + (e.TotalBytesToReceive / 1024.0 / 1024.0).ToString("F1") + "MB  " + e.ProgressPercentage + "%");
                        });
                    wc.DownloadFile(url, zipPath);
                }
                BeginInvoke((MethodInvoker)delegate { SetProgress(0, 100, "解压中 · Extracting…"); });
                using (var zip = ZipFile.OpenRead(zipPath))
                {
                    var entry = zip.Entries.FirstOrDefault(en => en.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                                ?? zip.Entries.FirstOrDefault(en => en.FullName.EndsWith("ffmpeg.exe", StringComparison.OrdinalIgnoreCase));
                    if (entry == null) throw new Exception("压缩包里没有 ffmpeg.exe");
                    entry.ExtractToFile(destExe, true);
                }
                try { File.Delete(zipPath); } catch { }
                BeginInvoke((MethodInvoker)delegate
                {
                    _busy = false;
                    SetProgress(0, 100, "");
                    DetectFfmpeg();
                    MessageBox.Show(this, "ffmpeg 下载完成 · Downloaded:\n" + destExe, "MP3 修复工具 / MP3 Fixer", MessageBoxButtons.OK, MessageBoxIcon.Information);
                });
            }
            catch (Exception ex)
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    _busy = false;
                    SetProgress(0, 100, "");
                    _dlLink.Visible = true;
                    _status.Text = "下载失败 · Download failed: " + ex.Message + " · 可手动下载放到本程序同目录 / or place ffmpeg.exe next to this tool";
                });
            }
        });
    }

    public void BeginWork(string[] paths)
    {
        if (_busy) return;
        if (_ffmpeg == null)
        {
            MessageBox.Show(this, "未找到 ffmpeg · ffmpeg not found.\n点击下方链接自动下载，或把 ffmpeg.exe 放到本程序同目录。\nClick the link below to download, or place ffmpeg.exe next to this tool.", "MP3 修复工具 / MP3 Fixer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _busy = true;
        _list.Items.Clear();
        Log("开始扫描 · Scanning…");
        ThreadPool.QueueUserWorkItem(delegate
        {
            int converted = 0, skipped = 0, failed = 0;
            try
            {
                var files = new List<string>();
                foreach (var p in paths)
                {
                    if (File.Exists(p)) files.Add(p);
                    else if (Directory.Exists(p))
                        try { files.AddRange(Directory.GetFiles(p, "*.mp3", SearchOption.AllDirectories)); } catch { }
                }
                // GetFiles("*.mp3") 在 Windows 上会按 8.3 短名误匹配（如 .mp4 不该进来）：
                // 精确过滤扩展名；上次异常退出的 _1 残留临时文件也跳过（防二次踩踏）
                files = files
                    .Where(f2 => string.Equals(Path.GetExtension(f2), ".mp3", StringComparison.OrdinalIgnoreCase))
                    .Where(f2 => !f2.EndsWith("_1.mp3", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                _total = files.Count;
                BeginInvoke((MethodInvoker)delegate { SetProgress(0, Math.Max(1, _total), ""); });
                foreach (var file in files)
                {
                    _done++;
                    string name = Path.GetFileName(file);
                    string dir = Path.GetDirectoryName(file);
                    BeginInvoke((MethodInvoker)delegate { SetProgress(_done, Math.Max(1, _total), _done + "/" + _total + "  " + name); });
                    try
                    {
                        bool isFtyp, isMp3;
                        Classify(file, out isFtyp, out isMp3);
                        if (!isFtyp && isMp3) { skipped++; Log("[跳过 Skip] 真 MP3  " + name); continue; }
                        if (!isFtyp && !isMp3) { skipped++; Log("[跳过 Skip] 非 MP3/无法识别  " + name); continue; }

                        // 输出到同目录 "名字_1.mp3"，成功后删旧文件、_1 改名回原名（原地替换）
                        string tmp = Path.Combine(dir, Path.GetFileNameWithoutExtension(name) + "_1.mp3");
                        try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = _ffmpeg,
                            Arguments = "-y -loglevel error -i \"" + file + "\" -c:a libmp3lame -q:a 2 -vn \"" + tmp + "\"",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using (var proc = System.Diagnostics.Process.Start(psi))
                        {
                            proc.WaitForExit();
                            if (proc.ExitCode == 0 && File.Exists(tmp) && new FileInfo(tmp).Length > 10000)
                            {
                                // 删除旧文件 → _1 改名回原名。被占用等瞬时失败重试 3 次；
                                // 仍失败时保留 _1（转换结果不丢，手动改名即可）
                                if (Retry(delegate { File.Delete(file); return true; })
                                    && Retry(delegate { File.Move(tmp, file); return true; }))
                                {
                                    converted++;
                                    Log("[完成 Done] " + name + "  (" + (new FileInfo(file).Length / 1024 / 1024.0).ToString("F1") + "MB)");
                                }
                                else
                                {
                                    failed++;
                                    Log("[失败 Fail] 替换被占用 · replace blocked, kept as " + Path.GetFileName(tmp));
                                }
                            }
                            else
                            {
                                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                                failed++;
                                Log("[失败 Fail] ffmpeg exit " + proc.ExitCode + "  " + name);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Log("[失败 Fail] " + name + "  " + ex.Message);
                    }
                }
            }
            catch (Exception ex) { Log("扫描异常 · Scan error: " + ex.Message); }
            int c = converted, k = skipped, fl = failed;
            BeginInvoke((MethodInvoker)delegate
            {
                Log("————————————");
                Log("完成 · Done: 转换 " + c + " · 跳过 " + k + " · 失败 " + fl);
                SetProgress(Math.Max(1, _total), Math.Max(1, _total), "完成 · Done: 转换 " + c + " · 跳过 " + k + " · 失败 " + fl);
                _status.Text = "完成 · Done: 转换 " + c + " · 跳过 " + k + " · 失败 " + fl;
                _busy = false;
            });
        });
    }

    private void SetProgress(int value, int max, string text)
    {
        if (max < 1) max = 1;
        if (value < 0) value = 0;
        if (value > max) value = max;
        int w = (int)((_barBg.ClientSize.Width - 4) * (value / (double)max));
        _barFill.Width = Math.Max(0, w);
        if (!string.IsNullOrEmpty(text)) _barText.Text = text;
    }

    // 瞬时占用重试：3 次 × 400ms（文件被播放器/杀毒短暂持有时常见）
    private static bool Retry(Func<bool> action)
    {
        for (int i = 0; i < 3; i++)
        {
            try { if (action()) return true; } catch { }
            Thread.Sleep(400);
        }
        return false;
    }

    // 文件头分类：ftyp 容器 / 真 MP3（ID3 或 MPEG 帧同步）/ 其他
    private static void Classify(string file, out bool isFtyp, out bool isMp3)
    {
        isFtyp = false; isMp3 = false;
        using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var b = new byte[12];
            if (fs.Read(b, 0, 12) < 12) return;
            if (b[4] == 0x66 && b[5] == 0x74 && b[6] == 0x79 && b[7] == 0x70) isFtyp = true;   // "ftyp"
            else if (b[0] == 0x49 && b[1] == 0x44 && b[2] == 0x33) isMp3 = true;                // "ID3"
            else if (b[0] == 0xFF && (b[1] & 0xE0) == 0xE0) isMp3 = true;                        // MPEG 帧同步
        }
    }

    private void Log(string msg)
    {
        try
        {
            BeginInvoke((MethodInvoker)delegate
            {
                _list.Items.Add(msg);
                _list.TopIndex = _list.Items.Count - 1;
            });
        }
        catch { }
    }
}
