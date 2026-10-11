# GOAL.md — 夜间长跑目标（FrameCapture 界面/交互收口）

> 每轮都读这个文件。它 + `PROGRESS.md` 末尾 + `git log` 就是一轮的全部上下文。
>
> 仓库里有**两份权威工作队列**，比本文更细，动手前先读：
> - `UIAUDIT.md` —— 逐界面动画/主题审计（**含未勾选项，这就是主工作队列**）
> - `TODO.md` —— 用户反馈与历轮修复台账（看"待办"与 `[!]` 注记）
>
> 这两份文档是判断"做到哪了"的依据。**文档可能与代码不一致**（B1 就是例子）：
> 以截图/自测证据为准，发现不一致就当场修正文档。

## Objective

把 FrameCapture 的界面与交互打磨到"可录发布视频的品质"并收口遗留项。当前未完成的工作，按影响排序：

1. **C1 · 绘制工具选中态（`UIAUDIT.md` C1，最明确的遗留视觉项）**
   现在工具选中**没有任何视觉反馈**。改为 accent 实心 pill + 反白图标，与其它窗口的选中语言统一
   （参考 C2：✓ 确定已是 accent 实心 pill）。

2. **B1 · 悬浮条浅色态对账（`UIAUDIT.md` B1）**
   B1 仍未勾选，但 D2 的档案描述写着"浅色=浅亚克力浅底+深色图标"。**先截图核实到底做没做**：
   做了 → 勾上 B1 并注明证据文件与 commit；没做 → 补完再勾。**不要盲信文档，也不要盲信代码注释。**

3. **E · 回归护栏（`UIAUDIT.md` E）**
   为每批改动补齐"构建 0 error + SelfTest 全过 + e2e 捕获链路完好 + commit"，并把结果记进 PROGRESS 段落。

4. **逐条清理 `UIAUDIT.md` 剩余未勾选项**
   能拿到证据的就做掉并勾选；**确实在自动化边界外的（如 D5 需真鼠标）就明确标注"需人工 + 理由"**，
   不要留着含糊的 `[ ]`。

5. **补齐尚未进档案的界面/状态**
   托盘右键菜单、错误与 HUD 边界态等还没进 `test-out/uiaudit/` 的，补进双主题截图档案。

## Definition of done

写 `status: done` 需**同时**满足，每条都要有证据：

- `UIAUDIT.md` 中所有能在自动化边界内完成的条目都已 `[x]`，各附 commit 或截图；
  剩下的每一项都明确写了"需人工 + 为什么"。
- `test-out/uiaudit/` 里明/暗双主题档案齐备、可对号入座。
- SelfTest 全绿（**基线 73/73**），且本轮新增能力有对应断言（不要为了变绿改弱断言）。
- e2e 捕获链路完好：捕获 → 编辑器 → Enter → GIF 入剪贴板（三种格式）。
- `git status` 干净。

## Hard constraints（违反即回退）

- **只在仓库内写文件。** 测试产物放 `test-out/`；绝不动工作区之外、绝不写 `System32` 或用户目录。
- **禁止 OS 级鼠标自动化。** 允许：热键合成（`Shot.exe hk` / `hkm`）与对自家窗口的 `PostMessage`
  （`keyw` / `ctrlw`）。用户明确反对"偷偷看我打字"。
- 允许对**自家窗口和桌面**截图做视觉审计。
- 图标**只用 Segoe Fluent Icons 的 PUA 字形**：新增图标先用 `GlyphProbe` 探测渲染再选型，**不要猜码位**
  （历史上踩过"豆腐块"）。
- **不要记录用户按键**：逐键 trace 只在 `FC_TRACE_KEYS=1` 时启用，保持这个隐私开关。
- 全站动效**必须引用 `src/FrameCapture/Core/Fluent.cs` 的 `Motion` 令牌**（Enter190/EditorEnter240/Exit150/
  Stagger110/Hover110/Theme260/State150），不要写裸时长；圆角遵循 12（窗）/10（控件）/13（正圆色块）。
