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

---

## Round 3 — 2026-10-03 17:13:26  (status: continue)

```text
Round complete. Summary:

**What I shipped (E guardrail 收口):**
1. **`scripts/guardrail.ps1`** — the E item as a single-command gate: kill instances → build app/Shot/SelfTest (counting `: error`) → SelfTest with a pass-count floor → injection reachability probe → 5 e2e scripts in isolated child `pwsh` processes → summary. SKIP≠FAIL semantics honored (anti-cheat blocks injection legitimately); a missing PASS marker counts as FAIL (no trusting silent runs); transcript to `test-out/guardrail-last-run.txt`; exit code is the gate verdict.
2. **Root-cause fix of false SKIPs/FAILs:** every injection regression script baked in vk 71/82 (Ctrl+Shift+G/R) while the live settings are **F6/F7**. New `scripts/lib-hotkey.ps1` derives Shot `hkm` calls from settings.json, mirroring `HotkeySpec.Parse` (bare keys only F1–F24; anything illegal throws). Wired into editor-keys, tap-semantics, both fuzz scripts, wait-then-run; hotkey-e2e's "restored settings" phase now presses the restored hotkey (its intentional `hk 71` "old default disabled" assertion kept and comment-protected); focus-safety pins default hotkeys during its run (Shot's `swallow` bakes Ctrl+Shift+G) and restores at every exit.
3. **Assertions strengthened, never weakened:** editor-keys now live-probes the real clipboard after Enter for all three formats (CF_HDROP available + a new non-empty `capture-*.gif` staged + CF_DIB/CF_BITMAP + custom `FrameCapture.Gif`) and exits 1 on FAIL (previously printed FAIL with exit 0). tap-semantics pins `TapAlwaysShowsRegion=false` so the threshold stays the only variable under test.
4. UIAUDIT.md **E checked off** with full evidence. This was the last automation-boundary-internal unchecked item; remaining `[ ]` D5 already carries an explicit "needs real mouse + why".

ROUND 3 REPORT
did: E 收口——新增 scripts/guardrail.ps1 一条命令全闸门 + lib-hotkey.ps1 修复全部回归脚本写死 vk71/82 与现设置 F6/F7 脱节的根因（假 SKIP/假FAIL）；editor-keys 断言增强为 Enter 后真剪贴板三格式取证且 FAIL 必 exit 1
verified: `pwsh -NoProfile -File scripts/guardrail.ps1` → GUARDRAIL PASS（FAIL=0 SKIP=1/9，exit 0）：build×3 errors=0、SelfTest PASS=75 FAIL=0、editor-keys（clipboard hdrop/gifFile/bitmap/gifBytes 全 True）、hotkey/tap/fuzz 全过（capture_hotkey=F6 派生生效）；唯一 SKIP 为 focus-safety 前台保护（前台为用户浏览器，by design）；commit 9642eea，git status 干净
next: GOAL 第 5 项——把尚未进档案的界面/状态补进 test-out/uiaudit/ 双主题档案（托盘右键菜单用桌面级截图尝试、错误态/HUD 边界态），拿不到像素的条目按"需人工+理由"明确标注，收口后长跑即可判定 done
status: continue
```

- repo dirty after round: no
- HEAD: 9642eea test(guardrail): E 鏀跺彛鈥斺€攕cripts/guardrail.ps1 涓€鏉″懡浠ゅ叏闂搁棬锛堟瀯寤鸿error鈫扴elfTest涓嬮檺鈫掓敞鍏ユ帰閽堚啋5椤筫2e鈫掓眹鎬伙紝SKIP鈮燜AIL锛岃浆褰曡惤 test-out/guardrail-last-run.txt锛夛紱鏍瑰洜淇锛氬洖褰掕剼鏈啓姝?vk71/82 涓庡綋鍓嶈缃?F6/F7 鑴辫妭鑷村亣SKIP/鍋嘑AIL锛屾柊澧?lib-hotkey.ps1锛堢儹閿覆鈫抙km 鏄犲皠锛屼笌 HotkeySpec.Parse 鍚岃锛夊苟鎺ュ叆 editor-keys/tap-semantics/fuzz脳2/wait-then-run锛沨otkey-e2e 鎭㈠闃舵鎸夋仮澶嶅€兼敞鍏ワ紱focus-safety 娴嬭瘯鏈熼拤榛樿鐑敭+鍏ㄥ嚭鍙ｈ繕鍘燂紱tap-semantics 閽?TapAlwaysShowsRegion=false 淇濋槇鍊煎敮涓€鍙橀噺锛沞ditor-keys 鏂█澧炲己锛欵nter 鍚庣湡鍓创鏉夸笁鏍煎紡(CF_HDROP+鏂板闈炵┖capture-*.gif+DIB/BITMAP+FrameCapture.Gif)涓?FAIL 涓€寰?exit 1锛涘疄璺?GUARDRAIL PASS(FAIL=0 SKIP=1/9锛屽敮涓€SKIP涓哄墠鍙颁繚鎶?by design)锛沀IAUDIT E 鍕鹃€夐檮璇佹嵁
- session: session-6250b40c-1c0b-4915-a2f3-27b3fdc943ca
- raw transcript: C:\Users\16892\.dsh\sessions\--D-Backup-Capture--\session-6250b40c-1c0b-4915-a2f3-27b3fdc943ca\session.v3.jsonl.zstd
- read it with: node "D:\Agent Playground\overnight-kit\rounds.mjs" --repo "D:\Backup\Capture" --show 3

