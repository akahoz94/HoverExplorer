# HoverExplorer
<img width="697" height="453" alt="compressO-PixPin_2026-10-19_05-25-04" src="https://github.com/user-attachments/assets/88e564d5-37a1-4b65-9a81-36abda3f564c" />



鼠标悬停即可浏览文件夹内容与预览文件，无需点击打开。

> Hover to browse folder contents and preview files — no clicks needed.

## 功能

- **桌面悬停**：鼠标悬停桌面文件夹 → 弹出文件列表，悬停文件 → 直接预览
- **资源管理器悬停**：同样支持在资源管理器中悬停浏览（可选开启），详细信息视图与图标视图均可
- **文件预览**：图片、文本、代码、压缩包(zip/7z/rar)、Office 文档、字体、exe、epub、音频/视频(播放+进度条)、SVG(图形+源码) 等
- **快捷键切换**：全局快捷键一键启用/禁用悬停功能（默认 `Ctrl+Alt+Q`，可自定义）
- **悬停延时可调**：设置面板中自定义悬停触发延时，对文件夹和文件预览统一生效
- **深浅主题**：深色/浅色两套配色（滚动条、进度条同步深色）
- **7 语言**：中文、English、日本語、한국어、Français、Русский、Español
- **关闭系统提示**：可选隐藏 Windows 原生 tooltip
- **开机自启**：可选写入注册表 Run 键，免安装
- **隐藏托盘图标**：隐藏后可在列表面板空白处右键呼出选项菜单

## 使用

直接运行 `HoverExplorer.exe`，程序驻留系统托盘。

右键托盘图标（或列表面板空白处）可：
- 切换语言 / 主题
- 开关资源管理器悬停 / 文件预览 / 调试日志 / 系统提示 / 开机自启 / 隐藏托盘图标
- **设置** → 自定义快捷键、悬停触发延时
- 退出

面板/预览窗口：拖动标题栏可移动位置（移动后自动固定）；点固定按钮固定；右下角拖拽手柄调整尺寸。

## 快捷键

默认 `Ctrl+Alt+Q`，功能为**切换悬停启用/禁用**。

在托盘「设置」面板中可：
- 勾选/取消「启用快捷键」
- 自由组合修饰键（Ctrl / Alt / Shift）
- 点击按键框后按下任意键设置主键
- 调整悬停触发延时（0.1–3 秒）

快捷键配置保存在 `config.json`，重启后生效。

## 编译

```powershell
# 使用 .NET Framework 4.x csc.exe 编译
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
# 需引用 UIAutomationClient / UIAutomationTypes / WindowsBase / PresentationCore / System.IO.Compression.FileSystem
# 输出 HoverExplorer.exe
```

## 文件说明

| 文件 | 说明 |
|------|------|
| `Program.cs` | 主循环、托盘菜单、快捷键、tooltip 隐藏 |
| `PopupForm.cs` | 悬停弹出面板、文件预览窗体 |
| `PreviewProvider.cs` | 各类文件预览渲染 |
| `ExplorerSupport.cs` | 资源管理器 UIA 悬停检测 |
| `RemoteReader.cs` | 桌面 ListView 远程读取 |
| `NativeMethods.cs` | Win32 API P/Invoke |
| `Win11.cs` | DWM 毛玻璃/圆角/暗色标题区 |
| `SettingsForm.cs` | 设置面板（快捷键 + 悬停延时） |
| `MediaPreview.cs` | 音视频播放面板、SVG 预览、自绘进度条 |
| `Config.cs` | 配置持久化 + 主题配色 |
| `I18n.cs` | 7 语言国际化 |
