# GOAL.md — 夜间长跑目标（FrameCapture 整站 UX 优化）

> 每轮都读这个文件。它 + `PROGRESS.md` 末尾 + `git log` 就是一轮的全部上下文。
>
> 仓库里有**三份权威工作队列**，比本文更细，动手前先读：
> - `UIAUDIT.md` —— 逐界面动画/主题审计。**本批次新增的「夜间走查」一节是当前主队列**
> - `TODO.md` —— 用户反馈与历轮修复台账（看"待办"、`[!]` 注记与各条「候选增强」）
> - `PROGRESS.md` 末尾 —— 上一轮做到哪
>
> 这三份文档**可能与代码不一致**：以截图/自测证据为准，发现不一致就当场修正文档。

## 本批次授权（用户 2026-10-18 01:35 离线前拍板）

- **全权**：可以改出厂默认值、可以改既有交互语义、可以删冗余设置项。凡动到默认值或语义的条目，
  在「今晨改动清单」里单独标注并写清如何一键否决。
- **屏幕随便用**：真实窗口、注入按键、逐帧连拍都可以跑（用户睡觉中，屏幕空闲）。
  用户醒来之前的最后一次动作必须是「重启托盘实例」，让早上打开的就是最新二进制。
- **禁区只有一个**：`site/` 宣传网页一个字不动。
- **四个方向全做**，按影响排序推进，不要求平均分配时间：
  ① 全站双主题走查 → 按影响逐条修；② `TODO.md` 挂着的候选增强；
  ③ 首次上手与可发现性（欢迎卡、帮助、空态、失败态措辞、键位冲突提示）；
  ④ 英文界面与文案质量（译名一致性 + 补齐欠的英文截图档案）。

## Objective

把 FrameCapture 的界面与交互再推一个身位，并在早上交付可逐条否决的成果。推进方式：

1. **先走查再动手**：全站每个可截图表面出明/暗双主题取证，产出问题清单进 `UIAUDIT.md`
   新增的「夜间走查」一节，每条写清现象、影响面（用户每天碰到几次）、证据文件名。
2. **按影响排序逐条修**：一条 = 一个连贯改动，带断言或截图证据，构建 0 error + SelfTest 只增不减 + commit。
3. **候选增强挑影响大的做**：`TODO.md` 里每条「候选增强」都标了当初为什么缓——
   本批次授权变了，值得做的做掉，仍不划算的把理由更新成当下的判断，别留含糊的 `[ ]`。
4. **英文面补齐**：`lint-i18n-coverage` 只管"有没有英文"，不管"英文读着对不对"；
   逐界面切到英文走一遍，欠的英文截图档案（toast / OCR / 倒计时 / 滚动预览）补进 `test-out/uiaudit/`。

## Definition of done

批次末（09:30 起）需**同时**满足，每条都要有证据：

- `UIAUDIT.md` 夜间走查一节：做完的 `[x]` 附证据，不做的写明"为什么不做"，不留含糊 `[ ]`。
- 全量 `scripts/guardrail.ps1 -AllowInjection` **PASS**（SKIP 必须写明原因）。
- 版本号升 **0.9.2**，`scripts/publish-release.ps1` 重打包 self + fd 两个产物，完整 SHA256 记进清单。
- 托盘实例跑的是最新二进制（trace `Startup done` + `gesture apply n=4`）。
- 「今晨改动清单」成文：每条 = 改了什么 / 为什么 / 证据 / 如何否决；附需真人鼠标验收的小清单
  （含 `UIAUDIT.md` D5 选区吸附/拖框态）。
- `git status` 干净；用户 4 条自定义手势、accent `#7F75FF`、主题 `system` 完好。

## Hard constraints（违反即回退）

- **只在仓库内写文件。** 测试产物放 `test-out/`；绝不动工作区之外、绝不写 `System32` 或用户目录。
- **禁止用 OS 级鼠标自动化跑测试。** 约束对象是**代理自己驱动鼠标来验证行为**：允许热键合成
  （`Shot.exe hk` / `hkm`）与对自家窗口的 `PostMessage`（`keyw` / `ctrlw`），不允许 `SendInput` 移光标、
  点按钮来跑回归。
  **产品功能不在本条范围内**：用户点名要的能力照常实现——滚动截图的「自动滚动」按用户明确选择用
  `SendInput` 注入滚轮。判据：用户按下按钮后程序动，与代理为了自测偷偷动，是两回事。见 `DECISIONS.md`。
