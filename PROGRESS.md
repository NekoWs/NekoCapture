# PROGRESS.md — 夜间长跑记忆

追加式。每轮在末尾追加一个 `## Round N` 段（含该轮自己的报告、仓库脏否、HEAD）。
**下一轮只读本文件最后 ~150 行** —— 连续性就靠这个跨会话传下去。

想给长跑纠偏就改下面的 "Standing state"；想中止就在仓库根放一个 `STOP` 空文件。

## Standing state

- **Objective**：见 `GOAL.md`。权威工作队列是仓库里的 **`UIAUDIT.md`**（逐界面审计、含未勾选项）与
  **`TODO.md`**（用户反馈台账）。
- **Last updated**：2026-10-03（开跑前初始化，尚未跑过任何一轮）
- **代码基线**：HEAD = `2ad5b38`（吸附到控件(Snipaste 式) + SelfTest T71）；**SelfTest 73/73**；
  发布包已是最新，无需重建即可继续开发。
- **Where things stand**：
  - **已完成的大块**：WGC 捕获后端（GDI 自动回退）、全局可自定义热键（有 e2e 证据）、点按/长按语义、
    GIF 并行编码（4.7s → 0.95s）、FrameRing 自适应内存预算、时间轴自绘画布（不用 WPF Slider）、
    明/暗双主题全站令牌化、设置行可见名称、吸附到窗口/内容区/控件、点按总是先显示选区、
    选区自动截取单帧 PNG、隐私修复（不再逐键记录）。
  - **最明确的遗留**：`UIAUDIT.md` **C1**（绘制工具选中态无任何视觉反馈）、
    **B1**（悬浮条浅色态：B1 未勾但 D2 描述说已做 —— **文档自相矛盾，需截图核实**）。
  - 参考：`UIAUDIT.md` 的 A/B2-B5/C2-C4/D1-D4/D6-D7 均已 `[x]`。
- **Known blockers / 自动化边界**：
  - `UIAUDIT.md` **D5**（选区吸附态/拖框态）需真鼠标悬停与拖动，**在自动化边界外** —— 按"需人工"处理。
  - 反外挂游戏（如《明日方舟》）在前台时系统级注入被拦截，注入类脚本会自动 **SKIP（≠FAIL）**。
  - **构建前必须 `Stop-Process FrameCapture`**，否则 MSB3027 文件占用导致构建静默失败。
  - 托盘右键菜单是系统原生渲染，像素取证受限。
- **Next step**：做 `UIAUDIT.md` **C1** —— 给绘制工具加 accent 实心 pill 选中态 + 反白图标，
  明/暗双主题各截一张放进 `test-out/uiaudit/`，勾选 C1 并注明证据与 commit。

---

<!--
下面是 run-round.ps1 自动追加的区域，**不要手改**。
要纠偏请改上面的 "Standing state"，或放一个 STOP 文件中止长跑。

每段形如（示例刻意缩进：不缩进的 `## Round n` 会被人和模型误当成"已经跑过的一轮"——这个坑已经踩过一次）：

    ## Round 1 — 2026-10-03 22:10:04  (status: continue)

    ```text
    <该轮完整的最终回复，含它的 ROUND 1 REPORT 块>
    ```

    - repo dirty after round: no
    - HEAD: 1a2b3c4 chore(overnight)[driver]: record round 1 in PROGRESS.md (status: continue)
-->

---

## Round 1 — 2026-10-03 16:52:32  (status: continue)

```text
Round complete. Summary of what happened:

**Key discovery (doc-vs-code contradiction, fixed in place):** UIAUDIT C1 claimed "现无选中态视觉" — this was **false since baseline `cafb068`**; my before-shot (`c1-before-dark.png`) proved the accent-fill + black icon renders on tool select. The claim was corrected in UIAUDIT.md along with the [x] and evidence.

**What I actually shipped (the real gaps the before-shot exposed):**
1. **Width-dot selector had no default-selection highlight** (`RefreshWidths` never called at build — only after first click) and lacked animation/inversion → now shares `HighlightBtn` with tools, initial state visible.
2. Highlight animation moved from `HoverMs` → `Motion.StateMs` (it's a state change, per the token's own doc).
3. Inversion color hardcoded black → `OnAccentColor()` picks black/white by accent luminance.
4. Theme-switch while editor open left stale-theme foreground on tool/width buttons → `Editor_OnTheme` now re-applies the selection language.
5. Tool toggle semantics extracted to pure `ToolToggle.Select` + two new SelfTest assertions (T72a toggle, T72b contrast).

**Also learned for later rounds:** capture hotkey in current settings is **F6** (not F7) — `scripts/test-editor-keys.ps1` still synthesizes `hk 71` and will silently auto-SKIP today; fixing that stale hotkey derivation belongs to the E-guardrail round.

ROUND 1 REPORT
did: C1 收口——先证伪"现无选中态视觉"旧注（改前截图实证 accent pill 自 cafb068 已渲染），再补齐线宽选择器缺失的默认高亮并统一选中语言（StateMs 动画、OnAccentColor 对比反白、换肤后前景随行），切换语义抽成纯逻辑 ToolToggle
verified: `dotnet build src/FrameCapture -p:Platform=x64` 0 error；SelfTest.exe → RESULT PASS=75 FAIL=0（基线 73 + T72a/T72b）；e2e：F6 长按→编辑器→选工具→Enter→trace "gif encoded bytes=147651"+编辑器关闭+剪贴板 FileDrop(GIF 文件存在)+Bitmap；双主题截图 test-out/uiaudit/c1-tool-{dark,light}.png 人眼复核通过；git status 干净，commit b843cd3
next: 核实并勾选 B1——用既有 light-editor-bars.png（D2 档案）对照悬浮条浅色态：浅底+深图标已在则勾 B1 附证据与 commit，缺什么补什么；顺手把 scripts/test-editor-keys.ps1 的过期 hk 71 改为按当前设置合成（捕获键现为 F6），避免 E 护栏轮拿到假 SKIP
status: continue
```

