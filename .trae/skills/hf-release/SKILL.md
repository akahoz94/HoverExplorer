---
name: hf-release
description: Build, version, commit, tag, push and publish a GitHub release for HoverFolder. Use when the user asks to 发版, 更新版本, bump version, create release, or push HoverFolder to GitHub. Do not use for ordinary code edits or debugging.
---

# HoverFolder 发版流程

项目根：`C:\Users\Administrator\Desktop\HoverFolder\cs`（git 仓库，远程 https://github.com/dlsdgj/HoverExplorer，分支 **main**，不是 master）。

## 1. 编译

先杀掉运行中的实例再编译，否则 exe 被占用写不进去：

```powershell
Stop-Process -Name HoverExplorer -Force -ErrorAction SilentlyContinue
$f = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"; $wpf = "$f\WPF"
& "$f\csc.exe" /nologo /utf8output /platform:x64 /target:winexe /out:HoverExplorer.exe /main:HoverExplorer.Program /r:"$wpf\WindowsFormsIntegration.dll" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.dll /r:System.Xml.Linq.dll /r:System.Xaml.dll /r:"$wpf\PresentationCore.dll" /r:"$wpf\PresentationFramework.dll" /r:"$wpf\WindowsBase.dll" /r:"$wpf\UIAutomationClient.dll" /r:"$wpf\UIAutomationTypes.dll" /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /resource:donate_qr.png,HoverExplorer.donate_qr.png *.cs
```

- 项目无 icon.ico，不要加 `/win32icon`。
- WPF/UIA 程序集全部在 `Framework64\v4.0.30319\WPF\` 子目录；`TestHarness.cs`/`TestI18n.cs` 有独立 Main，必须 `/main:HoverExplorer.Program`。
- `donate_qr.png` 是嵌入资源源文件（设置窗口二维码），禁止删除；换图保持同名。
- 编译成功后重启验证：`Start-Process .\HoverExplorer.exe`，确认无启动错误。

## 2. 版本号

版本号存两处，必须同步：`Program.cs` 顶部 `Program.Version` 常量（设置面板右下角显示）和 git tag（如 `v1.0.0.6`）。用户给定则用给定值；否则在最新 tag 上递增末位（`git tag -l | sort -V`），先改 `Program.Version` 再编译。

## 3. 检查 README

对照本次改动更新 README.md：功能列表、使用说明、文件说明表；删除已不存在的资源引用（如 demo.gif）。

## 4. 提交 + 推送

```powershell
git add <变更文件逐一列出，含 exe 与新资源>; git rm --cached <已删除文件>
git commit -m "v<版本>: <一句话变更摘要>"
git tag v<版本>
git push origin main
git push origin v<版本>
```

不 `git add -A`，避免带入临时文件（hover_debug.log 等，已在 .gitignore 则忽略）。

## 5. 创建 Release

```powershell
gh release create v<版本> HoverExplorer.exe --repo dlsdgj/HoverExplorer --title "v<版本>" --notes "<中文变更说明，分号分隔>"
```

附件只需 HoverExplorer.exe（免安装可直接运行）。gh 已登录 dlsdgj 账号，无需额外认证。

## 完成后

向用户报告 release 链接：`https://github.com/dlsdgj/HoverExplorer/releases/tag/v<版本>`。
在桌面创建程序压缩文件，名称带版本号。
