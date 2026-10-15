# 发行

## 一键构建

```powershell
pwsh -NoProfile -File scripts/publish-release.ps1                     # 两个产物都出
pwsh -NoProfile -File scripts/publish-release.ps1 -Publisher "你的名字"
pwsh -NoProfile -File scripts/publish-release.ps1 -Flavor fd          # 只出引导器
pwsh -NoProfile -File scripts/publish-release.ps1 -SkipVerify         # 只重打包
```

脚本顺序：读版本号 → 编译 + SelfTest + 护栏（任一不过就不出包）→ 清 Release 中间产物 →
发布 → 打包 → 报体积与 SHA256。

发布前必须清 Release 中间产物：apphost 由 CreateAppHost 生成后带自己的图标，改
`<ApplicationIcon>` 不会让它重新生成，增量发布出来的 exe 会沿用中间产物里那枚
（实测 148 KB 无图标 / 186 KB 有图标）。图标进不进包取决于构建缓存，这种不确定性不能带到发行物里。

引导器链走主项目的 `Theme.cs` 与三份 `Fluent*.cs`，这四个文件里出现对主工程其他项目
（GestureEditor、GestureCore）的引用，引导器就编译不过：它只带这四个文件。
主程序单独编译照样通过，所以这类断链只有整解决方案编译才看得见。护栏的 `build:Setup`
专门编引导器，把它拦在出包之前；需要共享的配色算法放主项目自己的 partial 文件里，
向导不链那份即可。

## 产物

| 文件 | 体积 | 用途 |
|---|---|---|
| `FrameCapture-Setup-<ver>-x64.exe` | ~6.9 MB | **主发行物**。双击即用，缺运行时时自己从微软官网下载 |
| `FrameCapture-<ver>-x64-offline.zip` | ~69.8 MB | 不能联网的机器。解压即用，不需要装 .NET |

引导器里内嵌的是框架依赖载荷（12 个文件，25.2 MB → 压成 6.8 MB）。运行时安装器约 55.9 MB，
只在目标机确实缺运行时才下载——内置一份等于让所有人多下一趟。

## 引导器为什么是 .NET Framework 4.8

引导器要在**任何**能跑主程序的机器上双击就起，因此它自己不能有任何前置。
Windows 10 2004（build 19041，正是主程序 TFM 下限）起系统自带 .NET Framework 4.8，
所以 4.8 的引导器零前置。

主程序需要 .NET 8 的理由只有两个，都在捕获侧：`Windows.Graphics.Capture`（WGC）与
`Windows.Media.Ocr`。UI 侧一行都没用到高版本特性——`Core/Theme.cs`、`Core/Fluent*.cs`
只用 `System.Windows` / `Controls` / `Controls.Primitives` / `Media` / `Media.Animation`，
这五个命名空间在 4.8 里同名同签名。引导器用 `<Compile Include>` 直接链这四个文件，
**不存在第二份样式定义**，改主程序配色会同步到安装界面。

参照：PCL2（Meloong-Git/PCL）`Plain Craft Launcher 2.vbproj` 是
`<TargetFrameworkVersion>v4.8</TargetFrameworkVersion>`，全仓 `net8`/`netcoreapp` 零命中。
那种动画手感跟运行时版本无关。

工程配置要点：

- `net48` + `UseWPF` + `PlatformTarget x64`（32 位进程写 Program Files 与注册表会被 WOW64 重定向）
- `ImplicitUsings=disable` + 本地 `GlobalUsings.cs`：WinForms 的隐式集会把
  `System.Drawing` / `System.Windows.Forms` 灌进来，与 WPF 的 `Button`/`Color`/`Brush` 撞名
- 链入的源文件依赖主项目的隐式 using，故 `LangVersion=latest`（4.8 默认 7.3 不够）
- 主程序那份 `Native/Win32.cs` 有 40 KB 且连锁依赖日志器，引导器改用 `Win32Shim.cs`，
  只声明圆角与分层窗口这 10 个符号，签名与主程序逐字一致