- repo dirty after round: no
- HEAD: b843cd3 ui(editor): C1 收口——选中语言补齐：线宽默认选中可见+与工具统一 HighlightBtn(动画StateMs/反白按accent亮度OnAccentColor)；换肤后工具/线宽前景随行；切换语义抽纯逻辑 ToolToggle；SelfTest T72 (75/75)；UIAUDIT C1 勾选并修正基线误注(选中态视觉自 cafb068 已在，c1-before-dark.png 实证)；明/暗取证 c1-tool-dark.png / c1-tool-light.png
- session: session-7b7f6c2f-0abd-410e-9d45-d920c34626ea
- raw transcript: C:\Users\16892\.dsh\sessions\--D-Backup-Capture--\session-7b7f6c2f-0abd-410e-9d45-d920c34626ea\session.v3.jsonl.zstd
- read it with: node "D:\Agent Playground\overnight-kit\rounds.mjs" --repo "D:\Backup\Capture" --show 1

<!-- RECOVERED by hand: run-round.ps1 was launched from Windows PowerShell 5.1 and died before
     its bookkeeping step (leftover %TEMP%\dsh-round-1-*.{out,err}.txt prove it). This entry was
     reconstructed from the captured stdout. The round itself completed and committed normally. -->

---

## Round 2 — 2026-10-03 17:04:46  (status: continue)

```text
Git status clean, no stray instances. Round complete.

**Summary:** B1 (float bar light theme) turned out to be the same doc-vs-reality staleness as C1: the feature has existed since baseline `cafb068` — `FloatBar.cs` builds its background from `Theme.PanelBg`, re-animates on theme change (`OnTheme`, ThemeMs), and icon foregrounds follow `Theme.Text`. I verified this not by trusting the docs but by re-capturing both themes on the current HEAD build via the `FC_THEME=light|dark` override → F6 long-press → `UICap region "FrameCapture Bar"`: `test-out/uiaudit/b1-bars-light.png` (light acrylic + dark icons/text, accent thumb/✓ pill) and `b1-bars-dark.png` (same layout, dark acrylic + light icons), both showing Round 1's new width-dot selection language with zero stale-theme residue. B1 is now `[x]` in UIAUDIT.md with full evidence; committed as `9bfa17c` (screenshots live on-disk under gitignored `test-out/`, matching the repo's existing convention for all evidence).

ROUND 2 REPORT
did: B1 收口——证实悬浮条浅色态自基线 cafb068 已实现（FloatBar 背景=Theme.PanelBg+OnTheme 换肤重动画，同 C1 的文档滞后模式），在 HEAD 上用 FC_THEME 覆盖现场复拍明/暗双取证并人眼复核，UIAUDIT.md B1 勾选附代码路径+截图证据
verified: `dotnet build src/FrameCapture -p:Platform=x64` 0 error；`UICap region "FrameCapture Bar"` 产出 b1-bars-light.png（浅底深图标）/ b1-bars-dark.png（深底浅图标）1340x195 各一张，双主题逐要素吻合；SelfTest.exe → RESULT PASS=75 FAIL=0；git status 干净，commit 9bfa17c
next: E 护栏收口——修 scripts/test-editor-keys.ps1 的过期 `hk 71`（那是 Ctrl+Shift+G，现捕获键为裸 F6，应改为 `hkm 117 0 0 0 0`，最好直接读 %APPDATA%\FrameCapture\settings.json 派生），跑全套护栏（构建+SelfTest+编辑器键位 e2e 不再 SKIP）后勾选 UIAUDIT.md E
status: continue
```

- repo dirty after round: no
- HEAD: 9bfa17c docs(uiaudit): B1 鏀跺彛鈥斺€旀偓娴潯娴呰壊鎬佹牳瀹炰负鍩虹嚎(cafb068)宸插疄鐜?鏈嬀鈮犳湭鍋氾紝鍚?C1 妯″紡)锛欶loatBar 鑳屾櫙=Theme.PanelBg+OnTheme 鎹㈣偆閲嶅姩鐢汇€佸浘鏍囬殢 Theme.Text锛汬EAD 鐜板満澶嶆媿鍙屼富棰樺彇璇?b1-bars-light.png(娴呬簹鍏嬪姏娴呭簳+娣卞浘鏍?/b1-bars-dark.png锛屽惈 Round1 绾垮閫変腑璇█鏃犳畫鐣欙紱SelfTest 75/75
- session: session-65df63bb-3d0c-4fb6-b069-62daf5961389
- raw transcript: C:\Users\16892\.dsh\sessions\--D-Backup-Capture--\session-65df63bb-3d0c-4fb6-b069-62daf5961389\session.v3.jsonl.zstd
- read it with: node "D:\Agent Playground\overnight-kit\rounds.mjs" --repo "D:\Backup\Capture" --show 2