---

## Round 4 — 2026-10-03 17:33:53  (status: timeout)

```text

```

- repo dirty after round: YES
- HEAD: 9cf9d80 chore(overnight)[driver]: record round 3 in PROGRESS.md (status: continue)
- session: session-46d7a5dc-8b46-488d-81db-6fac024308c5
- raw transcript: C:\Users\16892\.dsh\sessions\--D-Backup-Capture--\session-46d7a5dc-8b46-488d-81db-6fac024308c5\session.v3.jsonl.zstd
- read it with: node "D:\Agent Playground\overnight-kit\rounds.mjs" --repo "D:\Backup\Capture" --show 4

---

## Round 5 — 2026-10-03 17:58:54  (status: continue)

```text
Round complete. Summary:

**What happened:** Round 4's salvage had left `scripts/cap-tray-menu.ps1` committed-but-never-run. This round I finished that work properly:

1. **Third doc-vs-reality contradiction found & corrected:** UIAUDIT's boundary note claimed "托盘菜单为系统原生渲染" (tray menu is system-native). False — `TrayService.ShowMenu` builds a WPF `ContextMenu` with `Fluent.MenuResources()`, self-painted and theme-colored, so pixel evidence was fully feasible.
2. **Two fatal bugs in the salvaged script, found empirically:**
   - Its injection posted raw `WM_RBUTTONDOWN/UP` to the NotifyIcon hidden window — silently ignored. I traced the actual .NET 8 WinForms source (dotnet/winforms `NotifyIcon.cs`): the shell callback is `WM_USER+1024` (0x800) with the mouse event code in **lParam** (the old WM_USER+102 from .NET Framework docs also tested and confirmed non-working). Fixed protocol → menu pops at cursor via `PostMessage` only, no OS-level mouse automation.
   - `static long gArea; static RECT gBest;` had no access modifier → private → PowerShell read nulls → the first "PASS" was a garbage 28×28 crop. Fixed, real rect readback confirmed.
3. **Evidence archived + human-reviewed:** `d8-tray-menu-dark.png` / `d8-tray-menu-light.png` (311×449 menu) — dark card/light text vs light card/dark text, rounded corners, themed separators, full item list, no residue, no tofu.
4. **UIAUDIT.md:** new D8 `[x]` with mechanism + evidence; boundary line corrected. Added one permanent trace line (`Trace.Log("tray menu open")`) in `TrayService.ShowMenu` for future diagnostics.

UIAUDIT is now fully checked except D5, which carries its explicit "needs real mouse + why" annotation.

ROUND 5 REPORT
did: D8 收口——托盘右键菜单双主题像素档案进档；证伪"托盘菜单系统原生渲染"旧边界注（实为 WPF 自绘 Fluent 菜单随主题着色）；修好 Round4 遗留 cap-tray-menu.ps1 两处致命伤（注入协议改为 .NET(8+) 真实回调 WM_USER+1024 且事件码在 lParam，实测两种错误协议均静默失效；gArea/gBest 缺 private 修饰符致读回 null 裁出废图）
verified: `pwsh -NoProfile -File scripts/cap-tray-menu.ps1 -Theme dark|light` 双双 TRAY-CAP PASS（menu=3078,1487,3389,1936，367x505 裁片）；两张截图人眼复核（深卡浅字/浅卡深字、圆角、分隔线、无残留无豆腐块）；构建 0 error；SelfTest.exe → RESULT PASS=75 FAIL=0；commit 10bd819，git status 干净
next: GOAL 第 5 项剩余——错误态与 HUD 边界态（导出进度/取消/失败）补进 test-out/uiaudit/ 双主题档案或明确标注需人工+理由；随后跑一遍 scripts/guardrail.ps1 全闸门作为 done 判定的收口证据
status: continue
```