- `<Version>` 由 MSBuild 从 `FrameCapture.csproj` 正则读出，两处版本不会分叉
- `-p:PayloadZip` 必须传绝对路径：`EmbeddedResource` 的 Include 按工程目录解析

## 版本号

唯一来源是 `src/FrameCapture/FrameCapture.csproj` 的 `<Version>`。发布脚本与引导器工程都从那里读。

## RollForward

`FrameCapture.csproj` 有 `<RollForward>Major</RollForward>`。默认 `Minor` 只在同大版本内滚动，
会把「只装了 .NET 9/10」的机器判成缺运行时，既让用户多下一个包又没必要。
引导器的检测因此按 `major >= 8` 判定，与 runtimeconfig 的口径一致。

## 安装与卸载各做了什么

安装（一次 UAC，之后所有账户可用）：

1. 结束运行中的主程序
2. 已装目录整体移到临时目录作回滚点
3. 解内嵌载荷到 `C:\Program Files\FrameCapture`
4. 把引导器自身复制为 `FrameCapture-Setup.exe` 作卸载入口
5. 写 `HKLM\...\Uninstall\FrameCapture`，建开始菜单快捷方式
6. 任一步失败则把回滚点搬回原位

卸载（`--uninstall`）：结束进程 → 删程序目录 → 删快捷方式 → **删 `HKCU\...\Run\FrameCapture`
自启值** → 删卸载登记项。卸载器先把自己复制到临时目录再执行，才能删掉安装目录里那份自己。

## 已验证 / 未验证

已验证：

- 引导器编译 0 错 0 警；离屏渲染出图核对界面（`FrameCapture-Setup.exe --preview <png>`）
- 运行时检测：本机探到 `10.0.11`，与磁盘 `shared\Microsoft.WindowsDesktop.App` 实际内容一致
- 下载链路：真实连微软 CDN 限量下载 4 MB，TLS 握手、301 跟随、流式进度、取消后清理半截文件均正常
- 内嵌载荷：资源名 `FrameCapture.Setup.payload.zip`，12 条目无子目录，含带图标的 `FrameCapture.exe`
- 图标：引导器与主程序 exe 图标字节哈希相同
- 主程序验证链未受影响：`RESULT PASS=457 FAIL=0`、`GUARDRAIL PASS`、`LINT-COMMENTS PASS FAIL=0 WARN=3`

**未验证：真实的安装与卸载。** 需要管理员权限，本环境无管理员且批准提示被禁用。
发行前请在一台干净机器上手动跑一遍：安装、卸载、以及「故意只装 .NET 6」触发运行时下载这条路径。

载荷里含 `FrameCapture.pdb`（151 KB）。留着是为了用户报障时能拿到带行号的堆栈；
介意体积就在打包前剔掉。

## 发行前要定的三件事

1. **发布者名**：`-Publisher` 传值，会写进控制面板的 Publisher 字段。默认 `FrameCapture`。
2. **代码签名**：未签名。SmartScreen 会拦「未知发布者」，用户要点「仍要运行」。
   签名证书有年费；个人项目通常选择不签，接受这个提示。
3. **源码提供方式**：若采用 GPL2，随二进制发行必须同时提供源码或书面要约。
   上传 GitHub 并在 README 指过去即可满足。捆绑的 .NET 运行时是 MIT，与 GPL2 不冲突。
   **不要捆绑 ffmpeg**（GPL/LGPL），主程序按 `Core/VideoEncoder.cs` 的约定运行时探测它。

## 为什么不是 MSI

自写引导器换来三样：6.9 MB 的单 exe、缺运行时时按需下载、卸载能删掉 HKCU 自启值
（Windows Installer 的 Registry 表只能创建值、没有删除动作，`RemoveRegistryValue` 编译后不落任何表）。

代价要认：失去 GPO/SCCM 静默分发，失去 Windows Installer 的事务回滚与被占用文件的重启后替换。
现在的替代是杀进程 + 临时目录备份回滚 + 失败时保留原版本。
