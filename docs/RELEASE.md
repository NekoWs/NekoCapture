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

离线包包含 PDB 以提供带行号的故障堆栈。FFmpeg 由程序运行时探测，不随包分发。
未签名包可能显示 SmartScreen 提示；发布时应提供校验值与适用的许可证文件。