- repo dirty after round: no
- HEAD: 10bd819 test(uiaudit): D8 鏀跺彛鈥斺€旀墭鐩樺彸閿彍鍗曞弻涓婚鍍忕礌妗ｆ锛氳瘉浼?绯荤粺鍘熺敓娓叉煋'鏃ц竟鐣屾敞(TrayService 鑷粯 WPF ContextMenu+Fluent.MenuResources 闅忎富棰樼潃鑹?锛涗慨濂?Round4 閬楃暀 cap-tray-menu.ps1 涓や釜鑷村懡浼?鈶犳敞鍏ュ崗璁敊锛?NET(8+) NotifyIcon 鍥炶皟鏄?WM_USER+1024=0x800 涓斾簨浠剁爜鍦?lParam锛岄潪瑁?WM_RBUTTONUP/鏃х増 WM_USER+102鈥斺€斿疄娴嬩袱绉嶉敊璇崗璁潎闈欓粯涓嶈Е鍙戯紱鈶Area/gBest 鏃犱慨楗扮=private锛孭owerShell 璇诲洖 null 鑷磋鍒?28x28 搴熷浘)锛涘疄璺戝嚭 d8-tray-menu-dark/light.png(311x449锛屽渾瑙?鍒嗛殧绾?鏂囧瓧鍙屼富棰樻崲瑁呮棤娈嬬暀)骞朵汉鐪煎鏍革紱UIAUDIT 澧?D8+淇宸茬煡杈圭晫琛岋紱TrayService.ShowMenu 琛?trace 涓€琛?璇婃柇甯稿紑)锛汼elfTest 75/75
- session: session-1723fad8-8fd2-4473-af18-ca9325358a8e
- raw transcript: C:\Users\16892\.dsh\sessions\--D-Backup-Capture--\session-1723fad8-8fd2-4473-af18-ca9325358a8e\session.v3.jsonl.zstd
- read it with: node "D:\Agent Playground\overnight-kit\rounds.mjs" --repo "D:\Backup\Capture" --show 5

---

## Round 6 — 2026-10-03 18:09:51  (status: timeout)

```text

```

- repo dirty after round: YES
- HEAD: 7580692 chore(overnight)[driver]: record round 5 in PROGRESS.md (status: continue)
- session: session-7274f68e-d688-4521-b5c4-f51fba6e387e
- raw transcript: C:\Users\16892\.dsh\sessions\--D-Backup-Capture--\session-7274f68e-d688-4521-b5c4-f51fba6e387e\session.v3.jsonl.zstd
- read it with: node "D:\Agent Playground\overnight-kit\rounds.mjs" --repo "D:\Backup\Capture" --show 6

---

## Round 7 — 2026-10-03 18:34:52  (status: continue)

