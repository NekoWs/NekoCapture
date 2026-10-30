# 开发与交付

## 目标

FrameCapture 提供区域截图、录制、滚动截图、OCR、标注与贴图。
动作链的当前契约见 [docs/PLAN-ORTHOGONAL-CHAINS.md](docs/PLAN-ORTHOGONAL-CHAINS.md)，
验证状态见 [PROGRESS.md](PROGRESS.md)，人工检查见 [UIAUDIT.md](UIAUDIT.md)，待处理事项见 [TODO.md](TODO.md)。

## Hard constraints

- 代理只在仓库内写文件；测试产物放在 `test-out/`。测试设置、TEMP、TMP 指向仓库内隔离目录。
- 禁止代理用 OS 级鼠标自动化验证行为；允许热键合成与对自家窗口的 PostMessage。
  用户主动选择的产品功能可以注入滚轮，例如滚动截图的自动滚动。
- 允许对自家窗口与桌面截图；宣传页源码位于 `site/`，通过独立的 GitHub Pages 工作流部署。
- 图标只使用 Segoe Fluent Icons 的 PUA 字形；新增图标先经 GlyphProbe 渲染验证。
- 用户按键日志仅在 `FC_TRACE_KEYS=1` 时启用。
- 动效时长引用 `Fluent.Motion` 令牌；圆角采用窗口 12、控件 10、正圆色块 13 的基准。
- 构建前执行 `Stop-Process -Name FrameCapture`。不得削弱 SelfTest 与 `scripts/test-*.ps1` 的断言。
- 不改动 `tools/Shot`、`tools/UICap` 的命令约定。
- 测试对用户设置的临时改动必须在 `finally` 中还原；优先使用 `FC_SETTINGS_DIR` 隔离。
- 完成一次连贯改动后验证并提交。版本唯一来源是 `src/FrameCapture/FrameCapture.csproj`。
- 只有用户明确要求更新版本时才递增版本、创建版本标签和发布 Release；常规修改只验证并提交。

## 验证

原生常驻组件的发布需要 .NET 10 SDK、MSVC x64 工具链与 Windows SDK；发行组件无需 .NET 10 运行时。

```powershell
Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build FrameCapture.slnx -c Debug -v q --nologo
pwsh -NoProfile -File scripts/guardrail.ps1 -SkipE2e
pwsh -NoProfile -File scripts/guardrail.ps1 -Full
pwsh -NoProfile -File scripts/publish-release.ps1
```

`-AllowInjection` 显式启用焦点安全测试；未启用时该项为 SKIP。SKIP 不表示验证通过。
`test-out/guardrail-last-run.txt` 保存完整护栏结果。

窗口 Opacity 与 DWM 背板需要真实屏采；离屏渲染不能验证桌面合成效果。
定格帧与窗口透明通道由 SelfTest 校验，真实吸附和滚动手感由人工确认。