- 不改动 `tools/Shot`、`tools/UICap` 的既有命令约定（回归脚本依赖它们）。
- **不要为了"让测试变绿"而改弱 `scripts/test-*.ps1` 的断言语义。**
- 每轮至少一次 commit。

## Verification recipes

```powershell
# 构建（⚠ 必须先杀运行中的实例，否则 MSB3027 会静默失败）
Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build src/FrameCapture -c Debug -p:Platform=x64 -v q --nologo      # 输出里 " error" 应为 0
# ⚠ -p:Platform=x64 不可省略：省略会产出 AnyCPU 到 bin\Debug\，而运行实例在 bin\x64\Debug\——
#   旧 dll 会静默继续运行（R12 曾因此"看到旧界面"排查半天）。截图前验 dll 新鲜度：
#   FrameCapture.dll 的 LastWriteTime + 字符串探针（新符号在/旧符号无，如 BuildPanels=True、FloatBar=False）。

# 自测（当前基线 RESULT PASS=110 FAIL=0，只增不减）
dotnet build tools/SelfTest -c Debug -p:Platform=x64 -v q --nologo
tools/SelfTest/bin/x64/Debug/net8.0-windows10.0.19041.0/SelfTest.exe

# 视觉取证（产物放 test-out/）
tools/UICap/bin/Release/net8.0-windows/UICap.exe region "FrameCapture Settings" test-out/uiaudit/x.png
tools/UICap/bin/Release/net8.0-windows/UICap.exe burstdesk test-out/burst 90
tools/Shot/bin/x64/Debug/net8.0-windows/Shot.exe fg                        # 打印当前前台窗标题/类名
tools/Shot/bin/x64/Debug/net8.0-windows/Shot.exe hkm 118 0 0 0 0 300       # 合成裸 F7（5 个参数！）
tools/Shot/bin/x64/Debug/net8.0-windows/Shot.exe keyw "FrameCapture Editor" 13

# 发布（只在确实需要给人看时）
dotnet publish src/FrameCapture -c Release -p:Platform=x64 -r win-x64 --self-contained true -v q --nologo
```

**环境注意**：若《明日方舟》等带反外挂的游戏在前台，系统级注入按键会被完全拦截，依赖注入的脚本会
**自动 SKIP（不等于 FAIL）**——先确认游戏已退出再跑注入类回归，否则会得到假结论。

## 逐帧视觉审查（默认打磨工作流，用户钦定）

UI/交互改动一律"录窗口 → 逐帧亲眼看 → 按看到的改"：

1. `UICap.exe burstregion "<窗口标题>" <秒> <间隔ms> <前缀>`（标题定位，不是坐标；输出目录必须先存在）。
2. `UICap.exe montage <前缀> <完整输出文件路径.png> <列数> <格宽>` 拼总览，逐格看时序（入场闪帧/错位/残影）。
3. 细节用 System.Drawing 按原生分辨率裁条 + NearestNeighbor 放大 2–3× 再读图（蒙太奇缩放会吞掉 1px 缺陷）。
4. 图标选型必须看 `GlyphProbe matrix` 的**带 hex 标签渲染矩阵**，不要按 sweep 网格行号心算（R12 教训：
   F08C 实为太阳、F08E 实为下三角，心算错位导致传输键图标全错）。
5. 顶部边缘/圆角/阴影要显式裁出来看——整窗亮度均值会掩盖 chrome 缺陷。
6. **入场/透明路径改动必须验证内容可见**：行为 e2e 全绿不代表遮罩/提示渲染了（R11 曾让选区窗整窗隐形 6 轮）。
   量化法：同坐标取遮罩开/关两图均值对比（0x60 遮罩 ≈ ×0.63）。

## 每轮该做多大

**一轮 = 一个连贯的改动**，例如"给绘制工具加 accent 选中态 pill，并出明/暗两张取证截图"。
做完 → 验证 → 更新 `UIAUDIT.md` 勾选与证据 → commit → 停。

不要在一轮里塞多个不相关目标：下一轮会接手，而 `PROGRESS.md` 保证它知道你已经做到哪。