```text
Round complete. Summary:

**What this round fixed:** Round 6 timed out and the driver salvaged its uncommitted state (`b4320a0`: two test hooks + `cap-hud-states.ps1`) — never run, the same salvage-then-defer pattern round 4 left behind. I ran it, found its picker had a fatal bug, fixed it, and closed D9 properly.

**The bug found empirically:** the burst frame-picker demanded pixel-exact frame sizes before diffing, but the editor toolbars are `SizeToContent` — every HUD text change ("1/208 ⚠…" → "导出 GIF… d/208") resizes the whole same-title window group, so *every* burst frame was size-skipped (`diffPixels=-1`, dark's first run failed on exactly that). Fixed by taking the minimum common size across the whole burst and diffing center-aligned crops — handles both size growth and the origin drift from centered bars.

**Evidence shipped (all human-reviewed):** 8 screenshots in `test-out/uiaudit/` covering all four HUD boundary states × both themes — dropped-frames warning ("⚠内存预算已丢 N 帧", genuinely dropped via pinned 2500MB budget over a 25s capture), encode progress ("导出 GIF… 206/208"), cancel-recovery (pixel-identical to pre-encode state — verified in code that the cancel branch explicitly restores via `Show(_index)`, so identity *is* the correctness proof), and forced failure ("导出失败", editor survives, Esc then closes normally). D9 added `[x]` in UIAUDIT.md; D6b's stale "pixel evidence uncertain during encode" boundary is formally lifted with a back-reference. UIAUDIT is now fully closed except D5, which carries its explicit needs-real-mouse annotation.

ROUND 7 REPORT
did: D9 收口——跑通并修好 Round 6 遗留未验证的 cap-hud-states.ps1（选帧器"尺寸全等才比"被悬浮条 SizeToContent 全帧击穿，改为最小公共尺寸+中心对齐裁切差分）；HUD 四边界态（丢帧警告/导出进度/取消恢复/强制失败）×双主题 8 张像素档案进档并人眼复核；UIAUDIT 增 D9 [x]、解除 D6b"编码期像素取证不确定"旧边界并回注
verified: `pwsh -NoProfile -File scripts/cap-hud-states.ps1 -Theme dark` 与 `-Theme light` 均 HUD-CAP PASS（exit 0；trace 四断言 ring dropped / encode cancelled / gif encoded bytes= / encode failed: 全命中，进度帧差分 17913/17949 采样点）；8 张截图逐张人眼复核（双主题换装、无豆腐块、cancelled 与 dropped 逐像素相同经 EditorWindow L519 Show(_index) 证实为正确还原形态）；构建 0 error；SelfTest.exe → RESULT PASS=75 FAIL=0；settings.json MaxDurationMs 已还原 6000；git status 干净，commit d78d872
next: 跑一遍 `pwsh -NoProfile -File scripts/guardrail.ps1` 全闸门（构建+SelfTest 下限+注入探针+5 项 e2e）作为 done 判定的最终收口证据——UIAUDIT 仅剩 D5（已标注需人工），若闸门 PASS 即可宣布 status: done
status: continue
```

- repo dirty after round: no
- HEAD: d78d872 test(uiaudit): D9 鏀跺彛鈥斺€擧UD 鍥涜竟鐣屾€佸弻涓婚鍍忕礌妗ｆ锛堜涪甯ц鍛?瀵煎嚭杩涘害/鍙栨秷鎭㈠/寮哄埗澶辫触锛? 寮犱汉鐪煎鏍革級锛氳窇閫氬苟淇ソ Round 6 閬楃暀 cap-hud-states.ps1 閫夊抚鍣ㄨ嚧鍛戒激锛堟偓娴潯 SizeToContent 鑷?HUD 涓€鍙樺寘鍥寸洅灏哄灏卞彉锛屾棫鐗堝昂瀵稿叏绛夋墠姣斾护 burst 鍏ㄥ抚琚烦 diffPixels=-1锛涙敼涓哄叏搴忓垪鏈€灏忓叕鍏卞昂瀵?閫愬抚涓績瀵归綈瑁佸垏鍐嶅樊鍒嗭紝瀹炶窇鏈€澶у樊鍒?~1.8 涓囬噰鏍风偣锛夛紱鍙屼富棰樺疄璺?HUD-CAP PASS锛沝9-hud-cancelled 涓?dropped 閫愬儚绱犵浉鍚?鍙栨秷鍒嗘敮 Show(_index) 杩樺師鏃犳畫鐣欑殑姝ｇ‘褰㈡€侊紱瑙ｉ櫎 D6b'缂栫爜鏈熷儚绱犲彇璇佷笉纭畾'鏃ц竟鐣屽苟鍥炴敞锛涢『姝ｈ剼鏈ご娉ㄩ绠楀€?3000鈫?500锛涙瀯寤?0 error + SelfTest 75/75 + 鍙屼富棰樺叏閾捐矾锛堥暱鎸?25s 鐪熶涪甯р啋Enter鈫抔if encoded鈫掑叧绐楋級
- session: session-141d8a08-095e-4925-83a0-feb8cc850d2b
- raw transcript: C:\Users\16892\.dsh\sessions\--D-Backup-Capture--\session-141d8a08-095e-4925-83a0-feb8cc850d2b\session.v3.jsonl.zstd
- read it with: node "D:\Agent Playground\overnight-kit\rounds.mjs" --repo "D:\Backup\Capture" --show 7

---

## Round 8 — 2026-10-03 18:46:48  (status: done)

