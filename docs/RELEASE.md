# 发布

版本唯一来源为 `src/FrameCapture/FrameCapture.csproj` 的 `<Version>`。

```powershell
pwsh -NoProfile -File scripts/publish-release.ps1
```

脚本停止运行实例，执行构建、SelfTest 与完整护栏，产出 self 与 fd 两种运行时形态及其安装包。
通过 `-Flavor self|fd|both` 选择形态；`-Publisher` 设置发布者，`-OutDir` 设置仓库内输出目录。
`-SkipVerify` 仅用于当前代码已完整验证的打包。包版本含提交标识，打包前必须提交产品代码。

主程序要求 x64 Desktop Runtime 8 或更高主版本，`RollForward=Major`。
安装器使用 .NET Framework 4.8，运行时检测与主程序要求一致。
程序安装到 Program Files；安装失败使用备份恢复，卸载清除程序、快捷方式、自启项与卸载登记项。
安装与卸载需要管理员权限，发行前须在干净机器上人工验证。
自启提权验证包含两种勾选顺序、授权取消、注销后免 UAC 启动、关闭自启与卸载后的任务清理。

离线包包含 PDB 以提供带行号的故障堆栈。主包不包含 FFmpeg。
录制设置提供视频组件的安装和进度。发布同时产出 `FrameCapture-Codecs-Setup-<版本>-x64.exe`。
两个入口共用下载与安装逻辑，从公共加速入口或 GitHub 上游获取固定的 LGPL shared x64 归档。
组件安装器不内嵌 FFmpeg、不修改 PATH、无需提权；校验 SHA256、许可证和 MP4/WebM 编码后提交安装。
组件位置为 `%LOCALAPPDATA%/FrameCapture/Components/FFmpeg`，移除此目录即可卸载组件。
升级失败恢复已安装组件；下载失败或取消清理暂存目录。正在使用的备份不能删除时予以保留。
固定来源、大小和摘要集中在 `FfmpegComponentInfo`；发布者月末构建保留两年，发布前须检查来源仍可下载。
组件与源码授权边界见 [FFMPEG.md](FFMPEG.md)。
未签名包可能显示 SmartScreen 提示；发布时应提供校验值与适用的许可证文件。