- 允许对**自家窗口和桌面**截图做视觉审计。
- 图标**只用 Segoe Fluent Icons 的 PUA 字形**：新增图标先用 `GlyphProbe` 探测渲染再选型，**不要猜码位**
  （历史上踩过"豆腐块"）。
- **不要记录用户按键**：逐键 trace 只在 `FC_TRACE_KEYS=1` 时启用，保持这个隐私开关。
- 全站动效**必须引用 `src/FrameCapture/Core/Fluent.cs` 的 `Motion` 令牌**，不要写裸时长；
  圆角遵循 12（窗）/10（控件）/13（正圆色块）。护栏：`scripts/lint-motion-tokens.ps1`。
- 界面文案只在 `Core/Strings/<界面>.cs` 声明，取词点写 `K.<界面>.<名字>`；标签不带解释性括号。
  护栏：`scripts/lint-i18n-coverage.ps1`、`scripts/lint-copy-parens.ps1`。
- 不改动 `tools/Shot`、`tools/UICap` 的既有命令约定（回归脚本依赖它们）。
- **不要为了"让测试变绿"而改弱 `scripts/test-*.ps1` 与 SelfTest 的断言语义。**
- 测试临时改用户设置（主题/热键/手势）必须 try/finally 还原；批次末核验一次。
- 每轮至少一次 commit。

## Verification recipes

```powershell
# 构建（⚠ 必须先杀运行中的实例，否则 MSB3027 会静默失败）
Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build FrameCapture.slnx -c Debug -v q --nologo        # 输出里 ": error" 应为 0

# 一把梭（当前 18 项：构建 / SelfTest 下限 / 5 道静态扫描 / 安装器搬运探针 /
# 冒烟起进程 / 注入可达性探针 / 5 项 e2e；SKIP≠FAIL；转录落 test-out/guardrail-last-run.txt）
pwsh -NoProfile -File scripts/guardrail.ps1                 # 用户在机器上时加 -SkipE2e（e2e 会真起窗口）
pwsh -NoProfile -File scripts/guardrail.ps1 -AllowInjection # 批次末：屏幕空闲时才允许注入项

# 自测单独跑（当前基线 PASS=606 FAIL=0，只增不减）
tools/SelfTest/bin/x64/Debug/net8.0-windows10.0.19041.0/SelfTest.exe

# 视觉取证（产物放 test-out/；输出目录必须先存在）
tools/UICap/bin/Release/net8.0-windows/UICap.exe region "FrameCapture Settings" test-out/uiaudit/x.png
tools/UICap/bin/Release/net8.0-windows/UICap.exe burstregion "FrameCapture Editor" 1.6 25 test-out/b/x
tools/UICap/bin/Release/net8.0-windows/UICap.exe montage test-out/b/x test-out/b/montage.png 6 320
tools/Shot/bin/x64/Debug/net8.0-windows/Shot.exe fg
tools/Shot/bin/x64/Debug/net8.0-windows/Shot.exe hkm 118 0 0 0 0 300       # 合成裸 F7（5 个参数）
tools/Shot/bin/x64/Debug/net8.0-windows/Shot.exe keyw "FrameCapture Editor" 13

# 打包（版本单源 = src/FrameCapture/FrameCapture.csproj 的 <Version>，引导器按正则读它）
pwsh -NoProfile -File scripts/publish-release.ps1 [-SkipVerify] [-Flavor both|self|fd]
```

**取证边界（别再重复踩）**：`PrintWindow`/`UICap region` 拍不到 DWM 背板与 WDA 排除窗；
`RenderTargetBitmap` 拍不到窗口级 `Opacity`；SelfTest 只泵分发循环不跑 `Application.Run()`，
窗口有真实透明度却不进屏幕组合——淡入类动效必须真实屏采或生产进程验证。
连拍判时序用 `scripts/probe-window-fade.ps1`（内存采样 ~55fps），PNG 连拍实际只有 ~7fps。

**环境注意**：若《明日方舟》等带反外挂的游戏在前台，系统级注入按键会被完全拦截，依赖注入的脚本会
**自动 SKIP（不等于 FAIL）**——先确认游戏已退出再跑注入类回归。用户在机器上时 e2e 会真起窗口并抖，
加 `-SkipE2e`。

## 每轮该做多大

**一轮 = 一个连贯的改动**，例如"贴图方向键 1px 微调 + 断言 + 双主题取证"。
做完 → 验证 → 更新 `UIAUDIT.md` / `TODO.md` 勾选与证据 → commit → 停。

不要在一轮里塞多个不相关目标：下一轮会接手，而 `PROGRESS.md` 保证它知道你已经做到哪。