```text
Round complete. Everything landed cleanly.

**What this round did:** Executed the closing step round 7 set up — the final full gate on the audit-complete HEAD. `scripts/guardrail.ps1` returned **GUARDRAIL PASS (FAIL=0, SKIP=1/9, exit 0)**: all three builds 0 error, SelfTest 75/75 (above the 73 baseline, with the assertions strengthened in rounds 1/3 intact), and all five e2e items passed including the editor-keys capture→editor→Enter→3-clipboard-formats chain.

**SKIP forensics (the only ambiguity):** I ran `test-focus-safety.ps1` standalone to capture its actual reason: `SKIP: 用户前台窗口在用 (FG=…— Google Chrome)，避免抢占焦点` — the documented by-design foreground protection, *not* a recurrence of the false-SKIP disease round 3 fixed. Its PASS evidence chain remains covered by round 27's manual-equivalent run plus the wait-then-run idle watchdog.

**Definition-of-done audit against GOAL.md:**
- UIAUDIT.md: every automation-boundary item `[x]` with commit/screenshot evidence; the sole remaining `[ ]` (D5) carries its explicit "needs real mouse + why" annotation ✔
- `test-out/uiaudit/`: dual-theme archives verified present and matched to entries (editor/settings/help/bars ×2, c1-tool ×2, b1-bars ×2, d8-tray ×2, d9-hud 4 states ×2) ✔
- SelfTest 75/75 with no weakened assertions ✔
- e2e capture chain intact (fresh evidence this round) ✔
- `git status` clean ✔

The gate result was previously only recorded against round 3's HEAD; this round pinned it to the final audit-complete state as commit `d957c42`. All GOAL.md DoD criteria are now satisfied with evidence — the run can close.

ROUND 8 REPORT
did: E 终局闸门收口——在 D8/D9 归档完成后的 HEAD 上复跑 guardrail.ps1 全闸门并把收口记录写进 UIAUDIT.md（绑定最终 HEAD，此前闸门证据只挂在 Round 3）；现场复核唯一 SKIP 的真实原因为 focus-safety 前台保护本因（拒抢用户 Chrome 焦点，by design）
verified: `pwsh -NoProfile -File scripts/guardrail.ps1` → GUARDRAIL PASS（FAIL=0 SKIP=1/9，exit 0：build×3 errors=0、SelfTest PASS=75 FAIL=0、editor-keys/hotkey/tap/fuzz 全过）；`pwsh -NoProfile -File scripts/test-focus-safety.ps1` 打印 SKIP 本因 `用户前台窗口在用 (FG=…Chrome)`；`git status` 干净，commit d957c42
next: 长跑可判定 done；如需继续只剩人工项（D5 吸附/拖框态、focus-safety 空闲值守等效、TODO 保存对话框清单）——建议停止夜间轮次，交还用户做这几项人肉确认
status: done
```

