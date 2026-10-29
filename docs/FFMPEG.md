# 视频组件

FFmpeg 只用于录制自动保存和编辑器导出的 MP4、WebM。输入为 BGRA 帧，无音频。
MP4 使用 H.264，透明按填充色拍平；WebM 使用 VP9 并保留 alpha。GIF 使用程序自身的编码器。
视频编码缺席时自动保存回退 GIF；编辑器的视频导出提示安装组件。

独立安装器直接从 [BtbN](https://github.com/BtbN/FFmpeg-Builds) 下载 LGPL shared x64 构建，
主程序和组件安装器均不分发 FFmpeg 二进制。FFmpeg 的 DLL 只供其独立进程加载。
下载地址、构建版本、字节数、SHA256 与源码链接集中在 `FfmpegComponentInfo`。
安装保留 `LICENSE.txt`；`BUILD.txt` 记录构建参数、许可、来源及摘要。
MP4 支持 Windows Media Foundation 的 H.264 软件编码，用户自行安装的 libx264 构建也可使用。
Windows N 等缺少 Media Foundation 编码器的系统须安装系统媒体功能；安装自检失败不提交组件。

应用自有代码可维持专有授权，FFmpeg 及依赖保持各自许可证。
本组件构建为 LGPLv3 或更新版本，不包含 GPL-only 的 libx264，也未启用 nonfree。
若自行镜像或捆绑 FFmpeg 二进制，必须同时准备精确对应的 FFmpeg 和依赖源码、补丁、构建配方与许可证，
并满足各许可证的再分发及替换权利；上游源码链接不能替代自己的再分发义务。
编解码专利许可独立于开源许可证，需按产品分发地区和使用方式另行核定。
依据：[FFmpeg 许可说明](https://www.ffmpeg.org/legal.html)、[GNU LGPLv3](https://www.gnu.org/licenses/lgpl-3.0.html)。

```powershell
$env:FC_CODEC_TEST_ARCHIVE = '<仓库内的固定构建 ZIP>'
dotnet build tools/SetupProbe -c Debug
& tools/SetupProbe/bin/Debug/net48/SetupProbe.exe
Remove-Item Env:FC_CODEC_TEST_ARCHIVE
```

该探针只在仓库内安装和验证。无归档时仍检查归档边界、取消、摘要失败、目录替换与回滚。
`SelfTest video-component <FFmpeg绝对路径>` 验证真实 MP4 奇数尺寸、WebM 透明往返和取消清理。
