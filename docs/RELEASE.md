# 发行打包

> 一条命令产出 Windows 安装包。产物落 `release/`（已 gitignore），提交里只有配方：
> `packaging/FrameCapture.wxs` + `scripts/publish-release.ps1` + `scripts/make-icon.py` + `assets/FrameCapture.ico`。

## 一键出包

```powershell
pwsh -NoProfile -File scripts/publish-release.ps1
```

默认先跑完整验证链（编译 → SelfTest 断言 → 护栏），任一不过就**不出包**——发布包里塞进坏构建
比晚一天发版严重。只重打包时加 `-SkipVerify`。

```powershell
# 发布者名会显示在「应用和功能」的「发行者」栏，发行前换成你自己的
pwsh -NoProfile -File scripts/publish-release.ps1 -Publisher "你的名字"

# 只出一种形态
pwsh -NoProfile -File scripts/publish-release.ps1 -Flavor self   # 或 fd
```

## 两个包怎么选

| 包 | 体积 | 用户前置条件 |
|---|---|---|
| `FrameCapture-<ver>-x64-self-contained.msi` | 57.8 MB | 无，双击即装 |
| `FrameCapture-<ver>-x64-framework-dependent.msi` | 5.3 MB | 需先装 .NET 8 Desktop Runtime |

两个包共用同一 `UpgradeCode`，装另一个时会先干净卸掉已装的，不会留下两份文件。

框架依赖包缺运行时时的表现是 Windows 标准的「要运行此应用，你需要 .NET」对话框（带下载按钮），
不是崩溃。包内**不做运行时检测**：MSI 的注册表搜索只能查固定版本号，而 .NET 有 roll-forward，
按 8.0 查会在只装了 .NET 10 的机器上误报缺运行时。

## 不变量

- **版本号唯一来源**是 `src/FrameCapture/FrameCapture.csproj` 的 `<Version>`。脚本读它，不另存一份。
  MSI 只吃 `major.minor[.build]` 且每段 ≤ 65535，四段版本号会在脚本里被拦下。
- **`UpgradeCode` = `D13E621E-E92C-43F2-87F6-6C4076F366D9`，对外发行后永不能改。**
  它标识「这是同一个产品」；改了以后升级不会替换旧版，而是变成两份并存安装。
  `ProductCode` 由 WiX 按 版本+UpgradeCode+语言 稳定生成，不手写。
- **工具链钉在 WiX v5**（`dotnet-tools.json`，`rollForward: false`）。
  WiX v6 起官方二进制发布挂 OSMF 维护费 EULA：产生收入的用户按 <20 人 $10/月起收费。
  v5 及更早是纯 MIT，无 EULA。`rollForward: false` 挡住将来自动升到 v7 后构建直接 WIX7015 崩掉。
  首次在新机器上出包需要联网还原工具与 UI 扩展（脚本会自动补 `WixToolset.UI.wixext`）。

## 图标

`assets/FrameCapture.ico` 由 `python scripts/make-icon.py` 生成（10/16/20/24/32/40/48/64/96/128/256 多尺寸），
exe、开始菜单快捷方式、「应用和功能」共用同一份源图。改设计改脚本里的常量重跑即可，
预览图落在 `test-out/icon-preview.png`（浅底/深底各排一列，用来判断 16px 是否还认得出）。

## 已验证 / 未验证

打包环境没有管理员权限，因此**没有做过一次真实的安装/卸载**。已做到的是：

- 用 Windows Installer COM 读包内数据库：`ALLUSERS=1`（全体用户，装 Program Files，弹一次 UAC）、
  文件数与发布目录一致（自包含 250 / 框架依赖 12）、组件数 = 文件数 + 1 个快捷方式、
  图标已内嵌、快捷方式 Target = `[INSTALLFOLDER]FrameCapture.exe`、两个包同 UpgradeCode 不同 ProductCode。
- `msiexec /a`（管理员提取，只解文件不写系统）退出码 0，12 个文件正确解到 `PFiles64\FrameCapture\`。

**首次对外发行前，请在一台干净机器上真装一次、再卸载一次。**

## 已知残留

卸载**不会**删除开机自启值 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\FrameCapture`。
Windows Installer 的 Registry 表只能创建值、没有删除动作，`RemoveRegistryValue` 编译后不落任何表
（已实测），删它只能靠自定义操作——而自定义操作一旦失败会让整个卸载报错，代价远大于收益。

只有「开过自启、又卸载」的用户会留下这一条，且它指向已删除的路径，Windows 静默跳过，无报错无弹窗。
手动清理：

```powershell
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name FrameCapture
```

用户自己在设置里关掉「开机自启」也会删掉它（`Services/StartupService.cs`）。

## 发行前还需要你决定的三件事

1. **发布者名**：脚本默认 `FrameCapture`，是占位值。
2. **代码签名**：未签名的 MSI 在别的机器上会吃 SmartScreen 蓝条「Windows 已保护你的电脑 → 更多信息 → 仍要运行」。
   要消掉需要代码签名证书（OV 约 ¥1500/年，EV 更贵但立刻消除信誉警告），签完用
   `signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 release\*.msi`。
   个人项目常选「不签名，在发布页写清楚怎么绕过」，那是合理取舍，但要在下载页说明。
3. **许可证**：仓库里没有 LICENSE 文件。若按 GPL2 开源，分发二进制就必须同时提供完整对应源码
   或书面源码要约（README 里放仓库地址即可）；自包含包里的 .NET 运行时是 MIT，与 GPL2 兼容。
   **不要往安装包里塞 ffmpeg**——仓库「不捆绑 ffmpeg（体积 + 许可）」是既定决策，
   见 `Core/VideoEncoder.cs`，塞进去会把 GPL/LGPL 义务引进来。