- repo dirty after round: no
- HEAD: d957c42 docs(uiaudit): E 缁堝眬闂搁棬鏀跺彛璁板綍鈥斺€擧EAD d78d872 鐘舵€佸璺?scripts/guardrail.ps1 鍏ㄩ椄闂?GUARDRAIL PASS(FAIL=0 SKIP=1/9, exit 0)锛歜uild脳3 errors=0銆丼elfTest 75/75銆乪ditor-keys涓夋牸寮?hotkey/tap鍥涘満鏅?fuzz 鍏ㄨ繃锛涘敮涓€SKIP鐜板満澶嶆牳涓?focus-safety 鍓嶅彴淇濇姢鏈洜(FG=鐢ㄦ埛Chrome 鎷掓姠鐒︾偣, by design, 闈炲亣SKIP澶嶅彂锛涜椤硅瘉鎹摼鐢辩27杞墜鍔ㄧ瓑鏁堣鐩?锛沀IAUDIT 闄?D5(闇€鐪熼紶鏍囦汉宸?鐞嗙敱)澶栧叏閮ㄦ潯鐩檮璇佹嵁鏀跺彛
- session: session-e232a28b-35d1-45dc-b21a-7f9686447436
- raw transcript: C:\Users\16892\.dsh\sessions\--D-Backup-Capture--\session-e232a28b-35d1-45dc-b21a-7f9686447436\session.v3.jsonl.zstd
- read it with: node "D:\Agent Playground\overnight-kit\rounds.mjs" --repo "D:\Backup\Capture" --show 8

---

## 用户直接指派 — 2026-10-04 录制黄色边框（非夜间长跑轮次）

```text
did: 修掉"录制后全屏黄色边框持续存在"，并让边框按范围显示。根因不是本项目代码，而是 Windows 11
  自带的 WGC 捕获提示边框（#FFD11C）：WgcLiveCapture 用 GraphicsCaptureSession 捕获整台显示器，
  系统边框只按"被捕获对象"绘制，所以必然全屏、与所选范围无关。
  ① 后端用 IGraphicsCaptureSession3::put_IsBorderRequired(false) 关掉系统边框
     （IID F2CDD966-22AE-5EA1-9596-3A289344C3BE / 槽 6-7，取自本机 SDK 10.0.26100.0 的
     winrt\windows.graphics.capture.h；钉住的 19041 投影无此成员→手写 vtable；
     必须从原生 IUnknown QI，托管 CCW 只会 E_NOINTERFACE；set 后回读确认，
     因为官方语义是"未授权时 put 成功但被忽略"）。
  ② 改由自家 RegionOutlineWindow 按真实捕获范围画描边，WDA_EXCLUDEFROMCAPTURE 排除，
     保证只提示、不入镜；新增设置「录制时显示范围边框」（默认开）。
  ③ RegionOutlineWindow 改为构造期 EnsureHandle + 打排除，避免首帧漏进捕获；
     HideSilently/FadeAway/ShowNoActivate 的动画残留互相拽窗的隐患一并收口。
verified: 录制中桌面边缘像素 yellow=0（改前 (220,195,29)=220px）；轮廓落在 2566,474 258x39
  即所选范围（v3-region-zoom.png 人眼复核）；ScopeProbe 同区域桌面 BitBlt accentPx=0=排除生效；
  构建 0 error；SelfTest 77/77（基线 75/75 + 新 T73a/T73b）；pwsh scripts/test-editor-keys.ps1
  → EDITOR-KEYS PASS（剪贴板 CF_HDROP+gif 文件+DIB/BITMAP+FrameCapture.Gif 三格式俱全）
next: 无遗留；如需可把「录制时显示范围边框」做成录制中的呼吸动画
status: done
```

- repo dirty after round: 见本轮 commit
- 证据：`test-out/bugcheck/`（`scan_edges.py` 边缘扫描、`verify_fix.py` 范围/排除核对、`ScopeProbe/` 采集探针）

## 2026-10-11 长跑 R1-R2（DSH goal 轮，04:33 起）
- **R1 轮廓生命周期收口**（bbb2994）：新增 Core/CaptureGuards.cs 决策表（T75a-d）。修两处真实缺陷：
  1) BeginCapture 第一行无条件 HideOutline（守卫在其后）→ 编辑器开着再按捕获键把常显轮廓永久弄没；
  不变量=只有 StartCapture 允许隐藏。2) FinishCapture 无条件 FadeAway、注释谎称由后续分支重新点亮
  → 常显模式一次录制轮廓就不回来（关录制边框时更甚）。新增 scripts/test-outline-lifecycle.ps1
  （桌面截图矩形边框带差分 scripts/lib-band-diff.py + trace 增量断言）：PASS。SelfTest 77→81。
- **R2 全局钩子零阻塞**（90d50d6）：AppHost.Dispatch 阻塞 Invoke → BeginInvoke(Send)（FIFO 保序）。
  旧行为=钩子线程等 UI 跑完 WGC 初始化（trace 实测 ~130ms）才返回 → 全系统键盘卡住 + 超
  LowLevelHooksTimeout 静默摘钩风险；类注释不做耗时操作与代码矛盾。钩子决策抽成
  Core/HookDecision.cs 纯逻辑（T76a-d，81→85）；慢回调看门狗 hook slow 进 trace，回归脚本断言不出现。
- 环境注记：本会话 fs 写盘工具多次报成功但未落盘，此后所有源码改动一律写后读回验证；
  长命令易被 goal 轮打断 → 验证打包成后台任务写结果文件。
- **R3 编辑器退场一致性**（8deb809）：X 按钮走 CloseWithFade(Exit150)，Esc 与 Enter/Commit 却直接 Close()——
  同一窗口双退场手感。三处统一。EDITOR-KEYS PASS + OUTLINE-LIFECYCLE PASS + SelfTest 85/85。
