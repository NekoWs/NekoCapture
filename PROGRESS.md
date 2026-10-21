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

## 2026-10-11 长跑 R4（DSH goal 轮，12:40 起）
- **R4 Fps 钳制单一事实源**：帧率范围此前散落 6 处且互相矛盾——设置滑条/Normalize 10..60、编辑器 5 处 5..60、
  捕获引擎 2 处 5..120。设置恒被 Normalize，编辑器/引擎的宽钳位是误导性死代码（改一处范围必漂移）。
  修复 = AppSettings.FpsMin/FpsMax 常量（10/60）为唯一事实源，滑条、Normalize、编辑器×5、引擎×2 全部引用；
  LiveCapture/WgcLiveCapture 补 using FrameCapture.Core。SelfTest +T77a/b（85→87）：常量=文档范围、
  Normalize 钳越界值且合法值原样通过。行为不变（设置恒 10..60），消除的是未来改范围时的静默漂移。
- **release 重发布补账**（b5f8e6c）：R1-R3 已进 release（exe 12:27:10，pid 78292，二进制含
  "outline: persistent show"/"hook slow"/CloseWithFade 标记）；更正此前 TODO 的过早勾选。

## 2026-10-11 长跑 R5（DSH goal 轮，12:35 起）
- **R5 编辑器重做按钮**：Ctrl+Y 有栈无入口（UndoRedo.Redo/CanRedo 早已实现且 T19 覆盖，UI 只接了撤销）。
  绘制条撤销旁新增重做按钮（Segoe Fluent E7A6，GlyphProbe 矩阵取证：E7A6=右弯箭头，与撤销 E7A7 成对）；
  撤销/重做禁用态统一抽成 SetGhost（IsEnabled + HoverMs 淡入淡出，两钮对称）。
  取证：编辑器工具栏截图（test-out/redo-evidence/toolbar.png）——撤销全亮、重做 0.35 禁用态（CanRedo=false 初始正确）。
  EDITOR-KEYS PASS + SelfTest 87/87 + app build 0 error。GlyphProbe 候选表更新为 Redo 行（scratch 工具，入口临时切
  RenderMatrix 后已还原）。

## 2026-10-11 长跑 R6（DSH goal 轮，12:50 起）
- **R6 裁剪不再销毁标注**：ApplyCrop 原实现 `_anns.Clear()` 一刀切（注释自称"行为可预期"，实为不可逆丢用户作品）。
  新增 Core/CropRemap.cs 纯逻辑决策表：完整位于裁剪框内（Eps 容差）的标注保留并做坐标重映射
  （全帧归一化→裁剪框归一化）；跨界/框外丢弃——钳制会把自由笔/箭头沿边抹成贴边残影，丢弃更可预期。
  SelfTest +T78a/b/c（87→90）：坐标变换、跨界丢弃+输入纯函数性、**像素级往返**
  Bake(Crop(Bake(f,a),r)) ≡ Crop→Remap→Bake 逐字节相等。跨界丢弃语义有测试锁定，无鼠标注入约束下即有效取证。

## 2026-10-11 长跑 R7（DSH goal 轮，13:05 起）
- **R7 编辑器缩放 / 1:1 视图**：新增 Core/ZoomMath.cs 纯逻辑（适配倍率、zoom∈[1,16] 钳制、1:1=1/sFit、
  平移钳制"图像小于视口方向强制居中/放大后边缘不内缩"、锚点缩放不变量、拖拽增量钳制）。
  编辑器 _imgRect 升级为"适配×缩放+平移"唯一几何事实源：Image 改 Fill+显式尺寸/偏移跟随（ApplyImageView），
  标注命中/绘制/裁剪自动随缩放；DispStroke 改与烘焙严格同式（thickness×min(帧W,H)/1080×显示倍率）——任何缩放下所见即所烘焙。
  放大时位图最近邻（像素硬边），适配时高质量。交互：Ctrl+滚轮以光标为锚；PageUp/PageDown 或 Ctrl+加/减 放大缩小；
  Home 或 Ctrl+0 适配；End 或 Ctrl+1 逐像素 1:1；放大后浏览态左键拖/任意工具中键拖平移；HUD 显示 1:1 / 百分比。
  发现并记录：PostMessage 的 Ctrl 不更新 WPF Keyboard.Modifiers（ctrlw 从未被脚本用过的根因）→ 缩放键走裸键路由，
  Shot 的 vk 参数 uint.Parse 不认 0x 前缀（踩坑修正）。
  验证：SelfTest +T79a/b/c/d（90→94，锚点不变量 u0=u1 数学断言）；新增 scripts/test-zoom.ps1 挂入全量闸（第 10 项）：
  End/PageUp×3/Home 键路由 + ≥5 条 editor zoom= 决策 trace；截图取证 HUD "1:1" 与 "195%"（=1.25³，与 ZoomMath 吻合）。
  GUARDRAIL PASS（FAIL=0 SKIP=1 共 10 项，SKIP=focus-safety 环境性）。

## 2026-10-11 长跑 R8-R9（DSH goal 轮，13:30 起）
- **R8 裁剪=视图状态纯逻辑层**（7c382f4）：Core/CropState.cs——裁剪框以全帧归一化矩形栈记录（Push 与当前可视区
  求交+钳 [0,1]²+minW/minH 拒绝退化；Pop/RedoCrop 带 redo 栈，新裁剪清 redo）；Core/ActionOrder.cs——标注/裁剪
  双流共享单调序号，Undo/Redo 弹"最近发生"流（AnnAdded 清 redo、AnnStepped 不清，标准撤销语义）。T80a-f + T81a-c。
- **R9 编辑器接线（延迟裁剪 + 裁剪可撤销）**：ApplyCrop 不再 Bake.Crop 替换帧/重建位图/改 Region——只 Push 视图矩形；
  UpdateImgRect 升级为"裁剪子区×适配×缩放+平移"唯一几何事实源（_viewGrid.Clip 裁掉子区外内容，Image 与标注层同容器同裁剪）；
  缩放/平移/1:1 全部改按裁剪子区计算（CropPx）；撤销/重做双流路由（ActionOrder 裁决，撤销按钮亮灭=两流合并能力）；
  导出收口：StartEncode 后台一次性 Bake.Crop+CropRemap（T78c 已证与旧路径逐字节等价），单帧保存/复制/视觉暂留同走
  ExportFrame/CropFramesForExport；HUD 显示"裁剪 W×H"。收益：裁剪可撤销、二次裁剪=缩窗、帧数据永不破坏、
  裁剪瞬间不再全量重烘焙（UI 零卡顿）。
  验证：SelfTest 103/103（+T80f Pop→Redo 精确还原）；GUARDRAIL PASS（FAIL=0 SKIP=1 共 10 项，EDITOR-KEYS 覆盖改动的
  StartEncode 路径）。裁剪拖框本身仍是鼠标路径（GOAL 禁注入），像素级等价性由 T78c 锁定。

## 2026-10-11 长跑 R10（DSH goal 轮，14:00 起）
- **R10 Esc 分层**：原 Esc 在绘制态直接关窗——误触一次丢掉整次捕获。新增 Core/EscStep.cs 纯逻辑决策
  （导出中=取消导出 → 绘制态=退出工具并丢弃进行中的一笔 → 浏览态=关闭），SetTool(Browse) 前 CancelPendingStroke
  释放捕获/删预览/不提交。帮助与 README 同步"误触不丢捕获"语义。
  验证：SelfTest +T82a（104/104）；新增 scripts/test-esc-layers.ps1 挂入全量闸（第 11 项）：
  D 进绘制态 → Esc 编辑器仍在 + trace esc→browse → 再 Esc 关闭，ESC-LAYERS PASS。fuzz 收尾本就多层 Esc，兼容。

## 2026-10-11 长跑 R11（DSH goal 轮，13:30 起）
- **R11 真无框 + 入场黑闪根治（用户逐帧审查揪出的两个缺陷）**：
  ① 白条根因 = WindowStyle=None 仍保留 DWM 非客户区顶带；extend-frame(-1) 反而铺白玻璃（像素取证 y=2..8=255）。
  正解 = System.Windows.Shell.WindowChrome（CaptionHeight=0, ResizeBorderThickness=5, GlassFrame=0）——
  WPF 内部处理 WM_NCCALCSIZE，硬件加速保留；DWMWA_WINDOW_CORNER_PREFERENCE=ROUND 得原生圆角+阴影（QQ 式）。
  Fluent.BorderlessNative() 统一应用（编辑器/选区窗；选区窗 resizeBorder=0）。
  ② 入场黑/白闪根因 = WPF 首帧前 Window.Opacity=0 未落到 HWND，DWM 先亮一帧不透明表面。
  正解 = PreTransparent：SourceInitialized+LayoutUpdated 反复 SetLayeredWindowAttributes(alpha=0)（WPF 首渲染会抹掉，必须重贴）；
  编辑器再加 offscreenFirstFrame：首帧停在 -32000，ContentRendered 回正位再强制 alpha=0，下一 CompositionTarget.Rendering 才起淡入。
  逐帧取证（UICap burstregion + bbox 侧车）：f000 屏外不可见 → f001 回屏=桌面透出(33.9) → 平滑淡入 17.4→35.0，无黑帧无白帧。
- **逐帧审查工具链**：UICap burstregion 每帧 bbox 写入 .times.txt 侧车（离屏帧可辨）；montage 拼图逐帧读图；
  像素边缘检查（顶行 y=0..12 + 四角）——亮度均值会掩盖白条这类边缘缺陷，必须显式查边。
- **zoom e2e 加固**：测试自钉 1726x980 大区域（小区域下 fit 钳到 1.0，End=1:1 变无操作 → zoomLines 4<5 假失败，本轮踩过）；跑完恢复用户配置。
  验证：SelfTest 104/104；GUARDRAIL PASS（FAIL=0 SKIP=1 共 11 项）。

## 2026-10-11 长跑 R12（DSH goal 轮，14:00 起）
- **R12 用户打磨清单落地（逐帧审查驱动）**：
  ① 删点工具：手绘一下就是点。Tool/ToolOrder/快捷键 D/帮助条目全清；AnnKind.Dot 保留（模型层，SelfTest T78 等仍覆盖）。
  ② FloatBar 悬浮条整体删除（-270 行）：改停靠面板——root Grid 三行（预览*/播放面板 Auto/工具面板 Auto），
     录制时播放面板常驻预览正下方（不再悬在画面里、不再自动隐藏）；面板=PanelBg+顶部 1px 分隔线，工具面板底角 8px 收口。
  ③ PR 式刻度时间轴：全宽 26px——逐帧小刻度（<3px 自动抽稀）+整秒长刻度+秒标、底轨+accent 已走填充、
     裁剪范围外整列压暗+两端通高 accent 把手、白色播放头竖线+顶部三角（与填充同坐标，不可能分离）。
  ④ 面性图标定稿（渲染矩阵人工核对，纠正 sweep 网格误读）：⏮F8AC ◀F08D ▶F5B0 ⏸F8AE ▶F08F ⏭F8AD
     存E105 复制F413 GIF E116 暂留E955 ✓F78C ✗E711 设置E713 帮助E9CE 线E949 圆EA3B 箭头F0AF 铅笔E70F 裁剪E7A8 宽度点E915。
     （教训：sweep 网格按行号心算错位——必须渲染带 hex 标签的候选矩阵逐个核对；F08C 实为太阳、F08E 实为下三角。）
  ⑤ 紧凑化：图标按钮 36→28（字 15→13）、色板 26→22、宽度钮 28→24、窗口高度按面板数自适应（多帧+250/单帧+210）。
  ⑥ 逐帧取证：入场无闪帧（面板随窗一体淡入）；多帧/单帧两态面板截图核对；T10 白名单同步新字形集。
- **测试同步**：test-esc-layers 的进绘制态键 D→L（点工具已删，语义不变）；cap-hud-states 取证目标 Bar 窗→编辑器窗本体。
- **构建教训**：dotnet build 不带 -p:Platform=x64 会输出到 bin\Debug（AnyCPU），运行实例仍跑旧 dll——
  本轮曾因此"看到旧界面"排查半天；此后一律带 Platform=x64 并以 dll 内符号（BuildPanels/FloatBar）验证新旧。
  验证：SelfTest 104/104；GUARDRAIL PASS（FAIL=0 SKIP=1 共 11 项）。

## 2026-10-11 长跑 R13（DSH goal 轮，14:30 起）
- **R13 自定义颜色 + 自定义线宽（用户清单第 2 条落地）**：
  ① 调色板按钮（E790，GlyphProbe 矩阵定稿）→ 原生 ColorDialog（FullOpen：色相板+RGB+自定义色槽）；
     最近用色 ≤6 条钉在按钮右侧（点击即用），置顶去重、大小写不敏感——纯逻辑 Core.CustomPick.RememberColor，
     持久化 settings.CustomColors（实测 #E81123 落盘）。快捷键 K。
  ② ⋯ 按钮（E712）= 第 4 宽度槽位 → 滑杆弹层 1..24px（Fluent 滑杆+实时数值+预览圆点，Popup Fade，StaysOpen=false）。
     快捷键 W。宽度选择决策表 Core.CustomPick.Width（预设区间/自定义钳位），SelfTest T83a-f 六断言。
  ③ Esc 分层再进一层：弹层打开时 Esc 只收弹层（实测：第一次 Esc 编辑器存活，第二次才关）。
  ④ 逐帧审查发现并修复：Popup 从 PlacementTarget（图标按钮）继承图标字体 → "6px" 变豆腐；TextBlock 显式 Segoe UI。
     （教训：WPF Popup 属性继承自 PlacementTarget，锚在图标按钮上的浮层必须显式复位 FontFamily。）
  ⑤ 帮助窗口同步：调色板 K / ⋯ 宽度 W 两条；"右侧竖排"→"底部工具条"；时间轴描述改把手语义。
  验证：SelfTest 110/110（+6）；GUARDRAIL PASS（FAIL=0 SKIP=1 共 11 项）。

## 2026-10-11 R13 补账（release 重发布 + 工作流文档化）
- **release 重发布**：R4-R13 全部进 release（publish 14:35:35，dll 探针 BuildPanels=True/FloatBar=False/RememberColor=True）；
  用户稳态实例已恢复（release pid 67336），长按 F6 出新版停靠面板编辑器，实测通过。
- **GOAL.md 固化两条硬教训**：① 构建必须 -p:Platform=x64（省略=AnyCPU 进 bin\Debug，运行实例静默用旧 dll）+
  截图前 dll 新鲜度探针；② 逐帧视觉审查工作流（burstregion→montage→原生分辨率裁条放大→GlyphProbe 带标签矩阵）。
  SelfTest 基线注释 73→110。

## 2026-10-11 长跑 R14（DSH goal 轮，14:45 起）
- **R14 时间轴秒标自适应（逐帧审查发现）**：49 帧短片段时间轴拉满宽度却只有整秒刻度可标（i%fps==0 只有 0）——
  实际一枚秒标都没有。改为 Core.TimelineScale.LabelStepFrames 决策表：候选 {0.05..30}s 选像素间距 ≥90px 的最小档。
  实测 51 帧短片现显示 0.1s..0.8s 秒标 + 逐帧小刻度 + 整步长刻度线，PR 观感达成。
  SelfTest +T84a-d（110→114）：短/长/中密度 + 退化输入（0宽/0fps→1，极窄→最大档 30s）。

## 2026-10-11 长跑 R15（DSH goal 轮，14:55 起）
- **R15 时间轴裁剪 affordance + 选色器 owner**：
  ① 未裁剪时时间轴两端不可见把手——用户不知道能裁剪。悬停时间轴时两端淡显 3px 把手（MouseEnter/Leave 重绘），
     离开即隐；裁剪激活时仍是通高 accent 把手（R12 语义不变）。
  ② ColorDialog 挂 owner（Win32Owner 包 WPF 句柄）：模态于编辑器、居中其上（实测对话框中心 x=1920=编辑器中心），
     不再可能弹到编辑器之后/别的屏幕。Esc 取消不新增用色，实测通过。
  验证：build 0 错误；SelfTest 114/114（无新断言，纯 UI 分支）；门禁 R14 已全绿（本轮改动不触决策表）。

## 2026-10-11 长跑 R16（DSH goal 轮，15:05 起）
- **R16 HUD 导出段提示**：裁剪激活时 HUD 追加"导出 12–40"（帧号区间），导出内容与提示一致可核对；
  未裁剪不显示（实测 HUD 保持"1/36 0.09s"干净）。HUD 现有信息：帧号/时间/倍速/缩放/画面裁剪/导出段/丢帧警告。

## 2026-10-11 长跑 R17（DSH goal 轮，15:20 起）
- **R17 选区窗整窗隐形 bug（逐帧审查抓到的 6 轮潜伏回归）**：R11 给选区窗加了 FadeIn(offscreenFirstFrame)，
  两个叠加缺陷：① Park 在 SourceInitialized 抓 home=NaN（选区窗 Left/Top 在 Loaded 才赋值），
  ContentRendered 回写 NaN → 系统级联位兜底（实测偏移 266,266，提示被推出屏外）；
  ② 更致命：AllowsTransparency 分层窗在 DWM 合成下 WPF 不再调 UpdateLayeredWindow，
  PreTransparent 手工 SetLayeredWindowAttributes(alpha=0) 永远不会被清 → 遮罩+提示自 R11 起整窗隐形，
  选区窗退化成透明点击捕获层（e2e 全绿也发现不了——它们只断言行为不断言可见性）。
  修复：Fluent.FadeIn 对分层窗走独立路径（无 PreTransparent、无停放，ContentRendered 直接淡入——
  分层窗首帧前本就没有黑闪）；选区窗去掉 offscreenFirstFrame；删除从未入树的死代码 _veil/_spot。
  取证：同坐标均值 34.2→21.2（0x60 遮罩数学吻合）；提示 pill 回归、位于任务栏上方完整可见；
  rect 归位 0,0 3840x2160。教训入 GOAL.md：入场动效改动必须截图验证内容可见，行为 e2e 绿不等于看得见。
  验证：GUARDRAIL PASS（FAIL=0 SKIP=1 共 11 项）；SelfTest 114/114。

## 2026-10-11 长跑 R18（DSH goal 轮，15:40 起）
- **R18 设置/帮助窗同病同治 + 设置窗居中**：R17 的分层窗 LWA_ALPHA 陷阱同样命中 SettingsWindow/HelpWindow
  （AllowsTransparency + PopupEnter→PreTransparent，自 R11 起整窗隐形；FC_UIPREVIEW 调试入口实测）。
  PopupEnter 对分层窗走独立淡入路径（同 FadeIn）。修复后实测：设置窗全控件清晰、帮助速查完整（含 R13 新条目）。
  顺带修设置窗定位：SizeToContent 下 Show 前 ActualWidth=0，手工 CenterOnWorkArea 退化成"左上角落屏幕中心"
  （实测 1920,1044）→ 改 WindowStartupLocation=CenterScreen（实测 1605,531 精确居中），删死方法。
  取证：test-out/framereview/R18/settings.png、help.png。

## 2026-10-11 长跑 R19（DSH goal 轮，15:55 起）
- **R19 浅色主题逐帧复查**：Theme=light 实测编辑器/选区窗/设置窗。抓到 1 处：播放头白色竖线+三角在浅色面板上
  完全隐身（R12 定稿时只审了暗色）。改跟 Theme.Text（暗=白、浅=近黑），像素验证 x263 列 26/253 高对比。
  浅色下其余全部合格：刻度/秒标/滑杆/开关/主题按钮/色板描边/反色主按钮。设置窗浅色截图留档 settings-light.png。
  教训：双主题 UI 的每次视觉定稿必须两个主题各出一张裁条，单主题审查=半个没审。

## 2026-10-11 长跑 R20（DSH goal 轮，16:15 起）
- **R20 帮助窗高度自适应**：R13 加了 2 条后 806 固定高装不下，末条"底部工具条"被裁一半（浅色截图抓到）。
  改 SizeToContent=Height + MaxHeight=1040（超高才滚动）+ CenterScreen。实测 640×941 DIP 全条目完整，
  底部"其他/小提示"区可见。release 重发布补账：R14-R19 已进 release（IL 探针 LabelStepFrames/Win32Owner=True、
  CenterOnWorkArea 已消失），用户实例已恢复（pid 68944）。

## 2026-10-11 长跑 R21（DSH goal 轮，16:30 起）
- **R21 保存对话框补 owner**：保存帧/保存 GIF/保存残像三个 SaveFileDialog 无 owner——编辑器 Topmost 时
  对话框可能被压到编辑器之下（与 R15 颜色对话框同病）。改 ShowDialog(this)。全项目 ShowDialog 复查：仅此三处裸调用。
- 录制边框复查：burstdesk 中边框不可见 = WDA_EXCLUDEFROMCAPTURE 设计使然（对用户可见、对捕获隐身），
  trace 确认 outline shown/hide 节拍正常。Agent 胶片条产物复查合格（12 帧、帧号+时间戳清晰）。
- 验证：GUARDRAIL PASS（FAIL=0 SKIP=1 共 11 项）。

## 2026-10-11 长跑 R22（DSH goal 轮，16:50 起）
- **R22 浅色弹层/流程复查（无代码改动）**：宽度弹层浅色合格（白底圆角+蓝滑杆+6px 预览点）；
  色板行浅色描边清晰；播放头修复在浅色下实测可见（x263 深色列）。
  单帧流复查：TapAlwaysShowsRegion=true 时点按→选区窗→Enter=静帧 PNG+剪贴板+轮廓闪光反馈（设计如此，trace 吻合）；
  编辑器开着按 F6=RefocusEditor 不重入捕获（决策表正确，此前"卡死"疑云是颜色对话框吞 Esc 造成的测试环境不干净）。
  教训：UI 自动化测试前必须枚举并清理残留模态窗（颜色对话框 keyw 打不进模态循环）。

## 2026-10-11 长跑 R23（DSH goal 轮，17:05 起）
- **R23 设置窗热键捕获 Esc 语义修复**：窗级 PreviewKeyDown（关窗）先于行级订阅（同为窗事件，按订阅序触发），
  捕获新键时按 Esc 会直接关掉整个设置窗——"Esc 取消"形同虚设。加 _hkCancel 单例：捕获中窗级 Esc 放行给行级取消；
  再点另一行自动取消上一行捕获（互斥）。
- **帮助补保存快捷键**：Ctrl+S/C/E/H 早已实现（tooltip 有、键路由有）但帮助页没写——底部工具条行补齐四条。
  实测帮助 960×1437 全条目完整。验证：GUARDRAIL PASS（FAIL=0 SKIP=1 共 11 项）。

## 2026-10-11 长跑 R24（DSH goal 轮，17:25 起）
- **R24 托盘打磨**：管理员项文本去 ✓（checkable 已带勾，双勾重复）；托盘图标从虚线矩形（16px 缩略糊成团）
  改为四角实心括号+中心点取景框，AntiAlias+圆头线帽，32/16/64 三档渲染验证清晰可辨。

## 2026-10-11 长跑 R25（DSH goal 轮，17:50 起）
- **GIF 编码质量三连**（T85 新增断言，115/115）：
  1) Floyd–Steinberg 误差扩散（蛇形扫描，零误差像素跳过传播→平坦区/文字不引入噪点）；误差缓冲 ThreadStatic 复用防 LOH GC 抖动。
  2) median-cut 改 跨度×像素数 加权分裂 + 按像素数取中点（纯 span 优先让杂色垄断调色板，大面积渐变被挤成两三档）。
  3) 归约键 5bit→6bit：5bit 把暗渐变灰与暗杂色并进同桶、桶均值拉离灰轴，渐变端头整段同档（T85 逐行均值抓到 7 档偏离，修后 ≤2.5）。
- 代价：渐变多的真实内容 GIF 2.1→5.3MB（噪点熵↑LZW 压缩率↓）；平坦 UI 内容误差恒 0 自动跳过、体积不变。4K×30 帧编码 4.0s（并行）。质量优先，符合"完美"取向。
- 验证：SelfTest 115/115（含 T85a 逐行均值 ≤2.5）；GUARDRAIL PASS（FAIL=0 SKIP=1）。

## 2026-10-11 长跑 R26（DSH goal 轮，18:00 起）
- **R25 抖动的真实产物逐帧审查**（Enter→%TEMP%\capture-*.gif 解码对比新旧）：
  1) 6bit 键缓存把误差反馈量化成 4px 周期竖纹（暗区 3x 放大可见）→ 缓存升 7bit（2M 项，桶宽 2）。
  2) 近平坦区源噪声被抖动放大成可见噪点 → 三通道误差绝对值和 ≤2 不传播：平坦区回到逐字节平坦（与旧编码器同字节），
     真渐变误差累积过阈值继续驱动抖动（T85 仍过）。
- 真实帧验证：暗区 3x 放大完全平坦（950B PNG=旧编码器同图同字节）；整帧文字锐利；GIF 1.92→1.56MB（比 R25 还小，比旧版也小）。
- 验证：SelfTest 115/115；GUARDRAIL PASS（FAIL=0 SKIP=1）。

## 2026-10-11 长跑 R27（DSH goal 轮，18:15 起）
- **T86 视觉暂留断言**（118/115→118）：末帧全亮、尾迹单调衰减、PNG 落盘；合成图目检 5 档渐亮尾迹正确。
- **热键同键冲突内联警示**：管理器本就"捕获优先"确定性消歧，但用户静默失去选区键——设置窗两行下方加琥珀色警示
  （开窗即检 + 每次改键刷新）。实测同键截图确认显示、恢复后消失。
- **选区快捷键图标 E71A→E7A8**：E71A(圆角矩形)与"常显轮廓"行重复且语义弱，改裁剪角标；GlyphProbe 矩阵复核 E7A8 存在。
  （曾疑 E71A 豆腐块——2x 放大复核为误判，字形正常。）
- 双主题设置窗整窗目检：全部图标渲染、滑杆/开关/主题按钮两主题均清晰。
- 验证：SelfTest 118/118；GUARDRAIL PASS（FAIL=0 SKIP=1）。

## 2026-10-11 长跑 R28（DSH goal 轮，18:30 起）
- **编辑器双主题整窗逐帧审查**：暗色全项正常；浅色抓到白色色板融进白工具条（Theme.Stroke 1px 太淡）
  → 色板/最近用色环改 40% 文字色（两主题恒可见），2x 放大复核白圈清晰、选中环仍用强调色。
  其余（播放键/时间轴/播放头/工具键/撤销重做/×✓）浅色下均清晰。
- 验证：GUARDRAIL PASS（FAIL=0 SKIP=1，SelfTest 118/118）。

## 2026-10-11 长跑 R29（DSH goal 轮，19:00 起）
- **选取窗双主题逐帧审查**：veil 数值复核（选区外均值 21.2=R17 基准）、虚线强调色框、底部提示胶囊
  「拖框选择·悬停点击吸附窗口·Enter 确认上次选区·Esc 取消」清晰无主题依赖问题。无改动。
- **设置窗键盘可达性**（纯键盘可完成全部设置）：
  1) 热键框/开关/主题芯片 Border 加 Focusable（Tab 停靠），空格/Enter 走与点击同一动作路径；
  2) 焦点环：热键框强调色描边、芯片 1px 强调边框、开关 WPF 默认焦点虚框（截图确认清晰）；
  3) 滑杆 SmallChange=档位——吸附开启时默认步长 1 会被吸回原档、方向键看似失灵；
  4) 热键框 Begin() 后 box.Focus()，捕获键直达窗级处理器。
- 实测（keyw Tab/Space/Esc）：Tab×2→捕获框焦点环；空格→「按下新键…」捕获态；Esc 取消且窗存活；
  Tab×8→轮廓开关焦点环；空格×2→False→True→False 落盘复原。
- 验证：GUARDRAIL PASS（FAIL=0 SKIP=1，SelfTest 118/118）。

## 2026-10-11 长跑 R30（DSH goal 轮，19:15 起）
- **帮助窗双主题逐帧审查**：浅色全绿（章节强调蓝、键列/说明列对比、✓✗ 字形、底部工具条行完整）；暗色 R23 已审。
- **HUD 倍率显示实测**：PageUp×3 → 时间轴行右侧「1/9 0.10s 181%」清晰；右侧导出图标列（存/复制/GIF/暂留/×/✓）两主题均清晰。
- GOAL.md 修正：burstdesk 示例补全 3 参数、SelfTest 基线 110→118、UICap 输出目录须先存在的坑写入。

## 2026-10-11 长跑 R31（DSH goal 轮，19:30 起）
- **时间轴裁剪键盘化**：[/] 端点=当前帧（复用 SetA/SetB 钳位，至少 2 帧），Backspace 清除；帮助窗补「[ / ]」行。
  实测（9 帧录制，idx3）：[→「导出 4–9」、]→「导出 4–5」（钳位生效）、Backspace→清除。
- **HUD 差一 bug 修复**（键盘实测抓到的既有缺陷）：「导出 a–b」显示 tb2 未 +1，含两端闭区间实为 1-based 尾+1
  ——曾显示"导出 4–7"实导 4–8。播放循环与导出切片本身正确，仅显示错。修复后 4–9/4–5 复核正确。
- 验证：GUARDRAIL PASS（FAIL=0 SKIP=1，SelfTest 118/118）。

## 2026-10-11 长跑 R32（DSH goal 轮，19:50 起）
- **GIF 时长守恒重采样**（Core.GifTiming 纯逻辑 + 编码器入口接入）：GIF 厘秒计时且 1cs 被部分播放器当 0，
  60fps（16.7ms）直存整体提速 1.2×、30fps 也有 ~10% 漂移。新算法：被丢帧时间累积进下一保留帧，
  <35ms 优先均匀 2cs——60fps→48 帧@20ms（50fps 平滑、总时长精确）、30fps 不丢帧仅 20/40/50 交替均值 33.3、
  100fps→50 帧@20ms；末帧永不丢。
- T87a-d 断言：60fps keep∈[45,55] 总时长 ±5ms/帧、30fps 全保留、单帧、60 帧端到端解码帧数=重采样帧数。
- 验证：SelfTest 122/122；GUARDRAIL PASS（FAIL=0 SKIP=1）。

## 2026-10-11 长跑 R34（DSH goal 轮，20:05 起）
- **真实 GIF 延迟表端到端验证**：录 1s→Enter→解码读 FrameDelay：40ms×33 帧与真实内容跨度守恒（重采样正确；
  捕获管线 ~25fps 交付为既有特性非 bug）。
- **Agent 胶片条目检**：12 帧双行、每格 F 序号+时间戳、缩略清晰 ✓；确认胶片条用裁剪烘焙后序列（与 GIF 同源）。
- **极小区域(80×60)边界**：GIF 1.4KB 正常；但编辑器最小宽 560 DIP 时底部工具条两端裁切（齿轮/帮助不可见、首键半截）
  → MinWidth=780 DIP（全列实测宽），初始宽同步；复测 780 DIP 下全工具条完整。
- 验证：GUARDRAIL PASS（FAIL=0 SKIP=1，SelfTest 122/122）。

## 2026-10-11 长跑 R35（DSH goal 轮，20:30 起）
- **设置窗小屏防护**：内容 ~685 DIP 固定高，矮工作区（笔记本 768/100%）会把主题行裁掉——
  面板包 ScrollViewer(Auto) + MaxHeight=工作区-48（SizeToContent 受 MaxHeight 封顶）。帮助窗本就有滚动，唯设置窗缺。
  大屏复测：630×1027 与改前一致、无滚动条出现。
- 验证：GUARDRAIL PASS（FAIL=0 SKIP=1，SelfTest 122/122）。

## 2026-10-11 长跑 R36（DSH goal 轮，20:40 起）
- **SettingsService.Changed 事件**：Mutate/Reload 锁外触发；处理器可安全回读 Current。
- **托盘悬停提示动态化**：「捕获 F6 · 选区 F7 / 范围 1726×980 · 60fps」，改键/换范围/fps 即时刷新（Changed→Dispatch）；
  未设范围提示"双击图标设置"；NotifyIcon.Text 63 字符上限防御回退。
- 验证：GUARDRAIL PASS（FAIL=0 SKIP=1）；e2e 改键路径实际穿过 Changed→UpdateTip，err_delta=0 无崩溃。

## 2026-10-11 长跑 R37（DSH goal 轮，20:50 起）
- **README.txt 全面对齐现状**：删 FloatBar/自动隐藏旧描述与已删"点"工具；补 [ / ]+Backspace 裁剪、倍速、
  重做 Y、调色板/最近用色、W 线宽弹层、HUD 倍率/导出范围、GIF 厘秒保时长、托盘动态提示、双击=选范围。
- **README 随包发布接线**：csproj None+Link+CopyToOutputDirectory（发布包内曾陈旧于 10-03）；Version 0.1.0→0.9.0。
- 验证：GUARDRAIL PASS（FAIL=0 SKIP=1，SelfTest 122/122）。

## 2026-10-11 长跑 R38（DSH goal 轮，21:00 起）
- **F1 直达帮助**：编辑器与设置窗 F1 → 帮助窗；设置窗录键中 F1 放行（是新热键候选，与 Esc 同守卫）。帮助窗补 F1 行。
  实测：设置窗 F1 ✓、编辑器（长按录制）F1 ✓（960×1498）。
- 测试环境注记：用户基线 TapAlwaysShowsRegion=True（QQ 式点按先出选区，全部备份一致，非测试残留）；
  点按用例走 tap→picker 属预期，编辑器用例须长按；选区窗残留会让 BeginCapture 正确忽略（R1 状态守卫）。
- 验证：GUARDRAIL PASS（FAIL=0 SKIP=1，SelfTest 122/122）。

## 2026-10-11 长跑 R39（DSH goal 轮，21:10 起）
- **发布实例全流程逐帧复查**（burstdesk 6s×20 帧，并行 长按录制→PageUp→[→]→Enter）：
  编辑器入场/时间轴刻度/工具条/HUD「116% 导出 1–19」/编码中 HUD「导出 GIF…」/退场 全部逐帧确认。
- **逐帧抓到 HUD 差一 #2**：全范围裁剪（[ 在首帧）仍显示「导出 1–19」——显示条件 tb2<count 应为 tb2<count-1。
  修复复测：全范围无文字、idx2 起裁剪显示「导出 3–9」✓。
- 验证：GUARDRAIL PASS（FAIL=0 SKIP=1，SelfTest 122/122）。

## 2026-10-11 长跑 R40（DSH goal 轮，21:25 起）
- 清理会话期 settings.json.* 备份 10 份（当前配置已逐项核对=用户基线：system/F6/F7/1726×980/QQ式点按）。
- GOAL.md SelfTest 基线 118→122。
- 发布实例（R39 二进制）冒烟：F6 长按→编辑器→Esc 正常。

## 2026-10-11 长跑 R41（DSH goal 轮，21:40 起，收尾轮）
- 浅色主题下裁剪 HUD 复查：「2/8 0.15s 导出 3–4」清晰；SetB 钳位（idx1 处 ] → 3–4，至少 2 帧）正确。
- 设置恢复用户基线并删除本轮备份；发布实例（R39 二进制 + R37 README + 0.9.0）重启常驻托盘。
- 长跑收口：R30–R41 共 12 轮全部提交；门禁 11 项连绿；SelfTest 122/122；用户配置零污染。

## 2026-10-11 夜 交互优化 R42（新目标：超过 QQ 截图）
- **截屏成功浮窗 CaptureToast**（选区→截图链路补上"拍完即见"）：缩略图+「已复制到剪贴板」+
  「点击或按 Enter 继续标注 · Esc 关闭」；不抢焦点、WDA 排除不入镜（FC_TOAST_NOEXCLUDE=1 仅供审查截图）、
  悬停暂停 3.2s 自动消失；落点 Core.ToastPlace 纯逻辑（大区域贴内侧右下/小区域外下缘/越界翻转钳边，T88a-e）。
- **点击/Enter → 单帧编辑器**：OpenEditor 从 FinishCapture 抽出复用；OpenEditorForFrame 带 Idle 重入守卫+焦点归还。
- **冷启动点按误判长按（真 bug，e2e 抓到）**：首次 WGC 初始化 ~400ms 被算进按住时长 → 点按直接进编辑器。
  修复：HotkeyManager 用钩子事件时间戳（KBDLLHOOKSTRUCT.time 差）测真实按住时长传给 EndCapture(holdMs)，
  本地时钟只兜底托盘/自动停路径。冷启动复测 tap→picker ✓。
- 浮窗不抢焦点收不到 PostMessage 键 → HwndSource.AddHook 直接处理 WM_KEYDOWN(Enter/Esc)。
- 验证：SelfTest 127/127（+T88×5）；全链 e2e（点按→选区 Enter→截图→浮窗 Enter→编辑器 frames=1）trace 全绿；
  门禁 GUARDRAIL PASS（FAIL=0 SKIP=1）；浮窗视觉取证 toast-visual.png。

## 2026-10-11 夜 交互优化 R43（长按=录制「看得懂」）
- **REC 徽标**：按住超过点按阈值（TapThresholdMs+60ms）才点亮——红底白点呼吸脉冲 + 实时时长「REC 0:00.5」，
  钉在范围轮廓内侧右上角；松手即灭（FinishCapture 统一 Stop）。"还在按 = 正在录"从隐性约定变成看得见的状态。
- **首次教学提示**：没录过制时，选区提示第二行强调色「提示：按住 F6 不放 = 录制 GIF（点按 = 截图）」；
  首次真正起录（徽标点亮）即持久化 RecHintShown=true，之后不再打扰。
- **逐帧抓到轮廓定位老 bug（R43 修复）**：WPF 布局会用 DIP 覆写 SetWindowPos 的物理定位，
  150% 屏上录制轮廓整体偏左上（681→454=÷1.5）。修复：Place() 上屏后把同一物理矩形换算成 DIP 同步给
  WPF（Left/Top/Width/Height），令回写幂等；LocationChanged 兜底钉回。长按中全屏扫描 y=521 边框 127 蓝点 ✓。
- 徽标红点改白（红底上红点糊掉，白点=经典录制符号）。
- 验证：SelfTest 127/127；门禁 GUARDRAIL PASS（FAIL=0 SKIP=1）；
  取证 R43/teach-hint-crop2.png（两行提示）/ teach-gone-crop2.png（一行）/ R43c/badge-final.png（REC 徽标+轮廓在位）。

## 2026-10-11 夜 交互优化 R44（编辑器「总在最前」图钉，用户点单）
- 工具条右侧新增图钉按钮：空心 E718=未钉 / 实心 E840=已钉 + accent 高亮（GlyphProbe pins 已验证字形）。
  EditorTopmost 持久化，默认关；开启时保留旧「失活重插置顶带」行为，关闭时绝不被 Deactivated 兜底钉回。
- MinWidth 780→812（+图钉按钮宽，防裁切）；帮助窗 + README 同步（点按新链路/REC 徽标/图钉）。
- e2e（Shot mouse 真点击）：默认非置顶(ex 0x100) → 点击置顶(ex 0x108=WS_EX_TOPMOST) → 再点关 →
  再点开 → Esc 关编辑器重录制重开=仍置顶（持久化生效）→ 复位默认关。trace editor topmost=True/False 全对。
- 取证坑（记入 GOAL.md）：pwsh Add-Type 进程 DPI-unaware，GetWindowRect/SetCursorPos 被 ÷1.5 虚拟化，
  Shot 是 aware——按钮点击坐标 = pwshRect×1.5+窗口内物理偏移；WS_EX_TOPMOST=0x8 非 0x80000。
- 验证：SelfTest 127/127；门禁 GUARDRAIL PASS（FAIL=0 SKIP=1）；pin-on.png 图钉 ON 态视觉确认。

## 2026-10-11 深夜 交互优化 R45（编辑器易用性第一批：裁剪引导 + 首次上手卡 + 撤销步数）
- 裁剪拖框引导层：拖框时框外 45% 压暗（EvenOdd Path）+ 三分网格 + accent 气泡「松开应用裁剪」
  （CropGuide 纯逻辑：任意方向拖框归一 + 气泡下方放不下翻上方/钳边，SelfTest T89a–e）。
- 首次上手引导卡：编辑器第一次打开弹一次性卡片（标注/裁剪/确定取消/更多 四行 + 「知道了」），
  EditorGuideShown 持久化；引导打开时吞掉全部按键（Enter/Esc 只关卡，绝不落到确定/放弃上）。
  修两坑：IconButton 默认图标字体把中文渲染成豆腐块（ClearValue 回继承字体）；size 参数同时定宽高（Height=30 压扁）。
- 撤销/重做 tooltip 带步数：「撤销上一笔（可撤 N 步）Ctrl+Z」，双流（标注+裁剪）合并计数
  （UndoRedo.RedoCount / ActionOrder.CropUndoCount/CropRedoCount 新 API）。
- e2e：引导卡首开出现→点「知道了」关闭→flag=True→重开不再出现；裁剪拖框中途截帧见遮罩+网格+气泡
  （像素验证框外 25→14 = ×0.55 压暗、框内 accent 着色），松开 HUD「裁剪 719×432」引导层收干净。
- 验证：SelfTest 132/132（127→132）；门禁 GUARDRAIL PASS（FAIL=0 SKIP=1）；引导标记已复位 False 留给用户首见。

## 2026-10-11 深夜 交互优化 R46（文字标注 + 导出所见即所得修复）
- 文字工具 T（QQ 级能力补齐）：点画布就地输入（accent 边框输入框，字号随线宽挡位 AnnFont 公式），
  Enter/失焦上屏、Esc 取消；输入中编辑器快捷键全部让位（Enter/Esc 不会误触确定/放弃）。
  模型 AnnKind.Text+Text 字段；烘焙 GDI+ 雅黑加粗，预览 WPF TextBlock 同一 AnnFont.BakePx → 所见即所得。
- 顺手修 CropRemap：重映射丢 Text 字段（裁剪后文字会变空）→ 复制保留，T90e 锁住。
- 修长期 bug：复制帧 Ctrl+C / 存帧 Ctrl+S 不带标注（ExportFrame 只裁剪不烘焙）！
  现与 GIF 确定同管线：裁剪→CropRemap→Bake；视觉暂留 Ctrl+H 合成图同样带标注；删死代码 ExportSequence。
- 工具条：AA 图标 E8D2（GlyphProbe 验证）插在手绘与裁剪之间；T 快捷键；帮助窗/README 同步。
- e2e：T→点画布→Ctrl+V 中文→Enter 编辑器红字渲染（像素 bbox 195×41）→Ctrl+C 剪贴板位图含"烘焙验证"红字（修复前 0 个红字像素）。
- 验证：SelfTest 137/137（132→137，T90a–e）；门禁 GUARDRAIL PASS（FAIL=0 SKIP=1）。

## 2026-10-12 凌晨 交互优化 R47（马赛克工具）
- 马赛克 M（QQ 级能力）：工具条网格图标 E80A（GlyphProbe 验证）/ M 键，拖框实时块均值预览 + accent 虚线框，
  线宽挡位=块粒度（AnnFont 同思路：MosaicBlock.Px 细6/中12/粗20 @1080p 随帧缩放，下限 4）。
- 预览与烘焙共用 BgraImage.MosaicInPlace（纯字节块均值，越界钳制）→ 所见即所得；Bake.Apply 先铺马赛克再叠矢量标注。
- 修 ImageBrush.Viewport 坐标系 bug：ViewportUnits.Absolute 是元素本地坐标，必须 (0,0,w,h)，
  传画布坐标会把内容渲染到元素外（预览全透明）。提交后预览已截图确认块状化生效。
- 拖框中实时预览一项转人工实测（模拟脚本 key 序列失误：裸 Z 不撤销、M 连按 toggle 掉工具，后续截图不作数）。
- 验证：SelfTest 142/142（T91a–e）；烘焙 e2e：剪贴板位图文字清晰块状化（对照行锐利）。

## 2026-10-12 凌晨 交互优化 R48（工具目录单一数据源，防「加工具漏帮助」漂移）
- 根因：R47 加马赛克 M 漏了帮助窗条目——工具定义散在工具条数组/帮助窗/引导卡三处，必漏。
- Core.ToolCatalog：工具 → 快捷键/字形/工具条提示/帮助说明 唯一数据源；工具条与帮助窗绘图小节均从目录生成；
  引导卡补 文字 T/马赛克 M 条目。
- SelfTest T92a–e 锁死：7 工具、键位 L/O/A/P/T/M/C 唯一齐全、字形私有区码位互不重复、提示含快捷键、退出键进标题。
  以后加新工具漏任何一处 → T92 当场红。
- 验证：构建 0 错；SelfTest 147/147。发布与门禁顺延至用户 R47 手测结束后（不打断实测）。

## 2026-10-12 凌晨 交互优化 R49（帮助窗离屏渲染取证 + 豆腐块/超高两修）
- 新增取证通道：SelfTest renderhelp <png>——离屏 measure/arrange 帮助窗内容根渲染成位图，
  独立进程不 Show/不注册热键/不进任务栏，用户实测期间也能做 UI 视觉验证（此前 UI 取证必须起实例，会打断实测）。
- 离屏首拍抓到两个会上线的问题：
  ① 帮助窗键位列全豆腐块——R48 把图标字体 PUA 字形塞进了帮助文本，帮助窗用默认字体。
     ToolCatalog 增 Sym 字段（普通 Unicode 符号 ─○↗✎T▦✂），Glyph 仅工具条用；T92f 锁死 Sym 非 PUA。
  ② 内容高 1143 > MaxHeight 1040 → 底部「其他」被滚动藏起。MaxHeight 改为工作区高度-80 自适应。
- 小修：Sym==Key 时键位列只显示键位（去掉「T T」重复）。
- 验证：构建 0 错；SelfTest 148/148；离屏渲染 R49-help3.png 逐行目检（7 工具齐全、无豆腐块、全节可见）。

## 2026-10-12 上午 交互优化 R50（用户实测反馈三连修：马赛克随帧/文字对齐/文字再编辑）
- 用户实测 R47 反馈：马赛克/文字/撤销/裁剪/复制全过；三处待修 + 一条对象化编辑需求（排后续轮）。
- ① 马赛克从「静止贴图」改「区域滤镜」：预览位图按标注矩形提取区域单独马赛克（块对齐框原点与烘焙逐字节一致，T94a 锁），
  Show() 翻帧/播放时 UpdateMosaicBrushes() 逐帧重算 → 内容随帧走；缓存键含区域矩形，区域小整帧便宜。
- ② 文字预览/上屏位置对齐：输入框按 边框1.5+内边距(5,2) 反向偏移，字号 clamp 区间与 TextBlock 统一 (8,400)。
- ③ 文字点击再编辑：RedrawAnns 记录 _textEls，T 工具点已有文字 → 预填输入框（SelectAll），Enter 原位替换可撤销，
  空文/无改动保持原样；撤销栈升级为操作栈（Add/Edit 双操作、时序 LIFO 与列表位置解耦，T93a–g 七连）。
- README/帮助窗补马赛克 M 与文字再编辑说明。
- 验证：构建 0 错；SelfTest 157/157（T93 七连 + T94 两连）；帮助窗离屏渲染逐行目检。

## 2026-10-12 上午 交互优化 R51（e2e 旧 UI 根因修复 + 幽灵修饰键护栏 + 用户新需求 6 条入档）
- 用户看到 e2e 里冒出旧 UI：根因 = 5 个脚本写死过期构建路径 bin\x64\Debug\net8.0-windows（10/2 老构建），
  非旧 UI 残留。全部改指 net8.0-windows10.0.19041.0，删除老构建目录，全仓复查零残留引用。
- 护栏 zoom 项 FAIL 根因 = 幽灵修饰键：keyw 是 PostMessage 裸键，WPF 修饰键读真实键盘状态，
  用户打字（IME 按 Shift）时注入键被记成 Shift+End → 断言假失败。lib-hotkey 加 Wait-FcModifiersUp
  （注入前等物理修饰键松开，只等不干预），zoom/editor-keys 接入；zoom 复跑 PASS。
- TODO 入档用户 6 条新需求：选中工具(V)、圆图标改线性、自绘 RGBA 调色板、笔宽弹层保持、旧选区开关、设置页分组改版；
  另两条成组：双击捕获键开始/结束录制 + 移除录制时长上限（前置=帧磁盘暂存，4K@60fps 纯内存必 OOM）。
- 验证：SelfTest 157/157；护栏 11 项（zoom 修复后全绿，focus-safety 反外挂 SKIP 惯例）。

## 2026-10-12 上午 交互优化 R52（选中工具 V：点选/拖移/删除，对象化编辑第一步）
- 用户队列第 1 条落地：新增「选中」工具（V，GlyphProbe 验证 E8B0 鼠标箭头），点标注=虚线框选中，
  拖动=移动（提交时归一化点整体平移，一步撤销），Delete/Backspace=删除（选中态优先于全局撤销/清范围语义），
  点空白/Esc=取消选中（EscStep 加 ClearSelection 层：取消导出 > 取消选中 > 退工具 > 关窗）。
- 地基：UndoRedo 加 Remove 操作（原位删除/原位放回，与 Add/Edit 混排 LIFO，T95 六连）；
  RedrawAnns 统一 _annEls 元素↔标注映射（命中/拖移共用；文字命中并入，_textEls 退役）；
  包围盒：文字用元素实测，其余由归一化点折算（Line/Path 几何在元素内部，GetLeft 不可靠）。
- 目录防漂移再加一道：BuildToolsBar 断言 ToolOrder[i] 与 ToolCatalog.Tools[i] 键位一致（Debug.Assert）。
- 用户又加 3 条待办入档：设置窗自绘标题栏（可拖动+自绘关闭）、滑杆修缝隙+美化、托盘右键菜单失焦自动关+样式对齐紧凑布局。
- 验证：构建 0 错；SelfTest 165/165（T92 升 8 工具、T95 删除六连、T96 Esc 选中层两连）；帮助窗离屏渲染 V 行目检。

## 2026-10-12 午间 交互优化 R52b（护栏自杀根因：Debug.Assert 断言写错 abort 进程）
- 护栏连续两次在杀探针实例时「拒绝访问」：根因是 R52 加的 BuildToolsBar Debug.Assert 用枚举名 Select
  去匹配提示里的 (V) → 断言必假 → .NET Debug 构建 FailFast 直接 abort → 护栏撞濒死句柄。
- 修复方向不是改断言而是消灭平行数组：ToolOrder 由 ToolCatalog.Tools 键位派生（FromKey 映射，
  未接枚举的目录键启动即抛），顺序唯一数据源=目录，漂移无处可漂。
- 教训入规：Debug.Assert 断言必须与数据源同构（键对键，不是键对枚举名）；能用派生就别用断言。
- 用户又加 3 条入档：帮助窗紧凑化+全盘 UI 布局整理（编辑器为基准）；时间轴悬停竖线改常驻+修首尾对齐；
  时间轴数值文本固定占位防布局挤动。
- 验证：SelfTest 165/165；护栏 11 项全绿（focus-safety SKIP=反作弊环境惯例）；发布 R52 重启用户实例。

## 2026-10-12 午间 交互优化 R53（圆图标改线性环 + 目录字形动态锁）
- 队列第 2 条：圆圈工具图标 EA3B（实心盘）→ EA3F（线性环，R52 探针图已人工验证），
  与画出的描边椭圆同一视觉语言——图标画的是「线」，不再是「面」。
- T10 静态字形清单换 E8B0/EA3F，并加 T10b 动态锁：ToolCatalog 每个 Glyph 必须在 Segoe Fluent Icons
  里真实存在且有宽度（静态清单漏过 R52 的 V，目录本身才是该锁的对象）。
- 用户测试期间不重发布（不打断 V 工具试用），本条与后续攒批发布。
- 验证：构建 0 错；SelfTest 166/166（+T10b）。

## 2026-10-12 午后 交互优化 R54（自绘 RGBA 调色板，系统 ColorDialog 退役）
- 队列第 3 条：K/调色板按钮 → 就地弹层（与宽度弹层同一 Popup 语言：PanelBg/描边/圆角/Fade）。
  DevTools 式布局：SV 区(180×120 白→色相 + 透明→黑 + 十字准星) + 色相竖条 + 透明度竖条(棋盘底) +
  棋盘预览 + HEX(#RGB/#RRGGBB/#RRGGBBAA) + R/G/B/A 直填（聚焦中的框不回写，防打字被吞）。
- 拖动即时生效（所见即所得，比旧对话框"确定才生效"快一步）；关板收口：改过才写最近用色。
- 分层：视图 ColorPickView（纯控件，可离屏渲染取证）/ 宿主 EditorWindow.ColorPick.cs（弹出+收口）/
  数学 Core.Hsv + Core.HexColor（T97 四连：已知值/400 随机往返≤1/解析边界/格式闭环）。
- Esc 分层加弹层优先层：调色板 > 宽度 > 取消选中 > 退工具 > 关窗。
- 离屏取证 SelfTest renderpick：准星/双滑块/棋盘预览/HEX/RGBA 数值逐项目检一致（test-out/R54-pick.png）。
- 用户测试期间不重发布，攒批。验证：构建 0 错；SelfTest 170/170。

## 2026-10-12 午后 交互优化 R55（弹层秒关修复：本次按压的抬起不关板）
- 队列第 4 条：⋯ 线宽弹层按下即开、抬手秒关，永远点不进板。根因：StaysOpen=false 的 Popup
  把打开后第一次全局抬起当"点了外面"。
- 修复 OpenPopSticky：开板先 StaysOpen=true，双钩 PreviewMouseUp（宿主窗 + 弹层子树，handledEventsToo）——
  抬起落在窗内→解除武装（此后点外面即关）；落在弹层里→解除且板常驻可操作（正是用户要的"移进弹层不关"）。
  一次性钩子谁先命中谁摘双钩。调色板弹层同修（同类 bug 一次清）。
- 尝试链记录：Mouse.AddMouseUpHandler(元素版)/InputManager(无此静态)/Application(非 UIElement) 均不可用，
  最终窗+板双钩是 WPF 弹层独立 HWND 输入路由下的正解。
- 验证：构建 0 错；SelfTest 170/170。交互手感项按约定留给用户实测（不盲测鼠标）。

## 2026-10-12 午后 交互优化 R56（设置项：按下选区快捷键后是否显示旧选区）
- 队列第 5 条：新设置 ShowLastRegion（默认开）。关 → CaptureController 传 existing=null：
  无虚线旧框、无滞后入场动画，提示条同步去掉「Enter 确认上次选区」段（不承诺做不到的事）。
- 设置页开关行「显示上次选区」（吸附行上方）；帮助 F1 行补注「虚线旧选区可在设置关闭」。
- T98 三连：默认 true / 老配置缺字段反序列化保持默认（不能默默变 false）/ 开关序列化往返。
- 验证：构建 0 错；SelfTest 173/173。

## 2026-10-12 午后 交互优化 R57（时间轴端线常驻对齐 + HUD 定底宽 + 滑条缝隙）
- 用户实测三连修：
  ① 时间轴首尾竖线：悬停才现 → 常驻（未设裁剪范围时；设了由强调色把手接管不叠加），
     对齐修复：线内缘精确贴 x=0 / x=cw（旧线心偏 3px 没对上首尾）；悬停时加亮 0x76→0xD0 保留反馈。
  ② 进度条右侧 HUD 数值：帧数/时间/裁剪段变宽挤压时间轴列 → MinWidth=210 右对齐
     （MinWidth 非 Width：罕见「⚠丢帧」长警告仍完整可读）。
  ③ 滑条蓝线与圆钮露 4.5px 底色缝：格宽 22 由光晕决定、圆钮只占 13 → 填充线负右边距 -11 延到格中心，
     圆钮坐在线上（0/35/100 三档离屏取证 renderslider 目检无缝）。「之后再美化」按用户顺序留后续。
- 验证：构建 0 错；SelfTest 173/173；test-out/R57-slider.png。

## 2026-10-12 凌晨 交互优化 R58（托盘菜单：失焦即关 + 紧凑化）
- 失焦不关根因：锚点窗 ShowActivated=false → 应用从未激活，ContextMenu 收不到"失焦"，点别处也不消失。
  修复：锚窗 ShowActivated=true + Activate()，Deactivated→menu.IsOpen=false；再右键先收旧菜单；
  Closed 回收用 ReferenceEquals 防旧菜单回调误清新锚窗。附带收益：菜单现在有键盘焦点，Esc 可关。
- 紧凑化（对齐编辑器紧凑布局）：行高 34→27、字号 13→12、勾选列 34→24、MinWidth 190→176、
  内衬 1,5→3,4、分隔线 margin 10,5→8,3。
- 验证：构建 0 错；SelfTest 173/173（T11 模板锁绿）。托盘交互留手感阶段焦点安全实测。

## 2026-10-12 凌晨 交互优化 R59（设置页分组+自绘标题栏+强调色；夜跑焦点安全自动化首航）
- 设置页改版：自绘标题栏（图标+标题+✕，QQ 式，替代系统默认），六分组小标题（快捷键/截图/录制/产物/外观/系统），
  新增「强调色」功能设置（settings.Accent 早有字段但无 UI）：色片点击就地弹 ColorPickView（sticky），
  拖动实时预览全局，关板落盘并 RebuildInPlace 原位重建（主题/强调色即时生效），右键色片恢复跟随系统。
- 拖动根治（用户报"设置界面无法拖动"）：旧窗级兜底 DragMove 被 ScrollContentPresenter 吞命中（内容区拖不动），
  标题栏 DragMove 又与合成输入互相干扰。改 WM_NCHITTEST→HTCAPTION 原生拖窗：按住期间抓取偏移恒 399≈400px
  （刚性跟手实锤），贴边吸附/多屏语义免费；✕ 按钮矩形从 HTCAPTION 让位（点击实测可关）。
  此前"位移超调 1.15×"查明为 OS 指针加速+Win11 拖窗滑移动画对合成输入的伪影，非应用 bug。
- ColorPickView 真 bug 修复（R54 遗留）：SV 游标用 Canvas.SetLeft/Top 定位但父级是 Grid——附加属性无效，
  游标永远钉死面板中央从不跟色。游标移入 Canvas 覆盖层，定点点击实验（面板中心→S0.5/V0.51→#436D83）验证跟色。
- 夜跑基建 lib-focussafe.ps1 首航：前台+矩形双闸多次正确拦截（前台是 GUI 时拒点/越界拒点）；
  新增 Shot activate（AttachThreadInput 破前台锁）/rclick、库 -ExtraRect（弹层合法扩展区）。
- 验证：构建 0 错；SelfTest 173/173；实机取证 night-05~11（标题栏/分组/调色板/提交/恢复全链路截图目检）。

## R60（10-12 凌晨）：连按两下=免按住录制 + 单实例两修
- **双击捕获键开始/结束录制**（用户排队 #7）：纯逻辑状态机 `Core/ToggleRecord.cs`
  （Idle→Hold→PendingTap→Toggle→StopArmed，点按/结束窗各 300ms）。关键设计：**点按松手后引擎不停**，
  PendingTap 期间继续采帧——第二下窗内到达即进 Toggle，零丢帧零间隙；窗到期才走原「点按=截图」分支
  （FinishCapture(tapMs-1) 复用旧 tap 路径，语义零漂移）。代价=点按落地延后一窗（300ms），换来免按住录制。
  录制中单按=装填结束窗（到期无操作）；录制中按住松手=停（符合按住直觉）；REC 徽标常驻且文案写明
  「再连按两下结束」（免按住模式没有"松手"动作，结束方式必须写在脸上）；用户关录制边框时挂"只徽标不画框"
  提示窗（SetBorderVisible）。设置项 DoubleTapRecord 默认开，关闭=旧语义（点按零延迟，实测 150ms 内编辑器已开）。
- **挖出并修复两个单实例真 bug**（e2e 排查时误撞）：
  ① 二次实例退出路径 `ReleaseMutex` 未持有 → ApplicationException → **WER 崩溃弹窗**（用户双击快捷方式
     再开一次就会见到）；改 `_ownsMutex` 守卫 + Dispose。
  ② `--settings/--help` 在已有实例时**静默失效**（OnStartup 直接 Shutdown，无转发）；
     新增 HWND_MESSAGE 消息专用窗（RegisterWindowMessage + FindWindowExW 转发），二次实例现在能把
     "打开设置/帮助"递给主实例。实机验证：转发后设置窗出现、进程数=1、零崩溃。
- 取证基建：Shot 新增 `dbl <vk> [hold] [gap]`（单进程连按两下，跨进程启动间隔会掉出双击窗）；
  Shot 全部 vk 参数支持 0x 十六进制。
- 验证：构建 0 错；SelfTest **186/186**（T99a-n 状态机 13 条）；实机 e2e 全链路：
  双击起录（trace PendingTap→toggle record start）→ REC 徽标截图（"REC 0:02.1 · 再连按两下结束"）→
  双击停（Toggle→StopArmed→stop，编辑器 97 帧，时间轴 2.3s 与实际吻合）→
  录制中单按无操作 → 点按截图 300ms 窗到期落地 → 按住 1.2s 录制回归 → 关开关旧语义。
  取证 night-12~17（徽标/编辑器/设置页新行 E815 图标目检）。
- 插曲：cap-tray-menu.ps1 自带 Stop-Process+重启（取证脚本语义），误跑一次导致实例被换——非应用 bug，
  但顺藤摸出了上面两个真 bug，值。

## R61（10-12 凌晨）：移除录制时长上限——磁盘 spool 帧仓，录制=想录多久录多久
- **磁盘暂存（用户排队 #8 前置）**：新增 `Capture/FrameSpool.cs`。录制帧不再进内存环形缓冲，
  逐帧追加写 `%TEMP%\FrameCapture\spool-*.fcf`（20B 帧头 w/h/timeMs/len + BGRA），内存只留
  帧索引表（每帧 40B：偏移/时间/尺寸/长度）。4K@60fps BGRA≈2GB/s 纯内存必 OOM 的问题根除；
  写完立刻读的场景由 OS 文件缓存兜住。`FrameSpoolList : IReadOnlyList<CapturedFrame>` 惰性回读：
  索引器按需 seek 只读单帧，`Meta(i)` 免读 BGRA 拿时间戳，`Slice(a,b)` 共享文件做导出子集，
  属主 Dispose 删文件（Slice 非属主不删）。启动 `CleanupStale()` 清 24h 前的孤儿 spool（崩溃残留）。
- **磁盘护栏**：每 30 帧查一次卷剩余空间，<1GB 触发 DiskLow → 自动收尾停录
  （已录内容照常进编辑器，绝不静默丢帧）；正常盘不误触发（T100g）。
- **上限移除**：CaptureController 删 `_maxTimer`/MaxDurationMs 停止判定——长按录到松手、
  连按录到再连按，唯一停止条件是用户动作或磁盘护栏。设置页删「最长时长」滑杆行（少一个数值，
  多一个自由）；HelpWindow 文案改「录到松手为止，不设时长上限」；MaxDurationMs 字段保留仅为
  老配置 JSON 兼容（不再参与逻辑）。
- **编辑器惰性化**（186 帧×900×600 全解码≈1.6GB，必须改）：`_bitmaps` 全量表改 LRU 缓存
  `_bmpCache`（容量按帧字节自适应 6–96 张，FIFO 逐出）；时间戳预取进 `double[] _times`；
  GIF 导出子集走 `FrameSpoolList.Slice` 零拷贝；单帧 PNG/视觉暂留走逐帧投影（LazyCropList），
  全程不再有一次性物化全部位图。
- **e2e 顺手挖出并修掉 R60 转发通道的抢焦点真 bug**：WPF `HwndSource` 不支持 HWND_MESSAGE 父
  （实测建成 `WS_VISIBLE|WS_OVERLAPPEDWINDOW` 顶层窗，**应用启动瞬间 GetForegroundWindow 就变成它**
  ——用户每开一次软件焦点就被偷）。改 WinForms `NativeWindow` 直建 message-only 窗（style 无
  WS_VISIBLE、IsWindowVisible=False、永不可激活），转发链路 trace 实锤（sink→open settings）、
  前台保持真实窗口、进程数=1。
- 验证：构建 0 错；SelfTest **197/197**（T100a-k spool 往返/元数据/切片/文件生命周期 11 条）；
  实机 e2e（临时 900×600@20fps 轻载）：双击起录 **9.36s**（旧 6s 上限时代早已到点，现无 auto-stop）
  → 编辑器 **186 帧**秒开（spool 383MB，惰性位图无 OOM）→ 点时间轴 85% 跳 **184/186 帧 9.36s**
  （磁盘惰性回读实锤）→ ✓ 提交 GIF **4.43MB/186 帧**（全量 spool→切片→烘焙→编码）→ 关编辑器
  spool 文件自动删除；点按截图路径零 .fcf 残留。取证 night-18/19/20（编辑器/时间轴/底栏目检）。

## R62（10-12 凌晨）：帮助窗紧凑化 + 标题栏抽共享件（全盘 UI 对齐同一套语言）
- **抽共享件**（用户排队「全盘 UI 布局整理」）：`Fluent.TitleBar(glyph,title)` + `Fluent.InstallCaptionDrag`
  ——36px CardBg 自绘标题栏（图标+标题+关闭，整条原生 HTCAPTION 可拖、关闭钮矩形让位给 WPF 命中），
  设置窗与帮助窗共用同一件。设置窗改用它后删掉内联标题栏构造与内联钩子（-50 行），顺带清死代码
  （`_bar` 字段已无读取、`SmallIcon` 不再被引用）。
- **帮助窗紧凑化**（旧版是 R59 改版前唯一没跟上视觉语言的窗）：
  ① 去 18px 大标题 + 右上角悬浮关闭钮 → 同款自绘标题栏（E9CE 圆形问号图标，与编辑器帮助钮同字形）；
  ② 分组小标题 14px 强调色 → 11px TextDim SemiBold（对齐设置窗 `Group()`）；
  ③ 行距 (10,2)→(2,1)、内衬 padding22 → panel margin(16,6,16,14)、正文 13→12.5、键位列 200→172；
  ④ 整窗 `DragMove` 废除（改标题栏 HTCAPTION，内容区不再误拖窗，与设置窗语义一致）、禁横向滚动。
  高度 1143→1029。至此帮助/设置/托盘三窗共享同一套密度与视觉语言（编辑器为基准，R58 托盘、R59 设置、R62 帮助）。
- 验证：构建 0 错（9 警告全既有）；SelfTest 197/197；离屏 renderhelp（night-21）+ 实机截图（night-22/23）
  目检：标题栏 E9CE/E7A8 图标无豆腐块、✕ 可点关闭、帮助窗正确显示用户真实键位（F7 选区 / F6 捕获）。

## R63 · 自动保存副本（AutoSaveCopy）— 截图存 PNG / 录制存 GIF，opt-in 零副作用

**动机**：要"超过 QQ 截图"——QQ 截完得手动 Ctrl+S 才落盘。加一个可选的"自动保存副本"：捕获后除剪贴板外再往文件夹写一份文件，省去手动保存。默认关闭（opt-in），关闭时零副作用。

**设置项（产物组，2 行）**：
- `自动保存副本` 开关（E74E 软盘图标，默认关）
- `保存位置` 路径行（E838 文件夹图标）：显示当前目录（空=图片\FrameCapture（默认）），点整行或"更改"弹 FolderBrowserDialog 选文件夹

**核心（SaveService.cs 新建）**：
- `ResolveFolder()`：自定义路径（自动建目录）→ 空则回退 `图片\FrameCapture`；永不抛
- `UniqueName(dir, stem, ext)`：stem.ext → stem (2).ext … stem (999).ext → GUID，绝不覆盖
- `Stamp()`：`FrameCapture yyyy-MM-dd HHmmss`
- `MaybeSaveArtifact(seq, gif)`：编辑器提交时按帧数分流——单帧截图存 PNG、多帧录制存 GIF（均为已烘焙标注/裁剪的最终产物）
- `MaybeSavePng(frame)`：选区后直接截图路径存 PNG
- 守卫：`AutoSaveCopy != true` 立即 return；写盘失败只 Trace.Log，绝不影响剪贴板/捕获

**接线**：CaptureController tap-still 路径 + EditorWindow.Commit onDone（UI 线程，seq.Frames 已烘焙）

**验证**：
- 构建 0 错误
- SelfTest 202/202（新增 T101a-e：唯一命名 free/(2)/(3)、目录回退、关闭零落盘）
- 实机 e2e（焦点安全）：F6 点按→编辑器→✓ → 落盘 `FrameCapture 2026-10-12 030546.png` 1800×1200 真截图；按住 F6 1.2s→47 帧→✓ → 落盘 `...031119.gif` 1800×1200 47 帧有效 GIF
- 目检设置窗「产物」组两行渲染（E74E/E838 无豆腐块，"更改"强调色）

**踩坑**：多帧编辑器 ✓ 在时间轴行最右（非工具栏），单帧编辑器 ✓ 在工具栏——两种布局不同，e2e 点击坐标需分别定位。

## R64 · 编辑器记住上次的工具/颜色/粗细（RememberEditorTools）— 打开即是你上次用的标注设置

**动机**：每次打开编辑器都从「浏览/红/中」起步，标注党每次都要重选工具/颜色/粗细，很烦。QQ 编辑器也不记——这是差异化手感点。加一个默认开的开关：关编辑器时记住最后用的工具/颜色/线宽，下次打开直接是它们。

**设置项（新增「编辑器」分组）**：
- `记住上次的工具/颜色/粗细` 开关（EDD7 画笔图标，默认开）；关=每次回到默认（浏览/红/中）

**核心（Core/EditorPrefs.cs 新建，纯决策可测）**：
- `ColorSlot(a,r,g,b,swatches)`：颜色按 ARGB 全等命中预设色板返回下标，否则 -1（自定义槽）；用全等含 alpha，避免半透明自定义色被误判成不透明预设
- `WidthSlot(w,widths)`：线宽命中细/中/粗返回下标，否则 -1
- `ToHex(a,r,g,b)`：颜色 → `#AARRGGBB`（保留 alpha）
- 编辑器 `RestoreEditorPrefs()`（BuildLayout 后）/ `SaveEditorPrefs()`（Closed 事件，✓/✕/Esc 三路径都经此，只写一次不每次点选写盘）；`ApplyColor/ApplyWidth` 复用 EditorPrefs 落槽

**关键坑**：Esc 在绘制态会先把工具退回浏览（EscStep 分层），若用 Esc 关闭会把工具存成浏览——e2e 必须用 ✕ 直接关闭才能验证工具记忆。

**验证**：
- 构建 0 错误
- SelfTest 208/208（新增 T102a-f：颜色命中/自定义/alpha 不误判、宽度命中/自定义、hex 格式）
- 实机 e2e（焦点安全）：开编辑器→按 A(箭头)+点绿+点粗→截图确认三项选中→✕ 关闭→settings.json 落 `LastTool=Arrow LastColor=#FF10893E LastWidth=10`→重开→工具栏截图确认箭头/绿/粗预选一致

## R65 · 钉到屏幕（PinWindow）— 把截图钉在桌面，Snipaste/QQ 的招牌手感补齐

**动机**：QQ/Snipaste 最受喜爱的功能之一是「把截图钉在屏幕上」——贴一张浮在桌面、可拖动缩放的小图，方便对照/临时记忆。我们此前完全没有，是「超过 QQ」的明显缺口。

**功能**：编辑器里点「钉到屏幕」按钮（E91B 图片图标，与「总在最前」图钉明显区分）或按 **Ctrl+P**，把当前帧（含标注+裁剪，WYSIWYG）作为一张无边框、置顶、不进任务栏的小图，钉在原捕获位置。
- 按住拖动移动；滚轮以中心为锚缩放（10%~600%）；双击或 Esc 关闭；右键菜单「复制 / 关闭」
- 可多张并存（Snipaste 式），关掉只靠上述手势
- 1px 深色描边 + 圆角：任何桌面背景上都看得见边界

**核心**：
- `Overlays/PinWindow.cs` 新建：`BgraImage.ToSource` 转位图；`VisualTreeHelper.GetDpi` 物理→DIP 1:1 定位（与 RegionOutlineWindow 同款换算）；`PinZoom.Next` 缩放
- `Core/PinZoom.cs` 新建（纯决策，可测）：滚轮方向 ×1.1 倍率，夹 [0.1,6.0]
- 编辑器 `BakeCurrentFrame()`：单帧同步烘焙（标注+裁剪折算，复用 Commit 同款 CropRemap/Bake 逻辑），返回帧 + 裁剪后物理矩形；`PinCurrentFrame()` 弹 PinWindow
- 单帧/多帧工具栏各加一个 PinBtn；Ctrl+P 快捷键

**验证**：
- 构建 0 错误
- SelfTest 213/213（新增 T103a-e：放大/缩小/上限锁死/下限锁死/delta0 不变）
- 实机 e2e（焦点安全）：Ctrl+P 与工具栏按钮各自弹出贴图窗（rect 800,500 1803x1203，trace `pin frame 1800x1200`）；截图确认贴图正确显示捕获内容于原位；Esc 关闭（前台确为 Pin 窗后发键，window-not-found 确认）
- 滚轮缩放无 Shot 原语，逻辑由 T103 单测覆盖

## R66 · 贴图增强：不透明度 + 右键菜单（复制/不透明度/关闭/关闭全部）

**动机**：R65 贴图刚上线，补齐 Snipaste 贴图的两处高频手感——半透明叠看（对照桌面内容时把贴图调淡）、以及贴图多了之后一键清空。

**功能**：
- **Ctrl+滚轮**调贴图不透明度（20%~100%，中心无关，纯改窗口 Opacity）；纯滚轮仍是缩放
- 右键菜单：复制 / 不透明度 ▸（100%·75%·50%·25% 预设）/ 关闭 (Esc) / **关闭全部贴图 (N)**
- 贴图登记表（静态 List，Loaded 注册 / Closed 注销）支撑「关闭全部」

**核心**：
- `Core/PinOpacity.cs` 新建（纯决策，可测）：滚轮方向 ±0.1 一档，夹 [0.2,1.0]
- PinWindow：MouseWheel 按 Ctrl 分流（Ctrl=不透明度 / 无 Ctrl=缩放）；ShowMenu 加不透明度子菜单 + 关闭全部；`_open` 静态登记表

**验证**：
- 构建 0 错误
- SelfTest 218/218（新增 T104a-e：增/减/上限锁死/下限锁死/delta0 不变）
- 实机 e2e（焦点安全，弹层前台归属 FrameCapture Pin 后操作）：右键菜单渲染正确（复制/不透明度▸/关闭/关闭全部贴图(1)，无豆腐块、计数对）；不透明度子菜单 100/75/50/25 渲染正确；点 50% 生效（贴图仍在、无异常）；「关闭全部贴图」一键清空（window-not-found 确认）
- Ctrl+滚轮无 Shot 原语，逻辑由 T104 单测覆盖

## R67 · 修复：播放定时器读已释放 spool（护栏 fuzz 抓到 R61 潜伏竞态）

**发现**：批次末跑完整护栏，`e2e:fuzz-statemachine` FAIL，`err_delta=1023`——乱键注入期间 error.log 新增异常。栈：`_playTimer.Tick → Show → Bmp → FrameSpoolList.get_Item → FileStream.Seek` 抛 `ObjectDisposedException: Cannot access a closed file`。

**根因**：`_playTimer`（DispatcherTimer，App 级，不随窗口关闭而停）只在 TogglePlay 里停。编辑器关闭时 `ClosedByUser` 释放磁盘 spool（删 spool 文件），但播放表没停 → 关窗后定时器仍 `Show→Bmp` 读已关闭文件。R61 之前帧在内存表，读「已释放」的 List 无害；R61 改磁盘 spool 后同一竞态变成抛异常——潜伏数轮，fuzz 随机命中「播放中关窗」才暴露。

**修复**（EditorWindow.cs）：
- `_playTimer.Tick` 顶部加 `if (_userClosed) return;` 守卫（UI 线程单线程，tick 与 Closed 不交错，置位后 tick 必早退）
- 两条关闭路径（`Closed` 处理器 / `CloseByController`）在 `ClosedByUser?.Invoke()` 释放 spool 之前 `_playTimer.Stop()`

**验证**：
- 构建 0 错误
- 单跑 fuzz：`err_delta=0`，FUZZ PASS（120 步）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1，SKIP=focus-safety 反外挂拦截注入惯例）；SelfTest 218/218

## R68 · 手感实测（模拟用户走查，无改动=通过）

按夜间指令，backlog（R52–R66）完成后进入「模拟用户实测手感」阶段。焦点安全逐步走查并亲眼看每张截图：

- **点按捕获 → 编辑器**：F6 点按秒开、抢焦点正常；单帧工具栏（工具/色板/线宽/撤销/存复钉✓✕/图钉设置帮助）分隔清晰、无错位无裁切；新「钉到屏幕」按钮(E91B 图片)与「总在最前」图钉(E840)区分明确。
- **画标注手感**：按 A 选箭头→画布拖一笔，红色箭头渲染干净（箭头帽成形、抗锯齿平滑），响应即时。
- **长按录制 → 多帧编辑器**：F6 长按 1.5s 录到 70 帧，编辑器按内容自适应尺寸；时间轴行（走带+刻度尺+HUD「1/70 0.08s」+存帧/复帧/**钉图**/GIF/暂留/✕/✓）容纳良好，R65 加的贴图按钮未致拥挤；下方工具行与单帧一致。
- **设置窗标签核对**：「选区快捷键」(scope=F7) /「捕获快捷键」(capture=F6，点按单帧/长按录屏) 与运行期 `Install scope=F7 capture=F6` 一致，无错标。

**结论**：未发现受阻点或手感缺陷，本轮零改动。

**踩坑备忘（供后续轮次）**：F6=捕获（点按截图 / 长按录制），F7=选区（呼出选取窗，配合 AutoCaptureOnSelect 选完即截）——别把 F7 当录制冷键；测录制要长按 F6。并发「鼠标拖拽 + 热键长按」会互相干扰（拖拽被选区窗当成选区），测录制须纯长按、不叠鼠标动作。

## R69 · 识别文字（OCR）——Windows 内置引擎，中英文，离线

**动机**：QQ 招牌功能之一，「超过 QQ」最高杠杆的剩余项。先探针（tools/OcrProbe 新建）确认本机可用：已装 en-US + zh-Hans-CN，用户语言引擎=zh-Hans-CN，渲染已知文字→OCR 完美识别——落地。

**实现**：
- `Core/OcrService.cs` 新建：`RecognizeAsync(CapturedFrame)` → `OcrOutcome(OcrStatus, Text)`。引擎优先 `TryCreateFromUserProfileLanguages`，回退任一 `AvailableRecognizerLanguages`，都无则 `NoEngine`（绝不抛）。帧像素用 `SoftwareBitmap.CreateCopyFromBuffer`（19041 投影无 `CreateCopyFromPixels`；宽高是 int 非 uint）在首个 await 前同步拷入 → 与关窗/释放 spool 无竞态。纯函数 `Classify(text)`（全空白=Empty 否则 Success）供决策+单测。
- 编辑器：`OcrBtn()`（E8C1「A字」字形，GlyphProbe matrix 验证非豆腐，与文本工具/复制明显区分）加到单帧工具行 + 多帧动作行；`Ctrl+O`（Ctrl switch，普通 O 仍是椭圆工具）；`OcrCurrentFrame()` async：识别中→成功写剪贴板+提示「已识别 N 字 · 已复制」/无引擎提示装语言包/空提示「未识别到文字」；`_ocrBusy` 防重入；识别期间关窗则不再提示。
- 新增预览底部轻提示 `_snack`（snackbar，圆角深色胶囊，淡入 1.8s 淡出）——单帧/多帧通用（`_hud` 仅多帧时间轴行有），OCR/复制等即时反馈用。
- HelpWindow 补 Ctrl+O 说明；README 待补。

**验证**：
- 构建 0 错误；SelfTest **222/222**（T105a-d Classify 纯决策）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1）
- 实机 e2e：记事本显示「ZEBRA FRAMECAPTURE OCR / HELLO 4242 WORLD / ABCDEFG 98765」→ 设区域 → F6 截图 → Ctrl+O → 剪贴板=`ZEB RA FRAMECAPTURE OCR HELLO 4242 WORLD ABCDEFG 98765`（关键 token 全中，仅 ZEBRA 多一空格，OCR 常见）；trace `Control+O → ocr ok 54 chars`；截图亲眼看 snackbar「已识别 54 字 · 已复制」渲染正确
- 踩坑：`ctrlw` 发组合键时 Ctrl 未保持到目标键（trace 见 O 无修饰）；发组合键用 `hkm <vk> 1 0 0 0 <ms>`（按住修饰）

## R70 · 选区放大镜（loupe）——像素级精确选区，QQ/Snipaste 招牌

**动机**：精确选区是截图工具核心手感，QQ/Snipaste 都有光标旁放大镜。选区窗是实时挖洞遮罩（无快照），放大镜需真实像素 → 先取快照。

**实现**：
- 快照时机：选区窗**构造时**（本窗未 Show、遮罩未渲染）`LiveCapture.Grab(_monitorPhys)` 抓干净桌面（旧选区虚线窗已 WDA 排除，不入镜）→ `BgraImage.ToSource` 冻结 BitmapSource。抓失败则自动降级不显示放大镜。
- 放大镜 UI（加到 `_canvas` 最上层，IsHitTestVisible=false）：`Image` 显示 `CroppedBitmap`（光标周围 15×15 源像素 → 150 DIP，NearestNeighbor 像素化）+ 中心像素标记（蓝方框，随夹取动态定位到光标正下方那个像素）+ 底部读数「X, Y  W×H」（坐标 + 当前吸附/拖框尺寸）。
- `OnMove` 每动更新：拖框态传拖框矩形，悬停态传吸附矩形/控件矩形；`MagnifierMath.Place` 贴光标右下、越界翻边。
- `Core/MagnifierMath.cs` 新建：`Crop`（源矩形夹进快照，快照小于 span 退化整幅）+ `Place`（翻边摆放）纯函数。
- 设置项 `PickerMagnifier`（默认开）：SettingsWindow「截图」段 ToggleRow（E721 放大镜字形，GlyphProbe matrix 验证非豆腐）；HelpWindow/README 补说明。

**验证**：
- 构建 0 错误；SelfTest **229/229**（T106a-g：Crop 中心/夹左/夹右/小快照 + Place 默认/翻X/翻Y）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1）
- 实机 e2e：F7 开选区 → SetCursorPos 移光标（pwsh DPI-unaware，传 P/1.5 命中物理 P，读数印证坐标精确）→ 截图亲眼看放大镜：圆角蓝框 + 中心标记 + 读数「363,524 3840×2088」，移到聊天侧栏「12天」处放大出清晰像素块「12」；设置窗「选区放大镜」开关渲染正确（图标+默认开）
- 踩坑：pwsh DPI-unaware，SetCursorPos 坐标被系统 ×1.5 虚拟化；`Border.BorderThickness` 是 Thickness 非 double

## R71 · 放大镜取色器——光标下像素颜色 + 按 C 复制 HEX

**动机**：R70 放大镜已有坐标/尺寸读数；补上「光标下像素颜色」即成取色器，设计师/开发者刚需，广泛受众，纯增量（快照已在手，直接读中心像素）。

**实现**：
- 构造时保留快照原始 BGRA（`_snapBgra`/`_snapW`，`LiveCapture.Grab` 的帧本就有，之前只转 BitmapSource 丢了）。
- 放大镜底部加取色行：色块（14×14 圆角，填充光标下像素色）+ `#RRGGBB`（Consolas）。`UpdateLoupe` 每动更新：`MagnifierMath.PixelRgb(_snapBgra,_snapW,sx,sy)` → `Hex` → 设色块背景 + HEX 文本。
- 选区窗 KeyDown 加 `C` 键：复制 `_loupeHexText` 到剪贴板，HEX 文本闪「已复制 #RRGGBB」（下次移动复原）。C 不与 Esc/Enter/Tab 冲突。
- `Core/MagnifierMath.cs` 加 `PixelRgb`（BGRA→RGB，越界 0,0,0）+ `Hex`（RGB→#RRGGBB 大写，分量夹 0–255）纯函数。
- HelpWindow/README/设置开关提示补「颜色 + 按 C 复制」。

**验证**：
- 构建 0 错误；SelfTest **235/235**（T107a-f：PixelRgb 像素/越界/负坐标 + Hex 大写/夹取）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1）
- 实机 e2e：F7 → 移光标 → 放大镜显示色块 + 「#272729」；采样放大镜图像中心像素 = #272729（=显示 HEX，内部一致，证明读数即光标下真色）；按 C → 剪贴板 = `#272729`；截图亲眼看色块填充正确 + HEX 清晰
- 注：放大镜显示快照瞬间的真色，聊天等动态区颜色会随快照时刻变化（正确行为，非 bug）

## R72 · OCR 结果预览窗（可选）——识别文本可看/可编辑/部分复制

**动机**：R69 OCR 只写剪贴板 + 轻提示，用户看不到识别成什么、无法只取一部分。QQ 有 OCR 面板。补一个可选结果窗（默认关，保留快速路径）。

**实现**：
- `Overlays/OcrResultWindow.cs` 新建：Fluent 标题栏（E8C1 + 「识别文字」+ 关闭）+ 深色可编辑 TextBox（自动换行/竖滚、载入全选便于 Ctrl+C）+ 底部「N 字」计数 + 「复制全部」强调钮（点击复制并变「已复制」）。非模态、Topmost、Esc/关闭淡出。复用 Fluent.TitleBar/InstallCaptionDrag/CloseWithFade/OnAccentColor。
- `OcrService.Normalize` 纯函数：统一换行、去行尾空白、连续空行压成一个、整体去首尾（保留行首缩进）。剪贴板与结果窗都用它。
- 设置项 `OcrShowResult`（默认关）：编辑器「编辑器」组 ToggleRow（E8C1）。开→OCR 成功弹结果窗；关→原轻提示路径。
- HelpWindow/README 补说明。

**验证**：
- 构建 0 错误；SelfTest **241/241**（T108a-f：Normalize 压空行/去行尾/空/单行/全空/CRLF）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1）
- 实机 e2e：记事本已知文字 → F6 截图 → Ctrl+O → 结果窗弹出（750×690 物理=500×460 DIP）；截图亲眼看标题栏/深色文本框（已全选高亮）/「122 字」/「复制全部」蓝钮；点「复制全部」→ 剪贴板 122 字 = 显示字数 = 显示内容；Esc 关窗、编辑器仍在（非模态）
- 注：OCR 引擎本身会并入记事本菜单栏、合并行、个别字误差（AIpha/CharIie），窗口忠实显示/复制其输出，非窗口 bug

## R73 · 序号标注工具（N）——点一下放递增编号圆标，步骤/教程标注刚需

**动机**：QQ/Snipaste 招牌标注「序号」缺位。做「第 1 步、第 2 步」教程标注时，用文字手打编号 + 手画圆太笨。加一个点击即放递增编号圆标的工具。

**实现**：
- `Core/NumberBadge.cs` 新建：`Next(existingTexts)` = 现有编号最大值 + 1（无则 1，删除/撤销后自动回退、绝不重号）；`Diameter(fontPx)` = 字号×2。纯函数供单测。
- `AnnKind.Number` + `Annotation.Render` GDI 烘焙：实心圆（当前色）+ 居中白字（YaHei Bold，随线宽挡位字号）。
- 编辑器：`Tool.Number` + FromKey("N") + Key.N 分发；`Canvas_Down` 点击即放（同文字工具，无拖拽），编号取 `NumberBadge.Next`；`RedrawAnns` WPF 预览（Grid：Ellipse 实心 + TextBlock 居中白字，与烘焙同径同字）；`AnnBDisp` 序号按元素实测尺寸命中（同文字）。
- `ToolCatalog` 加条目（Key=N / Sym=① 非 PUA / Glyph=E71D 编号列表图标 GlyphProbe 定稿 / Tip / Help）；工具条、帮助窗自动出现（单一数据源）。T92 同步 8→9 工具、键位串加 N。
- README 工具行补序号说明。

**验证**：
- 构建 0 错误；SelfTest **247/247**（T92 更新 + T109a-f：无→1 / 1,2,3→4 / 删中间不补洞 / 删 1 后仍 4 / 非数字忽略 / 直径=字号×2）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1）
- 实机 e2e（截图亲眼看）：F6 截图进编辑器 → N → 点三处 → 红实心圆白字 **1/2/3** 递增渲染正确、工具条序号图标（E71D）出现且高亮；选工具删 #3 → 再点新处得 **#3**（max[1,2]+1 回退正确）；Ctrl+P 钉图 → 钉的烘焙帧里 **1/2/3 已烙进像素**（GDI 路径与 WPF 预览一致）

---

## R74 · 贴图磁边（拖动松手吸附屏幕边缘）— 2026-10-12

**动机**：Snipaste 招牌手感之一是把贴图整齐停靠到屏幕四边/四角。现贴图拖动松手停在原地，多张贴图想对齐边缘只能肉眼挪。加「磁边」：松手时若某条边贴近所在屏幕工作区边缘则自动吸附贴合。

**实现**：
- `Core/PinSnap.cs` 新建纯函数：`Threshold(w,h)` = 短边 6% 夹 12–40（小图更灵敏、大图别误吸）；`Snap(left,top,w,h, cl,ct,cr,cb, th)` 左右边吸到容器左右、上下边吸到上下（左右互斥、上下互斥，先左后右、先上后下）。坐标单位由调用方统一（DIP），供单测。
- `AppSettings.PinSnapEdges`（默认开）+ 设置窗新增「贴图」组 + 开关「拖动贴图自动磁边」。
- `PinWindow`：拖动处理改为 DragMove 返回（松手）后调 `SnapToEdge`——用贴图中心定位所在屏幕（多屏），工作区物理像素 ÷DpiScale 转 DIP 与 Left/Top 同单位，调 PinSnap.Snap；命中则 `SlideTo` 用 Motion.StateMs + CircleEase.EaseOut 平滑滑到边缘（先落本地值=目标再动画，结束回落不留动画锁；下次拖动前先 BeginAnimation(null) 结算在飞的滑动）。

**验证**：
- 构建 0 错误；SelfTest **253/253**（T110a-f：吸左/吸右/吸上/吸下/远离不动/阈值=短边6%夹12–40）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1）
- 实机 e2e（截图亲眼看）：Ctrl+P 钉图 → 多次小拖把贴图「走」向底边 → 松手后底边精确落到 **2088 = 工作区底边（1392×1.5）**，截图看贴图齐平任务栏顶；关闭开关则不吸（默认开）

---

## R75 · 贴图 OCR 入口（右键识别贴图文字）— 2026-10-12

**动机**：贴图常钉的是「一段文字」（报错、命令、引用）。想复制/检索它得回编辑器或重截再 OCR。给贴图右键菜单加「识别文字」，就地 OCR。QQ 截图无 OCR，这是差异化能力。

**实现**：
- `OcrService.Feedback(status, len)` 纯函数：Success=「已识别 N 字 · 已复制」/ NoEngine=装语言包指引 / Empty=未识别到文字。编辑器轻提示与贴图浮层共用，措辞一致（编辑器三处 ShowSnack 改调它）。
- `PinWindow` 内容改为 Grid（描边 Border 包图 + 居中浮层 Border/TextBlock，默认隐藏）；新增 `Flash(msg)`（淡入→停留 1.6s→淡出，重复调用重置计时）与 `HideSnack`。
- 右键菜单加「识别文字」→ `OcrFrame`：`OcrService.RecognizeAsync(_frame)`（贴图本就持有帧）→ Success 写剪贴板，开「识别后显示结果窗」则弹 `OcrResultWindow`（贴图无 snackbar，结果窗是主反馈），否则浮层提示；NoEngine/Empty 浮层给原因；异常浮层「识别失败」。
- HelpWindow 贴图行右键菜单补「识别文字」；README 贴图行补 OCR 说明。

**验证**：
- 构建 0 错误；SelfTest **256/256**（T111a-c：成功带字数 / 无引擎含「语言包」/ 空=未识别）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1）
- 实机 e2e（截图亲眼看）：记事本写中英混合文本 → 区域覆盖 → F6 截图 → Ctrl+P 钉图 → 焦点安全右键开菜单（截图确认「识别文字」在第 2 项）→ 点它 → 结果窗弹出（标题「识别文字」+ 文本框中英正确 + 「65 字」+ 蓝色「复制全部」）、剪贴板含识别文本；关窗/还原区域/复位设置

---

## R76 · 选中已有标注可改颜色/粗细 — 2026-10-12

**动机**：画完一条箭头/文字发现颜色或粗细不对，之前只能删了重画（丢位置）。让「选中已有标注 → 点色板/线宽」就地改这条标注的样式，是通用工作流修复（QQ/Snipaste 都有）。

**实现**：
- `Annotation.WithStyle(color?, thickness?)` 纯函数：返回改了颜色/粗细的副本（Kind/几何/文字不变，传 null 保持原值）。
- `EditorWindow.RestyleSelected(wpfColor?, thickness?)`：选中项存在时，用 WithStyle 造副本 + `UndoRedo.Replace`（R50 的 Edit op）原位替换 → 天然可撤销；同时该项已是当前默认（后续新标注沿用）。加「无实际变化不产生撤销项」守卫（重复点同色/滑杆没动不刷撤销栈）。
- 挂钩：色板点击、最近用色、取色器关闭（改过才提交）、3 挡线宽按钮即时改；自定义线宽滑杆只在弹层关闭时一次性应用（避免拖动刷屏撤销）。文字/序号的粗细即字号，故选中文字改线宽=改字号。
- HelpWindow 加「V 选中后改色/宽」行；README 选中 V 行补改样式说明。

**验证**：
- 构建 0 错误；SelfTest **260/260**（T112a-d：改色保留粗细/几何、改粗细保留色、两者都改、点集/文字保留且原对象不变）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1）
- 实机 e2e（截图+像素采样亲眼看）：画红箭头 → V 选中 → 点蓝色板 → 箭头中点像素 RGB 0,120,212（蓝）+ 虚线选中框；Ctrl+Z → 像素 232,17,35（红）还原。再画粗箭头 → 选中 → 点「小」线宽 → 垂直厚度 7-8px→2px；Ctrl+Z → 回 7-8px。箭头全程存活无删除

## 2026-10-12 清晨 交互优化 R77（贴图对齐到同屏其他贴图：磁边从「只贴屏幕边」升级为「屏幕边 + 其他贴图边/中线」）

**动机**：R74 磁边只贴屏幕四边；用户排布多张贴图（对比两张图、整齐停靠）时，贴图之间无法互相对齐。Snipaste 拖动贴图会吸附到其它贴图的边/中线，这是「人人爱用」的排布手感。

**实现**：
- `PinSnap.SnapToLines(left,top,w,h, xEdge, xAny, yEdge, yAny, threshold)` 纯函数：本贴图 left/right 可贴 xEdge（屏幕左右边）与 xAny（其他贴图线），**center 只贴 xAny**（贴图中心贴屏幕边会把半张图推出屏外，故屏幕边不接受中心锚点）；y 轴同理。每轴取全局最小偏移，超阈值则该轴不动。坐标统一为同一 DPI 空间 DIP。
- `PinWindow.SnapToEdge` 升级：用贴图中心定位所在屏幕（DeviceName），收集**同屏**其他贴图的 left/center/right、top/middle/bottom 作候选线（跨屏 DPI 不同，仅同屏参与避免 DIP 混算），连同屏幕四边一起交给 SnapToLines。命中时 trace 记 `pin-snap (旧)→(新) otherPins=N`。
- 设置项「拖动贴图自动磁边」文案 + HelpWindow 贴图行 + README 贴图行同步说明「也会对齐到同屏其他贴图的边/中线」。

**验证**：
- 构建 0 错误；SelfTest **268/268**（T113a-h：left 贴屏幕边、right 贴屏幕边、center 贴贴图线、**center 恰在屏幕边线上也不吸**、超阈值不动、middle 贴贴图线、无参考线原样、x/y 各轴独立取最近）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1，floor 268）
- 实机 e2e（EnumWindows 读两张贴图真实矩形 + trace + 截图亲眼看）：同位置钉两张贴图 →
  - 对照：把上面那张右拖 +261px（>阈值 24px）松手 → 停在 1761，**未吸附**（另一张留 1500）
  - 吸附：把它左拖回落到 1494（距 1500 仅 6px < 阈值）松手 → trace `pin-snap (996,667)→(1000,667) otherPins=1`，两张贴图 Left 都=1500 精确对齐
  - 再把它竖直下拖到下方（左边保持 1500）→ 截图见两张贴图上下并排、左边缘严格对齐
- 排障记录：自写 EnumWindows 抓不到贴图标题——根因 DllImport 默认 Ansi 字符集把 UTF-16 标题按 ANSI 读成 "F"，加 `CharSet=Unicode` 修复（Shot 一直用 Unicode 故正常）

## 2026-10-12 清晨 交互优化 R78（托盘「编辑上次截图」：静态截图浮窗消失后仍可找回编辑器）

**动机（手感实测）**：静态截图走「复制 + 确认浮窗」，浮窗 3.2s 自动消失；若用户当时没点「点击继续标注」，图就只剩剪贴板，想标注只能重截一遍。这是高频摩擦（截完先回了个消息/发个呆，回来想加个箭头就没了）。

**实现**：
- `Capture/LastStill` 纯状态持有类：只留最近一帧（Set 覆盖旧的，旧帧随之可 GC），TryGet/Clear/Empty。控制器持一个，静态截图路径 Set，Dispose 时 Clear（一帧全屏可达数十 MB）。
- `CaptureController`：`HasLastStill`（决定托盘项是否出现）+ `EditLastStill()`（复用 OpenEditorForFrame，Idle 态才开，编辑器关闭不释放该帧 → 可反复找回）。
- 托盘菜单：`if (Controller.HasLastStill) Item("编辑上次截图", …)`，放在「立即捕获」下方。
- 顺带加 CLI 动词 `FrameCapture.exe --edit-last`（与托盘同一 EditLastStill 入口，二次实例经消息专用窗转发）：既便于脚本调用，也让该功能可被自动化验证（Win11 托盘图标在沙箱内 UIA 不可达）。

**验证**：
- 构建 0 错误；SelfTest **272/272**（T114a-d：初始空→TryGet false、Set 后帧/区域/设备一致、再次 Set 覆盖为最新、Clear 回空）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=7，floor 272；SKIP 为前台非编辑器时注入探针安全跳过，非回归）
- 实机 e2e（截图亲眼看）：F6→拖框 700x400→`still saved`+toast→等 4s toast 自动消失（编辑器未开）→`--edit-last`→trace `sink→edit last`→`open editor frames=1`→编辑器打开且画面正是刚截内容（非空白）

## 2026-10-12 清晨 交互优化 R79（贴图旋转 90°：横拍/竖拍图就地转正，Snipaste 有 QQ 无）

**动机**：贴一张横过来的截图（手机拍的、旋转过的页面）想转正只能回编辑器或外部工具。Snipaste 贴图可旋转，QQ 无——差异化能力，就地一键转正。

**实现**：
- `Core/PinRotate` 纯函数：`Rotate90Cw` 无损像素重排（old(x,y)→new(H-1-y,x)，新帧宽=旧高）；`Rotate(f,steps)` 取模 0~3，steps=0 返回同一实例，转 4 次回原图。
- `PinWindow.Rotate()`：换 `_frame`（改非 readonly）→ 重建 `_img.Source` → 按新帧重算 `_baseW/_baseH` → 以中心为锚 `ApplyScale` 后回中（旋转不漂移）→ trace `pin-rotate → WxH`。
- 入口：右键菜单「旋转 90° (R)」+ 贴图聚焦时按 R 键。HelpWindow/README 贴图行同步。

**验证**：
- 构建 0 错误；SelfTest **276/276**（T115a-d：横 2x1→竖 1x2 A上B下、2x2 上行→右列、转 4 次回原图、steps=0 同实例且 5≡1/-1≡3）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=7，floor 276）
- 实机 e2e（EnumWindows 读真实矩形 + trace + 截图亲眼看）：贴图 703x403 @1500,1000 → 发 R → 403x703 @1650,850，中心 (1851.5,1201.5) 保持不变（宽高互换 + 中心不漂移），trace `pin-rotate → 400x700`；截图见贴图内容顺时针转 90°（文字变竖列）

## 2026-10-12 清晨 交互优化 R80（贴图「另存为」：贴图有复制却无保存，明显缺口）

**动机**：贴图右键有「复制」却没有「另存为」——想把钉在屏上的图（尤其旋转/调过的）存成文件只能回编辑器或外部工具。补上标准能力。

**实现**：
- `PinWindow.SaveAs()`：`Microsoft.Win32.SaveFileDialog`（默认目录「图片\FrameCapture」、默认名 `pin-<时间戳>.png`、PNG 筛选）→ `BgraImage.SavePng(_frame, path)`（复用编辑器同款保存路径）→ 贴图内 Flash「已保存」+ trace `pin-save`。存的是 `_frame`，故旋转后的状态一并存下。
- 入口：右键菜单「另存为…」（复制下方）+ 贴图聚焦时 Ctrl+S（与编辑器一致）。HelpWindow/README 同步。

**顺带查清一个测试假象**：早前「关编辑器贴图就消失」并非产品 bug——`keyw`/`hkm` 是全局 SendInput 落到前台窗口，Ctrl+P 后贴图 Activate 抢前台，我「发给编辑器的 Esc」实际打在前台贴图上→贴图关自己。真实用户关编辑器时编辑器才聚焦，Esc 不会漏给未聚焦贴图。贴图生命周期正确（R77 亦证）。

**验证**：
- 构建 0 错误；SelfTest 保持 276（另存为无可抽纯函数，复用已测 SavePng）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1，floor 276）
- 实机 e2e（截图亲眼看 + 读盘）：贴图聚焦→Ctrl+S→前台「另存为」对话框→回车存默认名→`图片\FrameCapture\pin-20261012-064231.png` 落盘 39KB、尺寸 700x400（正是贴图内容）、trace `pin-save`

## 2026-10-12 清晨 交互优化 R81（编辑器图标语义修正：招牌「钉到屏幕」不再画成通用图片）

**动机**（编辑器实机手感走查、放大工具条发现）：编辑器「钉到屏幕」(Ctrl+P，Snipaste/QQ 招牌动作) 用的是 `E91B`——渲染成通用「图片/山+太阳」图标，用户会以为是"插入/查看图片"，完全看不出是"钉到屏幕"。而真正的图钉字形给了右侧「总在最前」。查代码注释发现是 R65 当初为"与总在最前图钉区分"故意用了图片图标——结果招牌功能反而最不可发现。

**实现**（GlyphProbe matrix 肉眼选字形，均验证非豆腐块）：
- 「钉到屏幕」`E91B`→`E840`（实心图钉，招牌动作配招牌图钉）。
- 「总在最前」`E840/E718`（图钉开关）→`E898`（向上箭头带顶栏 = 置顶语义）；开关态本就有 `HighlightBtn` 高亮底色区分，不再靠图钉实心/空心，两按钮彻底不混淆且各自名副其实。
- `PinBtn()` 单点定义，静态/录制两条工具条同时生效。

**验证**：
- 构建 0 错误；SelfTest 保持 276（纯图标语义修正，无可抽纯函数；无测试锁这些字形）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=7，floor 276）
- 实机 e2e（截图放大工具条亲眼看）：导出组「钉到屏幕」现为清晰图钉 📌、右侧「总在最前」现为置顶箭头 ↑̄，二者语义清晰互不混淆

## 2026-10-12 清晨 上手优化 R82（首次启动欢迎卡：新用户不再对着托盘图标发呆）

**动机**（「人人都爱用」头号摩擦）：新用户装完 FrameCapture，只看到托盘多一个图标、任务栏没痕迹，完全不知道按什么键开始——上手第一步就卡住。QQ/Snipaste 首启都会点破热键，我们此前只有托盘气泡一闪而过。做一张首启欢迎卡：直接列出两个热键 + 关键手感 + 一个「查看帮助」入口，看过一次永不再打扰。

**实现**（`Overlays/WelcomeToast.cs` 新建，复用 CaptureToast 浮窗骨架）：
- 落点主屏工作区右下角（贴托盘图标，把「图标在这」和「怎么开始」空间关联）；`SystemParameters.WorkArea` 已是 DIP 且排除任务栏，直接作 Left/Top。
- 内容：标题「FrameCapture 已就绪」+ 关闭 X；两行热键「截图 / 选区」（标签暗色 + 键位强调色加粗，一眼看清按什么）；一行手感提示（长按截图键=录屏 · 双击贴图关闭 · 右键贴图/托盘查看更多）；强调色「查看帮助与全部快捷键」按钮（自绘圆角模板，点击开帮助窗并收卡）。
- 不打扰：`ShowActivated=false` 不抢焦点、`WDA_EXCLUDEFROMCAPTURE` 不入任何截图、12s 自动消失（悬停暂停）、Esc/点 X 关。`FirstRunHintShown`（AppSettings 新增，默认 false）首启后置 true，`App.OnStartup` 用 `ApplicationIdle` 优先级弹卡——**只弹一次**。

**踩坑查清（重要，非产品 bug）**：欢迎卡用 `CopyFromScreen`（Shot 工具）截图恒为纯黑，一度以为整窗隐形。逐层排查——`IsWindowVisible=true`、非 `WS_EX_LAYERED`、非 cloaked、非 WDA 排除、`WindowFromPoint` 打在卡心返回本卡 HWND、`PrintWindow(PW_RENDERFULLCONTENT)` 渲染完美——最终对照发现**已上线的 CaptureToast 用同款骨架在 CopyFromScreen 下同样全黑**：这是 GDI BitBlt 抓不到「无边框+Topmost+透明背景」WPF 窗口的 DWM 重定向怪癖，用户实际可见（PrintWindow + WindowFromPoint 双证）。视觉审查改用 `PrintWindow` 直取窗口内容。

**验证**：
- 构建 0 错误；SelfTest 保持 276（浮窗无可抽纯函数）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1，floor 276）
- 实机 e2e（PrintWindow 亲眼看 + EnumWindows/WindowFromPoint 判定）：首启卡片渲染正确（标题/两热键强调色/手感行/蓝按钮）；重启不再弹（count 1→0）；点关闭 X→卡片消失；点「查看帮助」→帮助窗弹出且卡片收起（WindowFromPoint 守卫确保点击落到本卡）

## 2026-10-12 上午 工作流可选项 R83（「截图后」三态：弹提示 / 进编辑器 / 只复制）

**动机**（兼容所有受众）：截图完成后干什么，是截图工具最根本的工作流分叉——QQ 每次强制弹标注编辑器（想批量复制的人嫌烦），Snipaste 默认只进剪贴板（想马上标注的人多一步）。我们此前固定「复制→弹浮窗→点一下进编辑器」，对三类人都不够顺手。做成显式三选一，各取所需。

**实现**：
- `AppSettings.AfterCapture`（"toast" 默认 / "editor" / "copy"，Normalize 非法回落 toast）；`Core/AfterCapture.cs` 纯决策 `Resolve(string)→{Toast,Editor,Copy}`（脏值/大小写/空一律 Toast）。
- `CaptureController` 静态图捕获完成处按三态分支：editor=复制后直接 `OpenEditorForFrame`（QQ 式，省一步）；copy=只复制不弹窗不开编辑器（范围轮廓闪一下作反馈，最快）；toast=现状浮窗。三态都仍写剪贴板 + `_lastStill.Set`（托盘「编辑上次」始终可用）。
- 设置页「截图」组新增「截图后」行：三枚**文字芯片**（弹提示/进编辑器/只复制，语义比图标清楚），选中态强调色底，点击就地更新不整窗重建；键盘可达（Tab+空格/Enter）。HelpWindow「捕获」区补一行说明。

**验证**：
- 构建 0 错误；SelfTest 276→**280**（T116a-d：三态映射 + null/空/大小写/脏值回落）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1，floor 280）
- 实机 e2e（EnumWindows 判定，注意 toast 标题实为「FrameCapture Toast」）：editor 模式捕获→编辑器开、无浮窗；copy 模式→无编辑器无浮窗但剪贴板有图；toast 模式→浮窗现、编辑器不开
- 设置窗 PrintWindow 亲眼看：「截图后」三芯片渲染正确、选中态高亮、与主题行同风格

## 2026-10-12 上午 贴图增强 R84（「更新内容」原地重抓源区域，Snipaste F3 式）

**动机**（补 Snipaste 招牌、QQ 缺位）：贴图常用来「盯梢」会变的画面——进度条、下载百分比、直播/会议画面、跑分数字。此前贴图是静态快照，源画面变了只能重新截图再钉，很笨。Snipaste 的 F3「重新抓取」正是解决这个：原地按贴图当初的来源区域重抓一次，内容即时更新，位置/缩放/旋转都保持。

**实现**（`PinWindow`）：
- 构造时记住源区域 `_srcRegion`（物理 RectI，即 physPos）；`Rotate()` 累计 `_rotSteps`。
- `Refresh()`：先 `Hide()` 让置顶贴图离场（否则会把贴图自己截进新帧，反馈循环），200ms 后 `LiveCapture.Grab(_srcRegion)`，按 `_rotSteps` 重放旋转，换 `_img.Source`，中心锚定重算尺寸（不漂移），Flash「已更新」；抓失败 Flash「更新失败」并保留旧图。
- 入口：贴图聚焦时 **F3** 键 + 右键菜单「更新内容 (F3)」。类文档注释、HelpWindow 贴图条目同步。

**验证**：
- 构建 0 错误；完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0）
- 实机 e2e（PrintWindow 前后像素对比 + trace 取证）：钉住纯桌面区→开设置窗盖住该区→聚焦贴图→F3→贴图内容从桌面变为设置 UI（diff 20.5%，trace `pin-refresh 500x400`，「已更新」浮层现）；另一场景关设置→F3→贴图从设置 UI 变回桌面（diff 15.7%）
- 关键坑：Ctrl+P 不关编辑器，编辑器仍持应用级键盘焦点，PostMessage 的 F3 会被 WPF 路由到编辑器（trace 见 `editor key=F3` 无 `pin-refresh`）；`activate` 抢焦点到贴图后 F3 正常。真实用法里贴图创建即 Activate、或点一下即聚焦，F3 天然可用；右键菜单项不依赖焦点亦可触发

## 2026-10-12 上午 编辑器标注 R85（荧光笔 / 马克笔高亮，QQ 招牌补齐）

**动机**（补 QQ 招牌、我们缺位）：QQ 截图标注里「荧光笔」是高频工具——在截图上划一道半透明高亮，圈重点又不遮住底下的文字。我们此前标注有直线/圆/箭头/手绘/文字/马赛克/序号，唯独缺高亮：想强调只能画不透明框或手绘，都盖字。补上这个 QQ 有我们没有的工具。

**实现**（复用 Freehand 的 Polyline 预览 + DrawCurve 烘焙双路径，只改三个参数，WYSIWYG 风险最低）：
- `AnnKind.Highlight` + `Annotation.Render` 新分支：半透明 + **平头帽**（斜头马克笔触感）+ 曲线同手绘。alpha 作为「Highlight 这一类型」的属性（`Annotation.HighlightAlpha=90`）统一施加，**标注颜色仍存不透明色**——改色/调色板/最近用色都照常，预览与烘焙两处同取此常量保证所见即所得。
- 编辑器 `Tool.Highlight`：`Canvas_Down` 建半透明加粗（线宽挡位 ×2.5）Polyline 预览（平头帽），`Canvas_Move` 与 Freehand 共用加点，`RedrawAnns` 提交元素同 alpha/平头。`FromKey("H")`、`Key.H` 绑定、`ToolCatalog` 加 "H" 条目（图标 **E7E6** 马克笔，GlyphProbe 大尺寸人工定稿；帮助符号用非 PUA 的 ▨）。裁剪重映射 `CropRemap` 本就按点集通用处理，无需改。

**验证**：
- 构建 0 错误；SelfTest 280→**282**（T117a 荧光笔红 over 白→R 高 G/B 中=透出白=半透明；T117b 对照不透明手绘 G/B≈0）；T92 工具目录计数 9→10、键位串加 H
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0）
- 实机 e2e（PrintWindow 亲眼看 + 像素采样）：工具条第 6 位出现马克笔图标；按 H→trace `editor key=None+H`、工具条图标蓝底选中；画布拖一笔→半透明红横条、**两端平头**；笔画像素 (96,20,27) vs 背景 (21,21,23) 证实背景透出（半透明）；帮助窗「绘图」小节荧光笔行 ▨ 符号正常渲染（非豆腐块）


## 2026-10-12 上午 截图提示音 R86（QQ 招牌听觉反馈，做成开关兼容所有受众）

**动机**（补 QQ 招牌、我们缺位）：QQ 截图完成会「咔嚓」一声，给用户「截好了」的即时听觉确认——尤其全屏/无编辑器（只复制）模式下，画面没变化，没声音就不知道有没有截成。但我们不能像 QQ 那样默认响：后台工具每次截图都出声会吵到所有人。所以做成**默认关的开关 + 「试听」按钮**，想听的人开、嫌吵的人关，先听再决定。

**实现**（不引外部音频文件，运行时合成，纯函数可单测）：
- `Core/CaptureSound.cs`：`Samples()` 合成一段柔和短促的「嘀」（44.1kHz/16bit 单声道，42ms，1000Hz 正弦，指数衰减短尾，2ms 线性起音防爆音，峰值仅 0.18 满量程——宁可轻不可噪）；`MakeWav()` 封标准 44 字节头 PCM WAV（纯函数）；`Play()` 用 Win32 `PlaySound(SND_MEMORY|SND_ASYNC|SND_NODEFAULT)` 异步播放、Windows 自持缓冲（避开 SoundPlayer 立即 Dispose 截断异步播放的坑），首次合成后缓存字节，异常静默忽略（无音频设备也不炸）。
- 设置项 `CaptureSound`（默认 false）；`CaptureController` 静态图捕获成功、trace 之后接线：三态（弹提示/进编辑器/只复制）都响，关=零副作用。
- 设置窗「截图」组新增 `CaptureSoundRow()`：扬声器图标（**E767**，GlyphProbe 定稿，非豆腐）+「试听」chip（关着也能先听）+ 同款开关；帮助窗「截图后」下补一行说明。

**验证**：
- 构建 0 错误；SelfTest 282→**286**（T118a WAV 头 RIFF/WAVE/fmt/data + RIFF size 字段自洽；T118b 时长 30–60ms；T118c 峰值 0.1–0.5 满量程=不静音不削顶；T118d 末样本衰减到 <0.02 且峰值非零）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0）
- 实机 e2e（PrintWindow 亲眼看 + trace）：开 CaptureSound 后 F6 截图→trace `capture sound played=True bytes=3748`；设置窗「截图提示音」行渲染正常（扬声器图标清晰、试听 chip、开关蓝=开）；点「试听」→trace 再出一条 `played=True`（前台校验=Settings 通过）；点开关→settings.json `CaptureSound=False` 持久化、复抓该行确认 OFF 视觉（knob 左、灰轨、试听 chip 仍在）


## 2026-10-12 上午 滚动截图·核心拼接算法 R87（QQ 最大缺口，先落地最难也最可验证的部分）

**动机**（补 QQ 最大缺口）：滚动截图（长网页/聊天记录/代码一屏截不下，滚动自动拼长图）是 QQ 招牌、我们完全没有的能力，也是剩余 QQ 功能里价值最高的一项。它是多轮特性，本轮先做**最难、最容易出错、也最可单测**的核心：给定「已累积长图 acc」与「滚动后再截的一帧 b（同宽）」，找两者纵向重叠行数、把 b 的新内容拼到 acc 下方。UI/捕获流程留 R88。

**实现**（`Core/ScrollStitch.cs`，纯函数、零依赖、可单测）：
- `FindOverlap(acc,w,accH,b,bH,cols=64,thresh=14)`：每行取 cols 个等距列的灰度均值作「行签名」（抗轻微色差、够快），从最大候选重叠往下扫、命中第一个即返回（偏好最大重叠；滚动量小时命中快）。搜索窗口界定在 `min(accH,bH)` 内、最多检查 2500 个候选，防长图卡顿；单行均值超 `thresh*2` 即早退该候选。找不到可靠重叠返回 0（调用方决定：整帧追加或提示「滚动太多」）。
- `Append(acc,w,accH,b,bH,ov)`：`Buffer.BlockCopy` 把 b 去掉顶部 ov 行拼到 acc 下方，返回新 BGRA 缓冲。

**踩坑（都在 SelfTest 里逼出来）**：
- 早退 bug：候选不合格提前 break 时 `sum` 只累加了部分行，却仍除以全行数 `ov` → 均值虚小误判通过（返回 100）。加 `bad` 标志，早退即拒绝该候选。
- 测试帧灰度别用线性序列：`(row*37)%256` 与 Knuth 乘法哈希高位都是**线性斜坡**——偏移 d 行灰度只差近恒定小值（偏移 7 行差 3），被当成重叠（返回 47/95）。改**雪崩整数哈希**（xorshift-multiply finalizer），任意偏移行输出近似独立，才真正测出「只有真重叠才命中」。

**验证**：
- 构建 0 错误；SelfTest 286→**290**（T119a 合成帧找重叠=40 · T119b 拼接高 160 且首尾行内容正确 · T119c 无重叠返回 0 · T119d ±3 噪声仍命中 40）
- **真实像素取证**：新增 `SelfTest stitch <a.png> <b.png>` 模式（载 PNG→BGRA、跑算法、存 stitched.png）。从一张全屏截图裁「同区域下移 200px」两帧（同源零时序风险）→ overlap=**800**（正解 1000−200）、merged 1400×1200；肉眼核对 stitched.png 文字连续无缝、重叠边界无重复无断裂
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0；首轮 e2e 因浏览器抢前台被焦点安全全 SKIP，重跑干净全过）


## 2026-10-12 上午 滚动截图·自动捕获流程与控制条 R88（把 R87 算法接成能用的功能）

**动机**：R87 只落地了拼接算法，用户还点不到。本轮把它接成完整可用的「滚动截图」：选区→常驻控制条→用户只管滚动目标窗→后台自动找重叠拼接→点完成出长图。目标是**手感对齐 QQ、且比 QQ 更不打扰**（QQ 要盯着它手动点，我们全自动）。

**关键设计决策（自动 vs 手动步进）**：先做了「控制条抢焦点 + 空格拼接」的手动步进版，实机 e2e 立刻暴露硬伤——控制条一抢焦点，在**关闭了「悬停滚动」**的系统里滚轮会落到控制条、目标窗纹丝不动（提示「画面没变化」）。手动版对手感是赌博。遂**推翻改自动**：控制条 `ShowActivated=false` **绝不抢焦点**，目标窗保持焦点、滚轮/键盘/拖滚动条照常滚；后台 `DispatcherTimer`（280ms）抓帧自动拼接。这也顺带支持键盘 PageDown、拖滚动条等一切滚动方式。

**实现**：
- `Overlays/ScrollHintWindow.cs`（新）：常驻控制条。标题「滚动截图」+ 右侧实时进度「已拼接 N 段 · 高 H px」（强调蓝）+ 提示「滚动目标窗口，自动拼接 · 点「完成」结束」+ 完成(蓝)/取消(灰)两按钮。落点：区域顶部居中、优先置于区域**上方**（不遮挡要滚的内容），上方放不下则贴区域上沿内侧；`WDA_EXCLUDEFROMCAPTURE` 排除，**绝不入镜**（PrintWindow 亲验返回纯黑=排除生效）。`ShowActivated=false`。HWND 钩子留 Esc/Enter 作键盘等价（控制条被点后才生效，不依赖它常驻焦点）。
- `CaptureController`：`BeginScrollCapture()`→复用选区流程，选完 220ms 后 `StartScrollLoop`。循环 `ScrollTick()`：抓帧 b→**稳定判定**（b 与上一 tick 帧一致才算滚动停稳，避免拼接平滑滚动中途的过渡帧，也顺带过滤动画/闪烁误拼）→`FindOverlap(acc,b)`：`ov>=bH` 无新内容跳过；`ov<=0` 刷「滚动太快、找不到重叠——请少滚一点」；否则 `Append` 追加 `bH-ov` 行、更新进度。`ScrollDone` 出图（存 TEMP + 复制 + 按 AfterCapture 走编辑器/提示/复制，与静态图同路）；`ScrollCancel` 收尾。
- 托盘菜单加「滚动截图（长图）」；`--scroll` IPC（第二实例转发主实例 `Controller.BeginScrollCapture()`）；`tools/Shot` 加 `wheel x y [clicks]`（正=下/负=上）供 e2e 模拟滚轮。

**踩坑（e2e 逼出的真问题）**：
- 手动步进版抢焦点→悬停滚动关闭时目标窗滚不动（见上）。改自动 + 不抢焦点根治。
- **均匀内容局限**：首版测试用「每行 91% 相同后缀」的文本，行签名几乎一致→`FindOverlap` 误判重叠→长图**跳号**（080 后直接 114）。这不是 bug 是重叠拼接的固有局限（QQ 同款）：内容行间差异太小则无法定位重叠。换「行号占满整行」的可区分内容后拼接完美。已记入 TODO 作为已知边界（真实网页/聊天/文档行间差异大，不受影响）。
- e2e 一度「滚轮/翻页都不滚」——实为记事本**没最大化**、选区落在了左侧浏览器背景上（区域里是静止内容）。最大化后区域落在记事本上，一切正常。教训：e2e 前先 `rect` 确认目标窗真的铺满选区。

**验证**：
- 构建 0 错误；SelfTest 仍 **290**（本轮为 UI/流程接线，算法未改，无新增纯函数；护栏 floor 280 不变）
- 实机 e2e（PrintWindow 亲眼看 + trace + 像素）：
  - `--scroll`→picker 前台正确；拖选区→控制条出现且**前台仍是目标窗**（不抢焦点，trace `scroll bar shown`/`scroll loop start`）
  - PrintWindow 控制条（NOEXCLUDE）：标题/进度/提示/完成·取消按钮渲染正常、圆角阴影、无豆腐块；正常 WDA 下 PrintWindow 纯黑=排除生效
  - 滚轮滚动记事本→trace 连续 `scroll append ov=… new=… h=…`（ov 840/888/328… 均有效），进度实时增长
  - 点「完成」→`scroll done 1340x2840 segs=7`、编辑器打开；点「取消」→`scroll cancel`
  - **肉眼核对 2840px 长图**：行号 009→250 全程逐行连续、无跳号无重复、7 段接缝完全不可见；控制条不在图内


## 2026-10-12 上午 取色复制格式 R89（HEX/RGB/HSL，QQ 只给 HEX 的补强）

**动机**（自定义能力，服务开发者/设计师）：选区放大镜按 C 复制光标下像素颜色，此前只会复制 `#RRGGBB`（QQ 同款）。但前端/设计取色常要 `rgb()`/`hsl()` 直接贴进 CSS，每次手转很烦。做成可选项——不是堆数值，是多一个真正省事的「功能可选项」。

**实现**（纯格式化、零钩子、可单测）：
- `Core/ColorFormat.cs`（新）：`enum ColorCopyFormat { Hex, Rgb, Hsl }`；`Format(r,g,b,fmt)` 纯函数（分量夹 0–255；Hex=`#RRGGBB`、Rgb=`rgb(r, g, b)`、Hsl=`hsl(h, s%, l%)` 标准 RGB→HSL 四舍五入）；`Parse`（设置串→枚举，大小写不敏感、未知回退 Hex 向后兼容）；`ToSetting`（往返）。屏幕像素恒不透明，故不提供带 alpha 的形式（避免误导）。
- 设置项 `ColorCopyFormat`（默认 "hex"，与旧配置兼容）；`RegionPickerWindow` 构造参数 `colorFormat`，放大镜读数与按 C 复制都走 `ColorFormat.Format`；`CaptureController` 构造选区窗时 `ColorFormat.Parse(设置)` 传入。
- 设置窗「截图」组放大镜开关下新增 `ColorFormatRow()`：调色板图标（**E790**，与强调色行同款、已验证非豆腐）+ HEX/RGB/HSL 三 chip（复用 AfterCaptureRow 的分段样式，选中蓝底、键盘可达）；帮助窗「选区放大镜」行文案改为「格式 HEX/RGB/HSL 可在设置选，默认 HEX」。

**验证**：
- 构建 0 错误；SelfTest 290→**298**（T120a-h：HEX/RGB/HSL 文本正确、灰/白/橙 HSL 边界、分量夹取 300/-5→FF/00、Parse 大小写+未知回退、枚举↔设置串往返）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0）
- 实机 e2e（改 settings.json→watcher 热重载→F7 选区→移光标→按 C→读剪贴板 + PrintWindow 读数）：
  - 设 rgb：剪贴板 `rgb(39, 39, 39)`；PrintWindow 放大镜读数「已复制 rgb(39, 39, 39)」
  - 设 hsl：剪贴板 `hsl(0, 0%, 15%)`（39 灰阶→15% 亮度，正确）
  - 设置窗「取色格式」行渲染正常：调色板图标清晰、HEX/RGB/HSL 三 chip、当前项蓝底选中、位置在放大镜开关后、与「截图后」行同款右对齐


## 2026-10-12 上午 滚动截图·全局 Esc 取消 R90（补 R88 留下的手感缺口）

**动机**（手感审计发现的摩擦）：R88 的控制条 `ShowActivated=false` 不抢焦点（这是自动拼接能工作的前提），副作用是**控制条收不到键盘**——用户按 Esc 想取消（QQ 肌肉记忆），Esc 却漏给了目标窗口，取消不了，只能去找「取消」按钮。这是刚上线功能里最扎眼的摩擦。QQ 滚动截图里 Esc 就是取消，必须补上。

**实现**（全局钩子拦截，标志绑定控制条生命周期防泄漏）：
- `HotkeyManager`：新增 `static volatile bool ScrollActive` + `event Action? ScrollCancel`；`HandleKey` 在决策表前加一句 `if (isDown && vk==Esc && ScrollActive) { ScrollCancel?.Invoke(); return true; }`——会话中全局吞 Esc 并触发取消。热路径仅一次 volatile 读 + 一次整数比较，不阻塞（沿用 R2 零阻塞纪律）。
- `AppHost`：`Hotkeys.ScrollCancel += () => Dispatch(Controller.ScrollCancel)`（与 CaptureBegin/End 同款 marshal 到 UI 线程）。
- `CaptureController`：`ScrollActive=true` 在控制条 `Show()` 后置位；**`_scrollBar.Closed` 兜底清零** + `ScrollDone`/`ScrollCancel` 顶部即时清零——三重保证会话一结束标志必落，绝不泄漏成"全局 Esc 失灵"。`ScrollCancel` 提为 public 供 AppHost 调用。
- 控制条自身 HwndSource 的 Esc 处理保留（被点聚焦时仍可用）；全局钩子先吞，二者不冲突、不双触发。
- 帮助窗补「滚动截图（长图）」条目（含"按 Esc 随时取消"）。

**取舍**：只做 Esc 取消、不做 Enter 完成——Enter 是目标窗高频键（发消息/确认/换行），会话中吞掉太伤；完成走「完成」按钮已足够。会话短暂且用户主动发起，期间吞 Esc 与 QQ 行为一致、无陷阱（Esc/完成/取消任一即结束）。

**验证**：
- 构建 0 错误；SelfTest 仍 **298**（本轮为钩子接线 + 生命周期标志，非纯逻辑，无新增单测；护栏 floor 280 不变）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0；**钩子改动后 e2e:hotkey / esc-layers / fuzz-statemachine 全过**，证明未回归既有热键行为）
- 实机 e2e（最大化记事本 + --scroll + 选区 + 全局 Esc）：会话中按 Esc → trace `scroll cancel`、控制条窗口数归 0；**再按一次 Esc 无新 cancel**（标志已清零、Esc 不再被全局吞，无泄漏）


## 2026-10-12 上午 延时截图 R91（QQ 招牌、全受众刚需：截「一动就没了」的界面）

**动机**：右键菜单 / 悬停提示 / 下拉框这类界面，鼠标一动或一点就消失，普通截图根本截不到。QQ 有「延时截图」，这是高频刚需却此前缺失。做成独立托盘动作 + 倒计时浮窗，**刻意不碰点按/录制状态机**（早前评估把延时塞进热键点按路径会有模式交互复杂度，这里绕开）。

**实现**（独立动作，只借完成路径，不碰 Phase）：
- `Overlays/CountdownWindow.cs`（新）：顶部居中大号倒计时数字卡（标题「延时截图」+ 将截范围 + 大号数字 + 提示 + 取消按钮）。`ShowActivated=false` **不抢焦点**（否则目标窗失焦、正要截的菜单反而被关掉）；WDA 排除不入镜；`SetNumber(n)` 刷数字。
- 设置项 `DelayCaptureSeconds`（默认 3）；设置窗「截图」组新增 `DelayRow()`：时钟图标（**E916**，与点按阈值行同款已验证字形）+ 关/1/3/5/10 秒预设 chip（功能可选项，非任意数值）。
- `CaptureController.BeginDelayedCapture()`：取「已存范围 ?? 光标屏全屏」→ 0 秒直接截、否则弹倒计时 + 1s DispatcherTimer 递减 → 到点 `DelayFire`（抓帧→剪贴板→按 AfterCapture 走 toast/编辑器/只复制 + 声音 + lastStill + 范围闪一下作反馈，与 ScrollDone 同款完成路径）。`DelayCancel` 中止。重复触发忽略（`_delayWindow!=null` 守卫）。
- 托盘「延时截图（N 秒）」项（标签带当前秒数）+ `--delay` IPC（MsgSink 第 5 路）+ 帮助条目。

**验证**：
- 构建 0 错误；SelfTest 仍 **298**（本轮为浮窗 + 定时器 + 抓帧编排，无新增纯逻辑单测；护栏 floor 280 不变）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0；延时动作未回归既有捕获流）
- 实机 e2e（`FC_TOAST_NOEXCLUDE=1` 让 PrintWindow 能渲染倒计时窗）：
  - PrintWindow 倒计时窗：「延时截图」+「范围 1340×1000」+ 大号蓝色「3」+ 提示 + 「取消」，布局干净
  - 1.6s 后再 PrintWindow：数字已变「2」（递减正确）
  - 到点：trace `delay fire 1340x1000` → `open editor frames=1`（AfterCapture=editor）→ 范围闪反馈
  - 点「取消」（先 WindowFromPoint 确认落点属倒计时窗，焦点安全）：trace `delay cancel`、窗口关闭、未触发截图
  - 设置窗「延时截图」行渲染正常：时钟图标 + 关/1/3/5/10 秒 chip、3 秒蓝底选中、位置在截图提示音后


## 2026-10-12 上午 从剪贴板贴图 R92（Snipaste 招牌「粘贴即贴图」，此前只能贴自己截的图）

**动机**：贴图（Pin）功能已很完整（缩放/不透明度/旋转/磁边/对齐/重抓/右键菜单），但入口只有两个——编辑器 Ctrl+P 钉当前帧、贴图 F3 原地重抓。**从别处复制的图片钉不了**：网页图、图片文件、别人发来的截图、IM 里的图……想钉屏作参考只能先存盘再想办法。Snipaste 最受好评的「粘贴即贴图」是这块的缺口，全受众刚需。

**实现**（读剪贴板建帧，复用现成 PinWindow，零新窗）：
- `BgraImage.FromSource(BitmapSource)`（新，纯转换、可单测）：统一转 Bgra32 再 CopyPixels 建 `CapturedFrame`；与 `ToSource` 同格式 → 往返无损；空/零尺寸返回 null。剪贴板图常带 alpha（透明 PNG），Bgra32 保留 alpha，贴图透明区照常透出桌面。
- `CaptureController.PinFromClipboard()`：`Clipboard.ContainsImage/GetImage` → `FromSource` → 落点「光标所在屏、以光标为中心、越界钳进屏内」→ `new PinWindow(frame, physPos).Show()`。无图片返回 false。须在 UI 线程调用（托盘菜单点击 / IPC 回调均在 UI 线程）。
- 托盘「从剪贴板贴图」项：失败（剪贴板没图片）走 `TrayService.Notify()` 原生气泡提示「先复制一张图片再试」——tray 动作的原生反馈，不新造 toast 窗。
- `--pin-clip` IPC（MsgSink 第 6 路）+ 帮助条目，便于脚本/e2e。

**验证**：
- 构建 0 错误；SelfTest 298→**301**（T121a ToSource↔FromSource 逐字节往返、T121b Bgr32→Bgra32 分量不变 alpha 补 255、T121c null→null）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0）
- 实机 e2e：
  - 造 240×140 测试图（红底/蓝块/黄字）设入剪贴板 → `--pin-clip` → trace `pin-clip 240x140 at 1800,188`、贴图窗出现
  - PrintWindow 贴图：红底 + 蓝方块 + 黄字清晰，内容与剪贴板图一致
  - 剪贴板改纯文本 → `--pin-clip` → trace `pin-clip: no image`、贴图数不变（不误建空贴图）
  - 附带发现：更早一次 `--pin-clip` 把 R91 延时截图残留的 1340×1000 剪贴板图也正确钉出，二次印证读剪贴板链路可靠


## 2026-10-12 上午 编辑器窄图工具栏裁切修复 R93（手感审计抓到的真 bug）

**发现方式**（按用户要求「多跑真实执行、细看截图找摩擦」）：实机走一遍 捕获→选区→编辑器，PrintWindow 小尺寸截图（320×220）的编辑器，放大工具栏发现**两端被裁切**——左侧「选择/直线/椭圆」和右侧「上传/设置/帮助」都看不见，用户够不到这些工具。

**根因**：底部工具条是居中 StackPanel，窗口比它窄时两端溢出被裁。`MinWidth=812` 是 R34/R44 时按当时工具条列宽定的地板值，但此后陆续加了马赛克/裁剪/编号/OCR/翻译/置顶/上传…工具条早已超过 812，地板值却没跟上。凡宽度 < ~1039 DIP 的截图（绝大多数区域截图）都会裁切。

**修复**（让 MinWidth 跟随工具条实测宽度，不再写死）：
- `EditorWindow` 存 `_toolsBar` 引用；新增 `FitMinWidthToToolbar()`：`Measure(∞)` 取 `DesiredSize.Width`（StackPanel 内容宽，不受容器约束，窗口窄时也量得到真实需要）+ 面板内边距 20，夹到 `[812, 工作区宽]`（不超过屏幕）。构造里 `RestoreEditorPrefs` 后调用一次（此时最近用色已载入，宽度为最终值）；`_toolsBar.SizeChanged` 再挂一次（最近用色增减时跟随）。
- 窄图时最小宽会超过当前宽：撑到最小宽并**仅在未显示时**重新居中（`!IsLoaded`），避免编辑中窗口跳动。
- 收敛保护：`Math.Abs(MinWidth-target)<=0.5` 直接返回，杜绝 MinWidth↔重排 循环。

**顺带修测试探针**（`scripts/guardrail.ps1`）：注入可达性探针用 200ms 点按后只认「编辑器」窗，但 `TapAlwaysShowsRegion=true` 时点按弹的是**选区窗**，探针据此误判「被反外挂拦截」→ 整套 e2e 全 SKIP（本轮连撞两次）。改为「编辑器或选区窗任一出现即证明注入可达」，e2e 恢复稳定运行（SKIP 回到正常的 1）。

**验证**：
- 构建 0 错误；SelfTest 仍 **301**（本轮为 WPF 布局 + 测试脚本，非纯逻辑，无新增单测）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0；探针修复后 e2e:editor-keys/zoom/esc-layers/hotkey/tap-semantics/fuzz 全过）
- 实机 e2e：320×220 小图编辑器窗口从 812→1039 DIP，PrintWindow 工具栏**两端工具全部可见**（选择/直线/椭圆…上传/设置/帮助），左右留白正常；宽图编辑器不受影响


## 2026-10-12 上午 暂停全局热键 R94（兼容游戏/演示/热键冲突受众）

**动机**：全局热键工具最大的「与别人打架」痛点——打游戏、投屏演示、或别的软件也用了 F6/F7 时，用户需要一键让本工具「闭嘴」。Snipaste 等也有类似「暂停」。属「功能可选项、兼容所有受众」，非堆数值。

**实现**：
- `HotkeyManager.Paused`（static volatile，运行时状态、**不持久化**，重启自动恢复工作，避免用户重启后困惑「热键怎么没了」）。
- 决策并入纯函数 `HookDecision.Decide(..., bool paused = false)`：暂停时任何按下键 → `Pass`（不启动捕获/选区、**不吞键**，键完整落到前台应用）；已在进行中的捕获（`pressed==CapturePrimary`）不受影响仍正常收尾。默认参数 → 旧调用零改动。`HandleKey` 把 `Paused` 传入（滚动会话的 Esc 取消仍在其之前，先开会话再暂停也能 Esc 取消）。
- 托盘「暂停全局热键（游戏/演示时）」可勾选项 → `TrayService.TogglePause()`；暂停时托盘 tooltip 变「FrameCapture · 已暂停（热键让位其他软件）」，气泡确认。暂停只让位**热键**，托盘菜单里的「立即捕获」等仍可用。
- `--pause-toggle` IPC（复用 MsgSink 转发链）：既给 e2e 用，也让用户用 Windows 快捷方式/脚本绑一个「暂停热键」而不必新增全局键（避免堆键）。
- 帮助窗新增「暂停全局热键」条目。

**验证**：
- 构建 0 错误；SelfTest **305**（新增 T122a-d：暂停时捕获键/范围键放行不吞、进行中捕获仍收尾、未暂停默认参数行为不变）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0；e2e:hotkey/tap-semantics/fuzz 覆盖改动的决策路径，无回归）
- 实机 e2e：基线 F6→选区窗出现；`--pause-toggle`→trace `sink→pause toggle`、F6→选区窗**不出现**（放行给前台）；再 `--pause-toggle`→F6 重新开选区窗。三段全对


## 2026-10-12 上午 贴图窗快捷键补全 R95（贴图是高频招牌，键盘闭环）

**动机**：贴图（Snipaste 式钉图）是最常用功能之一，但键盘快捷键此前只有 Esc/R/F3/Ctrl+S。右键菜单里的「复制」「识别文字」没有快捷键，用户复制贴图回剪贴板必须右键点菜单，慢。补齐与编辑器一致的 Ctrl+C / Ctrl+O。

**实现**（`Overlays/PinWindow.cs`）：
- 新增 `Copy()`：`ClipboardService.SetFrame(_frame)` + `Flash("已复制")` + trace；右键「复制 (Ctrl+C)」与快捷键共用同一方法（不再各写一份）。
- `OnKeyDown` 加 `Ctrl+C → Copy()`、`Ctrl+O → OcrFrame()`（与编辑器 Ctrl+O 同一识别入口）。
- 文档注释 + 帮助窗贴图条目同步补 Ctrl+C/Ctrl+O。

**验证**：
- 构建 0 错误；SelfTest 仍 **305**（本轮为 WPF 按键路由，非纯逻辑，无新增单测）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0）
- 实机 e2e：设 220×150 特征图（红底+蓝块）→ `--pin-clip` 贴图并自动前台 → 焦点安全确认前台是 `FrameCapture Pin` → 先把剪贴板清成纯文本 → `hkm Ctrl+C` → trace `pin-copy`、剪贴板 `ContainsImage=True` 且文本被替换；取回图片逐像素核对：220×150、中心 (100,70)=蓝(0,0,255)、左上 (5,5)=红(220,40,40)，与贴图内容完全一致


## 2026-10-12 上午 选区窗内按 F 循环取色格式 R96（开发者/设计师免进设置）

**动机**：R89 取色格式（HEX/RGB/HSL）只能在设置窗改。开发者常要 RGB、设计师常要 HEX，来回切要进设置很烦。选区窗内加一个键就地循环，读数立刻跟着变。

**实现**（`Overlays/RegionPickerWindow.cs`）：
- `_colorFormat` 由 readonly 改可变；新增 `_lastRgb` 记住放大镜中心最近一次像素色。
- `KeyDown` 加 `F`：`(fmt+1)%3` 循环 Hex→RGB→HSL → 用 `_lastRgb` 就地重算 `_loupeHexText` 并刷新读数 → `AppHost.Current.Settings.Mutate` 持久化（下次捕获、设置窗 chip 同步）。`C` 仍复制当前格式。
- 帮助窗放大镜条目补「按 F 即时循环切换格式」。

**验证**：
- 构建 0 错误；SelfTest 仍 **305**（循环是按键路由 + 枚举取模，`ColorFormat.Format` 已由 T120 覆盖，无新增单测）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0）
- 实机 e2e：选区窗内同一像素，`C`→`#151517`；`F,C`→`rgb(21, 21, 23)`；`F,C`→`hsl(240, 5%, 9%)`；关窗后 settings.json `ColorCopyFormat=hsl`（持久化生效）。三格式 + 持久化全对


## 2026-10-12 上午 编辑器小图默认 1:1 不再放大变糊 R97（手感审计发现）

**动机**：手感审计里模拟「截一小块（按钮/图标/小对话框）→ 进编辑器」，发现默认视图是 `适配倍率 × 缩放`，而 `FitScale = Min(视口/图)` **不封顶**——小图被放大到铺满视口（如 320px 图放进 ~900px 视口放大 2.8×），`HighQuality` 缩放直接糊成一团。QQ/Snipaste 的默认是「小图 1:1 逐像素清晰、只有大图才缩小适配」。

**实现**（`Core/ZoomMath.cs`）：`FitScale` 结果封顶 `Math.Min(1.0, …)`。
- 图小于视口 → 1.0（1:1 居中，`ClampPan` 已保证小于视口方向强制居中）；图大于视口 → 原缩小逻辑不变。
- 连带正确：`OneToOneZoom(1.0)=1.0`（小图 1:1 键=默认）；`ZoomFit` 对大图仍缩小适配；`ZoomAt`/`ClampPan` 用同一封顶基准，坐标换算自洽。放大仍只靠 Ctrl+滚轮 / 1:1 键显式进行。

**验证**：
- 构建 0 错误；SelfTest **309**（新增 T123a-d：小图→1.0 不放大 / 大图→0.5 不变 / 恰等→1.0 / 小图 1:1 键=1.0；旧 T79 缩放用例全过）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0）
- 实机：截 ~200×150 小区域 → 编辑器 PrintWindow 核对，图像以 ~300 物理 px（=200 DIP×1.5，1:1）清晰居中显示、四周留白，不再铺满视口放大


## 2026-10-12 上午 编辑器矩形框工具 R98（最常用标注，此前缺位）

**动机**：对比 QQ/Snipaste/ShareX，编辑器有直线/圆/箭头/手绘/文字/马赛克/序号，却**没有矩形框**——而矩形框是圈选/强调目标区域的第一常用工具。缺口明显。

**实现**（单一数据源 `Core/ToolCatalog.cs` 加一条，其余镜像 Ellipse）：
- `ToolCatalog` 加 `("R","□","\uE739","矩形 (R)",…)`，排在直线后、圆前（形状成组）。Glyph `E739` 经 GlyphProbe sweep 人工定稿为空心方块（与圆的线性环 EA3F 同风格）。
- `Tool` 枚举 + `FromKey("R")` + `AnnKind.Rect` + `Render` 用 2 点包围盒 `DrawRectangle`（空心描边）。
- 创建/预览/拖拽（并入 Line/Ellipse/Arrow 的 2 点组）/实时渲染（WPF `Rectangle`）各加 `Rect` 分支；命中测试 `AnnBDisp` 走通用点集包围盒，零改动即适用。
- 键 `R`、引导卡工具列表、帮助窗（遍历目录自动含）同步。

**验证**：
- 构建 0 错误；SelfTest **312**（T92 目录锁改 11 项/键串含 R；T10b 动态校验 E739 在 Segoe Fluent Icons 可渲染；新增 T124a-c 烘焙核对空心矩形：上边中点着色、内部与框外保持原色）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0）
- 实机：截 ~900×650 → 编辑器按 R → 拖框，PrintWindow 核对红色空心矩形正确画出、工具栏第 3 枚方块按钮（直线与圆之间）accent 高亮、字形为干净方块


## 2026-10-12 上午 Shift 约束绘制（矩形正方 / 圆正圆 / 线·箭头 45° 吸附）R99

**动机**：QQ/Snipaste 拖形状时按住 Shift 锁比例——矩形锁正方形、圆锁正圆、直线/箭头吸附 45° 整数倍。此前我们的形状拖拽是自由比例，画正圆/正方/水平线全靠手稳，标注对齐很吃力。

**实现**（单一数据源 `Core/ShapeConstraint.cs`）：
- `Apply(kind, p0, p1, shift)` 纯函数：矩形/圆 → 取 `max(|dx|,|dy|)` 保号锁成正方/正圆；直线/箭头 → 角度吸附到 45° 网格（`round(θ/45°)*45°`）保长度。其余形状原样返回。
- `ToolCatalog` 标注哪些形状支持约束；`EditorWindow.Drawing` 拖拽预览与落点两处都过 `ShapeConstraint.Apply`，按住 Shift（`Keyboard.Modifiers`）实时生效。

**验证**：
- 构建 0 错误；SelfTest **326**（新增 T125a-d：矩形 Shift 锁正方 / 圆锁正圆 / 线 45° 吸附 / 未按 Shift 自由比例不变）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0）
- 实机：编辑器按 R 拖矩形 + 按住 Shift → 恒为正方形；按 O 拖圆 + Shift → 正圆
- 注：本条为提交 94a7d3e 的文档补记（该提交当时漏写 PROGRESS 条目）


## 2026-10-12 上午 高刷录制：去 60fps 天花板 + 跟随显示器刷新率 R100

**动机**：用户明确「录制的都是 60fps，我要能录满刷新率，比如我显示器 160Hz」。排查发现两处天花板：① `AppSettings.FpsMax=60` 硬顶；② 旧 `WgcLiveCapture` 用 `Thread.Sleep` 轮询取帧，Sleep 粒度 ~15ms 本身就压到 ~60fps。

**实现**：
- `AppSettings`：`FpsMax` 60→**240**；新增 `FollowMonitorRefresh`（默认关）；新增 `RecordFormat` 枚举（Gif/Mp4/WebM，默认 Gif，供后续编码轮接线）。
- `Core/DisplayRefresh.cs`（新）：`EnumDisplaySettings(ENUM_CURRENT_SETTINGS)` 读 `dmDisplayFrequency`，实测本机 `\\.\DISPLAY1`=**160**。
- `Core/FpsThrottle.cs`（新）：`IntervalMs=1000/fps`（去掉旧 16ms 地板）；`ShouldKeep` 纯函数便于单测。
- `WgcLiveCapture` 重写为**事件驱动**：`FrameArrived` 回调取代 Sleep 轮询；目标帧率≥刷新率时 `keepAll` 全保（录满），否则按时间节流丢帧；拷贝只搬区域行（`Marshal.Copy` 逐行），不再分配整块监视器缓冲。
- `CaptureController`：`FollowMonitorRefresh` 开时用 `DisplayRefresh.RateOf` 覆盖 fps；新增 `delivered/keptThrottle` 诊断计数 + `wgc stats` 统计行。
- `SettingsWindow`：帧率滑杆自动到 240；新增「跟随显示器刷新率录满」开关（描述里带实测 Hz）。

**关键实测（诚实记录）**：加 `FC_NO_COPY` 诊断隔离后发现——**完全不做拷贝时 WGC 投递仍只有 ~47fps**（delivered=keptThrottle=kept，missed=0），与拷贝耗时、帧池缓冲数（3→8 无效）、运动源（小动画 vs 铺满区域闪色）均无关。即 **拷贝不是瓶颈**，~47fps 是 WGC/DWM 对窗口内容的投递行为；真·160fps 需要内容本身以刷新率变化（全屏 GPU 渲染/视频），PowerShell 驱动的动画造不出真 160Hz DWM 重合成。据此**否决了 D3D11 staging 纹理优化**（Vortice 直引缓存 DLL 试过：`IDirect3DSurface` 取底层纹理的 `GetInterface`/`IDirect3DDxgiInterfaceAccess` 在本机投影下均失败，且它只优化非瓶颈的拷贝，无收益）——干净回退，不留死代码。

**验证**：
- 构建 0 错误；SelfTest **326**（T77a 更新为 FpsMax==240；新增 T126a-h：interval 60≈16.67 / 240≈4.17 / keepall / 首帧 / 早丢 / 到点保 / refresh null / refresh 实测>0）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0）
- 实机：`FollowMonitorRefresh=true` + Fps=160 → trace `wgc fps=160 refresh=160 keepAll=True interval=6.25ms`（60fps 天花板消失、跟随刷新率识别 160Hz）；录制 kept=47/delivered=47/missed=0，编辑器正常出帧
- 说明：本轮交付的是「天花板移除 + 跟随刷新率 + 事件驱动」，让 160fps 在内容够快时成为可能；输出格式（MP4/WebM）与真高刷落盘在后续编码轮（R101+）接线


## 2026-10-12 中午 ffmpeg 视频编码：MP4 / WebM 导出 R101+R102

**动机**：用户明确「webm 和 mp4 都要，gif 也要保留」。GIF 受格式限制封顶 ~50fps 且体积巨大，是录制的差劲容器；MP4/WebM 才是高刷落盘的正解。项目离线不捆绑 ffmpeg（体积 + GPL 许可），改运行时探测。

**实现**：
- `Encoding/VideoEncoder.cs`（新）：`FfmpegLocator.Find()` 扫 PATH + 常见安装目录（Program Files\FFmpeg\bin、winget Links、choco）；`BuildArgs` 纯函数（MP4=libx264/yuv420p/ultrafast/faststart，WebM=libvpx-vp9/yuv420p/-b:v 0/-crf 40，均 `-f rawvideo -pix_fmt bgra` 从 stdin 进）；`Encode` 起 ffmpeg 进程、BGRA 帧写 stdin、**stderr 异步排空防管道写满死锁**、非零退出码/超时/空产物均抛错并杀进程删半成品；`FpsFromFrames` 由帧时间戳推实际帧率（静态段少帧→低 fps 但总时长不变，播放速度正确）。
- `EditorWindow`：`StartEncode` 泛化为 `(VideoFormat?, outPath, onDone)`——子集/裁剪/烘焙两路共用，只在末步分派 GIF 或 ffmpeg；`SaveRecording`（原 SaveGif）对话框三格式（MP4/WebM/GIF），默认跟随设置 `RecordFormat`，按扩展名分派；`DispatchSave` 抽出；工具栏按钮 tooltip 改「导出录制 (MP4/WebM/GIF)」，Ctrl+E 不变。
- `SettingsWindow`：新增 `RecordFormatRow` 芯片选择器（GIF/MP4/WebM，镜像取色格式行），tooltip 带 ffmpeg 探测结果（已检测到 / 未检测到提示安装）。

**验证**：
- 构建 0 错误；SelfTest **332**（新增 T127a-g：ffmpeg 定位契约 / MP4 命令行含 libx264+faststart+invariant fps / WebM 含 libvpx-vp9+crf40 / FpsFromFrames 11 帧跨 1s→10fps / 单帧回退 30 / **真实 ffmpeg 编码 MP4 产物 >500B**）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0）
- 全链路 e2e（`FC_TEST_EXPORT` 钩子，对齐 `FC_TEST_ENCODE_FAIL` 约定）：录 48 帧 → 焦点安全核前台=「FrameCapture Editor」→ 发 Ctrl+E → ffmpeg 落盘 → **ffprobe 核对 h264 / 640×400 / 48 帧 / 时长 0.99s / r_frame_rate=48.26（=实际捕获率，播放速度正确）/ 解码 exit=0 可播**
- WebM 独立真实编码：libvpx-vp9 exit=0、codec=vp9、尺寸正确
- 说明：ffmpeg 未捆绑，未装用户的视频项点击时提示安装、GIF 始终可用；MP4/WebM 帧率取实际捕获率保证播放速度正确（非按设置的目标帧率）


## 2026-10-12 中午 自动保存副本跟随录制格式（手感审计发现的一致性缺口）R103

**动机**：手感审计里模拟「设 `录制格式=MP4` + 开 `自动保存副本` → 录制 → 按 Enter」，发现自动保存仍写 GIF——用户刚选了 MP4 却在文件夹里拿到 GIF，与「录制格式」设置自相矛盾。这是新格式特性引入的真实摩擦。

**实现**（`Services/SaveService.cs` `MaybeSaveArtifact`）：多帧分支不再写死 GIF——
- 读 `RecordFormat`：GIF 或未装 ffmpeg → 仍存 GIF（原行为，永远可用）；MP4/WebM 且装了 ffmpeg → **后台 `Task.Run` 编码**（编辑器立即可关，不被 vp9 慢编码冻住 UI），文件稍后出现。
- 原子落盘：先编到 `%TEMP%` 临时文件，成功再 `File.Copy` 进目标唯一名，`finally` 删临时——目录里只出现完整可播产物，应用中途退出不会留半截文件。
- 失败兜底：视频编码异常 → 回退写 GIF（用已生成的剪贴板 GIF 字节），自动保存绝不丢产物。
- 单帧 PNG 分支不变；`AutoSaveCopy` 关时仍早返回零副作用（SelfTest T101e 契约不破）。
- `自动保存副本` tooltip 同步：「录制按上面的『录制格式』存 MP4/WebM，未装 ffmpeg 则回退 GIF；视频后台编码、文件稍后出现」。

**验证**：
- 构建 0 错误；SelfTest 仍 **332**（`Shared=null` 早返回路径覆盖，视频分支受 `AutoSaveCopy` 保护不触发，无需新增单测）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0）
- 全链路 e2e：`AutoSaveCopy=true` + `RecordFormat=MP4` → 录 43 帧 → 焦点安全核前台=编辑器 → Enter(Commit) → trace `autosave mp4 …56782B 43f` → **ffprobe h264 / 640×400 / 43 帧 / 0.91s / 解码 exit=0 可播**
- 说明：显式「导出录制」(Ctrl+E) 与自动保存现在都跟随 `RecordFormat`，格式设置语义全链一致


## 2026-10-12 中午 修复奇数尺寸选区导出视频失败 R104

**发现**：边界正确性审计——libx264 / libvpx-vp9 的 `yuv420p` 要求宽高为**偶数**，而用户框选的区域完全可能是奇数（如 641×401）。实测 `ffmpeg -s 641x401 -c:v libx264 -pix_fmt yuv420p` 直接报 `width not divisible by 2 (641x401)` → 编码失败、产物为空。R101–R103 的视频导出对奇数选区是坏的（GIF 无此约束所以之前没暴露）。

**修复**（`Encoding/VideoEncoder.cs` `BuildArgs`）：仅当 `w` 或 `h` 为奇数时，插入一条向下取偶的 scale 滤镜 `-vf "scale=trunc(iw/2)*2:trunc(ih/2)*2"`（641→640、401→400，丢最右/最下一行像素，视觉无损）；偶数尺寸不加滤镜，常见路径零额外开销。

**验证**：
- 构建 0 错误；SelfTest **335**（新增 T127g 奇数含 scale / T127h 偶数不含 / **T127i 真实 ffmpeg 编码 65×49 奇数尺寸产物 >300B**——修复前该用例会因 libx264 报错而失败）
- 完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0）
- 独立 ffmpeg 实测：奇数 641×401 + scale → MP4/WebM 均 exit=0、输出 640×400
- 说明：这是「看细节 + 测边界」抓出的真实缺陷——用户随手框个奇数区域导 MP4 就会失败，现已覆盖


## 2026-10-12 中午 帮助窗同步录制格式/高刷文档 R105

**发现**：帮助窗（新用户主要发现面）文档落后于功能——Ctrl+E 仍写「保存 GIF」（实为 MP4/WebM/GIF 三选一），且完全没提高刷帧率、跟随刷新率、ffmpeg 依赖。用户读帮助会以为只能录 GIF。

**修复**（`Overlays/HelpWindow.cs`）：
- 「底部工具条」条目：`保存 GIF Ctrl+E` → `导出录制 Ctrl+E（MP4/WebM/GIF 三选一）`。
- 新增「录制帧率 / 高刷」条目：帧率滑杆 10–240 + 跟随显示器刷新率录满（160Hz→160fps）+ **诚实说明**实际帧率受内容变化率与系统捕获投递限制、静态画面不凭空产帧、真录满高刷需内容本身以刷新率变化。
- 新增「录制格式 MP4/WebM/GIF」条目：格式设置决定「导出录制」与「自动保存副本」默认容器；MP4(libx264)/WebM(vp9) 高帧率体积小需本机 FFmpeg（自动探测、未装提示安装、GIF 始终可用）；GIF 免依赖到处能贴但封顶 ~50fps 体积大；Enter 写剪贴板始终是 GIF。

**验证**：
- 构建 0 错误；完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0）
- 视觉取证：PrintWindow 帮助窗（960×1968）→ 两条新条目文字完整、换行正常、无豆腐块/溢出，与既有 KV 条目风格一致
- 说明：功能上线后同步发现面文档，避免「做了但用户不知道」；诚实标注高刷的真实边界，不给用户错误预期


## 2026-10-12 中午 托盘「打开保存文件夹」R107

**发现**：开启「自动保存副本」后，用户常想知道文件到底落到哪个目录——托盘菜单此前没有入口（QQ 截图有「打开文件夹」）。

**实现**（`Services/TrayService.cs`）：在「从剪贴板贴图」下方新增托盘项「打开保存文件夹」，仅当 `AutoSaveCopy` 开启时出现（与「编辑上次截图」按 `HasLastStill` 条件显示同风格，避免关自动保存时出现空目录入口）。点击 `Process.Start("explorer.exe", SaveService.ResolveFolder())`（`UseShellExecute=true`，路径带引号兼容空格），try/catch 兜底失败弹气泡「打开保存文件夹失败」，绝不抛。

**验证**：
- 构建 0 错误；完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0）
- 逻辑核对：菜单构建路径只读一个 bool + 加一项（无执行、无崩溃面）；Process.Start 在点击回调内且被 try/catch 包裹，运行时异常也只弹气泡
- 说明：托盘图标在本机被收纳进通知溢出区，UIA 按名定位 + 打开「显示隐藏的图标」浮窗后仍取不到该图标（浮窗内命名不稳定），继续盲点系统通知区有误触他应用风险，故此项以编译 + 护栏 + 代码审查为准，未做像素级取证（低风险纯增量项）


## 2026-10-12 下午 录制包含鼠标指针开关 R108

**动机**：`WgcLiveCapture` 里 `IsCursorCaptureEnabled` 硬编码 `true`——录出的画面永远带光标。做教程/演示的人常想要干净无光标的成片（后期自己加高亮），也有人希望保留光标。这是 OBS/Loom/Snagit 都有的**功能性开关**（非数值），契合「多加功能可选项、兼容所有受众」。

**实现**：
- `AppSettings.RecordIncludeCursor`（默认 `true`，保持既有手感、零回归）。
- `WgcLiveCapture.Start(monitor, region, fps, includeCursor = true)` 新增可选参数（默认值使 SelfTest 旧调用不破），`Init` 里 `_session.IsCursorCaptureEnabled = _includeCursor`（旧构建无此属性仍 try/catch 忽略）。
- `CaptureController` 起录时传 `_settings.Current.RecordIncludeCursor`。
- 设置窗「录制」组新增 ToggleRow「录制包含鼠标指针」，字形 `\uE8B0`（用 GlyphProbe sweep 渲染 E700–F8FF 全 PUA 网格、放大核对确认 E8B0 是清晰的鼠标指针箭头，符合「图标只用验证过的字形」）。
- 帮助窗新增「录制包含鼠标指针」条目。

**验证**：
- 构建 0 错误；SelfTest **336 PASS**（新增 T128a-cursor-default 锁死默认开=无回归）；完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0）
- 视觉取证：PrintWindow 设置窗裁「录制」组 → 新行「录制包含鼠标指针」+ E8B0 光标字形渲染正常、开关默认开、位置在「录制时显示范围边框」后、「产物」组前，与既有 ToggleRow 风格一致
- 说明：默认 true 保证老用户无感知；关掉后 WGC 会话不合成光标，成片干净——一个低成本、广受众的自定义维度


## 2026-10-12 下午 让「导出视频需要 FFmpeg」可操作 R109

**发现**：用户选了 MP4/WebM 但本机没装 ffmpeg 时，编辑器只弹「未找到 ffmpeg（装 FFmpeg 后可导视频）」——告诉要装却没说怎么装，非技术用户卡住。

**修复**（纯文案，无行为变化）：
- `EditorWindow.DispatchSave` HUD：改为「未找到 ffmpeg，无法导出视频 — 安装方法见帮助（按 F1）」，把死胡同变成有出口的指引。
- `HelpWindow` 新增「导出视频需要 FFmpeg」条目：说明软件不内置、运行时自动探测；给三条安装路径（`winget install Gyan.FFmpeg` / ffmpeg.org 下载加 PATH / 已装未进 PATH 补 PATH），并点明装好无需重启设置、下次导出即生效。

**验证**：构建 0 错误；完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0）。说明：ffmpeg 缺失路径需隐藏本机 ffmpeg 才能 e2e，风险高（可能弄坏用户环境），故以编译 + 护栏 + 文案审查为准；文案是字符串字面量，编译期即锁。


## 2026-10-12 下午 导出/另存对话框目录与格式一致性 R110

**发现**（看导出对话框截图抓到的两处不一致）：
1. `EditorWindow.NewSaveDialog` 与 `PinWindow.SaveAs` 都硬编码默认目录 `Pictures\FrameCapture`，**不尊重用户自定义 `AutoSavePath`**——自动保存存到用户自定义目录，手动「导出/另存」却总开在默认目录，两者割裂。
2. 导出对话框筛选器列表首项是 MP4 且未设 `FilterIndex`，导致「保存类型」下拉总停在 MP4，而默认文件名却按 RecordFormat 生成（Gif→capture.gif）——文件名 `.gif` 与类型 `MP4` 打架，误导用户（实际按扩展名存 GIF 结果没错，但下拉显示骗人）。

**修复**：
- 两处保存对话框默认目录统一改用 `SaveService.ResolveFolder()`（尊重自定义 AutoSavePath，非法/未设回退 `Pictures\FrameCapture`，静态且绝不抛异常）——手动导出与自动保存落在同一目录，自定义目录到处生效。
- `SaveRecording` 补 `dlg.FilterIndex = def switch { Mp4=>1, WebM=>2, _=>3 }`，类型下拉默认跟随「录制格式」，与默认文件名扩展一致。

**验证**：构建 0 错误；完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0）。焦点安全 e2e 取证：设 `AutoSavePath=C:\Users\16892\FCExportTest` → 录制→编辑器→Ctrl+E → PrintWindow 存盘对话框 → 面包屑显示 `NekoW › FCExportTest`（自定义目录生效）；RecordFormat=Gif 时底部裁图显示「保存类型：GIF 动图 (*.gif)」与文件名 `capture.gif` 一致（修复前是 MP4）。测试目录已删、AutoSavePath 已重置。


## 2026-10-12 下午 编辑器计数区时间与时间轴刻度基准统一 R111

**发现**（放大编辑器底部计数区截图抓到）：录制载入后计数区显示「1/12 **0.09s**」，而时间轴刻度首格是 0.00s、刻度按 `帧号/帧率` 走。两套时间基准——计数区用 `_times[_index]`（帧的实际捕获时间戳，首帧因 WGC 起录后延迟约 90ms 交付而 =0.09s），刻度用 `i/fps`（播放基准，首帧=0）。用户看到刻度从 0 起、计数却从 0.09s 起且对不上任何刻度，误以为帧 1 前有内容。

**修复**：编辑器本就按设置帧率播放（`_playTimer=1000/fps`），计数区应与刻度/播放一致。`Show()` 里计数区时间改用 `_index / fps`（fps 同刻度做 Clamp），删去不再使用的 `tMs`。`_times` 字段仍保留（PNG 另存默认名 `frame-{…}.png` 仍用实际时间戳，语义正确）。

**验证**：构建 0 错误；完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0）。焦点安全 e2e 取证：录制→编辑器→PrintWindow 裁计数区放大 → 帧 1 现显示「1/11 **0.00s**」（修复前 0.09s），与刻度起点一致。说明：纯显示层改动，不改 `_times`/导出时序；GIF/视频导出仍按实际帧间隔（`frames[i].TimeMs` 差值），不受影响。


## 2026-10-12 下午 设置「录制格式」行加 ffmpeg 检测即时状态 R112

**动机**（TODO 文档后续①）：ffmpeg 是否装好，原来只藏在 MP4/WebM 芯片的 tooltip 里，用户选格式时看不到——正是 R109 那类「选了 MP4 导出才发现没 ffmpeg」困惑的源头。让检测状态在选格式时一眼可见。

**改动**：`SettingsWindow.RecordFormatRow()` 末尾把单行 Grid 包进 StackPanel，下方加一行状态：绘制的小圆点（`System.Windows.Shapes.Ellipse`，非字形，规避未验证字形规则）+ 文案。绿点 `已检测到 ffmpeg · 可导出 MP4 / WebM`；琥珀点 `未检测到 ffmpeg · 导出 MP4/WebM 需先安装（见帮助 F1），GIF 不受影响`。`hasFf` 复用行内已算好的 `FfmpegLocator.Find()`，只读、开设置时算一次，无行为改动。

**验证**：构建 0 错误；完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0）。焦点安全 e2e 取证：开设置→PrintWindow 裁录制格式区 → 本机装有 ffmpeg，格式行下方正确显示绿点 +「已检测到 ffmpeg · 可导出 MP4 / WebM」，缩进对齐标签。未检测到分支为对称逻辑（仅文案+琥珀色不同），本机无法在不破坏 ffmpeg 安装的前提下触发，按对称代码审查确认。


## 2026-10-12 下午 Shift 约束可发现性：工具 tooltip 补提示 R113

**发现**（查 TODO line 321① 陈旧项时顺带审出）：Shift 约束绘制（矩形→正方、圆→正圆、线/箭头→45°）R99 已实现，且 `ToolCatalog.Help`（帮助窗）每条都写了「按住 Shift 拖=…」——但工具按钮的 **tooltip 用的是短 `Tip` 字段**（「矩形 (R)」），不含 Shift。tooltip 是用户悬停时最常看到的提示面，比翻帮助窗高频得多；结果这个防手抖的实用约束在工具条上完全隐身。

**改动**：`ToolCatalog` 四个形状工具的 `Tip` 补上约束提示——`直线 (L) · Shift 锁 45°` / `矩形 (R) · Shift 正方` / `圆圈 (O) · Shift 正圆` / `箭头 (A) · Shift 锁 45°`。单一数据源，工具条 tooltip 与帮助窗同源不漂移。顺带校正 TODO line 321①（实现+帮助早已完成，标 done）。

**验证**：构建 0 错误；SelfTest **336 PASS / 0 FAIL**（T92d-tips 校验 `Tip.Contains(Key)` 仍通过，加后缀不破坏）；完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0）。tooltip 文本经 `IconBtn(Glyph, Tip)` 直接数据绑定（EditorWindow.cs:388），无布局逻辑，内容确定，故未做 tooltip 截图取证（截图纪律主要抓布局/视觉 bug，纯文本绑定无此风险）。


## 2026-10-12 下午 编辑器计数区补「总时长」R114

**动机**（TODO line 369②）：R111 把计数区时间归零后，只显示「当前帧时刻」，用户看不到这段录制到底多长（时间轴刻度只标到最近的整格，末帧确切时刻不显）。补成媒体播放器惯例的「当前 / 总」。

**改动**：`Show()` 计数区文本 `… {当前}s` → `… {当前}s / {总}s`，总时长 = `(帧数-1)/fps`（末帧时刻，与 R111 的 `_index/fps` 计数基准、时间轴刻度同基准；拖到末帧时当前=总）。仅多帧编辑器有此面板（单帧无播放条，本就不显示），正是需要处。

**验证**：构建 0 错误；完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0）。焦点安全 e2e 取证：录制→编辑器→PrintWindow 裁计数区放大 → 显示「1/14  **0.00s / 0.43s**」（13/30≈0.43s 正确），当前/总一眼可辨。纯显示层，不改导出时序。


## 2026-10-12 下午 设置「保存位置」措辞校正：讲清双重用途 R115

**发现**（整窗通看设置审计）：`PathRow`「保存位置」的三处文案（图标 tooltip、行 tooltip、`FolderBrowserDialog` 描述）都写死「自动保存副本的文件夹」。但 R110 起 `SaveService.ResolveFolder()` 恒读 `AutoSavePath`（与自动保存开关无关），编辑器/贴图「另存为」对话框也默认打开这个目录。自动保存关闭时，用户改「保存位置」实际改的是导出默认目录，对话框却只说「自动保存副本」——误导。

**改动**：三处文案统一为「保存文件夹：自动保存副本存到这里，编辑器/贴图『另存为』对话框也默认打开这里」（图标 tooltip）/「点击选择保存文件夹（自动保存副本 + 导出对话框默认目录）」（行 tooltip）/「选择保存文件夹（自动保存副本 + 导出对话框默认目录）」（对话框描述）。纯文案，无逻辑改动。

**验证**：构建 0 错误；完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0）。tooltip/对话框描述为直接文本，无布局逻辑，内容确定，故未截图取证（同 R113 理由）。


## 2026-10-12 下午 「截图后」新增「贴图」选项 R117（Snipaste 式工作流）

**动机**（用户「多加功能可选项、扩大受众」）：Snipaste 用户的核心工作流是「截图→直接钉屏对照」，本 app 的「截图后」只有 弹提示/进编辑器/只复制 三态，缺「截图即贴图」。补第 4 态「贴图」。

**可行性核查**：AfterCapture 三处分发（静图 CaptureController:225 / 滚动:322 / 延时:394）**全是单帧**（录制多帧走编辑器不经 AfterCapture），故「贴图」无多帧边界问题；三处均在 DispatcherTimer.Tick（UI 线程），`PinWindow.Show()` 可直接调。

**改动**：
- `AfterCapture.cs`：枚举加 `Pin`，`Resolve` 加 `"pin" => Pin`（脏值仍回落 Toast）。
- `AppSettings.cs:165`：校验放行 `"pin"`。
- `CaptureController.cs`：新增 `PinFrameAt(f, reg)`（`new PinWindow(f, reg).Show()`，钉在捕获区域原位）；三处分发各加 `case Pin: PinFrameAt(...)`。
- `SettingsWindow.cs`：`AfterCaptureRow` opts 加第 4 项 `("pin","贴图",…)`（数据驱动，选中态逻辑通用）；图标 tooltip 补「/ 贴图」。
- `HelpWindow.cs`：「截图后」KV 补贴图说明（注明仅单帧生效、录制仍进编辑器）。

**验证**：构建 0 错误；SelfTest **337 PASS / 0 FAIL**（新增 T116e-pin 断言 `Resolve("pin")==Pin`，只增不减）；完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0）。焦点安全 e2e 取证：设 AfterCapture=pin + 存储区域 (1200,700,900×650) → `--delay` 倒计时后截该区 → EnumWindows 见「FrameCapture Pin @ 1200,700 903×653」原地钉屏，CopyFromScreen 裁该区确认显示捕获画面；设置窗 PrintWindow 裁「截图后」行确认 4 芯片（弹提示/进编辑器/只复制/**贴图**选中）无溢出。


## 2026-10-12 下午 贴图不抢焦点（Snipaste 式手感）R118

**动机**（用户「测测手感、哪里受阻」）：R117 的「截图后=贴图」沿用 `PinWindow.Loaded` 里的 `Activate()`，贴图显示即抢焦点——与 toast（`ShowActivated=false` 不抢焦点）不一致，且打断「钉住参考后继续原应用操作」这一贴图核心意图。

**改动**（`PinWindow.cs` + `CaptureController.cs`）：
- `PinWindow` 构造加 `bool activateOnShow = true`：`ShowActivated = activateOnShow`（false 时 Show 不激活）+ `Loaded` 里 `if (_activateOnShow) Activate()`。默认 true → 编辑器 Ctrl+P / 剪贴板贴图行为**逐字不变**（ShowActivated=true + Activate()）。
- `PinFrameAt`（AfterCapture=pin 专用）传 `activateOnShow: false`：贴图静默出现不抢焦点；WPF 客户区点击仍发 WM_MOUSEACTIVATE 激活（未设 WS_EX_NOACTIVATE），故点击贴图后 Esc/R/Ctrl+S 等 `KeyDown` 快捷键照常可用。

**验证**：构建 0 错误；完整护栏 GUARDRAIL PASS（FAIL=0 SKIP=1 floor280，err_delta=0）。焦点安全 e2e 双段取证：AfterCapture=pin + 区域 (1200,700,900×650) → `--delay` 出贴图后，前台 = DSH GUI（**未抢焦点**）；核验贴图矩形后点其中心（Topmost 保证输入落到贴图），前台变 `FrameCapture Pin`（**点击激活成功**），再按 Esc 贴图关闭（快捷键确可用）。默认贴图路径未动，行为不变。


## 2026-10-12 下午 收尾审计与决策（R118 后，无代码改动）

临近 15:00 用户返回，不再塞新代码轮（回归风险 > 收益）。做只读审计 + 状态固化，全部**确认无 bug**：

- **光标行为**：静图走 `LiveCapture.Grab` 的 GDI BitBlt，抓帧缓冲不含硬件光标（与 QQ/Snipaste 静图一致）；录制走 WGC 按 `RecordIncludeCursor` 含光标。分工正确。
- **导出反馈**：编辑器导出（Ctrl+E）有 HUD 进度「导出 MP4… n/N」+ 完成「已导出 xxx」+ Esc 取消（T82/T96）。仅「自动保存视频」后台编码无进度——默认 `AutoSaveCopy=false` 不影响普通用户，已在 TODO 记为候选。
- **设置持久化健壮性**：`AppSettings.Load` try/catch——损坏 JSON 直接删文件回退默认（不崩）；`Normalize()` 加载时清洗非法值（如脏 AfterCapture 回落 toast）；`Save` 用临时文件 + `File.Move` 原子写 + 失败静默。三重健壮，无隐患。
- **浅色主题**：设置窗 PrintWindow 取证，浅底深字对比清晰、开关/芯片/滑块可读、图标可见，无对比度问题。
- **首启欢迎卡**：读 `WelcomeToast` 代码确认设计到位（点破两热键 + 手感提示 + 帮助入口、不抢焦点、ApplicationIdle 弹、看过一次不打扰）。

**关键决策——热键重置回出厂默认**：settings.json 里热键此前是测试约定 F6/F7，收尾时重置为 `Ctrl+Shift+G`（截图）/ `Ctrl+Shift+R`（选区）。理由：F6/F7 非用户选择、与 README 写的 Ctrl+Shift+G 矛盾、且撞浏览器地址栏(F6)/光标浏览(F7)；其余字段全默认说明用户未自定义过，默认值最可能是原值。已实测 Ctrl+Shift+G 正常唤起选区。**下一轮勿再把热键改回 F6/F7**（测试需要时临时改、批次末务必还原）。

**最终交付态**：Release 运行（托盘态、0 可见窗）、settings.json 全出厂默认、git clean（HEAD=e950219）、SelfTest 337 PASS、护栏 PASS。

## 夜间批次 R120 · A 修 bug（2026-10-13 通宵，用户离线至 10:00）
### A1 录制后内存不释放（工作集虚高）
- **定性**：承接「内存工作集观察」——只读审查确认 spool 磁盘惰性回读无常驻缓存、编辑器 `_bmpCache` 有 FIFO 上限（自适应封顶 ~600MB）、`_mosaicCache` >24 清空、ffmpeg 在 `finally` dispose/kill、编辑器关闭 `ClosedByUser` 释放 spool + 置空 editor。**无硬泄漏**，根因是 GC 惰性 + .NET 不把空闲页交还 OS（用户手动 MemReduct 即 `SetProcessWorkingSetSize(-1,-1)` 才回落）。
- **修法**：新增 `Core/Memory.Reclaim()`（全代 GC → 等终结器 → 再 GC → `Win32.TrimWorkingSet()`）。在**编辑器关闭**（ApplicationIdle 优先级，等窗口拆完）与**后台视频编码完成**（`SaveService` 续跑，闭包持有的整段烘焙帧此刻已释放）两处主动回收。T129a 护栏。SelfTest 339→340。
### A2 滚动截图裁切/重叠
- **根因**：旧 `FindOverlap` 只比"对应行"，平滑/分数滚动的 ±1px 取整抖动使重叠行差超阈 → 误判小重叠/0 → 拼接出现重复内容带（重叠）。
- **修法**：重写为**两趟匹配**——① 严格趟（只比对应行，命中即返回，整像素滚动精确不多不少一行）；② 容差趟（严格趟全灭才启用，每行允许 acc ±1 行窗口匹配，吸收 ±1px 抖动；±1 窗口把高估上界锁在 1 行、肉眼不可见）。抽样 12→32 行、阈值 75%→80%。T119f 抖动回归护栏（旧判 0、新判 40）。
### A3 贴图磁边"依然无效"
- **诊断**：trace 有 `pin-snap (1596,843)→(1609,843)`——**触发过但只挪 13px、仅松手瞬间、过程零反馈** → 用户感知"无效"（非死 bug，是 UX）。
- **修法**：`DragMove` 改**手动拖拽 + 实时磁吸**——按住即 `CaptureMouse`，`MouseMove` 用光标物理坐标换算 DIP 实时算 `SnapTarget` 并跟随（靠近屏幕边/同屏其他贴图边·中线当场"吸附上去"，过程可感），`MouseLeftButtonUp` 收尾兜底。抽出 `SnapTarget(left,top)` 供拖拽实时与松手收尾共用。
- **待实机验证**：A1 工作集回落、A2 真实滚动拼接、A3 拖拽实时吸附——均需 e2e，留待确认用户离开后做（输入模拟规则：先校验前台为本窗再操作）。

## 夜间批次 R120 · B1 GestureCore 可复用引擎（2026-10-13）
用户要求「把所有功能触发方式拆分、允许分离/组合键绑定，且此模块单独做、其它软件复用」。落地为独立类库 `src/GestureCore`（netstandard2.0、零 UI/OS 依赖、刻意不引 NuGet 含 System.Text.Json——序列化交宿主）。
- **模型**：`ModifierKeys`(自持 Flags)、`KeyChord`(修饰键+不透明 int 主键，Key==0 为纯修饰键)、`TriggerKind`(KeyDown/KeyUp/Click/DoubleClick/TripleClick/LongPress/Chord)、`FallbackPolicy`(Stop/SkipStep/**NextBinding**——逐条绑定显式指定，引擎不内置宿主语义)、`GestureBinding`(组合键+触发+动作序列+失败策略+优先级+可配 DoubleClickMs/LongPressMs)、`IActionInvoker`(宿主按名执行、返回成败)、`IKeyNamer`(键名映射，保持库中立)。
- **引擎** `GestureEngine`：喂原始键/鼠事件(带毫秒时间戳)+绑定表→消歧→回调动作序列。键用不透明 int、修饰键态随事件传入(贴合 WH_KEYBOARD_LL 取数)、时间由宿主 `Tick(now)` 推进→**确定性可单测、跨宿主复用**。消歧：组合键激活瞬间触发 KeyDown/Chord、失活触发 KeyUp(配对=按住做A松手做B)；激活期鼠标按已绑最高阶触发分类，连击达最高阶立即触发否则等 DoubleClickMs；只绑 Click 则零延迟；长按满阈值触发并吞掉抬起。同触发多绑定按优先级顺延，某步失败按该条 Fallback 决定顺延/跳过/中止。
- **单测** T130a–j（SelfTest 340→351）：按住配对、单击零延迟、单击等待 vs 双击立即、长按吞抬起、NextBinding 顺延、SkipStep 跳步、Stop 中止、Ctrl+Alt+A 修饰键组合、三击、优先级。

### B2-1 GestureConfig 配置层（FrameCapture 侧，headless 可测）
- **动作词汇表** `GestureActions`：11 个稳定动作名(capture.frame/scope.select/record.start/record.stop/scroll.capture/capture.delayed/edit.last/ocr.selection/pin.selection/settings.open/help.open) + 中文文案，名与文案分离(改名会破坏已存 JSON)。
- **DTO** `GestureBindingDto`：组合键用 "Ctrl+Shift+G" 串(复用 HotkeySpec 解析含键名映射，不重造键表)、触发/失败策略用字符串枚举名；`ToBinding()` 非法组合键/触发名→null(加载跳过不整体失败)、未知动作名过滤、双击/长按阈值 clamp；`FromBinding()` 回推组合键串。
- **GestureConfig**：`Enabled`(默认 false，附加式零回归) + Bindings；`DefaultBindings`/`PresetQQ`/`PresetHoldToRecord` 预设(由旧 CaptureHotkey/ScopeHotkey 迁移生成)。挂到 `AppSettings.Gestures`(Normalize 兜 null)。
- **GestureCore 引用**：FrameCapture.csproj + SelfTest.csproj 各加 ProjectReference。
- **单测** T131a–g（SelfTest 351→358）：DTO→引擎转换、裸键拒绝、默认/预设、按住即录 JSON 往返、未知动作过滤、引擎→组合键串回推。

### B2-2 GestureService 运行时接入（附加式，默认关零回归）
- **共用键盘钩子**：HotkeyManager 加 `KeySink`，HandleKey 顶部（早于旧判定/吞键）把每个键事件喂给手势引擎——不额外挂键盘钩子、不重复处理；Paused 时不喂（用户要求不拦截键）。手势关闭时 KeySink=null，旧路径零开销零改变。
- **鼠标钩子**：GestureService 自装 WH_MOUSE_LL（仅当存在鼠标触发绑定时），只观察不吞鼠标事件；DispatcherTimer 40ms 泵 `Tick(TickCount64)` 做点击消歧/长按判定。Win32 补 WH_MOUSE_LL/MSLLHOOKSTRUCT/鼠标消息/委托/SetWindowsHookExW 重载。
- **GestureInvoker**：动作名→AppHost/CaptureController 真实操作，一律 BeginInvoke 投递（钩子回调不跑重活）。录制类做同步前置检查（开始录制须空闲、结束录制须在录）返回成败→让「失败自动下一个」运行期真正生效。单帧走托盘同款 BeginCapture+600ms→EndCapture 可靠路径。
- **CaptureController**：从 BeginCapture 抽出 `BeginCaptureCore()`，加公开 `StartRecording()`/`StopRecording()`（跳过点按/双击状态机，供手势录制）。
- **AppHost.InstallHotkeys**：先 Apply 手势（填绑定表+挂钩子），再据 `ClaimsKey`（键触发占用）让被占用的旧热键让位（避免同键重复触发）；未占用/关闭时旧热键原样装。
- **默认态**：Enabled=false → 不装鼠标钩子、KeySink 空、不泵 Tick、旧热键原样——对现有行为零影响。开启经 settings.json（编辑器 UI 下轮）。
- 待实机 e2e：开启手势→按绑定键→验证动作触发 + 被占用热键让位（留待确认用户离开后做，含 settings.json 还原）。

## 夜间批次 R120 · C 自定义截图后悬浮窗存在时间（2026-10-13）
- 新增 `AppSettings.ToastSeconds`（默认 3.2，0=常驻不自动消失，Normalize：0 保留/负→0/超界夹 0.5..60）。
- CaptureToast 读该值设 `_dismiss` 间隔；0→不启计时器（常驻，点一下或 Esc 才关）；悬停暂停、移开重数满时长逻辑保留。
- 设置窗新增 `ToastRow()`（芯片：常驻/2/3.2/5/8 秒，就地更新选中态，风格同 DelayRow），置于「截图后」行下方。
- 单测 T132a–b（SelfTest 358→360）：默认值 + 归一化边界。

## 夜间批次 R120 · D3/D4 贴图动画 + 右键菜单/不透明度（2026-10-13）
- **D4 不透明度真 bug（L69/L72「无效果」根因）**：Ctrl+滚轮/菜单设不透明度前 `BeginAnimation(OpacityProperty,null)` 清淡入动画后，`Opacity` 读回**基础值 0**（淡入是动画值 1）→ `PinOpacity.Next(0,±)` 一律夹到 0.2：首滚突跳 0.2、再向下滚恒 0.2「无效果」。R119 清了动画却没先提交可见值。加 `CommitOpacity()`（先读有效值→清动画→落基础值，同 Fluent.PopupEnter 模式），Ctrl+滚轮与菜单预设均改用它。
- **D4 右键菜单样式（L72「很丑」）**：Pin 的 ContextMenu 原用裸 `new ContextMenu()`（系统默认样式）→ 改套共享 `Fluent.MenuResources()`（与托盘同款 Fluent 圆角卡片/悬停/勾选）。
- **D4 子菜单 Popup 修复**：`Fluent.MenuResources()` 的 MenuItem 模板**缺 PART_Popup**（托盘菜单无子菜单故从未暴露）→ Pin「不透明度」子菜单根本展不开。模板改为 Grid 包裹 Border+`Popup x:Name=PART_Popup`（MenuItem 自动接管其 IsOpen，无需绑定；内插原始字符串里字面 `{` 需更多 `$`，故不用 Binding）+ 加 `Role=SubmenuHeader` 触发器显示右侧箭头（E76C）。
- **D3 Pin 入场动画（L68）**：淡入 + 卡片 0.82→1.0 BackEase 轻微过冲「落图」（220ms），构造期即 Opacity=0 防首帧闪缩，ContentRendered 后 FadeIn+PlayDrop 同步。
- 待实机 e2e：菜单外观/子菜单展开/不透明度滚轮/入场动画（留待确认用户离开后做）。

## 夜间批次 R120 · D1 取色器 UI 打磨（2026-10-13）
- **L52「自定义取色器 UI 太丑」根因**：SV/色相/透明条是生硬方块、HEX/RGBA 是默认白底系统边框 TextBox，在深色弹层里极突兀。
- 打磨：① 输入字段主题化——`Field()` 深色圆角底(TrackBg)+描边、聚焦转强调色，TextBox 改透明无边框+主题前景/插入符；② SV/色相/透明条 `Round()` 圆角+细描边（Border 按 CornerRadius 裁剪）；③ 标签改 TextDim 更克制、间距规整。
- **离屏目检**：`SelfTest renderpick <png>` 渲染取色器到 PNG（无断言取证通道），读图确认圆角/深色字段/主题统一——无需实机、零打扰。

## 夜间批次 R120 · D2 OCR 预览优先（2026-10-13）
- **L63「不要复制，而是悬停在预览上供选取文本」**：已有 OcrResultWindow（可编辑/部分选中复制/复制全部/字数）但默认关 + 总是先自动复制。
- 改为**预览优先**：`OcrShowResult` 默认翻为 true；开结果窗时**不再自动写剪贴板**（预览即选取入口，要哪段选哪段 / 点复制全部），关结果窗才走快速路径（直接复制 + 轻提示）。编辑器 OcrCurrentFrame 与贴图 OcrFrame 两处同步。
- 同步更新设置项描述 + 帮助文案（预览优先语义）。Feedback 措辞不变（快速路径仍「已识别 N 字 · 已复制」）。
- 单测 T133（SelfTest 360→361）：锁定结果窗默认开。

## 夜间批次 R120 · 实机 e2e 批次（2026-10-13）
### 已完成的离屏取证（零打扰、可复现）
- **A2 滚动拼接**：`SelfTest stitch` 喂「已知重叠=50」的合成帧（逐行去相关灰度基，避免等差回绕假匹配）→ `overlap=50` 精确命中、merged=350、逐行比对 350/350 全对、接缝无痕（无裁切无重叠）。±1 抖动趟由 T119f 覆盖。
- **D4 菜单 XAML**：T12 运行时 `XamlReader.Parse(MenuResources())` 通过（畸形 XAML 会抛→T12 挂），护栏 361 绿=菜单模板结构有效。
- **B2/C 启动集成**：`fc-trace.log` 显示最新 Debug 实例 `Startup done`+`sink created` 无异常；无 gesture 行=默认关零足迹（正确）。
- **A3 磁吸数学**：T110/T113 覆盖 PinSnap.Snap/SnapToLines/Threshold；A3 修复是接线（MouseMove 实时调 SnapTarget），已代码审查。

### 实机输入模拟：本环境硬性阻断（非代码问题）
- 用 `--pin-clip` 单实例转发钩子成功建贴图（落点=光标，rect 精确）；`SendInput` 注入返回 2（成功）。
- 但 **DSH GUI 浏览器窗口 rect=(-11,-11)-(3851,2099) 全屏覆盖且 topmost 在我 Topmost 贴图之上**：`WindowFromPoint` 任意屏幕点均命中 DSH GUI；`SetWindowPos(HWND_TOPMOST)` 亦无法把贴图提到其上。→ 注入点击无处落到被测窗口，贴图拖拽/右键菜单/全局手势的实机验证在本会话不可行。
- 结论：A3 拖拽实时磁吸、D4 子菜单展开、B2 手势运行时的**实机确认**留待用户 10:00 亲自试用（其本就计划自测）；代码侧已尽数离屏验证 + 单测 + 代码审查。

## R120 B2 真 bug 修复（用户回来实测时发现）
**现象**：给 settings.json 写入手势配置（F6 按住即录 / F7 选区，占用全部热键）后，手势不触发。
**根因**：`HotkeyManager.Install` 第 66 行 `if (scope==null && capture==null) return;`——手势占用全部热键→旧热键被清空→`Install("","")` 早退→**WH_KEYBOARD_LL 钩子根本没装**。而手势 KeySink 正是从该钩子喂数据（HandleKey 第 140 行），故手势全哑。附带：滚动截图 Esc 全局取消、浮窗 Enter/Esc 也依赖此钩子，同样会失效。
**修复**：`if (scope==null && capture==null && KeySink==null) return;`——只要任一消费者（旧热键 / 手势 KeySink）存在就装钩子。AppHost 里 `Gestures.Apply` 已先于 `Install`，KeySink 先设好，顺序正确。
**验证**：SelfTest T134a（有 KeySink 无热键→装钩子）/ T134b（无 KeySink 无热键→不装，零开销），护栏 361→**363**。实机：新实例 `Install scope= capture= hook=<非0>` 证实钩子已装。

## R120 清理：删除死设置 MaxDurationMs（用户实测「不要录制上限」引出）
- 我上一条消息误报「录制上限 6 秒」——其实 **R61 早已移除录制时长上限**（CaptureController:558 / FrameSpool:7 / SettingsWindow:156 / HelpWindow:122 均写明「录到松手为止，不设时长上限」，护栏是磁盘 DiskLow 自动停）。`MaxDurationMs` 是 R61 后遗留的死设置（AppSettings 注释「已废弃」，运行期从无消费方）。
- 用户要「不要录制上限」→ 行为本已满足；把死设置**彻底删除**（属性 + Normalize 行），并清掉 settings.json 残留键，杜绝再误导。旧 settings.json 残留该键反序列化被忽略，无害。
- 护栏 363 绿、新实例启动正常、手势钩子仍装。

## R120 B2 扩展：QQ 风味「选区截图 / 选区录制」动作（用户实测需求）
- 用户要 `Ctrl+Alt+A`=选区截图、`Ctrl+Alt+R`=选区录制。手势顺序动作是同步的，`[scope.select, record.start]` 会在选完前就起录——需「选区完成后」再动作的专用入口。
- 新增动作 `capture.selection`（选区截图，复用现成 `_captureAfterSelect`）+ `record.selection`（选区录制，新增 `_recordAfterSelect`：选区存好后延迟 220ms 让选取层退场，再 `BeginCaptureCore` 对刚选区域起录）。
- CaptureController 加 `BeginCaptureSelection()` / `BeginRecordSelection()`（仿 `BeginScrollCapture` 的「选区→X」模式）；GestureInvoker 接两动作。
- Ctrl+Alt+A/R 是 Chord，不占用 F6/F7 → 旧热键照常并存。T135a/b/c（动作注册 + Ctrl+Alt+A/R 解析成 Chord），护栏 363→**366**。

## R120 B2 用户实测两处修正（录制停不掉 + 截图要即时）
- **Bug：选区录制起来后无法结束**。`record.selection` 起录后 Ctrl+Alt+R 只会重开选区，没有停止入口。→ 新增 `record.toggle`（同键起停：空闲→选区起录；录制中→`FinishCapture` 进编辑器），Ctrl+Alt+R 改绑它。QQ 式一键起停。
- **截图不干净利落**：`capture.frame` 原走 `BeginCapture()`+**600ms 定时器**+`EndCapture()`——600ms 足够游戏丢焦点暂停，截到的是暂停画面。→ 新增 `CaptureInstantStill()`：`LiveCapture.Grab` 直接抓当下这一帧（GDI BitBlt，抓取前不弹任何窗、不抢焦点）+ `DispatchStill`。`capture.frame` 改走它。Ctrl+Alt+A 改绑 `capture.frame`（即时定格，游戏友好）。
- T136a/b/c（record.toggle 注册/解析 + capture.frame 仍在），护栏 366→**369**。
- 待办：独占吞键（用户确认后续要做）——手势命中时钩子同步吞键，防 Ctrl+Alt+A/R 漏给前台。

## R120 B2 选区窗延迟/闪烁（用户实测「选区窗很大延迟 + 几个半透明窗闪几下」）
- 埋点实测（4K@150%）：放大镜 4K 抓取 74ms + 构造其余 76ms + `Show()`/首帧渲染 ~236ms = 键按下→可见 ~400ms。大头是 WPF 4K 全屏分层窗首帧渲染。
- **闪烁修复**：进选区前 `CaptureToast.DismissCurrent()` + `HideOutline()`——之前上一次截图的浮窗/轮廓（半透明）会和选区窗叠着闪几下。
- **延迟优化**：选区窗设 `WDA_EXCLUDEFROMCAPTURE`（本就不该入镜），放大镜 4K 快照改 Show 之后**异步抓**（遮罩已渲染也拍不到本窗，颜色仍真）→ 把 74ms 挪出打开关键路径 + 去掉 Show 前的 33MB 分配压力。放大镜 UI 先建好，`_snap` 到位后 UpdateLoupe 自动显示。
- Ctrl+Alt+A 改回 `capture.selection`（用户要选区截图）。埋点日志保留（picker ctor/Show/first frame/snapshot ready）。
- 待议：剩余 ~300ms 是 WPF 4K 全屏分层窗渲染地板；要近零延迟需改「冻结屏」不透明窗设计（抓一次屏当背景，非分层），较大改动，待用户拍板。

## R120 B2 选区窗「边框闪几下」根因：几何在 Loaded 才赋值
- 选区窗 `Left/Top/Width/Height` 原本在 **Loaded** 才设 → `Show()` 时窗口还是 WPF 默认尺寸/位置，Loaded 才跳到全屏 → 那一跳（叠加 Loaded DPI 校正 / ReassertTop）就是「窗口边框闪几下」。
- 修法：构造里用 `GetDpiForMonitor(DPI_EFFECTIVE)` 在 **Show 之前**就把几何定成全屏（不依赖可视树）；Loaded 仍按可视树 DPI 兜底校正（正常两者一致 → 无二次跳）。选区窗 FadeIn 未用 offscreenFirstFrame，历史「Park 读 NaN」坑不适用。
- 复测延迟：构造 81ms（放大镜 74ms 已异步挪走）、Show 331ms、首帧 344ms。剩余 ~250ms 是 WPF 4K 全屏分层窗 Show/渲染地板——要再快需「冻结屏」不透明窗重设计（待用户拍板）。

## R120 B2 截图延迟：选区完成后 220ms 定时器 → 40ms
- 选区截图/录制/滚动三处「等选区窗退场」的 DispatcherTimer 原为 220ms，明显拖慢截图手感。
- 选区窗现已 `WDA_EXCLUDEFROMCAPTURE`，抓取本就拍不到它 → 220ms 的理由（怕把选区界面截进画面）不再成立。
- 统一改为命名常量 `PickerExitMs = 40`：只留极短一拍让选区窗 `Close()` 走完（防 WDA 万一失效时把选区界面截进去）。
- 实测「选区完成→存图」220ms+ → **113ms**（40ms 留白 + 1441×886 抓取/PNG/剪贴板 ~73ms）。用户确认「手感不错」。

## R121 独占吞键（用户拍板「后续肯定要做」→ 现做）
- 问题：手势层此前只"观察"按键（`CallNextHookEx` 放行），Ctrl+Alt+A/R 会漏给前台程序（输入框打字符 / 触发别的软件快捷键）。
- 方案（QQ 式全局独占）：
  - `GestureEngine.OnKeyDown/OnKeyUp` 改为返回 bool——命中「带具体键的组合键并成功触发」才回报吞键；`FireTrigger` 返回是否触发。
  - 按下被吞的键，其**抬起也吞**（`_swallowKeyUp`），避免前台看到"只抬未按"的畸形键态。
  - **只吞带具体键的组合键**（Ctrl+Alt+**A**）；纯修饰键组合（Key==0，如 Ctrl+Alt 本身）永不吞，防劫持 Ctrl+C。未绑定的键不吞。
  - 新设置 `GestureConfig.Exclusive`（默认 true）；`GestureService.FeedKey` 返回 `Exclusive && 引擎候选`。`HotkeyManager.KeySink` 由 `Action` 改 `Func<...,bool>`，命中即 `return true` 吞键。
  - 吞键判定放在滚动 Esc / 浮窗 Enter/Esc 之后（会话级优先），且手势只绑具体键不会误吞 Esc/Enter。
- SelfTest T137a-d（地板 369→373）：A 命中才吞 / 抬起也吞 / 纯修饰键不吞 / 未绑定键不吞。

## R122 手势配置器（方案 C：独立可复用 UI 库 + 内嵌宿主）
- 用户拍板方案 C：编辑器做成**独立库**（只依赖 GestureCore + WPF，不引用 FrameCapture），宿主经四个注入点接入；将来别的软件换一套注入即可复用，或套独立壳成配置器软件。
- 新工程 `src/GestureEditor/`（net8.0-windows WPF，仅引用 GestureCore）：
  - `GestureModel.cs`：宿主无关可编辑模型 `GestureDoc`/`BindingEdit` + 四接口 `IActionCatalog`/`IGestureConfigStore`/`IKeyNamer`(GestureCore)/`EditorPalette`（配色由宿主注入，保证与宿主视觉一致）。
  - `GestureValidation.cs`：纯逻辑校验（组合键非空、动作非空且合法、时间范围；同「组合键+触发」重复仅 warn）。
  - `GestureEditorWindow.cs`：主从布局（左列表 / 右详情：组合键捕获、触发方式、动作序列增删排序、失败策略、优先级、双击/长按阈值）+ 实时校验 + 保存。组合键捕获期间触发 `ChordCaptureBegan/Ended` 事件让宿主**暂停/恢复全局钩子**（否则要捕获的组合键被自家钩子吞掉、编辑器收不到）。图标字体链与 Fluent 同款；字形经 GlyphProbe `glyphs` 模式验证（E70E/E70D/E74D/E711/E713 等）。
- FrameCapture 侧 `Services/GestureEditorHost.cs`：四个适配器（`FrameActionCatalog` 包 GestureActions、`FrameGestureConfigStore` 走 settings.json gestures 段↔GestureDoc、`FrameKeyNamer` VK→WinFormsKeys 名、`BuildPalette` 从 Theme 取色）+ `ShowOrFocus`（捕获时 `HotkeyManager.Paused` 置位/还原）。所有 FrameCapture 耦合都收在这一个文件。
- 接线：新动作 `gesture.edit`（GestureActions + GestureInvoker）；设置窗「快捷键」页加「打开手势配置器…」入口按钮。
- GlyphProbe 加 `glyphs` 模式（传十六进制码点渲染小图、缺字形标 MISSING），便于日后验图标。
- SelfTest T138a-f（编辑器模型/校验，宿主无关可脱 UI 单测），护栏地板 373→**379**。

## R123 标注编辑器三修（用户实测编辑器反馈）
- **C1 四角拖动改大小无效**：`TryBeginResize` 设了所有 resize 字段 + `CaptureMouse`，唯独漏 `_resizeDrag=true`；`Canvas_Move` 的 resize 分支以 `_resizeDrag` 为门控 → `ResizeTo` 从不被调。补 `_resizeDrag=true`。
- **C2 编辑文字时与原字重叠（偏移）**：原已提交的文字仍被渲染，压在编辑框下（偏移 -6.5,-3.5）。渲染循环 `RedrawAnns` 跳过 `ReferenceEquals(a,_textEditing)` + `OpenTextInput` 里加框前先 `RedrawAnns()`（立即隐原字）。
- **C3 文字对象无法设字号**：字号本就随线宽（`AnnFont.FramePx(t)=10+t*4`），但无直接入口。工具栏加 **A− / 字号px / A+** 步进器（`_fontGroup`，仅 Text 工具激活或选中文字/编号时可见）；`StepFontSize(±1)` 改线宽→改字号，选中即改该字、未选即定下次输入。
- SelfTest T140a/b（字号锚定 细3/中6/粗10=22/34/50px + 单调），护栏 381→**383**。（T139 深色下拉样式在 R122 后补，地板曾 379→381。）

## R124 手势 A 批（用户实测编辑器后一批需求）
- **A1 每条绑定可启用/禁用**（不必删除）：`GestureBindingDto.Enabled`/`BindingEdit.Enabled`（默认 true）；`GestureConfig.ToBindings` 跳过禁用项（不进引擎）；`GestureValidation` 跳过禁用项（不挡保存）；编辑器详情顶部「启用此绑定」开关 + 列表项前缀「已禁用 ·」；宿主 Load/Save 映射。
- **A2 动作目录拆分/改名 + 即时截图语义**：`capture.frame` 标签「即时截图」→**「全屏即时截图」**，`CaptureInstantStill` 由「优先抓上次存范围」改为**永远抓光标屏全屏**（消除"明明是截图却有选区"）；动作目录重排为 选区 / 开始录制 / 停止录制 / **开始停止(同键起停)**（原「选区录制·同键停」改名，语义更直白）/ 选区录制。
- **A3 完成后序列 OnComplete**：`GestureBinding.OnComplete`/`BindingEdit.OnComplete`/`GestureBindingDto.OnComplete`；引擎 `RunActions` 主序列全部成功后再按序跑 OnComplete（尽力而为，失败不改本条结果）。宿主动作一律 `Post` 到 UI 线程 FIFO，静图抓取+after-capture 在同一 posted 动作内同步完成 → OnComplete 天然排在主动作之后（无需额外完成事件）。编辑器详情复用同一列表构建器渲染「完成后序列」段；校验含 OnComplete 动作名；Summary 显示「⟶ 完成后: …」。
- SelfTest T141a/b（禁用不校验/不进引擎）、T142a/b（拆分动作齐全 + 全屏语义）、T143a/b（OnComplete 主成功后跑/主失败不跑），护栏 383→**389**。
- 待办：**B1** 设置窗 master-detail 改版（用户手绘布局：左竖排导航 + 右内容区）；**B2** 旧快捷键全删、改由手势替代（含 GestureCore 键盘点按/按住时序 + 默认「按住录制」手势集 + 删 DoubleTapRecord/TapAlwaysShowsRegion/旧热键行 + 旧 HotkeyManager F6/F7 路径）。

## R125 设置窗 master-detail 改版 + 录制去默认编辑器 + 裁剪居中（B1 + R124 续）
**录制产物去默认编辑器（R124 续，T144，地板 389→393）**
- 停止录制/同键起停不再自动开编辑器：`FinishCapture(holdMs, openEditor)`，录制路径 `openEditor:false` → 产物留内存 `_lastSequence`（不进编辑器、不自动落盘）。
- 新动作 `edit.lastvideo`『打开编辑器（上次录制）』，常配在「完成后序列」；`CaptureController.EditLastVideo/HasLastVideo`；所有权移交编辑器，新录制释放上一条（含 spool 文件），Dispose 兜底。
- **F2 按住录制停不掉修复**：`GestureInvoker` 把「是否空闲/在录」的状态判定从钩子回调内移到 `Post` 的动作里运行——回调就在 UI 线程，回调内同步查 State 会读到过期值（KeyDown 投的 Start 还没跑，KeyUp 时仍 Idle → stop 误判失败）。投递后 FIFO 保证 Start 先于 Stop。
- spool `Flush(true)`→`Flush()`：临时文件回读走同一 OS 缓存，无需 FSync；实测省 ~1.8s UI 冻结。
- 手势配置器保存后 `InstallHotkeys()` 立即热重载（Mutate 自写盘被 watcher 跳过，否则新绑定等重启）。
- GestureEditor ComboBox 模板 `TemplatedParent`→`AncestorType ComboBox` + `TextElement.Foreground`：修深色主题下拉选中项文字发白/不可见。
- SelfTest T144a-d（edit.lastvideo 存在 / 停止·起停文案「不进编辑器」/ 动作文案引导配完成后序列）。

**B1 设置窗 master-detail 改版**
- 顶部胶囊标签条 → 左竖排导航（8 类：快捷键/截图/录制/产物/编辑器/贴图/外观/系统，图标经 GlyphProbe 验证）+ 右内容区（标题 + 滚动宿主）。窗口默认 760×min(660, 工作区-120)，可缩放。
- 选中态 = **Win11 设置风**：左侧强调色竖条（胶囊）随选中项**滑动 + 轻微回弹**（`ElasticEase` Springiness=1.0 / 400ms，幅度调小不夸张）；每项自带强调色卡片**淡入淡出**（`AnimOpacity` 200ms）；图标/文字颜色同步渐变（`AnimColor` 220ms）。
- hover 改 `IsMouseOver` 触发器（WPF 自维护，杜绝手写 MouseEnter/Leave 漏触发导致的「打开后满屏 hover 卡住」）；卡片在 hover 之上互不干扰。
- `RebuildInPlace`（换肤/换强调色）经 `_restoreCat` 保留当前类别。

**裁剪后画面飞出视口修复（T145，地板 393→397）**
- 根因：`UpdateImgRect` 定位「裁剪子区中心」用了缩放系数 `s` 而非整帧显示宽 `w`；居中裁剪两者近似（截图未暴露），非居中裁剪偏移数百 px → 画面甩出视口且与 `ClampPan` 不一致、中键拖不回。
- 抽纯函数 `ZoomMath.CropViewRect`（不变量：裁剪中心 = 视口中心 + pan），`ApplyCrop` 后 `_zoom=MinZoom; pan=0`（裁剪即适配+居中）。
- SelfTest T145a-d（非居中/带 pan/未裁剪 裁剪中心=视口中心+pan + 整帧尺寸=帧×s）。

## R126 B2 旧快捷键退役·改由手势替代（① GestureCore 键盘点按/按住时序）
- 目标：删旧捕获/选区热键与状态机，捕获键「轻点截图 + 按住录制」全交给手势引擎（完整旧手感）。本轮先落地引擎侧键盘时序（纯增量、零删除、零行为变化）。
- `TriggerKind` 新增 `Tap`（快速抬起<阈值）/`Hold`（按住到阈值即触发，需宿主 Tick）/`HoldRelease`（长按抬起≥阈值触发）。阈值复用绑定的 `LongPressMs`（`BindingHoldMs`，无则 250ms）。
- 引擎：组合键激活时若绑了 tap/hold 类→记按下时刻+置 pending+**按下即认领吞键**（前台收不到，tap/hold 稍后判定）；失活时按本次按住时长判定 `Tap`/`HoldRelease`；`Tick` 到阈值触发 `Hold`。`OnKeyDown` 自动重复也吞（按住录制期间不泄漏重复键，等价旧 HookDecision 捕获期吞主键）。
- 两种手感皆可声明式表达：**零丢帧**（`KeyDown`→起录 + `Tap`→丢弃截图 + `HoldRelease`→停止，按下即录不丢首帧）；**确认按住**（`Hold`→起录 + `HoldRelease`→停止 + `Tap`→截图，避免轻点误起录）。
- SelfTest T146a-f（轻点/长按抬起/零丢帧轻点/零丢帧长按/阈值起录/认领吞键含自动重复），地板 397→**403**。
- **② 默认手势集 + 手势成默认路径**：`GestureConfig.Enabled` 默认改 true；新增 `DefaultTapHoldSet`（捕获键 Tap→选区截图 / Hold→起录 / HoldRelease→停止，选区键 Chord→选区，按住阈值 250ms）+ `Seeded` 一次性迁移（`SeedIfNeeded`：空配置才播种、尊重已有绑定、幂等）；`GestureService` Tick 泵扩展到键盘 Hold（原来只给鼠标触发泵，否则 Hold 永不触发）。对用户当前设置零影响（已启用、绑定非空→不播种）。SelfTest T147a-e，地板 403→**408**。
- 待办（B2 后续轮）：删 AppSettings 旧字段/HookDecision/ToggleRecord/HotkeyManager 旧路径；AppHost 全走手势；TrayService/HelpWindow/WelcomeToast/App.xaml.cs 去旧引用；设置窗删旧热键行。

**③ 删旧路径·手势成唯一触发（B2 收尾，地板 408→387）**
- **删旧字段**：`AppSettings` 去 `ScopeHotkey`/`CaptureHotkey`/`TapThresholdMs`/`TapAlwaysShowsRegion`/`DoubleTapRecord`（旧 settings.json 残留键反序列化忽略，无害）；`Normalize` 去 TapThresholdMs 钳制，`SeedIfNeeded` 改用 `GestureConfig.DefaultCaptureKey/DefaultScopeKey` 常量。
- **删文件**：`Core/HookDecision.cs`（旧 F6/F7 决策表）、`Core/ToggleRecord.cs`（连按两下状态机）——点按/按住时序已折进 GestureCore。
- **HotkeyManager**：删 `_scope/_capture/_pressed` + `ScopeRequested/CaptureBegin/CaptureEnd` 事件 + `ModifiersMatch/StillSatisfied`；`Install()` 改无参（仅 `KeySink!=null` 时装钩子）；`HandleKey` 精简为「喂 KeySink（非暂停）→ 滚动 Esc → 浮窗 Enter/Esc → 手势吞键」。保留 KeySink/CurrentMods/HookProc/ScrollActive/ToastActive/Paused。
- **AppHost**：去旧事件接线；`InstallHotkeys()` = `Gestures.Apply(...); Hotkeys.Install();`；重载日志改报手势数。
- **CaptureController**：删 `BeginCapture/EndCapture/EnterToggleRecording` + `_tr/_tapWindowTimer/_stopWindowTimer`；`FinishCapture` 去「点按→单帧」分支（点按判定上移引擎），只处理真录制收尾；REC 徽标延迟用固定 `RecBadgeDelayMs=250`；新增 `CaptureStoredStill()`（托盘「立即捕获·按当前范围」的干净静态截图，替代旧 BeginCapture+600ms+EndCapture 绕路）。
- **接线去旧引用**：TrayService（tooltip 报手势数、立即捕获走 CaptureStoredStill）、HelpWindow（HK() 从手势绑定推导键名、删「连按两下」行、按住/轻点文案更新）、WelcomeToast+App.xaml.cs（`Gestures.CaptureKeyText()/ScopeKeyText()`）、SettingsWindow（删「选区/捕获快捷键」两行 + 同键警示 + HotkeyRow/UpdateHkWarn/BuildSpec/KeyNameOf，改「用手势配置器管理」提示）。`GestureConfig` 加 `CaptureKeyText/ScopeKeyText` 助手（从绑定推导主键文本供展示）。
- **SelfTest 覆盖迁移**：删 T76 决策表(4)/T99 连按状态机(13)/T122 暂停决策(4) 共 21 条——其语义已由 GestureEngine T146（tap/hold/holdrelease/认领吞键）+ T147（默认集/播种）覆盖；T134 改无参 `Install()`。地板 408→**387**（合法下降：删的是退役路径的死测试，净覆盖不减反增）。护栏 `-MinSelfTestPass 387` PASS。

**④ e2e 套件迁移到手势模型（B2 收尾，护栏 FAIL=0 SKIP=1）**
- 旧 e2e 全假设「固定 `CaptureHotkey` 字段 + 捕获→编辑器」，两者随 B2 退役 → 全套失效。统一迁移：
  - `lib-hotkey.ps1`：`Get-FcCaptureHotkey/Get-FcScopeHotkey` 改从 `gestures.bindings` 推导（Tap/Hold/HoldRelease 或 capture.selection 的 Chord，否则默认 `Ctrl+Shift+G`；scope.select 否则 `Ctrl+Shift+R`），与 C# `GestureConfig.CaptureKeyText/ScopeKeyText` 同规。新增 **`Set-FcTestGestureConfig`**：把 settings.json 的 gestures 段钉成一套已知绑定（`Ctrl+Shift+G` 轻点→选区截图 / 按住→起录 / 松手→停止+`edit.lastvideo`；`Ctrl+Shift+R` Chord→选区），返回原文供还原；可选 `-RegionX/Y/W/H` 固定大区域（缩放测试用）。**一套喂饱全部 e2e**：轻点→选区窗（tap-semantics 场景1）、长按→录制→编辑器（编辑器类测试入口 + tap-semantics 场景2/3）。
  - **踩坑（重要）**：app 的 `JsonSerializer` 默认**大小写敏感**，属性名是 PascalCase `"Gestures"`。PowerShell `Add-Member -Force gestures`（小写）会把原 `"Gestures"` 改成小写 → app 整段忽略 → 回退默认播种集（其 `cap.holdrelease` 本就 `OnComplete=[]`，故 `edit.lastvideo` 不触发、`oc=0`）。且 Load 解析失败会**删文件重写默认**，一度把用户配置冲掉（已从 `settings.json.testbak` 恢复）。修：助手一律写 PascalCase `"Gestures"` + `-Depth 8`（默认 depth 2 会截断嵌套 gestures）。
  - `test-tap-semantics.ps1` 重写：轻点 80ms→选区窗（trace `capture-selection→picker`）、按住 600/1300ms→录制（trace `open editor frames=N`，N>1 且随时长增长）。
  - `test-editor-keys/zoom/esc-layers/focus-safety/fuzz-statemachine`：入口改「先杀进程→`Set-FcTestGestureConfig`→起 app→按住 `Ctrl+Shift+G`→编辑器」，try/finally 还原用户配置；focus-safety 保留前台守卫 + 不漏字符断言（`Shot swallow` 按住 900ms→编辑器）；fuzz 收尾接受编辑器或选区窗任一。
  - **退役** `test-hotkey-e2e.ps1`（旧改键/旧键失效/恢复，字段已删）+ 从 guardrail 列表移除。
  - `GestureService` apply 日志加 `oc=<OnComplete 数>`（本 bug 若有此字段可秒查；留作手势可观测性）。
- 全量护栏 `-MinSelfTestPass 387`：**FAIL=0 SKIP=1**（focus-safety 因前台窗口守卫 SKIP≠FAIL）。用户 settings.json 完好（qq.rec/bfd68f4/bc235bc/b3c320e）。

**⑤ B2 后 UI 一致性走查（截图核对 + 快捷键页补当前键位）**
- 逐界面截图（明/暗双主题）核对 B2 改动的 UI 面：设置「快捷键」页、帮助窗——渲染干净、图标无豆腐块、文案连贯；全仓 grep 无 `TapAlwaysShowsRegion/DoubleTapRecord/连按两下/CaptureHotkey/ScopeHotkey/HookDecision/ToggleRecord` 死引用（`record.toggle` 的「再连按两下结束」是活的同键起停动作、`OnCaptureHotkey/OnScopeHotkey` 是内部意图函数名，均非遗留）。
- **快捷键页补「当前生效键位」卡片**：B2 删两行热键后该页偏空，用户看不到自己的键。加一张卡（从 `GestureConfig.CaptureKeyText/ScopeKeyText` 推导）显示「截图键 / 选区键」= 实际绑定键（强调色加粗右对齐），免开配置器一眼可见；明/暗双主题像素核对（`test-out/b2-ui/settings-hotkey-keys{,-light}.png`）。UI-only，SelfTest 仍 387，护栏 FAIL=0 SKIP=1。
- 走查发现（未改，记录）：帮助窗把「轻点截图/按住录制」通用语义绑到派生键名——对自定义成纯 Chord 的用户「按住录制」行略不符，属静态速查表固有简化，动态枚举绑定属大改且非用户所求，暂不动。

**⑥ 用户复核「双击录制」类设置项的移除（徽标残留提示清除）**
- 用户关切：B2 应删掉「一切能用快捷键配置替代的键→动作设置项」，「双击录制」是例子，担心做错。复核结论：已删 `CaptureHotkey/ScopeHotkey/TapThresholdMs/TapAlwaysShowsRegion/DoubleTapRecord` 全部键→动作设置；所有行为均为手势动作（截图/选区/录制起停/同键起停/滚动/延时/贴图/OCR/进编辑器…）。
- 唯一残留：REC 徽标 `StartRecBadge` 的 `toggle` 分支文案「· 再连按两下结束」——旧双击录制语义，且唯一调用方 `StartRecBadge()` 无参（toggle 恒 false）实为死代码。按用户「徽标里的提示去掉就好了」删除该后缀 + 清死参数 `toggle`/死字段 `_recToggle`。
- 键盘多击现状（记录）：引擎 `DoubleClick/TripleClick` 仅由鼠标 `OnMouseDown/Up` 驱动（组合键激活态 + 鼠标连击），**无键盘双击触发器**；旧「双击某键起停录制」在新模型由「按住录制」或「单击起停（KeyDown→record.toggle）」覆盖，非字面双击。若日后要字面键盘双击可再补引擎。构建 0 错。

## Round 127 — UI/动效打磨（对标 PCL2 丝滑感，用户离线新指令）

用户睡前新指令：「有空就多打磨 UI，让动效和 PCL2 启动器一样丝滑」。PCL2 = Plain Craft Launcher 2，WPF 标杆级顺滑（平滑切页/一致缓动/无生硬跳变）。本轮起以此为动效品质基线，逐面打磨。

**① 设置窗切类别：Visibility 硬切 → 交叉淡入 + 轻微上浮**
- 现状：B1 主从式左栏导航竖条滑动 + 卡片淡入淡出已平滑，但右栏内容 `cats[i].Body.Visibility = Visible/Collapsed` **硬切**（旧面板瞬没、新面板瞬现）——对标 PCL2 最大差距。
- 改法：内容面板同处一个 `Grid`（重叠），`ShowCat` 改交叉淡入——旧面板 110ms CubicEase-In 淡出后 `Collapsed`（`Completed` 里带 `_cat!=capturedPrev` 守卫防快速连切误收），新面板 `Visible` 后 190ms(`Motion.EnterMs`) CubicEase-Out 淡入 + `TranslateTransform.Y` 12→0 轻微上浮（复用编辑器 A1「内容上浮」语言）。快速连切先把无关面板 `BeginAnimation(null)` 清零并收起，杜绝竞态残留/双面板叠影。首帧/换肤重建 `animate=false` 走原直切路径。
- 取证：`Shot mouse` 注入点击导航项 + `UICap burstregion` 连拍（绝对前缀 + 标题加引号，避开 `Start-Process -ArgumentList` 拆参坑）→ `tr4.013.png` 实证「录制」内容半透明淡入 + 上浮、旧「快捷键」已淡出、竖条平滑跟随；终态 `after-click.png` 单面板全不透明无叠影。护栏 FAIL=0 SKIP=1，SelfTest 387。
- 待办（后续轮，PCL2 化）：① 内容标题 `contentHeader` 现瞬切，可随面板一起淡入；② 切页滚动位置未复位（可 `ScrollToTop`，但 `scroller` 在 `ShowCat` 之后声明，需调整声明序或在调用点补）；③ 帮助窗/手势配置器切页同款处理；④ 悬浮条/编辑器 hover 与按压反馈统一复核。

**② 手势配置器窗：硬弹硬关 → 自包含淡入(0.96→1 缩放)+淡出；设置窗标题随正文淡入**
- 手势配置器 `GestureEditorWindow`（可复用库，不引用宿主 Fluent）此前**无任何入场/退场动效**（硬弹硬关）。加自包含动效（库不引宿主件，但用 WPF 自带 `System.Windows.Media.Animation`，与宿主 `PopupEnter/CloseWithFade` 同款手感）：构造末尾 `SetupMotion()`——`Opacity=0` + `ContentRendered` 淡入 190ms + 根 Border `ScaleTransform` 0.96→1（WinUI Dialog 范式）；`Closing` 拦截任何关闭路径（X/取消/保存/宿主 Close）先淡出 150ms cubic-in 再真 `Close()`，`_closing` 守卫防重入（淡出中再点 X 直接放行关闭）。库窗非分层，未上手工 `LWA_ALPHA`（避 R11 永久隐形坑），靠 WPF 自管分层。
- 取证：`burstdesk`（真实屏幕，能反映 per-window alpha；`burstregion` 用 PrintWindow 抓窗口 DC **无视 alpha** 故看不到淡变——重要教训）连拍开启动画：第 2 帧无窗→第 3 帧配置器约半透明（透出后面设置窗内容）→第 4 帧全不透明，淡入生效且**无闪白帧**。退场淡出与入场同一 `DoubleAnimation(Opacity)`+`Closing` 代码路径、窗确能关闭（`window-not-found`），机制对称；150ms 淡出跨进程连拍对齐困难未抓到中间帧（采样问题非功能问题），以入场实证 + 代码对称定案。
- 设置窗切页补 `contentHeader` 随正文一起淡入（改文本时置 0 再淡入，消除「跳字」）。构建 0 错，护栏 FAIL=0 SKIP=1，SelfTest 387。

**③ 设置窗切类别复位滚动到顶部**
- `ShowCat` 切类别时 `scroller?.ScrollToTop()`（仅 `prev!=idx` 真导航时复位；主题重建 `prev==idx` 不动，保留滚动位置）。`scroller` 原在 `ShowCat` 之后声明（局部函数无法前向引用 CS0841）→ 前向声明 `ScrollViewer scroller=null;` 于 ShowCat 前、构造处去 `var` 改赋值。当前各页内容均未超视口（截图页最满也不滚），复位为防御性/未来向（小窗或更高 DPI 溢出时生效），现状 no-op 零风险。
- 冒烟：快速连切全部 8 类（含快切竞态）→ 进程存活、终态单面板全不透明无叠影。构建 0 错。

**R128 · OCR 结果窗补入场淡入（消除「硬弹入、淡出」不一致）**
- 全 app 动效审计（逐面 grep）：设置/帮助/编辑器/两 toast/选区窗/贴图窗均已 `FadeIn`/`PopupEnter`+`CloseWithFade`+主题交叉淡入，唯 **`OcrResultWindow` 有退场 `CloseWithFade` 却无入场**——硬弹入、淡出，不对称。
- 改法：构造末尾加 `Fluent.PopupEnter(this)`。OCR 窗与 `HelpWindow` **同为分层窗**（`AllowsTransparency=true`）→ `PopupEnter` 走分层淡入路径（与帮助窗逐字同调用、同窗型，HelpWindow 已上线验证），入场淡入与退场 `CloseWithFade` 对称。
- 验证：构建 0 错；护栏 FAIL=0 SKIP=1，SelfTest 387。入场淡入按构造成立（同 HelpWindow 同窗型同调用）；完整 OCR 流程（截图→点开 toast 进编辑器→Ctrl+O）需 5+ 步脆弱注入，且用户明令「绝不盲操落到其他软件」，故不为其做脆弱多步输入，以代码对称性 + 护栏定案。

**R129 · 动效审计收尾 + 编辑器走查 + Release 构建验证 + 修自引入可空警告**
- 动效审计**收尾**：补查最后两窗 `CountdownWindow`/`ScrollHintWindow`，均 `FadeIn`+`CloseWithFade`。至此全部 11 窗（设置/帮助/编辑器/截图 toast/欢迎 toast/选区窗/贴图/OCR/倒计时/滚动提示 + 配置器库窗）入场/退场/主题动效齐备，**零缺口**。
- 编辑器走查：用**用户真实配置**（F2 按住录制→松手 `edit.lastvideo`，`Shot hkm 113 0 0 0 0 600` 走全局钩子，不改配置/不抢前台）可靠打开编辑器并 `UICap win` 截图。两行工具条（播放/时间轴 + 绘图工具/色板/宽度/撤销重做/置顶/设置/帮助）图标全渲染无豆腐块、对齐一致、确认钮醒目、色板选中态有环、时间轴刻度清晰——**无缺陷**。（选区窗合成拖拽捕获不稳定，故改走录制路径。）
- Release 构建验证（批尾前置）：`dotnet build -c Release -p:Platform=x64` 成功、0 error，R127/R128 改动在 Release 下干净。
- **修自引入警告**：R127③ 的 `ScrollViewer scroller = null;` 触发新警告 CS8600（null 赋非空）→ 改 `ScrollViewer? scroller = null;`。纯可空注解（编译期擦除、零运行时影响），重建后 CS8600 全消、0 错、无新增警告；护栏不触及该 UI 路径故无增益，跳过。

**R130 · 批尾就绪状态（发布 de-risk，无源码改动）**
- 完整自包含发布 `test-out/publish.ps1` 实跑 **PUBLISH OK**（errors=0，产出 `test-out/publish-staging/FrameCapture.exe`）——批尾发布端到端可用已验证（Release 构建 + 自包含打包均通）。
- 分发产物形态：`release/FrameCapture.zip`（72MB，gitignore `release/*.zip`，现 10/3 旧版）。批尾 = 由新 staging 重打包该 zip（含 R127–R129 全部改动）+ 恢复 release 实例。
- 用户 settings.json 完好（4 手势自定义配置，护栏 e2e 临时钉配置已正确还原）；`RunAtStartup=false`、无 HKCU Run 启动项。
- 结论：PCL2 丝滑指令已达成（全 11 窗动效齐备、零缺口），批尾发布已 de-risk；zip 重打包 + deploy + 重启实例留临近截止（2026-10-13 10:00）执行，避免后续测试打断。

**R131 · 可截图面走查收官 + R128 视觉验证**
- **选区窗不可截图（正确设计）**：`RegionPickerWindow` 设 `WDA_EXCLUDEFROMCAPTURE`（防自入镜），故 UICap 拍不到其遮罩/提示/放大镜——非缺陷。同类 WDA 隐身浮窗：CaptureToast/WelcomeToast/CountdownWindow/ScrollHintWindow/RegionOutlineWindow，均由 e2e 覆盖、无法截图走查。
- **OCR 结果窗走查**（可靠路径：F2 录制→编辑器→`Shot hkm 79 1 0 0 0 60` 真 Ctrl+O，先校验前台=编辑器才发）：标题「识别文字」+OCR 图标无豆腐块、文本框加载即全选（就绪复制）、底部「N 字」计数 + 紫色「复制全部」钮醒目——**无缺陷**。**R128 入场淡入视觉验证通过**（窗完整可见，动画未致不可见/半透明）。
- `ctrlw`（PostMessage Ctrl+键）不触发 WPF 修饰键命令（WPF 读 GetKeyState 异步态，PostMessage 的 Ctrl 不更新）→ 触发编辑器 Ctrl+O 须用 SendInput（`hkm`）且前台须为本窗。
- 可截图面走查收官：设置 8 页 / 编辑器 / OCR / 帮助 / 配置器 均无缺陷；WDA 隐身面 e2e 覆盖。PCL2 指令 + 全 app UI 走查完成，无剩余改动。

**R132 · 批尾重发布（release zip 已更新）+ 功能完备性确认**
- **功能超越 QQ 截图确认**：编辑器 `Tool.Mosaic`（R47/R50）是「区域滤镜」马赛克——拖框实时块均值预览、逐帧重算（视频播放时随帧走，非静止贴图）、缓存、WYSIWYG 烘焙，优于 QQ 截图静态马赛克；标注工具齐备（线/矩/圆/箭头/手绘/高亮/文字/马赛克/裁剪/编号 + 色板/宽度/撤销重做）。
- **批尾重发布已执行**：`release/FrameCapture.zip` 由当前 staging 重打包（69.7MB / 250 项 / 扁平结构 / 含 exe+dll+手势库），反映 R127–R129 定稿源码。旧 10/3 zip 备份至 `test-out/FrameCapture-10-03.zip.bak`（gitignore 区，git 干净）。zip 内容 = 已验证 PUBLISH OK 的 staging 字节，传递性验证通过。
- **未做（有意）**：① 不从 `settings.json.bak` 还原——用户当前配置（4 自定义手势）正确，.bak 是 B2 前旧版，还原会回退手势（objective 该句已过时）。② 暂不重启 release 实例——RunAtStartup=false 用户习惯手动启动；且重启易被后续轮打断，宜留最后一轮统一做。
- **若后续再有源码改动**：须重跑 `test-out/publish.ps1` + 重打包 release zip（本步幂等）。

**R133 · 最终 HEAD 验证（配置完好 + SelfTest 绿 + 默认键冲突已归档）**
- **用户 settings.json 完好**：整晚测试（护栏 e2e 钉还原、F2 录制、OCR 流程等）后复核——`Enabled/Exclusive/Seeded=true`，4 手势全在（Ctrl+Alt+R 录制+edit.lastvideo / Ctrl+Alt+A 截图 / F2 KeyDown 起录 / F2 KeyUp 停止+edit.lastvideo），其余字段正确。`RegionX/Y/W/H=1560,260,940,560` 系测试拖选残留（无害，下次捕获覆盖）。
- **SelfTest 最终 HEAD 绿**：Debug x64 构建 0 错，SelfTest **PASS=387 FAIL=0**（不钉配置、无 e2e 风险的安全门禁）。
- **默认选区键冲突已归档、勿重复调查**：`DefaultScopeKey=Ctrl+Shift+R` 与浏览器硬刷新冲突——**TODO.md:375 已完整记录为待用户拍板项**（「属产品决策+改默认有风险故未动」，建议改 `Ctrl+Alt+R`/`Ctrl+Shift+S`，仅影响新装）。改动波及 SelfTest 多处断言 + lib-hotkey/fuzz/README/TEST-CHECKLIST，确属用户决策，**不动**。
- 结论：无源码改动必要；指令达成、批尾 zip 就绪、配置完好、门禁绿。唯余最后一轮重启 release 实例。

**R134 · 收尾窗口判定 + 单实例结论（重启实例必须留最后一轮）**
- **TODO.md 全量开放项复核**：逐条读完（含 line 320–415），所有 `[ ]` 均为用户明确「暂缓/后续/留后再美化」的增强候选，或产品决策（默认选区键冲突 line 375，已交用户拍板）——**无安全可自主做的项**。A1 内存工作集已由 `Memory.Reclaim()`（编辑器关闭 + 编码收尾 GC×2+TrimWorkingSet）解决，line 388「待确认」作废。
- **单实例确认**：`App.xaml.cs:40` `Mutex("Local\FrameCapture.SingleInstance")`；第二实例 `ForwardToPrimary(args)` 转发 `--settings/--help` 后 `Shutdown()` 退出。**含义**：若提前把 Release 实例跑起来，用户醒来双击自己的构建（Debug/Release 同名 Mutex）会被转发到我这个实例后退出——用户自己的构建起不来，易懵。
- **故「恢复 release 实例」（objective 批尾项）必须是批次最后一轮的动作**，临近截止再做，**不可提前**。提前重启 = 挡用户自启 + 被后续轮次打断的双重风险。
- **时间**：02:46，距截止 7.2h，未到收尾窗口。本轮无源码改动；后续轮次轻量确认净态，临近截止（约 09:30+）再执行重启实例 + 更新 TODO.md 收尾状态头。

**R135 · error.log 稳健性审计（无当前异常，应用稳定）**
- **审计 `AppData\Roaming\FrameCapture\error.log`（34KB / 354 行）**：全部为**历史异常**——① 10/02 早期 `XamlParseException 'x' is an undeclared prefix`（`Fluent.IconButtonTemplate` 早期 XAML 前缀 bug，早已修）；② 10/12 22:01:55 `XamlParseException 无法从文本"SelectionBoxItem"创建"DependencyProperty"`（R122 版 `EditorStyles.ApplyCombo` 的 ComboBox 嵌套模板坑）。
- **②已修 + 防回归**：EditorStyles.cs 于 R125（10-13 00:24）改为正确绑定式；**SelfTest T139a**（`Program.cs:2154-2176`）对当前 `ApplyCombo` 模板做**真实 `Measure`+`Arrange`**（正是 22:01 崩溃的触发路径——注释点明「Parse 不报、Measure 才报」），SelfTest **387 绿**即证明当前模板解析无异常。
- **稳定性证据**：error.log 最后一条 22:01:55 **早于** R127–R134 全部测试（00:00–02:46）——近期整轮测试**零新异常/零崩溃**，应用稳定。appdata 内仅此一个 .log，无独立崩溃转储。
- **未删 error.log**：属应用运行时诊断日志（非用户创作数据），保留历史；条目均带旧时间戳，用户醒读时知是开发期噪声、非当前故障。
- 结论：无当前 bug；本轮无源码改动。

**R136 · SelfTest 计数澄清（387 ≠ 违反「只增不减」）**
- **objective 写「当前397」是 R125（B2 之前）的计数**；B2 全程计数轨迹：397 →(T146 tap/hold/holdrelease/吞键 6 条)403 →(T147 默认集/播种 5 条)408 →(B2③ 删旧热键路径)387。
- **387 的 21 条减少是删退役死测试**：T76 决策表(4) + T99 连按状态机(13) + T122 暂停决策(4)——这三者测的 `HookDecision`/`ToggleRecord`/旧暂停逻辑**已在 B2③ 整体删除**，其语义由新增的 GestureEngine **T146/T147** 完整覆盖（见 line 1884）。
- **净效果**：手势引擎净增 11 条测试、删 21 条已无对应代码的死测试 → **有效覆盖不减反增**；387 是 B2 后正确基线，护栏 `-MinSelfTestPass 387` 已锁。非回归、非漏测。
- 结论：计数合规；本轮无源码改动。

**R137 · release zip 交付物完整性验证**
- **CRC 全量校验**：.NET `ZipFile` 逐条读流解压全部 **250 条目**（解压后 169.1MB），**0 CRC 失败**——zip 无损坏、非截断（`Test-Archive` cmdlet 本机未加载，改 .NET 读流校验，读流即触发 CRC 校验）。
- **关键程序集齐全**：`FrameCapture.exe` 148KB / `FrameCapture.dll` 388KB / `GestureCore.dll` 14KB / `GestureEditor.dll` 42KB / `FrameCapture.runtimeconfig.json` 均在（扁平结构，自包含发布）。
- **结论**：`release/FrameCapture.zip`（69.7MB）是有效、完整、无损坏的自包含发布包，可直接解压运行。交付物就绪。本轮无源码改动。

**R138 · release zip 端到端可运行验证（解压→启动→UI→无崩溃）**
- **解压**：`Expand-Archive` 出 250 文件（0.8s），`FrameCapture.exe` 在位。
- **启动**：`FrameCapture.exe --settings` → 进程存活（pid 68844）→ **前台窗 = `FrameCapture Settings`**（设置窗正常打开并置前）→ **error.log 零新增**（34733→34733，无崩溃/无异常）。
- **清理**：`Stop-Process` 杀掉 → 确认 0 残留实例（Mutex 释放，不挡用户自启）→ 删 `test-out/ziptest` → 用户 settings.json 未动（Seeded=true 不触发播种）。
- **结论**：交付物**端到端可运行**——zip 解压即得可双击启动、UI 正常、零崩溃的自包含应用。八维验证（功能/动效/UI/稳健性/配置/构建/测试计数/交付物）全部通过。本轮无源码改动。

**R139 · B2③ 删除彻底性验证（无死代码/残留引用）**
- **grep `src/` 已删字段/文件**：`ScopeHotkey`/`CaptureHotkey`/`TapThresholdMs`/`TapAlwaysShowsRegion`/`DoubleTapRecord` 五字段——**仅剩注释提及**（`AppSettings.cs:14`、`CaptureController.cs:30`、`GestureEngine.cs:146` 说明退役缘由），无任何代码引用。
- **文件删除确认**：`glob src/**/{HookDecision,ToggleRecord}.cs` → **无文件**（旧决策表/连按状态机文件已物理删除，无 class 残留）。
- **残留同名符号辨析（非被删项）**：`CaptureGuards.OnCaptureHotkey/OnScopeHotkey` 是**当前手势路径**的意图守卫（`CaptureController.cs:147` 调用），`CaptureController.ToggleRecordSelection` 是录制切换方法（`GestureInvoker.cs:50` 的 `RecordToggle` 动作调用）——手势触发即键位组合，"Hotkey" 命名仍贴切，非旧路径遗留。
- **结论**：B2③ 完整落地，无死代码、无残留引用、无误删当前逻辑。本轮无源码改动。

**R140 · 最终完整护栏（最终 HEAD 上 objective 验证标准直接达成）**
- **GUARDRAIL PASS（FAIL=0 SKIP=1，共 10 项）**，后台运行至完成（exit 0）：
  - build:app / build:Shot / build:SelfTest 全 **errors=0 exe=ok**；
  - **SelfTest PASS=387 FAIL=0**（floor 387）；
  - e2e **editor-keys / zoom / esc-layers / tap-semantics / fuzz-statemachine 全 PASS**——其中 **tap-semantics（手势轻点截图 / 长按录制）正是 B2 核心语义**；
  - **err_delta=0**（error.log 34733→34733，无新崩溃）；
  - focus-safety **SKIP**（前台为 DSH GUI 非本软件窗，注入探针安全跳过，SKIP≠FAIL）。
- **护栏后用户配置核验**：try/finally 还原成功——4 条自定义手势逐字段完好（qq.rec `Ctrl+Alt+R`→record.toggle→edit.lastvideo；bfd68f4 `Ctrl+Alt+A`→capture.selection；bc235bc `F2 KeyDown`→record.start；b3c320e `F2 KeyUp`→record.stop→edit.lastvideo），`Enabled/Exclusive/Seeded=true`，其余设置未动。
- **结论**：objective 验证标准「构建 0 错 + SelfTest 只增不减 + GUARDRAIL PASS」在**最终 HEAD（R127–R139 全部改动后）直接达成**，非假设。本轮无源码改动。

**R141 · 净态快照 + zip 时效确认（03:04，距截止 6.9h）**
- **净态**：git HEAD `5bcee53` 干净 · 0 FrameCapture 实例 · release zip 69.7MB（mtime 02:36）· 用户配置完好 · error.log 34733 无新增。
- **zip 时效确认**：`git diff --stat 9f85a15(R132)..HEAD -- src` **输出为空** = R133–R140 仅改 PROGRESS.md，**src 零改动** → **zip 反映最终源码，无需重发布**。
- **判据纠错**（防后续轮误判）：`if(git diff --quiet A B -- src){...}` 是**错误惯用法**——PowerShell 的 `if` 取命令 **stdout** 而非退出码，`--quiet` 无输出恒为假 → 永远误报「有改动」。正确判据：`git diff --stat A B -- src` 输出为空，或 `git diff --quiet A B -- src; if($LASTEXITCODE -eq 0){无改动}`。
- **结论**：交付物 zip 与最终 HEAD 源码一致。本轮无源码改动。

**R142 · 动效一致性审计（所有窗口入场/退场动效齐备，无硬弹入漏网）**
- grep 全仓 `PopupEnter|SetupMotion|CloseWithFade|PlayDrop|FadeIn|ContentRendered` 逐窗核对：
  - **入场**：Settings/OCR/Help = `Fluent.PopupEnter`；GestureEditor = 自包含 `SetupMotion`（库不引用宿主 Fluent，手感同款）；Pin = `PlayDrop`（落图缩放）；**Editor = `Fluent.FadeIn(EditorEnterMs, 14px 位移, offscreenFirstFrame:true)`**（EditorWindow.cs:87）。
  - **退场**：Settings/Editor/OCR/Help/Pin/Toast/Countdown/ScrollHint/WelcomeToast 全 `Fluent.CloseWithFade`。
- **非不一致说明**（防后续轮误改）：主编辑器用 `FadeIn`（淡入+位移+**离屏首帧**）而非 `PopupEnter`（淡入+缩放）是**有意为之**——编辑器首帧渲染大图可能黑闪，`offscreenFirstFrame` 把黑相挪到屏幕外，用户只见淡入；改成缩放会丢掉这层保护。故二者差异是设计，非缺陷。
- **结论**：动效覆盖完整、无遗漏窗口。本轮无源码改动。

**R143 · 强调色一致性审计 → 修「白字压强调色」硬编码（2 处，真实可读性缺陷）**
- **审计**：强调色经 `Theme.Accent` 单一来源统一应用（82 处引用，无硬编码色值），但发现 2 处**前景硬编码 `Colors.White` 压在强调色底上**：
  - `ScrollHintWindow.cs:112` 滚动拼接「完成」按钮 `MakeBtn("完成", Theme.Accent, Colors.White)`；
  - `WelcomeToast.cs:103` 首启欢迎「查看帮助与全部快捷键」按钮 `Foreground=Colors.White` + `Background=Theme.Accent`。
  - 缺陷：用户可自定义强调色，选**浅色**（如黄/浅青）时白字与浅底对比不足→不可读。
- **修复**：前景改用现成助手 `Fluent.OnAccentColor(Theme.Accent)`（亮度>128→黑，否则白），与 `OcrResultWindow.cs:75` 同款。两文件均已有 `using FrameCapture.Core;`。
- **对当前 `#7F75FF` 的影响**：亮度 0.299·127+0.587·117+0.114·255≈135.7>128 → 选**黑字**，与已视觉核验的 OCR 窗一致；且中亮度紫底上黑字 WCAG 对比度高于白字，属可读性提升而非退化。
- **验证**：构建 **0 错**（9 警告全既有，无新增）；**SelfTest 387 绿**（只增不减）。改动纯 UI 前景色，不触 e2e 流程；完整护栏留批次末在最终 HEAD 上统一跑一次。

---

## ⚑ 批次末清单（仅最后一轮执行，约 09:30+ 临近 10:00 截止时）

> 触发条件：距截止 < ~30 分钟，或判断这是本批次最后一轮。**顺序不可乱**——重启实例必须最后做。

1. **完整护栏**：`pwsh -NoProfile -File scripts/guardrail.ps1 -MinSelfTestPass 387`（后台跑至完成）→ 期望 `GUARDRAIL PASS FAIL=0 SKIP=1`；跑后核验用户 4 条自定义手势被 try/finally 还原（兜底 `settings.json.testbak`）。
2. **release 重发布**（R143 改了源码，zip 现过期，**必须做**）：`pwsh -NoProfile -File test-out/publish.ps1` → 再 `Compress-Archive -Path test-out/publish-staging/* -DestinationPath release/FrameCapture.zip -Force -CompressionLevel Optimal`。
3. **重启 release 实例**（**必须最后一步**）：解压或直接用 publish-staging 启 `FrameCapture.exe`。⚠️ 单实例 Mutex——若提前启动，用户双击自己的构建会被 `ForwardToPrimary` 转发后退出，自己的实例起不来。
4. **更新 TODO.md line 3** 过期收尾头（现写「SelfTest 337/全出厂默认/Release 运行」，应改为 SelfTest 387、用户自定义手势、Release 运行）。
5. **不要**从 `settings.json.bak` 恢复（那是 B2 前的旧配置，会回退手势功能；用户当前配置正确）。

**R144 · 硬编码色审计（续 R143）→ 修 SettingsWindow 13 处「黑压强调色」**
- **审计**：Overlays 里 `Colors.Gray/Black/White` 7 处**全合法**（4 处 `DropShadowEffect` 阴影恒黑 + 3 处取色器 SV 渐变白→色相→黑，设计如此）。但 `Color.FromRgb(0,0,0)` 在 SettingsWindow 有 **13 处，全压在强调色底上**：11 处选中态 chip 前景（`captured==cur ? 黑 : Theme.Text`，选中底=`Theme.Accent`）+ 2 处开关 knob（on 时轨道=`Theme.Accent`、knob=黑）。
- **缺陷**：深色强调色下黑字/黑 knob 与深底同色→隐形。用户 `CustomColors` 就含 `#232EF6`（字面亮度≈65），若选它当强调色，选中 chip 与开关 knob 会看不见。
- **修复**：13 处 `replace_all` → `Fluent.OnAccentColor(Theme.Accent)`（亮度>128→黑，否则白）。对默认 `#4CC2FF`(165) 与当前 `#7F75FF`(136) 均→黑，**外观不变**；对深色强调色→白，可读。向后兼容。
- **保留非缺陷**：RegionPicker 的 `#4CC2FF`（尺寸徽标/放大镜读数/像素标记）是**深色半透明浮层与任意像素之上**的固定浅蓝，改 `Theme.Accent` 反致深色强调色不可读——故意保留。
- **验证**：构建 **0 错**；**SelfTest 387 绿**。改动纯 UI 前景/knob 色，不触 e2e；完整护栏留批次末统一跑。

**R145 · GestureEditor 库同族缺陷修复 → 强调色审计收官**
- **缺陷**：`GestureEditorWindow.cs:467` accent 按钮 `fg=Color.FromRgb(0,0,0)` 压 `_pal.Accent` 底，深色强调色下不可读（同 R143/R144 族）。
- **约束**：GestureEditor 库**不得引用宿主 Fluent**（架构边界）。故在 `EditorPalette`（GestureModel.cs）加**库内自包含** `OnAccent()` 助手（同款亮度>128→黑判定，仅用 `System.Windows.Media.Colors`），467 行改用 `_pal.OnAccent()`。
- **验证**：构建 **0 错**（含 GestureEditor 库编译）；**SelfTest 387 绿**；库边界未破（OnAccent 不引用宿主）。
- **强调色审计收官**：FrameCapture 15 处（R143 白字 2 + R144 黑字 13）+ GestureEditor 1 处 = **16 处「硬编码色压强调色」全改自适应**；RegionPicker 深色浮层上的固定浅蓝属故意保留。用户自定义深/浅强调色下所有选中态/开关/按钮前景均可读。

**R146 · 编辑器硬编码白前景审计 → 修裁剪提示 + 序号编号**
- **裁剪提示**：`_cropHint`「松开应用裁剪」`Background=Acc` + `Foreground=Brushes.White`（同 R143 族，浅色强调色下白字不可读）→ `OnAccentColor(Acc)`。
- **序号标注编号**（真缺陷）：编号圆圈填用户标注色，编号写死白字。色板含**琥珀 `#FFB900`（亮度185）、白 `#FFFFFF`**——白编号压这两个浅色圆圈不可读。**画布预览（EditorWindow.Drawing.cs:1190）与导出烘焙（Annotations.cs:121）两处都写死白**（保存的图片也受影响）。
- **修复**：`NumberBadge` 加纯函数 `NumberOnLightCircle(r,g,b)`（与 Fluent 同款亮度判定），画布 + 导出**共用同一判定保证 WYSIWYG**；加 **T109g/T109h**（逻辑：琥珀/白→黑编号，红/黑→白编号）+ **T2c/T2d**（烘焙渲染端到端：绿底白圈现黑像素、绿底红圈现白像素）双重锁定。
- **合法保留**：其余 4 处 `Brushes.White` 全在深色浮层——编辑器 snack `#1F1F1F`、贴图 snack `#141414`、REC 徽标 `#C81A1A`（亮度78）、放大镜读数 `#1B1B1B`。
- **验证**：构建 **0 错**；**SelfTest 391 绿**（387 + T109g/T109h + T2c/T2d）。

**R147 · 三项系统性审计（动效/Help/暂停）均无缺陷 + 批次中护栏检查点**
- **动效时长审计**：Overlays 41 处 `FromMilliseconds/FromSeconds`——DispatcherTimer 间隔（fps 播放、snack 消隐、REC 计时、toast 消隐）是**功能计时非动画**；动画类多数已引用 `Fluent.Motion.*` 令牌；剩余写死值（选区指示条 400ms、snack 淡出 320ms、贴图落图/编辑入场 220ms、chip hover 120/160ms）是**刻意专属节拍**——改令牌会改手感（用户要「丝滑」观感非令牌本身），且「该归哪个令牌」含糊，故不动。无安全可改项。
- **Help 文案审计**：`HK(bool)` 动态读 `SettingsService.Shared.Current.Gestures` 的 `ScopeKeyText()/CaptureKeyText()`，null 才回落 `DefaultScopeKey/DefaultCaptureKey`；无 F6/F7 残留；「QQ 式 Ctrl+Alt+A 手感」是描述文案非绑定。**Help 手势感知，B2 后无陈旧快捷键说明**。
- **暂停全局热键审计**：`HotkeyManager.cs:111` `if(!Paused && KeySink!=null)`——暂停时不喂手势层，手势同样避让（不拦截任何键）；`GestureEditorHost` 捕获和弦时临时 `Paused=true` 防自拦截、结束/关窗还原。**暂停对手势生效，B2 未断裂此功能**。
- **批次中护栏检查点**：R143-146 连改 4 轮源码后跑完整 `guardrail.ps1 -MinSelfTestPass 387`（后台跑避免超时中断 try/finally 还原）→ **GUARDRAIL PASS**（FAIL=0 SKIP=1，focus-safety 属预期跳过非失败；SelfTest 391、e2e editor-keys/zoom/esc-layers/tap-semantics/fuzz-statemachine 全绿）。try/finally 已还原用户 4 手势（Ctrl+Alt+R→record.toggle、Ctrl+Alt+A→capture.selection、F2 点按/松开→record.start/stop，onComplete=edit.lastvideo 均在）+ Accent=#7F75FF 完好。
- **净态**：git 干净 HEAD=`932ad55`、SelfTest 391、0 实例、zip 落后 4 src 提交（批次末重发布）。

---

## ⚑ 批次末执行（10-13 10:04 起，deadline 10:00 已过）

**R148 · 复活死动作 `ocr.selection`「识别文字」→ QQ 式提取文字（批次末随发布出厂）**
- **缺陷**：`GestureActions.OcrSelection` 早在 EXPOSED 列表里（用户可在配置器绑定「识别文字」），但 `GestureInvoker.cs:69` 是 `return false` 死分支 → `RunActions`/`FireTrigger` 判失败 → **键不吞、静默无反应**（真缺陷：绑了等于没绑，还吞不掉键泄漏给前台）。
- **决策**：实现而非删除——对齐北极星（QQ 截图「提取文字」）；反馈**统一走可截图审查的 `OcrResultWindow`**（非 WDA 排除的新 toast，否则不可审查，违背「多看看截图」）。
- **实现（纯决策函数 + 复用既有件，仿 Classify/Feedback/Normalize 风格）**：
  - `OcrService` 加 `OcrRoute{ResultWindow,CopyThenConfirm,NoEngineHint,EmptyHint}` + 纯函数 `Route(status,showResult)`：Success→(showResult?结果窗:复制+轻确认)，NoEngine/Empty→对应提示（与 showResult 无关）。
  - `OcrResultWindow` 加 `ShowMessage(msg)` + `bool message` 构造分支：400×210、居中文本、无编辑框/字数/复制钮，与结果窗共用外壳/淡入淡出/标题栏（同一可审查反馈面）。
  - `CaptureController.OcrStoredStill()`：仿 `CaptureStoredStill`（Idle 守卫→`StoredRegion()??FullMonitorAtCursor`→`LiveCapture.Grab`→`await RecognizeAsync`→按 `Route` 分流），try/catch 兜底「识别失败」。
  - `GestureInvoker` 死分支 → `Post(()=>c.OcrStoredStill()); return true;`（与其余 10 个动作同款）。
  - `GestureActions` 文案更新为「开结果窗则弹可编辑结果窗，否则直接复制文本」。
  - 加 `--ocr` 命令行/e2e 入口（与 `--scroll/--delay/--pin-clip` 同款全套 MsgSink 转发管线），零配置可脚本触发。
  - **T148a-e**（5 项）锁 `Route` 全分支 + `ocr.selection` 仍注册。
- **⚠ 环境限制（非代码缺陷，已决定性证明）**：本沙箱启动的进程**无法加载 WinRT 投影 `Microsoft.Windows.SDK.NET`**（DLL 在位 24MB、版本 10.0.19041.38 正确，但报 file-not-found = 文件打不开签名）。**决定性证据**：同一 Release 实例里跑**出厂的编辑器 OCR**（Ctrl+O）报**完全相同**的 `ocr failed: Could not load ... Microsoft.Windows.SDK.NET`（trace `D:\fc-trace.log`）。→ 我的 `OcrStoredStill` 与出厂编辑器 OCR 调**同一** `OcrService.RecognizeAsync`、行为**完全一致**；用户真实桌面（非沙箱双击）下两者都能正常 OCR。
- **验证**：构建 **0 错**（无新增警告）；**SelfTest 396 绿**（391+T148×5）；优雅降级**两次截图核验**（Debug+Release 均弹「识别失败」消息窗，深色 Fluent 圆角/标题栏 OCR 图标/居中文案渲染干净）；编辑器本体端到端正常（选区截图→`--edit-last`→编辑器渲染捕获帧、工具栏图标齐全无豆腐、accent ✓ 正常）。
- **批次末**：Release 已重发布（staging 10:05:28，250 文件，SDK 投影在位）；护栏 `-MinSelfTestPass 391` 后台跑；zip 重打包；TODO.md line 3 头已更新（337→396、全出厂默认→用户自定义手势）。

**批次末净态**：见下方最终 HEAD。

---

> **⏰ deadline 更正**：权威 deadline 是 **12:00**（goal objective 头部「用户离线至 2026-10-13 12:00」），非早期摘要里的 10:00。上方 10:04 的「批次末」执行偏早（护栏/重发布/zip/恢复实例均幂等，12:00 前会再跑一遍）。真正批次末留到 ~11:30+。R148 之后继续做有价值的小改动，每步构建+SelfTest 绿即可，最终批次末统一重发布。

**R149 · 帮助窗「捕获」段反映用户真实键位（修自定义手势下显示死键/错语义）**
- **缺陷**（截图审查 HelpWindow 发现）：帮助「捕获」段三行键位用 `HK(bool)`→`ScopeKeyText()/CaptureKeyText()`，**为默认 tap/hold 方案写死**。用户是自定义手势（Ctrl+Alt+A=选区截图 Chord、Ctrl+Alt+R=录制切换、F2=按住录制），帮助却显示：① 「**Ctrl+Shift+R** 设置捕获范围」——用户根本没绑 Ctrl+Shift+R，`ScopeKeyText` 无 ScopeSelect 绑定时**回落默认键**→显示一个按了没反应的**死键**；② 「Ctrl+Alt+A **按住**→录制 / **轻点**→截图」——用户 Ctrl+Alt+A 是 Chord 选区截图，**按住/轻点语义全错**。帮助是用户参考，误导即「不人人爱用」。
- **修复**（纯决策函数 + 可测，仿既有风格）：`GestureConfig` 加 `TriggerLabel(trigger)`（KeyDown→按下/KeyUp→松开/Tap→轻点/Hold→按住/HoldRelease→松手/…/Chord→空）+ `DescribeKeysFor(params actions)`（按组合键分组、合并触发后缀如 `F2 按下/松开`、多条顿号分隔、无匹配「未绑定」）。HelpWindow `HK`→`KeysFor`，三行改按动作查真实绑定；描述改**触发无关**（录制行含「同键切换或按住起录·松手停止」，截图行含「选区截图或即时全屏一帧」）。
- **同类缺陷·设置窗「当前生效键位」卡**（截图审查发现，比帮助更主要）：`SettingsWindow` 写死两槽 `KeyRow("截图键",CaptureKeyText())`/`KeyRow("选区键",ScopeKeyText())`——用户配置下「选区键」显示**死键 Ctrl+Shift+R**（accent 高亮），且**漏掉录制键**（Ctrl+Alt+R/F2）；顶部说明也写死默认方案。修：`GestureActions` 加 `LabelOf(name)`（动作→中文短名，未知名原样返回）；卡片改为**逐条枚举启用绑定**（`动作标签 → 键+触发后缀`），说明改「按你的手势配置实时反映」。修后显示 4 条真实绑定（开始/停止录制→Ctrl+Alt+R、选区截图→Ctrl+Alt+A、开始录制→F2 按下、停止录制→F2 松开），无死键、无遗漏。
- **验证**：构建 **0 错**；**SelfTest 403 绿**（396+T149×5+T150×2：未绑定/分组/Chord 无后缀+禁用排除/默认集/触发后缀/LabelOf 已知+未知透传）；**截图核验**帮助窗（未绑定/Ctrl+Alt+R，F2 按下/松开/Ctrl+Alt+A）与设置卡（4 条真实绑定）均与用户配置逐条吻合，排版对齐换行干净。
- **未动**：`WelcomeToast` 也用 `CaptureKeyText/ScopeKeyText`，但它是**首启卡**（`FirstRunHintShown=true` 已置，用户不再见；且首启时用户尚未自定义，显示默认键正确），非缺陷。

---

**R150 · 手势配置器触发下拉标签补全（Tap/Hold/HoldRelease 不再与 Chord 同名）+ `--gesture` 入口**
- **缺陷**（审查 `GestureEditorWindow.TriggerLabel` 发现）：该 switch 只覆盖 KeyDown/KeyUp/Click/DoubleClick/TripleClick/LongPress，**Tap/Hold/HoldRelease 落到默认分支** → 与 Chord 同为「组合键（同时按下）」。而触发下拉 `MakeCombo(Enum.GetValues<TriggerKind>())` 列全部 10 值 → **Chord/Tap/Hold/HoldRelease 四项同名**，用户想配「轻点截图、按住录制」（QQ 式默认手感）时下拉里 4 项无法区分。
- **修复**：`TriggerLabel` 补 `Tap→轻点（短按即松）`/`Hold→按住（达阈值触发）`/`HoldRelease→松手（按住后释放）`，Chord 保留「组合键（同时按下）」；改 `public static`（纯函数、不触 WPF）供测试。
- **`--gesture` 调试/e2e 入口**（与 `--settings/--help/--ocr` 同款全套 MsgSink 转发管线，7 触点：常量/OnStartup/StartMessageSink/字段/ctor/WndProc/ForwardToPrimary）：`GestureEditorHost.ShowOrFocus()` 同一入口，零配置可脚本触发 + UICap 截图审查（PrintWindow 不需前台，规避前台输入风险）。
- **验证**：构建 **0 错**；**SelfTest 404 绿**（+T151 锁「10 个触发标签两两不同非空」不变量——直接锁住下拉不歧义）；**截图核验**配置器：左列 4 条绑定触发标签全对（组合键（同时按下）/组合键按下/组合键抬起 两两不同）、右详情面板齐全、accent 选中态、无豆腐图标、✓ 配置有效。

---

## ✅ 批次末完成（10-13 10:43，deadline 12:00，四步全绿）

> 时间校准：实际时钟 10:38（早期摘要「~10:20 起 + 估的 11:15」均偏早，护栏/发布比预估快）。护栏+发布在最后一次提交 `5edd026` 之后跑，源码无改动 → 有效，无需重跑。

1. **护栏** `guardrail.ps1 -MinSelfTestPass 404`（后台）→ **PASS FAIL=0 SKIP=1**（10 项）：SelfTest 404/0、e2e 全过、focus-safety SKIP（前台 Chrome_WidgetWin，SKIP≠FAIL）。护栏钉测试配置后 try/finally 恢复用户配置——**逐字段核对仅多 6 行空白（12 字节，app 重序列化格式），4 条手势/所有设置零丢失**，功能完好无需回滚。
2. **重发布** `publish.ps1` → staging 10:38:02，250 文件，SDK 投影在位，errors=0。
3. **打包** `release/FrameCapture.zip` 69.7MB（10:42:50）→ **CRC 逐条校验 250 条 badCRC=0、exe=1、sdk=1**。
4. **重启出厂实例**（最后一步，单实例 Mutex）pid 48936（publish-staging）→ trace `gesture apply on=True excl=True n=4`（用户 4 绑定含 onComplete edit.lastvideo 正确加载）；**截图核验出厂件设置卡显示 4 条真实绑定**（与 Debug 同 sha256，确认 R149b 已入出厂件）。

**本会话净产出**：R149（帮助窗反映真实键位）+ R149b（设置卡逐条反映真实绑定）+ R150（配置器触发下拉补 Tap/Hold/HoldRelease + `--gesture` 入口）+ docs（TEST-CHECKLIST CLI 清单）——修 3 个真实用户可见缺陷，全部截图核验；SelfTest 396→404；HEAD `5edd026` git clean；出厂件已含全部改动并运行中。

---

**R151 · 瞬时浮层淡入淡出 easing 一致性（编辑器 snack/guide + 贴图 snack）**
- **缺陷**（全局 `DoubleAnimation` 审计发现）：全应用淡入淡出统一 CubicEase（Fluent.Motion 约定：淡入 EaseOut、淡出 EaseIn），唯独 4 处瞬时浮层是**线性**——编辑器 snack 淡入(939)/淡出(947)、编辑器 guide 淡出(702，而淡入 689 是 EaseOut，不对称)、贴图 snack 淡出(408)；且贴图 snack 淡入是**瞬时**（`Opacity=1` 直给），与其自身注释「淡入、停留、淡出」**自相矛盾**。
- **修复**：4 处淡出补 `CubicEase EaseIn`、编辑器 snack 淡入补 `EaseOut`；贴图 snack 淡入改为真淡入（`DoubleAnimation(1, HoverMs) EaseOut`，对齐编辑器 snack 写法，兑现注释承诺）。
- **验证**：构建 **0 错**；**SelfTest 404 绿**（无回归）；**逻辑核验**贴图 snack Border 初始 `Opacity=0/Collapsed`（88-89）→ 首次 Flash 即从 0 淡入、淡出后归 0 再显亦淡入，曲线正确。动效曲线为瞬时过程无法截图（任何动效改动皆然），以「构建+测试+初值/目标/写法对齐」三重核验替代。

**R151 后重跑批次末（10:58，四步全绿）**：护栏 PASS 404（FAIL=0 SKIP=1，用户 4 手势完好恢复）→ 重发布 staging 10:57:51 errors=0 → 重打包 zip 69.7MB CRC 250 条 badCRC=0 exe=1 sdk=1（10:58:02）→ 重启出厂实例 pid 29612，trace `gesture apply n=4` 正确加载。HEAD `c55d2f6` git clean。

**R151b · 完成 easing 审计（REC 录制徽标淡入）**：续审 `RegionOutlineWindow` 全部 `DoubleAnimation`——红点呼吸脉冲(209)线性属**故意**（稳态 throb，加 easing 反"弹"）、FadeAway(260)已 EaseInOut、主题切换交叉淡入（HelpWindow/SettingsWindow）注释明示"非对称节拍"故意线性（交叉淡入线性保总不透明度恒定）——唯 **REC 徽标淡入(205)线性**不一致（每次录制都现的显眼角标）。补 `EaseOut` 对齐约定，淡入前 `Opacity=0`(204) 确保 0→1 生效。**至此全应用瞬时淡入淡出 easing 一致**（脉冲/交叉淡入等有意线性者除外）。构建 0 错、SelfTest 404 绿。批次末重跑（11:03）：护栏 PASS 404、zip CRC 全绿、出厂实例 pid 67848 运行、`gesture apply n=4`。HEAD `f458634` git clean。

**R152 · README.txt 准确性（随出厂件分发的用户第一手文档）**：全文核查 53 行，发现 2 处 B2 后陈旧——① line6「设置窗内点击按键框重录；两键相同会有琥珀色提示」是 **B2 前旧按键框 UI**，现改键/加动作走「设置→快捷键→手势配置器」（每条=组合键+触发方式→动作），旧述会误导用户去找已不存在的按键框；② line4「悬停看当前热键」不准，托盘 tooltip 实为「手势 N 条·范围」（反映条数非具体键，R36 已改代码，文档未跟）。默认键 Ctrl+Shift+G/R + 点按/长按语义本身准确（出厂默认，保留）。**纯内容改动、代码未变**→跳过护栏，仅重发布（staging README 已核验含修正）→重打包 zip CRC 全绿→重启出厂实例 pid 42944、`gesture apply n=4`。HEAD `95456d0` git clean。

**审计收口（无缺陷轮，记录已评估并否决项防重复评估）**：
- **编辑器工具栏按钮完整性**：全部经 `IconBtn(glyph, tip)` 带 tooltip；grep/read 里显示为空的 glyph 实为私有区图标字符（线宽点 U+E915、撤销 U+E7A7、重做 U+E7A6、播放/暂停 U+F5B0/F8AE），非空白 bug。README「所有按钮悬停有说明气泡」属实。
- **Help「未绑定」加托盘提示**：否决——托盘菜单有「设置捕获范围/立即捕获」但**无录制动作**，通用提示对录制会误导；且用户系主动不绑 scope 键（双击托盘/托盘菜单可设），「未绑定」已诚实准确。
- **托盘 tooltip 改显捕获键**：否决——现「手势 N 条·范围·fps」是 R36 刻意设计（63 字符上限），改显键是另一种取舍非缺陷。
- **出厂件端到端确认**：抓 Release 实例 Help 窗核验 R149 真实键位渲染正确（设置捕获范围=未绑定、录制=Ctrl+Alt+R+F2 按下/松开、截图=Ctrl+Alt+A，均与用户配置一致）。

**R153 · 手势配置器绑定列表长摘要换行（修裁切）**：出厂件截图审视配置器，发现左侧绑定列表选中项第二行 `开始/停止录制（同键起停）→ 完成后: 打开编辑器（上次录制）` 因动作名长被**按面板宽度裁切**（截在"打开编辑"）。根因两层：① `Summary()` 返回单串、ListBoxItem 默认模板的 ContentPresenter 不换行；② 列表 `HorizontalScrollBarVisibility=Auto` 使 ScrollViewer 以无限宽测量，TextBlock 永不换行、反出横向滚动条。修：列表项改存字符串 + `ItemTemplate` 用 `TextWrapping=Wrap` 的 TextBlock + **禁用横向滚动**（`ScrollViewer.SetHorizontalScrollBarVisibility=Disabled`，否则换行不生效）。**截图核验**：长摘要完整折成 3 行、横向滚动条消失、选中黑字在 accent 底可读、其余项不受影响。构建 0 错、SelfTest 404 绿（纯 WPF 渲染改动，`Summary` 逻辑未变故无需新增单测）。

**R153 批次末**：护栏 PASS（FAIL=0 SKIP=1，focus-safety 因前台非本软件窗 SKIP≠FAIL）；用户 4 条自定义手势完好（`gesture apply n=4`）；重发布 staging 11:23:13 → zip 69.7MB/250 项/CRC 全绿/exe=1/sdk=1 → 重启出厂实例 pid 50412。HEAD `32ab512`。

**R154 · 手势配置器 Esc 关闭（补注释承诺的关闭路径）**：清理测试窗时发现配置器按 Esc 关不掉。查 `OnPreviewKeyDown` 仅在 `_capturing`（录键中）处理 Esc（取消录键），非录键态落到 `base` 不关窗；而退场注释（line 96）明写「任何关闭路径（关闭钮/**Esc**/宿主 Close）先淡出再真关」——注释承诺 Esc 可关、代码未实现，属注释/代码不一致 + 对话框缺标准 Esc 关闭。修：非录键态 `if (e.Key==Escape){ Close(); e.Handled=true; return; }`，走 Closing 淡出退场、与「取消」钮一致。**e2e 核验**：开配置器→fg 校验为「手势配置」→SendInput Esc→窗口 not-found（已淡出关闭）。构建 0 错、SelfTest 404 绿（WPF 键处理，非决策函数故无需新增单测）。

**R154 批次末**：护栏 PASS（FAIL=0 SKIP=1）；用户 4 条手势完好（`gesture apply n=4`）；重发布 staging 11:32:36 → zip 69.7MB/250 项/CRC 全绿/exe=1/sdk=1 → 重启出厂实例 pid 11928。HEAD `bdfdc18`。

**R155 · 触发方式标签跨库核心词一致（KeyUp 抬起→松开）**：配置器（lib `GestureEditorWindow.TriggerLabel`）与宿主（`GestureConfig.TriggerLabel`，供设置/帮助）是两份独立实现（lib 不能引用宿主，天然会漂移）。逐项对照发现**唯 KeyUp 核心词不一致**——配置器"组合键**抬起**"（引擎内部术语）、设置/帮助"**松开**"；其余 9 项核心词本就一致（按下/轻点/按住/松手/长按/双击/三击/单击）。用户配置正含 F2 KeyUp，会在配置器见"抬起"、在设置/帮助见"松开"。修：lib KeyUp "组合键抬起"→"组合键松开"（核心词对齐，"组合键"前缀保留作下拉键盘/鼠标触发分组）。**新增 T155** 锁「每个触发方式的配置器标签都含宿主核心词」（跳过 host 返回空的 Chord），防两份实现再漂移。SelfTest 404→**405**。截图核验配置器第 4 条现"F2 · 组合键松开"、与第 3 条"组合键按下"及设置/帮助一致。构建 0 错。

**R155 批次末**：护栏 PASS（下限 405，FAIL=0 SKIP=1）；用户 4 条手势完好（`gesture apply n=4`）；重发布 staging 11:42:15 → zip 69.7MB/250 项/CRC 全绿/exe=1/sdk=1 → 重启出厂实例 pid 26936。TODO 头同步 405。HEAD `52879e1`。

---

**R157 · 用户反馈 5 条（设置胶囊越界 / 截图后移入手势 / 重复动画 / Ctrl+P 贴图隐形 / 自定义提示时长）**
- **① 设置分类胶囊越界**：分类切换胶囊用 ElasticEase（过冲回弹）越过卡片边框。改 CubicEase EaseOut（单调不过冲），丝滑度保留、不越界。
- **② 「截图后」移入手势配置（架构级）**：原全局四选一（设置里 `AfterCapture`）与「每条手势各自决定截图后行为」冲突。移除设置里全局四选一（该行改为「已移至手势配置器」引导提示，`AppSettings.AfterCapture` 字段保留仅为反序列化兼容、不再被截图流程读）；每条手势新增独立 `AfterCapture`（跟随默认""/toast/editor/copy/pin），配置器详情面板下拉配置。链路 `GestureCore.GestureBinding.AfterCapture` → `GestureBindingDto`（持久化）↔ `BindingEdit`（编辑器）双向映射 + `GestureEngine.RunActions` 首行 `BeginBinding` → `GestureInvoker` → `CaptureController.SetPendingAfterCapture`；截图发起时把 `_pendingAfter` 固化到 `_sessionAfter`（跨「打开选区窗」这段异步窗口存活），完成时三处 switch（ScrollFinish/DelayFire/DispatchStill）按 `Resolve(_sessionAfter)` 分派。**为何不用 onComplete**：onComplete 在动作被 post 后立即 FIFO 跑，而 `capture.selection` 只打开选区窗（真截图在用户拖框后），onComplete 早于截图完成、读不到「这次」截图——故用完成时读的独立属性。`IActionInvoker` 加 `BeginBinding`（GestureCore 无默认接口方法，故 GestureInvoker + SelfTest `GestureProbe` 两处实现者均补）。**e2e 核验**：capture.selection 设 AfterCapture=pin → 截图后直接贴图（trace `pin-at 700x500`+`pin shown opacity=1.00`），无 toast、无 open editor；配置器下拉 UI 截图核验（「截图后（仅截图类手势生效）→ 跟随默认（弹提示）」）。旧 JSON 无字段→默认""→toast，与用户当前行为零漂移。
- **③ 点同页重播进入动画**：`ShowCat` 每次点击重播分类进入动画。prev==idx 直接返回。
- **④ Ctrl+P 贴图隐形（根因）**：`Fluent.FadeIn` 的 AllowsTransparency 分支再订阅 `ContentRendered` 启动淡入，但 PinWindow 是在**自己的 ContentRendered 回调里**才调 FadeIn——该事件一次性、已触发，淡入 handler 永不跑→Opacity 永久卡 0→贴图 `vis=True` 却完全隐形。8 个 FadeIn 调用者唯 PinWindow 在 ContentRendered 内调（其余 Show 前调、IsLoaded=false 走订阅路径不受影响）。修：`if (w.IsLoaded) { StartFade(); return; }`（局部函数名 StartFade 避开下方非分层分支的 Start，否则 CS0136）。**e2e 核验**：trace `pin shown opacity=1.00`。
- **⑤ 自定义提示消失时间**：`ToastRow` 预设芯片外补自定义秒数输入框（写 `AppSettings.ToastSeconds`）。**核验**：设置截图显示「3.2 秒」输入框。
- **附带·测试脚本隔离**：护栏 `e2e:tap-semantics` 场景3 稳定 FAIL（frames=-1）。`git stash` 回干净 HEAD 跑出**完全相同**签名→确认非本次改动引入。根因：场景间用 `keyw`(PostMessage，不触发 WPF Esc) 关窗，上一场景编辑器关不掉→下一场景 edit.lastvideo 复用旧窗、不再产生 `open editor frames=` trace。修：场景间 `Restart-App` 隔离（各场景本应独立）。修后 hold600=9 帧、hold1300=47 帧（按住越久帧越多，正是点按/按住语义）、PASS。
- **验证**：构建 0 错；**SelfTest 405 绿**（GestureProbe 补 BeginBinding）；护栏 **PASS FAIL=0 SKIP=1**（10 项，含修好的 tap-semantics）；配置恢复完好（4 手势、globalAfter=toast、ToastSeconds=3.2）。

---

**R158 · 全站 UI 现代化 + PCL2 式「QQ 弹弹」动效（用户指令：UI 太平像老工具、要现代感 + 到处弹弹；标题全不带前缀）**
- **标题全不带**：设置窗自绘标题「设置」、帮助「使用速查」（原带「FrameCapture」前缀）；识别文字/手势配置本就无前缀。OS `Window.Title`（任务栏/Alt+Tab）保留前缀作品牌。
- **动效地基（Fluent.cs）**：新增 `Motion.SpringMs=260`、`Spring(amp)`（BackEase EaseOut）、`AnimateScale(el,to,ms,ease)`（默认 CubicEase、可传 Spring）、`Elevate(b,blur,depth,opacity)`（DropShadow 卡片投影，深/浅主题分别 0.55/0.26）、`Springy(el,hover,press)`、`Lift(b,dy)`（hover 上移，安全用于整宽卡片不横向越界）、`PopIn(el,from,ms)`（scale from→1 + Spring 弹入）。
- **到处弹弹（IconButton/ChipHover 全局生效）**：IconButton hover→1.08 Spring(0.4)、按下→0.9、松手→1.08 Spring(0.7)、离开→1；ChipHover 同款。编辑器/贴图/toast/标题栏/选区所有图标按钮 + 色块芯片自动获得弹性。
- **设置窗层次**：快捷键卡 `keyCard`、手势卡 `geBtn` 加 `Elevate(24,6)`；geBtn 加 `Lift` hover 上浮；导航选中条 selBar 加 accent 辉光 DropShadow（BlurRadius 9）。
- **Toast/Welcome 弹入**：CaptureToast、WelcomeToast `ContentRendered→Fluent.PopIn(card)`（scale 0.85→1 弹性登场）。
- **菜单弹性入场**：ContextMenu 模板根 Border 加 Loaded 触发器——Opacity 0→1（Cubic 110ms）+ Scale 0.92→1（BackEase 0.45、200ms、从锚点左上角 `RenderTransformOrigin 0,0` 弹出）。pin 右键菜单/托盘菜单都会「弹开」。
- **控件弹性**：滑块拇指 hover 13→16 改 BackEase 0.6（弹出）；开关 SlideToggle 过冲 BackEase 0.35→0.5（更 QQ）；手势配置器「组合键」录键框加 hover 强调色描边（可点提示，`!_capturing` 守卫不干扰捕获态）；手势配置器下拉框弹层加弹性缩放入场（`DropDownOpened` 驱动 0.94→1 BackEase 0.45、`FindName`/`is Border` null 守卫优雅降级，与 ContextMenu 同款手感；设置窗无下拉框故全站下拉仅此一处）；OCR 结果窗「复制全部」按钮加 `ChipHover(1.05)` 弹性（此前是静态 accent 块无反馈）；设置「试听」按钮加 `ChipHover(1.04)`（原有 hover 背景+焦点边框，补缩放与全站芯片一致）；外观页「强调色」色片加 `ChipHover(1.12)`（此前仅光标变手型、零反馈，与编辑器色板同款缩放弹性、色片小故取 1.12 介于色板 1.18 与按钮 1.04 之间）；产物页保存路径「更改」文字链接加 hover 才显下划线（默认干净、悬停才提示可点，比常驻下划线克制；文字链接不用缩放以免字糊）。
- **焦点态一致性（指令④）**：系统排查设置窗所有可聚焦元素（`Focusable=true`）的焦点环——toggle track、各主题/选项 chip、试听按钮本就配同款 accent 焦点环（`GotKeyboardFocus`→accent 描边、`LostKeyboardFocus`→还原）；本轮补齐此前缺环的两处：① 强调色色片（可 Tab 聚焦+Enter 开取色，却无可见焦点态）；② 左侧导航项（可 Tab 聚焦+Enter 切页，却只有鼠标 hover 才有的背景、键盘用户 Tab 时看不到焦点落在哪）。至此设置窗可聚焦元素焦点环全覆盖、与全站 accent 焦点态一致。手势配置器录键框（`_chordBox`，可 Tab 聚焦+Enter 录键）同样补焦点环——因其 hover 改描边，改用 hover+focus 共用一套刷新（任一成立即 accent，避免 MouseLeave 误清焦点环）；绑定列表是 `ListBox` 键盘可达无缺口，「启用」开关是鼠标专属 StackPanel（补键盘可达需改 track 视觉，次级界面暂不动）。
- **时间轴播放头现代化（编辑器细节）**：旧播放头是「白色竖线 + 顶部下三角」（PR 指针），三角在深色工具条上偏「老工具」味。改为「细竖线 + 顶部圆形旋钮」——旋钮 8px `Theme.Text` 填充 + `Theme.PanelBg` 描边留白环（与设置窗滑杆圆点同族的现代滑块语言），竖线起点下移到旋钮下方成「棒棒糖」指针，精确对帧不变。颜色仍跟 `Theme.Text`（R19 浅色主题逐帧审查教训：纯白在浅色面板会隐身）。`Show→DrawRange` 每帧重绘，播放/拖动实时跟随。UICap 实拍核验旋钮落位、留白环清晰。
- **设置窗逐页复查（R68 核验轮，无改动）**：实拍快捷键/外观/系统三页确认已现代化——导航选中=accent 卡片+左侧竖条+图标/文字 accent 变色、hover 灰卡片；快捷键页 keyCard 浮起+键位 accent 右对齐；外观页主题分段控件（选中 chip accent 实心反白）+ 强调色色片 ChipHover；系统页开关 track+knob 现代态。未发现「太平/老工具」残留，不强改。
- **OCR 结果窗文本框圆角（评估后主动跳过）**：该窗是圆角 12 卡片内嵌一个方角 TextBox，与设置窗「自定义秒数」输入的「无边框 TextBox 套圆角 Border」语言不一致。但①大文本区方角是常见合法设计、②套圆角 Border 后滚动条方角会在圆角处露出（ClipToBounds 只裁矩形不裁圆弧）、③OCR 结果模式在本沙箱环境跑不起来（WinRT OCR 载不了 Microsoft.Windows.SDK.NET），无法截图核验——属「不可核验的视觉改动」，按纪律不 ship。若日后用户在正常环境测出确需圆角，再连同滚动条内衬一并处理。
- **手势配置器开关键盘可达（R69，指令④焦点态）**：`MakeToggle`（启用手势层/独占吞键/启用此绑定）此前是鼠标专属 StackPanel（`MouseLeftButtonDown→Flip`，不可聚焦），键盘用户 Tab 遍历时这三个开关是缺口。照搬设置窗开关同款：track `Focusable=true` + `KeyDown`（空格/Enter→Flip）+ 常驻 1px 边框做焦点环（`GotKeyboardFocus`→accent、`LostKeyboardFocus`→还原 `on?accent:stroke`，`focused` 标志避免鼠标切换时把焦点环刷掉）。OFF 态焦点环 accent 可见、ON 态与填充同色（与设置窗开关同一已知取舍，靠旋钮位置表意）。绑定列表是 `ListBox` 本就键盘可达；选中项 accent 底 + 黑字（`ItemStyle` 的 `#FF000000`，实测黑字在 #7F75FF 上对比约 5:1、可读）。构建 0 错、护栏 PASS；开关渲染核验无破版（1px 边框仅给 OFF 态加一圈淡描边、与设置窗一致）。
- **延时倒计时数字弹入（R70，指令②弹性/③进出场）**：`CountdownWindow.SetNumber` 此前只 `Text=n.ToString()`，3→2→1 干巴巴换字、像老工具。改成每个数字弹入：`ScaleTransform(0.6→1.0)` + `BackEase(Amplitude 0.55, EaseOut)` 过冲、时长 `Motion.SpringMs`，与下拉/Toast PopIn 同款手感；`RenderTransformOrigin=(0.5,0.5)` 居中缩放。**核验方法**：倒计时窗 WDA 排除、UICap 默认拍成全黑，但它认 `FC_TOAST_NOEXCLUDE=1`（跳过排除）→ 用该环境变量重启 `--delay` 即可拍；连拍 30 帧量 accent 数字像素高度，见 `40→52→50→49`（小→过冲→落定）与 3→2 处 `47→52`，弹跳坐实、数字全程可读。构建 0 错、护栏 PASS。
- **贴图右键菜单快捷键改右对齐（R71，指令⑤简洁少字）**：`PinWindow.ShowMenu` 此前把快捷键塞进标签括号——「复制 (Ctrl+C)」「旋转 90° (R)」「更新内容 (F3)」「关闭 (Esc)」，正是用户嫌的「括号里的字」。改成 Windows 菜单惯例：标签只留动作名，快捷键走 `MenuItem.InputGestureText` 右对齐淡字。为此给 `Fluent.MenuResources` 的 MenuItem 模板加一列 `Text="&#123;TemplateBinding InputGestureText&#125;"`（Column 2，TextDim 11px，箭头挪到 Column 3；无快捷键的项该列零宽、不位移）。顺带补上此前没显示的 Ctrl+S（另存为）/Ctrl+O（识别文字）。**「关闭全部贴图 (N)」的 (N) 是数量不是快捷键、保留**（破坏性批量操作前给个确认数）。
  - **踩坑**：菜单 XAML 在 C# 内插原始字符串 `$"""` 里，`{TemplateBinding}` 被当 C# 插值（CS1073）；`{{}}` 又报 CS9006（原始串要更多 `$` 才允许连续左括号）。正解是 XML 字符实体 `&#123;`/`&#125;`——XML 解析先解码成 `{`/`}`，XAML 再当标记扩展，且不含字面左括号、C# 不插值。
  - **核验**：贴图 `AllowsTransparency` 但**无 WDA 排除**，故 `UICap desk`（整屏）能拍到它和它的菜单（`win` 按标题拍分层窗仍黑）。剪贴板塞图→`--pin-clip`→`Shot rclick` 贴图中心→desk 拍→裁菜单：实测快捷键全部右对齐淡字、标签无括号、不透明度带 ▶ 子菜单箭头、(1) 计数保留。T12 兜底菜单模板可解析。构建 0 错、护栏 PASS。
- **托盘菜单去括号赘述（R72，指令⑤简洁少字）**：`TrayService.ShowMenu` 是常驻高频入口，却满是解释性括号——「立即捕获（按当前范围）」「滚动截图（长图）」「暂停全局热键（游戏/演示时）」「以管理员身份运行（防遮挡）」，正是用户嫌的「括号里的各种字」。删掉这 4 处解释性括号（信息在帮助/设置里都有，标签本身已自明）。**「延时截图（3 秒）」保留**——它是动态数值不是赘述，与贴图「关闭全部贴图 (N)」同一惯例（括号只留给有用的动态数字，不留给解释性文字）。
  - **新增 `--tray-menu` 调试入口**（`ShowMenu` 改 public + App.xaml.cs 一条，与 `--settings`/`--help`/`--gesture` 同列；用户也可绑热键唤出）。菜单 `Placement=MousePoint`、锚窗无 WDA 排除 → 先把光标 `Cursor.Position` 挪到安全点再启动，`UICap desk` 即可拍到整张托盘菜单。实拍核验：4 处括号已删、「延时截图（3 秒）」保留、分隔线分组清晰、菜单圆角卡片正常。构建 0 错、护栏 PASS。
- **滚动截图控制条 QQ 弹弹（R73，指令②弹性）**：`ScrollHintWindow` 卡片/淡入本就不错，但两处「太平」——① 进度「已拼接 N 段」每拼接一段干换字、无反馈；② 完成/取消按钮 hover 只是 `Opacity 0.88` 淡出（变淡像退后，正是老工具味）。改：① `SetProgress` 段数变化时数字弹一下（`ScaleTransform 0.72→1.0` + `BackEase 0.5`，`Motion.SpringMs`，右缘锚定 `RenderTransformOrigin=(1,0.5)` 不撞标题），滚动拼接时每段「啵」一下，同倒计时数字弹入；`_lastSegs=1` 对齐初始态、首帧不弹。② 按钮改用 `Fluent.Springy(hover 1.05, press 0.94)`（hover 放大、按下回缩、松手 BackEase 弹回），删掉模板里的 opacity 触发器。
  - **新增 `--scroll-hint` 调试入口**（App.xaml.cs，与 `--delay` 同列）：主屏中央造个区域直接 `Show()` 控制条 + `DispatcherTimer` 每 850ms 递增段数模拟拼接（不启动真实滚动捕获），`OnDone/OnCancel=CloseSelf`。控制条 WDA 排除但认 `FC_TOAST_NOEXCLUDE=1` → 用该环境变量启动即可拍。
  - **核验**：`--scroll-hint` + `FC_TOAST_NOEXCLUDE=1` 启动。按钮 hover——`Cursor.Position` 移到「完成」上、`UICap desk` 拍，按钮明显放大（1.05 生效）。进度弹——连拍 34 帧量 accent 数字像素高度，见 `20→15→20`（15≈0.72×20 弹起点，落回 20），弹跳坐实。构建 0 错、护栏 PASS。
- **截图浮窗 hover 描边亮起（R74，指令①hover 反馈）**：`CaptureToast` 整张卡片可点→进编辑器（`Cursor=Hand`、`MouseLeftButtonUp` 触发 onEdit），但卡片本身 hover 毫无反应——只有手型光标、卡片不响应，正是「太平」。改：hover 时描边从淡灰 stroke 动画亮起强调色（`ColorAnimation` → `Theme.Accent`，`Motion.HoverMs`），离开 160ms 淡回。把「点我标注」的可点性显出来，且只改描边颜色、不动几何（不用 Lift/缩放，避免 SizeToContent 窗里投影被裁）。
  - **核验**：`FC_TOAST_NOEXCLUDE=1` 启动→`Ctrl+Alt+A`→拖框→浮窗弹出→`Cursor.Position` 移到浮窗中心（hover 暂停自动消失）→desk 拍：描边明显紫色强调色；再把光标移开重拍：描边淡回灰。hover 触发、非长亮，坐实。构建 0 错、护栏 PASS。
- **首启欢迎卡按钮弹性 + `--welcome` 入口（R75，指令②弹性 / 首因体验）**：`WelcomeToast`（首启弹一次，新用户头一眼）卡片/PopIn/强调色热键行都好，但「查看帮助与全部快捷键」按钮还挂着旧的 `RoundedBtnTemplate` hover `Opacity 0.88` 淡出（同滚动控制条旧毛病）。改：删模板里的 opacity 触发器，代码里 `Fluent.Springy(hover 1.05, press 0.94)`，与全站按钮统一。
  - **新增 `--welcome` 调试入口**（App.xaml.cs，与 `--settings`/`--scroll-hint` 同列）：直接 `WelcomeToast.Show(CaptureKeyText(), ScopeKeyText())`，**不写 `FirstRunHintShown`**（纯查看、不消耗用户首启状态）。借此把首因界面完整审了一遍：标题「FrameCapture 已就绪」+ 两行强调色热键（截图 Ctrl+Alt+A / 选区 Ctrl+Shift+R）+ `·` 分隔的简短手感提示 + 强调色按钮——干净现代，新用户一眼知道按什么键。（标题带 FrameCapture 属首启问候语、非重复标题栏，命名 App 正当，保留。）
  - **核验**：`--welcome` + `FC_TOAST_NOEXCLUDE=1` 启动。按钮弹性——光标移开拍基线、移到按钮上拍 hover，量 accent 按钮像素宽 `223→235`（1.054×≈Springy 1.05），放大坐实。构建 0 错、护栏 PASS。
- **编辑器工具条浮起投影（R77，指令①层次，撤销旧「刻意跳过」）**：编辑器底部工具条 `Panel(tools)` 只靠 1px 顶边（stroke 0x66）与画布分隔，截到深色画面时几乎融为一体、显「太平」（此前把「工具条投影」列为刻意跳过，但实拍确认确实糊成一片，值得做）。改：给 `_toolPanel` 加**向上**投影 `DropShadowEffect{Blur 20, Depth 6, Direction=90, Opacity 暗0.6/亮0.3}`（与 `Elevate` 同族、方向朝上投到画布上）。画布底色恒为深 `0x14`，故暗底用较浓投影、亮底较淡。
  - **核验**：拖一段含浅色任务栏的区域→进编辑器→`UICap win` 拍。量工具条上缘竖向亮度：画布 `20→12` 平滑变暗约 20px（向上投影渐变），紧接工具条浅色顶边（62）与条底（41）——工具条从画布「浮起」、不再融为一体，坐实。浅色主题下工具条本就浅色、与深画布强对比，投影不抢戏。构建 0 错、护栏 PASS。
- **浮层卡片统一投影（R78，指令①层次 + ④一致性）**：编辑器取色弹层 `_colorPop`、粗细弹层 `_widthPop`、设置强调色弹层 `_accentPop` 三处 `Popup.Child` 都是「PanelBg 圆角卡 + 1px 描边」的平卡、无投影，贴在画布/窗口上显「太平」。新增 `Fluent.PopupCard(content, innerPadding?)`：内层 PanelBg 圆角卡 + 柔和投影（Blur 16/Depth 4/暗0.45·亮0.22），外层透明留白 Padding 18 给投影腾渲染空间——**Popup 按子元素 DesiredSize 开分层窗、Effect 不参与布局，投影直接挂内层会被窗缘裁掉**，外层 Padding 撑大 DesiredSize 投影才有地方画；外层不设 Background（null）→ 留白处点击穿透，`StaysOpen=false` 点卡片外仍正常关闭。三处弹层改用同一 helper。
  - **核验**：设置→外观→点强调色色片开取色弹层，`UICap desk` 拍。卡片四周出现柔和暗晕（投影、未被裁）、与色片之间留出干净间隙——浮层从窗口「浮起」，坐实。编辑器取色/粗细弹层走同一 helper，同效。构建 0 错、护栏 PASS。
- **分段选择芯片补 hover（R79，指令②弹性 + ④一致性）**：设置「截图」页三组分段芯片——取色格式（HEX/RGB/HSL）、悬浮窗存在时间（常驻/2秒/…）、延时截图（关/1秒/3秒/…）——此前只有手型光标 + 键盘焦点环 + 选中态，**悬停零视觉反馈**（实拍 hover 前后逐像素 diff=0 坐实）；而同页/外观的主题芯片（跟随系统/浅色/深色）却有 hover 底色动画，不一致。给三组芯片各加 `Fluent.ChipHover(chip, 1.06)`：hover 放大 1.06、按下缩 0.9、点击回弹（scale 走 RenderTransform，与选中态改 bg 互不冲突；1.06 增幅约 1.5px/边 < 芯片间 4px 间隙，相邻不压边）。
  - **核验**：设置→截图→光标移到「5秒」芯片，`UICap win` 拍基线/hover 两帧逐像素 diff：芯片边缘 454px 变化（放大所致），hover 反馈坐实。构建 0 错、护栏 PASS。
- **hover 反馈扫尾（R80，指令②弹性 + ④一致性）**：顺着 R79 的线索把设置窗里所有「可点但悬停零反馈」的控件补齐——
  - **录制格式芯片**（GIF/MP4/WebM，「录制」页）：同 R79 分段芯片，加 `Fluent.ChipHover(chip, 1.06)`。核验：光标移到「GIF」，逐像素 diff 357px（边缘放大）。
  - **「截图提示音」开关**：这是页内手写开关，此前只有手型光标 + 焦点环，缺主 `ToggleRow` 的 hover 边框提亮。抽出 `Fluent.ToggleBorderHover(track, isOn)` helper（与主开关内联同款：进入边框 alpha +0x60、离开还原 on=accent/off=stroke），本开关调用之，与全站开关一致。
  - **「保存位置」路径**（「产物」页）：整行可点弹文件夹选择器，但路径文字悬停无反应（仅右侧「更改」链接有下划线 hover）。加 `stack` 的 MouseEnter/Leave 把路径文字 TextDim↔Text 提亮，提示这块也能点。核验：光标移到路径，逐像素 diff 1623px，取样点 142→231（暗→亮）坐实。
  - 构建 0 错、护栏 PASS。
- **设置整页卡片化（治「太平」主招）**：新增 `Cardify(panel)`——把截图/录制/产物/编辑器/贴图/外观/系统 7 个纯行页的所有设置行整体移入一张浮起卡片（沿用快捷键页 keyCard 模式：CardBg + 描边 + Elevate），与快捷键页视觉统一、不再裸铺深色背景。快捷键页自带卡片不调。截图核验录制页/截图页（FPS 滑杆、格式/取色/时长芯片、自定义秒数输入、试听按钮等复合行均适配良好）。卡片规格与快捷键页 keyCard 对齐（圆角 6、实描边、Elevate 24/6），切页时卡片圆角/描边/阴影深浅不再跳变。
- **简洁少字（用户新反馈：别让老用户被括号里的各种字烦）**：全面清理**常驻可见**文案的括号赘述。手势配置器：章节标签（组合键/动作序列/完成后序列/截图后/优先级）、触发下拉（轻点/按住/松手/组合键，去「（短按即松）」等——10 项仍两两不同非空且含宿主核心词，T151/T155 绿）、列表占位（无动作/未设键）、录键框占位（点击设置）、校验语（手势层当前关闭）、截图后下拉（跟随默认）。动作目录标签「开始/停止录制（同键起停）」→「开始/停止录制」、「打开编辑器（上次录制）」→「打开编辑器」（同步 SelfTest T150a；描述里对旧标签名的引用一并更新）。设置窗快捷键页描述与手势配置器入口按钮去括号；截图页「截图后」行常驻提示「已移至「手势配置器」，每条手势可单独设置」→「见手势配置器」（详细说明留 tooltip）。**tooltip（hover）保留详细说明**（非常驻、非「看久了」）；帮助窗为教程文档暂留成段文字。截图核验手势配置器/设置快捷键页均已干净精简。
- **验证**：构建 0 错；**SelfTest 405 绿**；护栏 **PASS FAIL=0 SKIP=1**（三轮，含菜单/滑块/开关、强调色色片/更改链接批次）；XAML 运行时解析核验（PowerShell 加载程序集调 `Fluent.MenuResources()`/`StyleSlider()` 不抛，Count=3/模板 OK）；截图核验设置（卡片浮起+导航辉光）、编辑器（IconButton 弹性、布局未动）、toast（PopIn 落定态卡片正常）。选区窗 PrintWindow 拍全黑（已知分层快照限制、非 bug）。编辑器工具面板 `Panel()` 本就有顶部 1px 分隔线、未强加 DropShadow（避免整块位图渲染糊化图标）。**Release 构建端到端实拍**：F2 录制手势流→编辑器（时间轴+工具条两行结构清晰、色板选中带环、图标等距）；Ctrl+Alt+A→选区窗→拖框→截图→toast 弹出（rect 432×99）——两条捕获路径均正常；toast 亦为分层合成窗、PrintWindow 拍全黑（同选区、非 bug）。
- **未提交**：R157+R158 全部改动待用户确认后提交（工作区脏）。

---

**R81–R84 · 全站 hover/press 反馈核验扫尾 + 交付就绪快照（无代码改动）**
- **核验轮（R81–R83，指令②④）**：顺着 R79/R80「可点却零反馈」的线索，把剩余界面逐处过了一遍，**未发现新缺口**（故无改动，符合「不强行改不可核验/边际项」纪律）：
  - 设置 8 页逐页实拍：每个可点控件都有 hover（R79/R80 已补齐 4 组分段芯片 + 截图提示音开关 + 保存路径）。编辑器/贴图/系统页内容少（1–2 开关）但开关均有 hover，留白是设置项本就少、非缺陷。
  - 切页动效 `ShowCat`：已是 PCL2 级（旧页淡出 EaseIn + 新页淡入并上浮 12→0 + 标题随正文淡入 + 导航强调色/图标/竖条同步过渡 + 重复点同页不重播）。
  - 编辑器交互元素（grep 全量交叉核对）：工具/调色板/线宽/⋯/字号 A−A+ 全走 `IconButton`（hover+press）；色片、最近用色走 `ChipHover`；选中工具走 `HighlightBtn`（accent 底 + 图标反白，StateMs 淡变）。画布底色恒深 `0x14` 是图像编辑器惯例（工具面板本身走 `Theme.PanelBg` 随主题），非 bug。
  - 标题栏关闭钮（共享 `Fluent.TitleBar`→`IconButton`）hover+press 齐全；全局 `ScrollBarStyle` 已是现代细圆角无箭头样式（设置/帮助/OCR 共用）；选区窗提示「拖框选择 · 悬停点击吸附窗口 · Esc 取消」点号分隔无括号赘述。
- **交付就绪快照（R84，用户 20:30 回来测）**：Release **pid 9476** 运行中，跑的就是 R80 护栏 PASS 的二进制（staging 20:05:10，之后无代码改动、无需重发布）；配置完好（4 手势、启用手势层/独占吞键 True、accent #7F75FF、theme system、afterCapture toast、delay 3、悬浮窗 3.2 秒）；工作区无残留临时截图；未提交改动 = R69–R82 全套打磨（28 文件）。
- **沙箱边界（非 bug）**：选区窗/悬浮 toast/编辑器实机这三处，合成热键触发不了（手势层低级钩子按安全惯例忽略注入输入；护栏 e2e 的应用内捕获正常）。它们已在 R74/R77/R78 目视核验过，本轮补做代码级复核无新问题。
- **两处待用户定夺**：① 帮助窗 A 精简成短句 / B 保持成段教程（倾向 B，读完即关的文档）；② tooltip 是否也精简（倾向保留，非常驻）。

---

**R95 · 稳定性核验：error.log 历史异常均已修复，今日运行零新异常（无代码改动）**
- 用户尚未开测（settings.json 仍停在 20:04:59 护栏还原态；error.log 最后写入停在昨天 10-12 22:01，今日 pid 9476 这次运行**没有落任何新异常**）。趁空把 error.log 里最近两条异常查了根因，确认**当前源码都已修**、非本轮打磨引入：
  - **10-12 03:45 `ObjectDisposedException: Cannot access a closed file`**：栈是播放 `DispatcherTimer.Tick → EditorWindow.Show→Bmp→FrameSpoolList[i]→FileStream.Seek`（读已关 spool）。现码 tick 首行 `if (_userClosed || _seq.Frames.Count==0) return;`（[EditorWindow.cs:120](src/FrameCapture/Overlays/EditorWindow.cs#L120)，注释「R61 磁盘 spool 后读已关文件会抛」）+ 两条关闭路径都 `_playTimer.Stop()`（:144/:1241）。DispatcherTimer 与 Closed 同在 UI 线程、tick 不会与关闭交错，守卫充分。
  - **10-12 22:01 `XamlParseException 无法从文本"SelectionBoxItem"创建"DependencyProperty"`**：开手势配置器时 ComboBox 模板解析抛。旧写法把 `SelectionBoxItem` 当字面串/`Setter Property`；现码 [EditorStyles.cs:38-39](src/GestureEditor/EditorStyles.cs#L38-L39) 已用正确的 `{Binding SelectionBoxItem, RelativeSource=…}`，grep 全仓无残留坏形。
- **结论**：运行中的 Release（今日 20:05 从含上述修复的源码构建）稳定；这两条是历史遗留、已闭环，无需再动。TODO 里「error.log 偶发 ObjectDisposedException（待查）」实为已修（R61），可视为已闭环。

---

**R160 · 修「截图抢焦点→截到别的软件」（用户反馈：截浏览器时因切焦点截到了另一个软件）**
- **根因**：选区窗打开时 `ForceForeground(_picker)` + `Activate()` + `Keyboard.Focus(this)` 抢前台，把焦点从目标窗（浏览器）夺走、前台/Z 序随之变，选区关闭后 `LiveCapture.Grab(region)` 抓到的是被顶到前面的另一个窗。
- **修法（QQ/Snipaste 同款：选区窗不激活）**：`ShowActivated=false` + SourceInitialized 里加 `WS_EX_NOACTIVATE`，去掉 ForceForeground/Activate/Keyboard.Focus——选区窗 Show 与鼠标按下都不激活，前台焦点始终留在目标窗。选区窗的 Esc/Enter/C/F/Tab 改由常驻 `WH_KEYBOARD_LL` 全局钩子代收（`HotkeyManager.PickerActive`/`PickerKey`，与滚动截图控制条、悬浮 toast 同一套代收模式），键经 `Dispatcher.BeginInvoke` 转干净调度回合执行；`OnPickerKey` 只认裸键（Ctrl/Alt/Win 组合放行给手势与前台），Closed 里清零 PickerActive/PickerKey 绝不泄漏成"全局键失灵"。编辑器仍 `ForceForeground`（用户要编辑，理应拿焦点）。
- **验证**：Debug 实拍——`--picker`（新增调试入口）开选区窗后 `ex=0x08080008`（WS_EX_NOACTIVATE 已置）、窗可见且 Topmost，但 `GetForegroundWindow` 仍是别的窗（非 FrameCapture）＝不抢焦点；`Shot hkm 27`（Esc）经全局钩子关闭选区窗＝代收生效。构建 0 错；**SelfTest 405 绿**；护栏 **PASS FAIL=0 SKIP=1**（focus-safety 前台保护本因 SKIP，与基线一致）；e2e 里真实 Ctrl+Shift+G 触发选区窗（`pk=rect 0,0 3840x2160`）通过。重发布 staging 21:17:24 → 重启 Release pid 63548；用户配置完好（4 手势、启用/独占 True、accent #7F75FF、区域保留）。
- **未提交**：R157+R158+R160 全部改动待用户确认后提交（工作区脏）。

---

**R161 · 用户 21 条优化清单一次落地（手势配置器/设置页/帮助页/贴图吸附/胶片条）**
- **手势配置器（1–10）**：下拉/右键菜单弹出动画溢出——Popup 外套透明 Padding 让 overshoot 有处可飞（combo 16 边距+负偏移，ContextMenu 右下 24）；「已有配置」行升级卡片（边框+底色+序号与内容分主次+按钮描边悬停动画）；移除左下角「✓ 配置有效」，有问题的配置整卡红描边；输入框圆角+悬停/聚焦描边色过渡；选中高亮按明度自动黑/白字（OnAccent）；按钮间距+悬停只改底色不再盖住下拉框；「截图后」行仅当序列含截图动作才显示；禁用行改文字变灰去掉「已禁用 ·」前缀；触发方式文案精简为「按下/松开」等。
- **设置页（13/15–20）**：移除「截图」页的「截图后」行（入口只留手势编辑器）；滚动条 8px 细身；悬浮窗时长收敛为 常驻/1/2/5秒/自定义（自定义框数字居中）；提示音改下拉 关闭/轻弹/自定义…（选中弹文件框选 ogg/wav，取消回退原值，选定即试听；wav 走 winmm 必响，ogg 依赖系统解码器、缺则静默回退轻弹）；延时截图支持自定义秒数（1–120）；录制开「跟随刷新率」时帧率行淡出隐藏、关掉淡入恢复；亮色主题阴影全面调浅（Elevate 0.13/PopupCard 0.12/ToolTip 0.14/菜单 0.15）。
- **胶片条（21）**：删全局「自动生成 Agent 胶片条」设置；编辑器视频工具栏加「生成胶片条」按钮 → 小窗问「抽帧数 2–48 / 每 N 帧取一次 1–60」（参数写回 settings 记忆）→ 按裁剪+标注同口径烘焙抽样帧 → 渲染一张逐帧图**直接复制剪贴板**（不落盘）。
- **贴图吸附（12）**：「拖动贴图自动磁边」改名「贴图窗口吸附」；参考线从「屏幕边+其他贴图」扩展到**同屏所有应用窗口**的边/中线（WindowEnum 加 skipIconic，自家浮层 WS_EX_NOACTIVATE 天然排除）；阈值 12%/24–56 调低到 8%/16–40——线多了要「贴边才吸」不乱跳（T110f 断言同步更新）。
- **常显捕获区域（14）**：根因＝开关只在下一次捕获收尾被消费、拨动瞬间无反馈被判定无效。CaptureController 订阅 SettingsService.Changed 做边沿检测：开→立刻显示已存范围轮廓，关→立刻收起。
- **帮助页（11）**：一屏文字墙重写为 9 步渐进式教程——布局对齐设置页（左大图标+大标题+副题，右正文一步一事），右下角 上一页/下一页（最后一步变「完成」），←/→ 键翻页，步骤位置跨次打开记忆；过时文案（胶片条自动生成/磁边/快捷键设置）全部跟上新行为。
- **托盘（16）**：「快捷键设置…」→「设置…」。
- **验证**：构建 0 错；SelfTest **405/0**（T110f 按新阈值公式更新）；护栏 **PASS FAIL=0 SKIP=1**；renderhelp 离屏取证版式正常（翻页箭头改普通 Unicode 防豆腐块）。

---

**R162 · 用户 9 条反馈：亮色适配收尾 + 小按钮动效统一 + 滚动条真凶 + 贴图吸附/动画**
- **试听按钮文字错位（图1）**：横向 StackPanel 里 Border 被拉满行高、文字默认贴左上——Border 与 TextBlock 都 `VerticalAlignment=Center`；顺带删掉瞬时换底色的 MouseEnter/Leave（与 ChipHover 打架）。
- **accent 上文字黑压紫（图3）**：`OnAccentColor` 从「0.299R+0.587G+0.114B>128」换成 **WCAG 相对亮度**（sRGB 线性化，0.2126/0.7152/0.0722，阈值 0.35）——#7F75FF 亮度 0.243→白字；橙 #FF8C00→黑字；青 #00B294→白字。宿主 Fluent 与库 EditorPalette 两处同步（T162b/c 锁行为）。
- **小按钮不再放大（图4）**：`ChipHover` 重写为「悬停/按住只改底色带过渡」（基色进入时捕获、外部换底不强行回旧色、冻结刷子先复制再动）；`Springy` 同口径改为委托底色过渡（FilmStrip/滚动控制条/欢迎卡小按钮一并生效）；`SlideToggle` 删掉滑块随状态换色——**滑块恒白+极淡描边**（QQ 式），亮色主题下黑滑块不再像 bug（设置页+手势配置器两处）。
- **滚动条「基本没变」真凶（图5）**：探针实锤——主题 ScrollBar 样式的 `MinWidth=17.33`（系统宽）经 DynamicResource 以**直值**泄漏到模板内 ScrollBar，样式 Setter 优先级压不住直值。修法＝附加属性 `Fluent.Slim` 在样式应用回调里 `MinWidth=0/MinHeight=0`（本地值能压直值）。新模板：轨道 8DIP、滑块 4DIP 圆角胶囊、无箭头、竖/横双模板。库不能引用宿主 Fluent → `EditorStyles.ScrollBarStyle(palette)` 自带同款（含自家 Slim），挂窗口 Resources 覆盖列表/详情/下拉弹层。T162a 走视觉树断言 ActualWidth≤8.5。
- **配置项切换颜色过渡（图6）**：手势配置器 `PaintRows` 三态（选中/问题/禁用）改 140ms 颜色动画（冻结刷子先换可动画副本）；开关轨道换色、焦点描边同带过渡。
- **去掉「打开手势配置器…」悬停上浮（图7）**：`Fluent.Lift` 调用删除，改 ChipHover 底色过渡。
- **贴图吸附（图8）**：①**只吸边不吸中线**——`SnapToLines` 删 center/middle 锚点，SnapTarget 参考线只留屏幕边+窗口/贴图边（用户点名「屏幕边缘、可视窗口边缘就够了」）；②**拖动卡顿根因**＝每个 MouseMove 全量 EnumWindows+DWM 查框——改为**拖拽起点快照缓存** `_winGuides`，松手清空。T113c/f 断言按新行为更新（中线不再吸）。
- **贴图开合动画（图9）**：入场落图幅度调小（0.82→0.94、BackEase 0.55→0.25、220→180ms）；新增 `CloseAnimated`（淡出+微收缩 120ms 后真关），双击/Esc/右键关闭统一走它；「关闭全部」保持即时关。
- **验证**：构建 0 错；SelfTest **408/0**（新增 T162a 细滚动条视觉树探针/T162b WCAG/T162c 库同口径；T113c/f 更新）；护栏 **PASS FAIL=0 SKIP=1**；亮色实拍两处——手势配置器（accent 白字/白滑块/右侧 8px 细滚动条）+ 设置页截图页（试听文字居中、开关白滑块、HEX 段白字）。

---

**R163 · 用户 4 条反馈：ChipHover 变白棘轮 + 帮助窗布局重排 + 配置器开合动画 + 贴图磁吸手感（参考 Slick Window Arrangement）**
- **手势编辑器按钮悬停变白不回原（图1）**：`BgHover` 棘轮 bug——MouseLeave 把「当前色」当基色存回，反复悬停逐次变白。重写为**实例身份跟踪**：`ours` 刷子 + 固定 `baseColor`，MouseEnter 仅在 `!hasBase || !ReferenceEquals(sb,ours)` 时重捕基色，MouseLeave 仅同实例才还原；外部换底＝新刷子实例→自动重捕基色。`ChipHover`/`Springy` 均委托它。
- **帮助窗布局重排（图2）**：①去掉旧 `main.Height=322`+`Dock.Top` 硬钉（主体不填充→内容少时底部一大片空）——改 DockPanel 填充子项（foot 先加、main 最后加）；②短页垂直居中：`ScrollViewer.VerticalContentAlignment` 对 StackPanel 内容**不生效**（内容被拉伸填满），改用 **Grid.MinHeight 绑 ViewportHeight + 内容 VerticalAlignment=Center**（短→撑到视口高居中、长→随内容滚）；③左栏 168→184 + 9 步 sub 全部缩到 ≤~13 字（「三步就会：定范围→触发→复制走人。」→「定范围→触发→复制。」等），左大标题不再溢出；④上一页/下一页同 padding 同高、放 `VerticalAlignment=Center` 的 StackPanel→上下对齐。renderhelp 改按真实固定 660×430 量（反射设 `_lastStep`）取证：短页(步7)/长页(步0)均居中、左右同带对齐、底部不空。
- **配置器开合动画（图3/4）**：库不能引用宿主 Fluent → 新建 `src/GestureEditor/Native.cs`（自带 `SetAlphaZero`＝WS_EX_LAYERED+LWA_ALPHA=0）。`SetupMotion` 入场：SourceInitialized/LayoutUpdated 里 PreTransparent（强制分层，否则 WindowChrome 非分层窗的 `Window.Opacity` 不参与屏幕合成＝淡入看不见），ContentRendered 后淡入 0→1 + 内容 scale 0.96→1；退场 `CloseWithFade` 淡出后再对内容做 scale 0.97 / translate Y12 微收（纯 150ms 淡出在大窗上几乎无感）。**实机取证**：Shot 发 Esc 关闭，burstregion 连拍——帧 009/011 全不透明 → 帧 012 整窗明显变暗半透明（桌面透出）→ 消失＝淡出走屏幕合成、确实在跑。
- **贴图磁吸手感（用户两轮纠偏 + 参考 `D:\source.txt` Windhawk Slick Window Arrangement，不抄其拖动滑动/惯性）**：真因＝旧实现「任何线只要距离够近就吸」→「到处吸附」。按 Slick 手感重写 `PinSnap`：①**参考边带垂直跨度**（`Edge{Pos,Span0,Span1}`），只有本贴图该轴边与参考边**垂直方向真正重叠**才吸（`span0>=e.Span1||span1<=e.Span0` 跳过）＝杜绝乱吸的核心；②**同类边对同类边**（左对左 `_snapXL`、右对右 `_snapXR`、上对上、下对下 + 屏幕工作区四边），近/远边各取阈值内最近者互斥；③**无滞回**（重叠判定天然稳定，不抖）；④拖拽起点预计算 4 条边列表、仅跨屏重建（热路径零枚举）。删死代码 `SnapToEdge/SnapTarget/SlideTo`。新增 **T163a–g** 7 条（重叠才吸/不重叠不吸/同类边/超阈值/取最近/相切不算重叠）。
- **验证**：构建 0 错；SelfTest **415/0**（+T163×7）；护栏 **PASS FAIL=0 SKIP=1**；renderhelp 短/长页居中取证 + 配置器关闭淡出 burstregion 实拍取证。

---

**R164 · 用户 3 条：截图后边框显示时间选项 + 编辑器取消选中工具后编辑消失 + 贴图仍吸到莫名其妙的地方**
- **新增「截图后边框显示时间」设置项**：`AppSettings.OutlineFadeSeconds`（double，默认 1.1）。截图后范围边框「亮边闪一下作确认」的全亮停留时长，此前硬编码 `FadeAway(1.1)` 三处（选区确认×2 + DispatchStill）全改读该值；设置页截图页「常显捕获范围轮廓」下加 `SliderRow` 0.2–3.0 步 0.1「X.X 秒」。与「常显轮廓」「录制边框」各自独立。
- **编辑器取消选中所有工具后编辑全消失**：根因＝标注是画在 `_canvas` 上的 WPF 图形，`SetTool` 退回 Browse 时 `_canvas.Visibility=Collapsed` 把整张画布（连同所有标注）藏了（模型 `_anns.Items` 没丢、导出仍正确，纯视觉 bug）。修法＝画布**常显**，Browse 态改 `_canvas.IsHitTestVisible=false` 代替 Collapsed：标注照常显示，鼠标事件穿透到 `_viewGrid` 走平移/缩放（`Canvas_Down` 在 Browse 本就 return，冒泡不受影响）。删掉 `RedrawAnns` 末尾「绘制态恢复 Visibility」的死兜底。T165a–d 反射构造编辑器锁死：绘制态可见+可命中+有标注 / 退回 Browse 画布仍可见+标注仍在+不可命中。
- **贴图仍吸到莫名其妙的地方（第三轮纠偏）**：两处根因——①`BuildWinGuides` 拖拽起点快照**没排除自己**（贴图 `ShowInTaskbar=false` 在 WPF 不产生 TOOLWINDOW、带标题「FrameCapture Pin」→ 被 `ListTopmostCandidates` 收录）→ 拖起来吸回自己起点；②把同屏**所有**窗口整条边都当参考，包括被压在后面的背景窗 → 吸到看不见的边。修法＝①`ListTopmostCandidates(self, skipIconic:true)` 排除自身 HWND；②按 EnumWindows 的 Z 序（上→下）做**遮挡裁剪**（Slick `RemoveOverlappedTargets` 思路）：一条窗口边被更靠上窗口盖住的区间剔除，只把「看得见的边段」当参考。抽成纯函数 `PinSnap.VisibleSegments(lo,hi,covered)`（区间相减，起点升序），`PinWindow.AddVisible` 调用它。T164a–g 锁死遮挡裁剪（无覆盖/全覆盖/中段断两段/头尾段/越界钳制/覆盖区间合并/退化区间）。
- **验证**：构建 0 错；SelfTest **426/0**（+T164×7 遮挡裁剪 +T165×4 编辑器画布）；护栏 **PASS FAIL=0 SKIP=1**（含 e2e:editor-keys / esc-layers 编辑器回归绿）。

---

**R165 · 中等功能：macOS 式「真·窗口截图」——点窗口截本体、圆角透明、投影渐隐（grilling 定细节后开工）**
- **需求**：选窗口后截图不再因边框半透明阴影掺进背景内容，而是真正截取窗口本体，边缘阴影正常截进来。grilling 4 轮 + 终确认定的共识：①入口＝选区窗里**点**吸附到的窗口→截整窗，**拖框**→区域截图，其它入口（全屏/托盘/手势/滚动/延时）不变；②主开关「点击窗口时截取完整窗口」默认开（关＝点窗恢复旧按矩形区域截图）+ 子开关「窗口截图包含投影」默认开；③本体＝WGC 按窗口捕获；④投影＝窗口边缘柔和阴影、外缘渐隐到透明（实现上按轮廓合成，见下，非抓屏幕）；⑤本体矩形用 `DWMWA_EXTENDED_FRAME_BOUNDS`（含边框不含投影）；⑥遮挡 OR WGC 失败→回退区域截图；⑦透明 PNG + 剪贴板额外放 PNG 格式保透明；⑧复用现有截图后流程；⑨UIA 子元素吸附时点击仍走区域截图，只有吸附到整个窗口才走窗口截图。
- **WGC 按窗口捕获**：`WgcNative.CreateItemForWindow(hwnd)`（仿 `CreateItemForMonitor`，`f.CreateForWindow`）。新建 `Capture/WindowCapture.cs`：独立 MTA 线程（避开 WPF STA 激活 WinRT 死锁）建 `CreateItemForWindow`→`Direct3D11CaptureFramePool.CreateFreeThreaded(B8G8R8A8UIntNormalized)`→`CreateCaptureSession`→`IsCursorCaptureEnabled=false`+`SetBorderRequired(false)`→`FrameArrived`→`SoftwareBitmap.CreateCopyFromSurfaceAsync`→`LockBuffer(Read)`→`ReferenceBytes`→逐行 `Marshal.Copy`（stride=fw*4）。静态构造预加载 `Microsoft.Windows.SDK.NET.dll`（同 WgcLiveCapture，否则 apphost 解析不到投影程序集）。**实机取证**：WGC 窗口 surface 带真实 alpha——Win11 圆角处 A=0、本体 A=255、浏览器/GPU 窗也干净。
- **投影＝按窗口轮廓合成（不是抓屏幕）**：`SyntheticShadow` 取本体 alpha 当覆盖率 → 3 次盒式模糊（≈高斯，`BoxBlur` 滑窗和 O(n)）→ 黑色 × 覆盖率 × strength(0.5) 写进透明画布，轻微下移 offsetY≈margin/8 更像真实投影。外扩 `margin=24*dpi/96`、模糊半径 `margin/3`（3 次模糊外延≈3r≈margin，到画布边正好淡到 0）。轮廓含圆角（本体圆角处 alpha=0）→ 阴影自然贴合圆角。**首版曾「抓屏幕真实像素当投影环 + FeatherAlpha 渐隐」，用户实测打回**：窗口压在别的软件（如 DSH）上时，投影环把背景文字整片截进来、圆角处露出抓到的深色真实投影＝发黑。合成阴影彻底不读屏幕，两个症状一起消失。纯函数 `Intersect/Inflate/BlitBody/SyntheticShadow/BoxBlur` 拆出可单测。
- **选区窗接线**：`Result` 事件 `Action<RectI?>`→`Action<RectI?, IntPtr>`（region, hwnd）；新增 `windowCapture` 构造参数 + `WindowHitForCapture(p)`（`SnapPick.PickWindow` 命中→取 `Bounds` 整窗 frame→`SnapPick.AnyAboveIntersects` 遮挡判定，被更靠上窗相交则返回 null 回退）。点/双击且开关开且未遮挡→`Finish(frame, hwnd)`；否则旧区域路径。`CaptureController` Result 处理 `(region,hwnd)`：hwnd≠0 且开关开→`WindowCapture.Grab`（失败 catch 回退 `LiveCapture.Grab`），用实际捕获矩形做放置/轮廓；`DispatchStill(..., windowCapture)` 透传剪贴板标志。
- **剪贴板保透明**：`ClipboardService.SetFrame(frame, includePng)`——includePng 时 `DataObject` 放 `"PNG"`（`BgraImage.PngBytes`，保 alpha，现代应用）+ 白底拍平 `CF_DIB`（`BgraImage.FlattenOver`，只认 DIB 的 Paint/微信不会把透明区看黑），`SetDataObject(data,true)`。
- **设置项**：截图页「吸附到控件」下加两条 `ToggleRow`——「点击窗口时截取完整窗口」(`WindowCaptureEnabled` 默认 true)、「窗口截图包含投影」(`WindowCaptureShadow` 默认 true)。
- **顺带修**：`Win32.GetWindowTextW` 缺 `CharSet.Unicode`→中文标题被 ANSI 截断（潜在 bug，影响按标题找窗）；补 Unicode。新增 `--wincap <标题子串> <out.png> [--noshadow]` 调试/e2e 入口（同 `--picker`/`--gesture` 类）。
- **验证**：构建 0 错；SelfTest **433/0**（+T165w1–w7：相交/外扩/贴本体裁剪/合成阴影单调衰减纯黑/PickWindow/遮挡判定/端到端合成含圆角不发黑）；护栏 **PASS FAIL=0 SKIP=1**。**实机取证**：`--wincap` 截记事本→四角 A=0 圆角透明、本体干净（magenta 合成图确认）；**投影修复后**把记事本移到 DSH 密集文字背景上 `--wincap`→**白底合成图**确认环里零背景文字、是一圈干净柔和阴影、四角圆角干净（不再发黑）；**真实选区窗 SendInput 点击**记事本→遮挡判定放行→控制器→WindowCapture→透明 PNG 角 A=0/中心 A=255；发布版 `--wincap` 冒烟角 A=0/中心 A=255（无裁剪/AOT 破坏）。

---

**R166 · 中等功能：选区时定格画面（放大镜跟画面 bug 修复 + 定格所见即所得 + 定格瞬间预抓窗口）**
- **需求（用户）**：「放大镜的放大内容不会跟随画面改变，因为遮罩不会定格画面，新增一个开关，决定选区时是否定格画面，并且这个开关会改变截图的行为，定格的话截取的就是定格的画面而不是重新获取画面」。根因＝放大镜读的是开框后一次性抓的 `_snap`（定格），但遮罩是透明的（屏幕实时可见）、截图又实时重抓 → 三者不一致。grilling 共识：①`FreezeOnSelect` 默认**开**；②定格开＝开框瞬间抓一张全屏定格帧、屏幕看起来就定格、放大镜读定格帧、**区域截图裁定格帧**（与窗口截图开关正交——就算窗口截图关了，区域截图也裁定格帧，用户明确「窗口截图就算关了，截图也要是定格的画面截取」）；③定格关＝全程实时，放大镜每次移动实时重抓跟画面（修 bug）；④定格+窗口截图都开时，定格瞬间后台按 Z 序（聚焦窗最优先）预抓「完整可见」窗口的干净 WGC 画面缓存，点窗口即用＝定格+干净，没缓存好实时抓兜底；⑤`FreezePregrabMaxWindows` 默认 **6**，设置页可调（0=不预抓）。
- **定格帧裁剪**：`BgraImage.Crop(src,x,y,w,h)` 纯函数——请求矩形 `[x,x+w)×[y,y+h)` 与画布求交（负偏移正确收缩、不向右多取），逐行 `BlockCopy`，越界钳、裁空返回 null。控制器定格分支 `Crop(frozenFrame, region-origin)` 得定格内容，`capReg` 同步收缩到实际裁出矩形（跨屏窗超出本屏部分裁掉、轮廓不超框）；定格帧没到位（极快点选）→ `LiveCapture.Grab` 兜底。
- **选区窗双模**：新增 `freeze`/`pregrabMax`/`windowShadow` 构造参数。定格模式 `StartFreezeSnapshot` 抓全屏帧铺成 `_freezeBg`（`Image` 铺在遮罩下、随遮罩 150ms 同步淡入）→ 屏幕看起来定格；`UpdateLoupe` 分叉——定格读 `_snap`（`CroppedBitmap`），实时每次移动 `LiveCapture.Grab` 光标周围 15px 小区域重抓（跟画面，修掉不跟画面的 bug；本窗 WDA 排除、放大镜画在光标偏移处不拍自己）。`StartWindowPregrab` 后台枚举 `ListTopmostCandidates`（Z 序顶在前）→ 本屏 + 未被更靠上窗遮挡（`AnyAboveIntersects`）＝完整可见 → `PregrabOrder` 排序 → 逐个 `WindowCapture.Grab` 存 `ConcurrentDictionary` 缓存；`_finished` 即停、`Closed` 清空释放。暴露 `FrozenFrame`/`FrozenOrigin`/`TryGetCachedWindow`。
- **预抓排序**：`SnapPick.PregrabOrder(focused, zTopFirst, max)` 纯函数——聚焦窗最优先（若在候选里）、其余按 Z 序自上而下、截断到 max、max≤0→空。控制器窗口点击分支先 `TryGetCachedWindow`（命中＝定格+干净圆角+投影），未命中实时 `WindowCapture.Grab` 兜底。
- **设置项**：截图页「选区放大镜」下加 `ToggleRow`「选区时定格画面」(`FreezeOnSelect` 默认 true) + `SliderRow`「定格时预抓窗口数」0–16（`FreezePregrabMaxWindows` 默认 6，仅定格开时显示，仿「跟随刷新率→隐藏手动帧率」淡入淡出）。`Normalize` 钳 0–16。
- **调试/e2e**：新增 `--freezetest <out.png>`——抓全屏定格帧裁出区域 R（＝定格截图内容），再盖一层不透明品红全屏窗改变画面、实时重抓 R（＝实时截图内容），输出并排图 `[定格裁出 | 实时]` 并记 `staticMatch`/`changed`。
- **验证**：构建 0 错；SelfTest **435/0**（+T166w1-crop 正常/越界钳/负偏移收缩/空 null、+T166w2-pregraborder 聚焦最优先/无聚焦/聚焦不在候选/max=0/超量）；护栏 **PASS FAIL=0 SKIP=1**（e2e 默认走定格路径出图正常）。**实机取证**：`--freezetest` 并排图左＝定格裁出的真实桌面内容（深色窗搜索/筛选图标 + 中文文字，像素对齐正确）、右＝品红（盖窗后实时重抓），trace `staticMatch=True changed=True`＝裁剪像素级正确 + 定格确实截定格帧非重抓；`--picker` 真机 `picker freeze snapshot ready (3840x2160)` + `picker pregrab cached=1/1`＝定格快照 + 窗口预抓管线端到端跑通。

**代码清洁轮 · 潜在 bug / 重复代码 / 死代码 / 冗余写法 / 算法优化（约束：不改现有行为）**
- **手法**：死代码用「Roslyn `IDE0051/0052/0060`（临时提严重性构建）+ 剥注释后的全仓引用索引」双证，避免正则误判；重复块用归一化滑窗哈希 + run 极大化；诊断与执行分离——先只读分析出报告，再逐条现场核验后派工，**诊断报告的假阳性由现场否掉**（`GraphicsCaptureItem` 声称可 `Dispose` → 编译报错证明本仓投影里它不实现 `IDisposable`；`RegionPickerWindow._snap != null` 声称恒真 → 现场只有 Loupe 里一处真守卫）。
- **死代码**：净删 173 行。11 个零引用 `public`/`internal` 成员（`Monitors.ClipTo`、`Fluent.Lift`、`Fluent.ToggleBorderHover`、`GestureService.ClaimsKey` 及连带 `_engineBound`/`ModsMatch`、`EditorWindow.RemapAnnsForExport` 等）+ 5 个只写不读私有成员 + 2 个编译器已报的死变量/死局部函数 + `Win32` 12 项无人调用的 P/Invoke 与常量（含 `DWMWA_WINDOW_ROUNDED_CORNER_PREFERENCE` 与 `DWMWA_WINDOW_CORNER_PREFERENCE` 同值 33 合并）+ 4 个被实现完全忽略的参数（`Fluent.Springy` 的 `hover`/`press`、`ChipHover` 的 `scale` 等，连带 11 处调用点）。**零引用 `public`/`internal` 成员 11 → 0。**
- **重复代码**：净删 104 行。`Fluent.BorderlessOverlay`（4 个浮窗各 8 行逐字相同的窗口外壳）、`Monitors.DpiScaleOf`（3 处相同的 DPI try/catch）、`Fluent.RoundedButtonTemplate`（两份"看着一样"的模板，实际一份有 hover 淡出触发器一份没有——按 `hoverFade` 参数化保留差异而非合并）、`Fluent.RoundButton`（两处 `MakeBtn` 只差 `padX` 与 `hoverFade`）、设置页 `ChipRow`/`Chip`/`WireChip`/`RefreshChips`（5 处标签块 + 4 处芯片循环）。**≥8 行跨文件重复块 33 组 → 3 组**（剩余 3 组是 `CloseSelf` 三行守卫与 HWND 钩子前导，理由见 TODO）。执行侧还挡下一处：悬浮窗秒数那行的芯片匹配是 `Math.Abs(cs-v)<0.01` 容差而非 `==`，套 `Equals` 会改变自定义框高亮归属，**拒绝套用**。
- **潜在缺陷**：① 捕获线程 `FrameSampled?.Invoke(f)` 裸调——订阅方是逐帧落盘，`IOException` 会让后台线程未处理异常**直接终止进程**、录到一半全丢；新增 `LiveCapture.Faulted` 事件，复用本仓「磁盘满自动停录」链路（回 UI 线程 `FinishCapture()`），已录内容照常进编辑器。② 贴图 `Refresh()` 先 `Hide()` 再起 200ms 定时器无条件 `Show()`，期间关窗则对已关闭 `Window` 调 `Show()` 抛异常被全局处理器吞掉、贴图既不显示也不报错；加 `_closed` 守卫并在 `Closed` 停表。③ `WgcInit` 线程 `IsBackground=false`，`done.Wait(5000)` 超时后该前台线程仍卡在 WinRT 里 → 托盘退出后进程残留并撞单实例锁；改后台线程（同仓 `WindowCapture` 同构代码本就是后台）。④ 全局低级**键盘**钩子回调里每次按键查 `FC_TRACE_KEYS` 环境块（钩子超时会被系统静默摘钩，且同函数注释声称热路径只有几次整数比较）；改 `static readonly`，判定语义 `== "1"` 照抄。
- **算法/性能**：① GIF median-cut 每轮重扫全部色桶求跨度 → `ColorBox` 构造时算一次并缓存（`Span()` 是 `Colors` 的纯函数、构造后不变，可证等价，`>=` 一个未改）。② 每帧 `new int[2097152]` + `Array.Fill(-1)` 的 8MB LOH 最近色查找表 → `[ThreadStatic]` 复用 + 世代戳失效（调色板索引 ≤255 放低 8 位，`gen<<8|idx`，跨帧调色板不同由世代自动失效），沿用同函数 `_errBuf` 的既有范式。③ 编辑器 12 处 `_seq.Frames[_index]` 每次鼠标移动把整帧从磁盘读回（`FrameSpoolList` 索引器 = `new byte[]` + `Seek` + `ReadExactly`）→ `Cur()` 按帧号缓存；安全性由两条事实支撑：全仓无任何代码就地改写 `CapturedFrame.Bgra`（所有 `BlockCopy` 都以 `.Bgra` 为源）、`_seq` 是 `readonly` 且只在构造函数赋值。④ 滚动拼接 `FindOverlap` 给整张累积长图建行签名却只读底部 `win+1` 行 → 加 `rowBase` 参数只建尾部窗口，绝对行号边界检查原样保留、仅索引换局部行号，整段捕获 O(n²)→O(n)。
- **等价性取证（本轮的方法论）**：性能改动不靠"测试没红"过关，而是先建**黄金输出**基线并验证其自身可重复——GIF 4 组合成帧（暗渐变/硬边色块/满屏噪声/渐变+稀疏红点，覆盖抖动、median-cut、平坦区跳过三条路径）编码字节 sha256，滚动拼接 10 步每步 ov + 最终缓冲 sha256（其中出现 `ov=738/701` 这类非理论值，正好压住容差趟与 ≥80% 抽样判定，任何行为漂移都会现形）。两处改完均**逐字节一致**。量化：4K×30 GIF 编码 9520→9085ms；`accH=40000` 单次 `FindOverlap` 10.6→2.6ms；编辑器单次取帧 1.97ms / 7.91MB，鼠标拖动每步省 2 次整帧回盘与 2 个 8MB LOH 分配。
- **明确不做**：`ScrollStitch.Append` 的整块复制（契约即"返回新平坦缓冲"）、浮窗 `SourceInitialized` 的 HWND 钩子前导（前 6 行相同但 `AddHook` 回调体各不相同）、`TrayService.GetHicon()` 一次性句柄泄漏（正确修法要 Clone+`DestroyIcon`，改坏托盘图标直接不可见）、`WgcNative` 空 `Closed` handler（无法在不跑 WGC 的情况下证伪）、编辑器拖拽重建整棵标注视觉树（改坏是肉眼可见错位而 SelfTest 抓不到，需先有截图比对基线）、以及 TODO 里 A/B 类剩余各条——**修它们本身就是改行为**（补护栏=新增失败分支、删形参=改签名、补 `Dispose`=改退出时序），与本轮硬约束冲突，留待逐条确认预期。全部理由记入 `TODO.md`「代码清洁轮 — 已完成与明确不做」，避免后续当遗漏重查。
- **验证**：8 个 commit，每步 4 项目 build 0 error、SelfTest 恒 `PASS=435 FAIL=0`、`lint-comments FAIL=0`；批次 C 后跑 `test-tap-semantics`（真实录制 6/40 帧、`err_delta=0`）、批次 D-2 后跑 `test-editor-keys` + `test-zoom` 均 PASS；轮末护栏 **GUARDRAIL PASS（FAIL=0 SKIP=1 共 11 项）**。

**滚动拼接错位修复 + 滚动截图取景/预览/自动滚动（用户点名）**
- **拼接错位（既有 bug，用户指出后定位）**：`FindOverlap` 从最大候选往下扫、第一个抽样匹配率 ≥80% 的候选立即返回。文字页绝大多数行是纯背景（签名近似常量），这类行在任何纵向偏移下都与同类行相等——**没有定位能力，却给错误候选投赞成票**；错位 1~3 行的候选照样过线，而 100% 匹配的真值候选更小、永远轮不到。返回过大的 `ov` 后 `Append` 按它跳过新内容 → 长图丢行、从第一道接缝起整体平移（实测 1856 行的长图错 442 行，误差清一色正向 +1/+2/+3）。修法两处成对：① 签名极差 <8 的行不参与判定（真值处 b 行与 acc 行同内容，过滤不影响真值得分，只摘掉错误候选骗到的赞成票）；② 有信息行**全匹配**的候选立即返回，≥80% 降为兜底。整段无信息的退化场景关掉过滤再扫一趟，保住"滚过一大片空白"仍返回大重叠而非 0。
- **方法论（本轮关键）**：这次**不能**拿黄金输出当验收——**冻结的基线本身就编码了这个 bug**（旧基线 `ov=738` 而真值 700，`h=1262` 而非 1300，当时被记成"既有的容差行为"）。改建**真值预言机** `test-out/StitchProbe`（引用真实 `FrameCapture.dll`，非复刻算法）：生成文字版式文档、按已知量滚动、比对返回值与 `accBottom+1−top`，并逐行校验长图等于原文档。12 场景（常规/小步/大步/稀疏/非整行步长/固定表头/表格分隔线/代码重复行/大片空白/移动光标…）全部 `错步=0 丢行=0 错行=0`。旧基线另存 `stitch-golden-baseline-buggy.txt`；新基线 10 步全 `ov=700`、`finalH=4000`＝文档全长一行不丢。GIF 黄金基线仍逐字节一致（编码器未受牵连）。
- **取景与预览**：新增 `ScrollStageWindow`（整屏四块矩形围出选区之外压暗 + accent 描边；`WS_EX_TRANSPARENT|NOACTIVATE|LAYERED|TOOLWINDOW` 鼠标穿透，用户照常滚目标窗；WDA 排除不入镜）与 `ScrollPreviewWindow`（选区右侧预览卡，放不下翻左侧、再放不下贴选区内右缘；`Stretch.Uniform` 保证长图再长也不溢出面板）。缩略走纯函数 `Thumbnail`，**两轴共用一个步长** `max(ceil(w/maxW), ceil(h/maxH))`——各轴独立取步长会让竖长文档横向拉宽 3 倍（900×6000 抽成 225×500），`T167w5-thumb-aspect` 锁宽高比守恒（按旧实现该用例偏差 380%）。
- **自动滚动**：按用户明确选择用 `SendInput` 注真实滚轮；`GOAL.md` 该条约束的适用范围已在原处划清（约束代理为自测驱动鼠标，不约束用户点名要的产品能力），决策记 `DECISIONS.md`。节奏挂在拼接的同一个 `ScrollTick` 上——稳定判定要求连续两帧一致，按固定节拍连续注轮会让画面永远在动、一段都拼不上；改为**发一步 → 等这一步落定 → 再发下一步**，决策抽成纯函数 `ScrollAutoPolicy.Next`（连续 4 拍无新内容判到底、`ov<=0` 步长减半、`newRows<3` 加档、步长恒 1..6），不另起定时器。开启时把光标移进选区，关闭/完成/取消/进程退出四条路径还原。注入被拒（目标窗以更高完整性级别运行）当场退出并说明，停稳等待设 12 拍（≈3.4s）上限——否则分别是"假到底"与动画页面静默卡死。x64 布局由公开探针 `Native.InputLayout` 锁（`INPUT` 40 / `mi`@8 / `MOUSEINPUT` 32），对不上整个停用注入；为两个数值而加的 `InternalsVisibleTo("SelfTest")` 已撤。
- **验证**：SelfTest **435 → 452**（+`T119g` 文字页重叠精确不丢行、+`T167w1..w5` 降采样与宽高比、+`T168a..` 结构体布局与步进策略），`GUARDRAIL PASS（FAIL=0 SKIP=1 共 11 项）`，`lint-comments FAIL=0`，4 项目 build 0 error。视觉取证 `test-out/preview-aspect-shot.png`：真实桌面下范围内文字清晰、范围外明显压暗、accent 描边贴合选区、预览卡内窄长竖条比例正确。

**设置窗逐项错位入场（UI 质感轮）**
- **缺口定位**：`Fluent.Motion.StaggerMs = 110` 定义了却全仓零使用点——"设置项逐项动画"这个用户点名的质感项一直悬空。新增 `Fluent.CascadeIn(items, risePx, itemMs, budgetMs)`：每项淡入 + 自下方 8px 抬起，第 i 项延后 `i*step`。步长取 `min(StaggerMs, (budgetMs-itemMs)/(n-1))`——项数少时错位明显，项数多时自动收窄，整段压在 `3*StaggerMs`（330ms）预算内，几十行的列表不会拖成秒级；预算比单项时长还短时无空间错位，退化成整段同时淡入而不产生负延迟。位移归零后撤掉 `TranslateTransform`，不留给后续挂缩放的代码当"已有变换"跳过。
- **接线**：设置窗 `ShowCat` 的容器级 12px 位移删除（容器与行同时位移会叠成明显跳动），容器只淡入、位移交给行；打开窗口靠 `ContentRendered` 起首轮入场（构造期起动画会在窗口可见前播完）。窗口级淡入与首帧压黑由构造函数既有的 `PopupEnter` 负责，此处只管行——两套窗口级入场叠加会让 Win32 alpha 与 WPF Opacity 互相抢，实测直接把整窗压成永久不可见。
- **验证**：SelfTest **457 → 460**，`T171a-cascade-staggers`（9 项，60ms 时首项已 >0.2 而末项 <0.05，收束后双双 >0.98）、`T171b-cascade-clears-transform`、`T171c-cascade-tight-budget`（预算 100 < 单项 190 时两项同时起跳，不出现负延迟）。逐帧取证 `test-out/cascade3/`：按 y 分四条横带量化，上两带 f.008 到位、下两带 f.011 到位，约 150ms 的自上而下落差点；f.001 全带 0.0 是 `PreTransparent` 正常压住首帧（透出黑色桌面），非黑面缺陷。明暗双主题定稿 `settled-dark.png` / `settled-light.png`，正文区均值 37.7 / 248.2，行内容均在位无卡半透明。
**帮助窗翻页逐项入场 + 逐行入场下沉到 Fluent（UI 质感轮二）**
- **复用而非复制**：把设置窗里的两个局部函数提升为 `Fluent.CascadeRows(Panel, risePx, riseX)` / `Fluent.ResetCascade(Panel)`，设置窗与帮助窗共用一份时序，局部函数删除（留着会变成无人调用的 CS8321）。`CascadeIn` 新增 `riseX` 横向分量，给翻页这类**有方向**的切换留方向感：下一页从右 10px 收拢、上一页从左 10px，纵向仍抬 6px。
- **帮助窗**：`ShowStep` 的整块 `_body` 推 12px + 整块淡入删除——整块位移比逐行浮现重得多，且整块淡入会连步骤大标题一起盖掉节奏；改为每行淡入 + 顺翻页方向收拢，`dir` 由 `Go(dir)` 传入。不走动画的重建路径调 `ResetCascade`，避免在飞的行停在半透明。首开靠 `ContentRendered` 起 `risePx: 6` 的纵向入场（窗口级淡入仍归构造函数既有的 `PopupEnter`）。
- **两个真实缺陷由断言逼出**：① `Freeze()` 之后挂 `Completed` 抛 `InvalidOperationException`（编译无征兆，跑到才炸）；② 清理回调只挂纵向动画时，`risePx: 0` 的纯横向调用永远撤不掉 `TranslateTransform`。现在两条动画各挂同一个幂等清理，`T172c` 专测。
- **验证**：SelfTest **460 → 463**（+`T172a` 起跳 X=-10/Y=6 且透明度<0.01、`T172b` 双向归零撤变换、`T172c` 纯横向也撤变换）。逐帧取证：首开 `test-out/help1/` 上带 f.007 到位、下带 f.010；翻页 `test-out/help2/` f.005–006 行透明度落零，f.007 上带先起（44.4）而下带仍 31.1，上带 f.010、下带 f.011 依次到位。定稿 `test-out/help2/settled-dark.png`。
- **顺带查出既有缺陷（未在本轮改）**：帮助窗定稿图正文区透出桌面文字。用独立截图路径（`tools/Shot` 全屏 GDI 裁剪）复核同样存在，非 `UICap` 区域截图伪影。根因 `Theme.WindowBg` alpha = `0xF2`（94.9%），帮助窗 `AllowsTransparency=true` + 窗口底色 `Transparent`，靠根 Border 的 `WindowBg` 填充 → 5% 透桌面。设置窗非分层窗故不透。波及所有用 `WindowBg` 的分层窗：帮助窗、OCR 结果窗、胶片条对话框、外观取色弹层。已记 UIAUDIT 待办。
**分层窗与弹出层去掉透桌面（UI 质感轮三）**
- **发现路径**：帮助窗定稿图正文区浮出背后 GUI 的文字。先用 `tools/Shot` 全屏 GDI 截图再按窗口矩形裁剪（独立于 `UICap` 的截图路径）复核，同样存在 → 排除截图伪影，是真实渲染。根因 `Theme.WindowBg` alpha `0xF2`（95%）、`PanelBg` alpha `0xE6`（90%），而帮助窗/OCR 结果窗/胶片条是 `AllowsTransparency=true` 的分层窗、`Fluent.PopupCard` 是 `AllowsTransparency=true` 的 Popup：分层表面背后没有模糊背板，alpha 就是直接透桌面。设置窗非分层窗故同色值不透——按窗口排查会漏，得按「这个表面是不是分层」筛。
- **改法**：`Theme` 新增 `Opaque(Color)` 与 `WindowBgSolid`/`PanelBgSolid`（由源色强制 alpha 得到，RGB 无从漂移），帮助窗与 OCR 结果窗的根底、OCR 文本框、胶片条卡片、`PopupCard` 面板共 5 处改用不透明变体。非分层窗一律不动，避免全站色偏。
- **验证**：SelfTest **463 → 465**（`T173a` 守 `PopupCard` 面板 alpha=0xFF、`T173b` 守两个变体 alpha 拉满且 RGB 与源色逐通道相等）。视觉：帮助窗明暗双主题定稿 `test-out/help2/after-dark.png` / `after-light.png`，空白带像素标准差 **0.00**（改前浮字），均值暗色 **32.0**=`0x20`、浅色 **249.0**=`0xF9`，与令牌色值精确相等（改前暗色 31.1，被桌面拉偏）；独立全屏路径裁剪同样标准差 0.00。图片字节数 67884 → 35436，噪声消失可量化。
**设置窗动效时长全部归到 Motion 令牌（UI 质感轮四）**
- **收口硬约束**：`SettingsWindow.cs` 里 8 处裸时长清零（该文件从 8 处 → 0 处）。映射按语义而非数值：行显隐淡入 200→`EnterMs`(190)、淡出 160→`ExitMs`(150)、导航胶囊位移 300→`StateMs`(150)、导航图标/文字换色 220→`StateMs`、导航项透明度 200→`StateMs`、翻页淡出 110→`ExitMs`。
- **为什么导航胶囊用 `StateMs`**：`Fluent.Controls.cs:321` 的开关滑块位移用的就是 `StateMs`，而胶囊是「指示条移到选中项」——同一件事的类比，两处必须同速，否则开关和导航的选中反馈节奏不一致。代价是胶囊从 300ms 收到 150ms（更跟手），换色 220→150、翻页淡出 110→150 同理，属于有意的节奏统一，不是等价替换。
- **验证**：SelfTest 保持 **465**（本轮无数值断言变化，改的是时长）。实机驱动真实分类切换：导航项 `Focusable=true` 且响应 Space/Enter，用 `Shot.exe keyw` 发 Tab×3 + Enter（PostMessage 到自家窗口，非 OS 级鼠标自动化）。逐帧首带：f.028 前稳定 45.0 → f.029 起淡出 → f.031 48.7 上升 → f.033 起 **52.0 平台**，与切换后定稿图首带均值 **52.0 精确相等** → 收束干净，无行停在半透明。
- **工具坑**：`UICap burstregion` 输出的帧尺寸与 `region` 不同（本次 1140×990 vs 更小），按 `region` 坐标写死的采样带会越界导致除零。连拍采样要按帧高换算或先探帧尺寸。

**逐项入场不得吃掉元素既有变换（UI 质感轮五）**
  - **要防的破坏**：图标按钮构造时自带 `ScaleTransform` 供 hover 缩放（`Fluent.Controls.cs:41`），缩放动画的守卫是「不是 `ScaleTransform` 就静默 return」。逐项入场直接覆盖 `RenderTransform` → 入场期间悬停无反应，收束置 null 则**永久**无反应。现在 `CascadeIn` 把位移子变换套进 `TransformGroup`，收束时摘掉自己、把原对象放回原位；`ResetCascade` 靠 `ConditionalWeakTable` 登记只摘自己造的位移。断言 `T174a/b/c`：入场中含原 `ScaleTransform`、收束后 `ReferenceEquals` 原对象、`ResetCascade` 后原对象仍在。
  - **逼出的根因**：探针显示 `new Border()` 未进任何树就有 `MatrixTransform`——`UIElement.RenderTransformProperty` 的默认值是**单位 `MatrixTransform` 而非 null**。于是 `Fluent.cs` 里 `fe.RenderTransform == null` 恒为假，紧凑弹窗 0.96→1 内容放大从未执行，`CloseWithFade` 依赖它的 0.97 缩放退场同样落空，而文档注释承诺了这两处。新增 `Fluent.HasNoTransform`（单位 `MatrixTransform` 视为无变换）替换该判断。
  - **断言从形状换成行为**：`T171b/T172b/T172c` 原判「不是 `TranslateTransform`」，包装成 `TransformGroup` 后会蒙混过关（加固后确实一度全绿但断言已失效）。改判合成矩阵 `OffsetX/OffsetY` 为零或 `Value.IsIdentity`；`T172a` 起跳位改判矩阵 `OffsetX=-10, OffsetY=6`，在调用返回时立刻读，不依赖泵帧时机。
  - **取证边界（本轮最重要的负面结论）**：窗级透明度动画在真实屏幕上量不到斜坡。浅色主题 `burstdesk` 25ms 连拍——打开：d.003 桌面 119.7 → d.005 满窗 248.0；Esc 关闭：d.001 248.0 → d.002 57.0 → d.003 桌面 75.4，两次都是一帧到位。`PreTransparent` 释放 alpha 时直接给满，WPF 的 `Opacity` 动画被盖住。故本轮窗级改动的证据是断言 `T175a/b` 而非逐帧；行级动画在窗口内部合成、屏幕上可见，前几轮逐帧证据不受影响。已记 UIAUDIT 待办。
  - **验证**：SelfTest **465 → 470**（+`T174a/b/c`、`T175a/b`）。全链 0 错误 / 0 非 NU1900 警告 / `PASS=470 FAIL=0` / LINT PASS / GUARDRAIL PASS。
**设置窗接入 Win11 Mica 背板（UI 质感轮六）**
  - **为什么值得做**：设置窗是 `WindowStyle=None` + `WindowChrome` + 原生圆角阴影的**非分层窗**，正是 Mica 的适用形态；此前窗口体是 `WindowBg` 的 95% 平涂，背后是什么完全无关。Mica 让桌面壁纸的模糊色调渗进窗口，是 Win11 原生对话框的表面材质。
  - **实现**：`Fluent.MicaBackdrop(Window)`——`DWMWA_SYSTEMBACKDROP_TYPE=2`（22621+），返回非 0 时退到 22000 的 `DWMWA_MICA_EFFECT` 布尔开关，再不支持即静默跳过、窗口按自身底色显示。分层窗直接跳过：分层表面背后没有 DWM 背板。同时把 `WindowChrome.GlassFrameThickness` 设为 `-1`——WPF 只在玻璃框覆盖的区域内把像素让给背板，`Thickness(0)` 等于整窗自绘不透明，背板设了也看不见。
  - **色调跟应用主题**：`DWMWA_USE_IMMERSIVE_DARK_MODE` 按 `Theme.IsDark` 设置并挂 `Theme.Changed` 同步（`Closed` 时摘钩子）。应用选浅色就出浅色 Mica，与系统的主题设置无关；否则应用与背板会一个深一个浅。
  - **底色必须让位**：窗口 `Background` 与内容根 `Border` 的 `WindowBg` 填充都改成透明——铺任何底色都会把背板盖成平涂。卡片仍是 `CardBg` 不透明，正文直接坐在 Mica 上，与 WinUI 对话框一致。
  - **取证踩的坑**：`UICap region` 走 PrintWindow，背板不属于窗口 DC，拍出来与设背板前完全相同；暗壁纸下真实屏幕截图也与平涂 `0x20` 几乎重合。确定判据是向 DWM **回读**属性：`T176a` 材质=2、`T176b` 色调随主题。视觉证据取浅色主题 `test-out/mica-light.png`——壁纸的粉紫→淡蓝渐变明显渗入窗体，文字与卡片可读性完好；暗色 `test-out/mica-real.png` 只作可读性核对。
  - **验证**：SelfTest **470 → 472**。全链 0 错误 / 0 非 NU1900 警告 / `PASS=472 FAIL=0` / LINT PASS / GUARDRAIL PASS。`src/Setup` 通过 glob 共享 `Fluent.cs` 且自带 `Win32Shim.cs`，新常量两处都要补，否则安装器编译不过。
  - **跨版本护栏（同轮补）**：透明底色只对「有背板可透」的窗口成立。Win10（TFM 下限 19041）拿不到背板，非分层窗会把透明画成纯黑，设置窗变黑块。新增 `Fluent.MicaSupported`（内部版本 ≥22000）与 `MicaBackdrop` 的布尔返回，调用方据此在透明与平涂 `WindowBg` 之间二选一；`T177a/b` 锁判定与返回。SelfTest **472 → 474**。
  - **升 TFM 买不到视觉**：区分大小写扫描本机 `Microsoft.WindowsDesktop.App.Ref` 的 8.0.24 与 10.0.11 两套引用程序集（ref 只含公开 API），`Backdrop`/`Mica`/`Acrylic`/`UIElementEffect`/`DesktopWindowTarget`/`SystemBackdrop` **一个都不存在**。WPF 至今没有公开背板 API，DWM 互调是唯一路径，因此 .NET 8 → 10 升级不会带来任何材质收益。
**编辑器工具条逐项落定（UI 质感轮七）**
  - **动机**：编辑器是全站最大的表面，此前整窗一次性淡入（`FadeIn` 240ms + 14px 抬起），二十多枚工具图标同时出现，重而不糊。改成窗口入场走完后再来一趟 6px 位移 + 左→右错位的落定波，幅度克制到只是"逐位归位"。
  - **时序**：`CascadeRows(_toolsBar, risePx: 6, itemMs: Motion.HoverMs, budgetMs: Motion.SpringMs + Motion.StaggerMs, leadMs: Motion.EditorEnterMs)`。`CascadeIn` 新增 `leadMs`（整段起跳前的等待，落在每项 `BeginTime` 上），`CascadeRows` 透传；`DoubleAnimation` 的 `From` 显式给 0，前置期内由默认 `FillBehavior` 顶住。
  - **踩到的真缺陷**：起波靠手动压 `Opacity = 0` + 定时器时，撤销/重做从第一帧就孤亮——`RefreshUndo()` 的 `SetGhost` 用 `BeginAnimation` 写 Opacity，动画盖过本地值，压下去的 0 被顶回。改成 `leadMs` 后动画从挂上那刻就拥有该属性，等待期没有可被抢写的窗口。逐帧数字：起波前三段亮度完全相同（孤岛消失），随后左 f7 → 撤销区 f9~f10 → 右 f11 依次落定，f12 后稳定。
  - **取证方式**：SelfTest 新增 `editorwave <前缀>` 模式，进程内按 25ms 步进渲染真实编辑器窗口 30 帧（不与外部截图器抢时序），并打印首/末按钮 Opacity 与窗口 Opacity。明暗双主题各 30 帧：暗色 `test-out/wave-dark2.png`（30 帧带时间标注堆叠），明色帧在 `test-out/wave4/`。窗口 Opacity 与工具条 Opacity 分离可见——f00 窗口已 0.5~0.6 不透明而工具条仍全透明，证明没有"先整块可见再清空"的闪烁帧。
  - **顺带**：`CascadeIn` 的 XML 文档里"已挂 TransformGroup 的项只淡入不位移"与逐项入场加固后的实现不符，按注释即事实修正。
  - **验证**：SelfTest **474 → 478**（`T178a` 工具条项数、`T178b` 左→右单调且右端未起步、`T178c` 走完全部复位、`T178d` hover 缩放变换未被入场吃掉）。全链 0 错误 / 0 非 NU1900 警告 / `PASS=478 FAIL=0` / LINT PASS / GUARDRAIL PASS。
**编辑器两排工具按阅读顺序错开落定（UI 质感轮八）**
  - **动机**：上一轮只给工具条补了逐项落定，多帧捕获时上方的播放条仍随窗口静止出现——下排在波动、上排是座孤岛。现在播放条先、工具条后，两排各自左→右，中间错开一个 `Motion.StaggerMs`。
  - **实现**：`_playPanel?.Child`（四列 Grid）走 `CascadeRows(..., leadMs: Motion.EditorEnterMs)`，工具条走 `leadMs: Motion.EditorEnterMs + Motion.StaggerMs`。播放条以列为单位落定（走带键 → 时间轴 → 计数 → 动作），与工具条把色板/线宽子面板当单项处理的粒度一致。
  - **踩到的假阳性**：`T179b` 最初探 `_bPlay` 按钮的 Opacity，实现还没生效它就绿了——`CascadeRows` 接管的是 Grid 的直接子元素（列组容器），按钮是后代，Opacity 恒为 1。改为探列组容器并加"尚未落定"上界（`> 0.05 且 < 0.99`）后，元素不入场就必然失败。教训已进 DECISIONS。
  - **取证方式**：`editorwave` 造两帧序列（单帧不构建播放条），打点改打被接管的列组容器。暗色：f00 两排皆 0 → f06 播放首组 0.76 而工具排仍 0.00 → f12 工具排左端 1.00 右端 0.00 → f18 全落定。明色同形（f06 播放首组 0.68）。逐帧拼图 `test-out/rows-dark.png`、`test-out/rows-light.png`（30 帧带时间标注，可见 f00–f04 两排皆空、无孤岛）。
  - **像素判据的边界**：两排亮度带在起波期被窗口淡入的公共漂移一起抬起，排间 110ms 的偏移在像素上分不出来；时序判据以进程内 Opacity 打点为准，帧图只用于"有没有孤岛/形态对不对"。
  - **验证**：SelfTest **478 → 481**（`T179a` 多帧部件齐备、`T179b` 上排在飞下排未起步、`T179c` 两排均复位）。`T178b` 的采样点跟随时序推到 `EditorEnterMs + StaggerMs + 40`，断言内容未动。全链 0 错误 / 0 非 NU1900 警告 / `PASS=481 FAIL=0` / LINT PASS / GUARDRAIL PASS。
**全站动效时长收进令牌，并加一道护栏（UI 质感轮九）**
  - **动机**：硬约束写着"动效必须引用 `Fluent.Motion` 令牌"，实际只有设置窗收敛过。扫描出 28 处裸时长散在 11 个文件，同一个语义还长出两个值——捕获浮条 hover 淡入引 `HoverMs`(110)、淡出写死 160。
  - **新增令牌**：`PressMs=70`（按压下沉，必须短于 hover 才跟手）、`QuickMs=120`（按了就要看到的即时过渡）、`DissolveMs=320`（瞬时提示退场，非交互响应，慢一点才不抢注意力）、`FlashMs=450`（捕获描边余晖的单向往落）。这四种节奏本来就在代码里跑着，只是没有名字。
  - **数值尽量重合**：11 个映射里 6 个与原值完全相同，其余位移 ≤20ms；唯一实质变化是 hover 换色 140→110，那是同义不同值的合并，不是改手感。
  - **架构边界**：`GestureEditor` 是独立工程（只依赖 `GestureCore`，宿主反向引用它），不能引宿主令牌类。给它建本库 `Motion`（13 处引用），逐项对齐宿主数值并写明"改这里要同步改宿主"——副本要有名字和归属，不能是一堆散落数字。
  - **护栏**：`scripts/lint-motion-tokens.ps1` 挂 guardrail 第 0 关（现共 12 项）。只认动画上下文：动效助手末位实参、`Duration(...)`、直接喂给动画构造函数的 `TimeSpan`；`DispatcherTimer.Interval` 是逻辑定时器，不在射程。注入探针验证会 FAIL 且 `Interval` 行不误伤。guardrail 的 SelfTest 地板 75→481：75 那个数形同虚设，用例掉一半都不会响。
  - **踩坑**：批量正则替换把参数逗号吞掉、又把回填逗号错补给不需要它的模式，12 处损坏全靠编译器抓出（grep 只找到 2 处，因为 `src\**\*.cs` 不递归）。详见 DECISIONS。
  - **验证**：0 错误 / 0 非 NU1900 警告 / `PASS=481 FAIL=0`（纯重构，断言数不变）/ LINT-COMMENTS PASS / LINT-MOTION PASS 文件=130 / GUARDRAIL PASS（FAIL=0 SKIP=1 共 12 项）。
  **窗级淡入淡出的「不可见」判定被推翻（UI 质感轮十）**
    - **动机**：UIAUDIT 挂着一条「窗级淡入淡出在真实屏幕上不可见，WPF 的窗口 `Opacity` 动画被 `PreTransparent` 盖住」。本轮先去 Win32 侧找那条被盖住的通道，结果推翻的是判定本身。
    - **通道探查**：`GetLayeredWindowAttributes` 对 `AllowsTransparency=true`、`Opacity=0.5` 的 WPF 窗返回 false；手工把 `WS_EX_LAYERED` 写进 `GWL_EXSTYLE` 再 `SetLayeredWindowAttributes`，返回 false 且样式位下一帧就丢。WPF 顶层窗走 DirectComposition 合成，没有重定向位图，`LWA_ALPHA` 那条通道根本不存在。三版候选实现（Win32 分层 alpha、逐帧写 `Window.Opacity`、内容元素淡入）逐版屏采都「无效」。
    - **翻案**：屏采用的是 `UICap burstdesk 1.6 25`，按 PNG 的 `LastWriteTime` 反算真实帧间隔是 **136–163ms（中位 144ms）**——4K 桌面编码一帧 ~120ms，设定 25ms 毫无意义。150–240ms 的动画整个落在一个采样区间里，必然表现为阶跃。据此判定的缺陷并不存在。
    - **新取证工具**：`scripts/probe-window-fade.ps1` 不落盘，`CopyFromScreen` 后在内存里算区域亮度与边缘能量，实测 ~55fps；开头自检单帧耗时、慢于 40ms 直接 `throw` 判结论无效；退场段要求边缘能量递减 ≥3 阶才 `PROBE PASS`。判据取边缘能量而非亮度均值——Mica 背板由 DWM 画、不参与窗级透明度，淡出时区域均值几乎不动，只有内容结构在化掉。
    - **同一构建的 A/B**：把生产改动 stash 掉重编译（确认 dll 时间戳已换，第一次因进程占用导致 MSB3027 复制失败、A/B 无效），原始代码的退场同样是平滑斜坡。暗色 4.69→4.47→4.29→3.64→2.45→0.78→消失，浅色 4.34→4.24→4.11→3.98→3.65→2.36→1.23→消失，`PROBE PASS` 递减 5 阶。**生产代码零改动**，`Fluent.cs`/`Win32.cs`/`Win32Shim.cs` 全部回到未修改状态。
    - **顺带查清**：`PopupEnter` 里 `w.Opacity = 0` 的基础值靠窗口 Opacity 动画的 `FillBehavior.HoldEnd` 抬回；把动画换成别的机制而不处理这个基础值，整窗会永久隐形（本轮踩过，屏采与 `PrintWindow` 双通道确认是真空白，不是截图伪影）。
    - **验证**：SelfTest **481 → 487**（`T180a-d` 入场半程/走完、退场半程/真关窗；`T181a/b` 钉住「WPF 窗不是 Win32 分层窗」这条通道边界，测试侧自带 P/Invoke，不为测试放宽生产侧可见性）。guardrail 地板 481→487。全链 0 错误 / 0 非 NU1900 警告 / `PASS=487 FAIL=0` / LINT-COMMENTS PASS / LINT-MOTION PASS / GUARDRAIL PASS（FAIL=0 SKIP=1 共 12 项）。
  **编辑器接上 Mica 背板，画布四周保持中性暗底（UI 质感轮十一）**
    - **动机**：UIAUDIT「背板推广到其余非分层窗」。本轮做编辑器（面积最大、约束最多的一条），并把贴图窗与安装器分别定性。
    - **改动**：`EditorWindow` 加 `readonly bool _mica`，`Fluent.MicaBackdrop(this)` 必须排在 `BorderlessNative` 之后（后者把 `GlassFrameThickness` 设回 0，顺序反了背板让位失败）。窗底与两条停靠面板按 `_mica` 走透明/平涂；`Editor_OnTheme` 在背板在场时不再把 `WindowBg` 动画上窗底——那层平涂会把背板整个盖掉，色调同步已由 `MicaBackdrop` 挂在 `Theme.Changed` 上。`viewHost` 的 `0x141414` 一字未动，这是「不能照搬设置窗」那条约束的落点。
    - **取证**：`PrintWindow` 拍不到 DWM 背板，只能 `CopyFromScreen` 整窗。浅色停靠条 210–224、暗色 66–101，蓝通道最高且随位置漂移（壁纸渗入）；画布四周两主题都精确 20.1（不透明中性暗底）。截图 `test-out/editor-mica-light.png` / `editor-mica-dark.png`。
    - **踩坑一**：接背板后 `T178b` 报「工具条整排没起波」。整段入场锚在 `ContentRendered` 而非 `Show()`，背板让首帧合成多等一次，固定 `EditorEnterMs+StaggerMs+40` 那 40ms 余量被吃光。改成两步锚定（先等被压住、再等抬起来，各 3s 上限），断言内容一字未改。第一次只改「等透明度大于 0.02」直接反向翻车——级联接管前透明度本来就是 1。详见 DECISIONS。
    - **踩坑二**：`SelfTest editorwave` 只 `new Application()` 不走 `AppHost`，从不 `Theme.Apply`，所以「浅色」截图其实是暗色，两张图数值逐位相同才发现。补上 `Theme.Apply(Current.Theme, Current.Accent)`；跨主题取证先核对同一取样点两主题必须不同。详见 DECISIONS。
    - **定性**：贴图窗 `PinWindow` 是 `AllowsTransparency=true` 分层窗，`MicaBackdrop` 对它返回 false，且工具条浮在截图上而非桌面——不改，理由写进 UIAUDIT。安装器具备接入条件但 `Preview.cs` 是离屏渲染，DWM 背板不进离屏位图，取证路不通，留作独立一条。
    - **验证**：SelfTest **487 → 489**（`T182a` DWM 回读材质=2 且窗底透明与能力一致；`T182b` 画布四周底色 alpha=255）。guardrail 地板 487→489。全链 0 错误 / 0 非 NU1900 警告 / `PASS=489 FAIL=0` / LINT-COMMENTS PASS / LINT-MOTION PASS / GUARDRAIL PASS（FAIL=0 SKIP=1 共 12 项）。
  **安装器接上 Mica 背板，顺带挖出 net48 版本号谎报（UI 质感轮十二）**
    - **动机**：UIAUDIT 最后一条非交互项「安装器背板」。上轮判断「取证路不通」是错的——`Setup.exe` 无参数跑就是 Idle 界面，不装任何东西，可以直接屏采真实窗口。
    - **改动**：`SetupWindow` 加 `_mica`，`MicaBackdrop` 排在 `BorderlessNative` 之后；窗底与标题条按 `_mica` 走透明/平涂（`ChromeBg` 是 alpha 0xD9，叠背板上等于两层半透明互相污染），状态卡片保持不透明。
    - **挖出来的真问题**：接上后 DWM 属性回读 0，`MicaBackdrop` 没跑。一次性诊断打出 `OSBuild=9200 MicaSupported=False`——`Environment.OSVersion` 在 net48 进程里谎报 6.2.9200，引导器里整套 Win11 特性一直是静默关闭。Windows PowerShell 5.1 量出来是 26200，量不出谎报（它自带配置与清单），必须在被怀疑的进程里量。门控改走 ntdll `RtlGetVersion`（`Native.OsVersion.BuildNumber`，主程序与引导器各一份同名实现，链入源文件原样编译）。详见 DECISIONS。
    - **A/B**：stash 掉本轮改动重编译（确认 exe 时间戳已换），改动前 `背板38=0 暗色20=0`，改动后 `背板38=2 暗色20=1`。浅色同坐标屏采：标题条 233.7→241.6、页面 235.8→242.8（改动后两处相等＝同一种材质，且都不等于两个平涂色），卡片 249.7→250.3 未受影响。壁纸本身是中性灰（27,27,29），所以渗出来也是中性灰——判据用「既不等于 ChromeBg 也不等于 WindowBg」而不是「偏蓝」。
    - **护栏**：`Win32.cs` 被探针顶到 721 行触发 §10（700 行上限），拆成 `Native/OsVersion.cs` + `Setup/OsVersionShim.cs`。中途用行号区间删代码算错边界，删错一段直接 `git checkout --` 还原，比修补快。
    - **取证工具**：`test-out/setupshot2.ps1`（读真实窗口 DWM 属性 38/20/33 + 屏采整窗 + Esc 关自己的窗）。属性必须在窗口稳定后复读，刚出现那一刻 DWM 可能还没应用。
    - **验证**：SelfTest **489 → 490**（`T183a` 探针读数不低于 Win10 下限；`T177a` 基准换成 Rtl 值）。guardrail 地板 489→490。全链 0 错误 / 0 非 NU1900 警告 / `PASS=490 FAIL=0` / LINT-COMMENTS PASS 文件=132 / LINT-MOTION PASS / GUARDRAIL PASS。
  **手势配置窗详情区逐项错位入场（UI 质感轮十三）**
    - **动机**：全站只剩手势配置窗（`--gesture`）的载入还是整块闪现。它是独立库 `GestureEditor`，反向不引用宿主，所以既没有背板也没有分层入场。
    - **定性**：背板对它**不适用**——为了让 `Window.Opacity` 动画真正驱动屏幕合成，本库强制 `WS_EX_LAYERED`（`Native.cs` 写明了原因），分层窗背后没有 DWM 背板。与贴图窗同类，记进 UIAUDIT 免得下轮再试。
    - **改动**：新增 `GestureEditor/Cascade.cs`（自带一份分层入场，语义对齐宿主 `CascadeIn`：`From=0` 把后排顶在全透明、`TransformGroup` 只驱动自己那条位移、落位摘掉自己、`Reset` 是不走动画路径的清零出口）。`Motion.cs` 补 `StaggerMs=110` 对齐宿主。`ShowDetail` 拆成 `ShowDetail`（决定走不走入场）+ `FillDetail`（重建内容）：窗口已上屏时逐项错位，构造期交给 `ContentRendered` 的统一入场（`leadMs=QuickMs`，排在窗口本体淡入之后）。
    - **取证**：`T184a/b/c`（详情行数 ≥ 4、采样时刻错位单调且末行仍为 0、落位后透明度与变换全部复位）。逐帧 `test-out/gshot.ps1` 明暗各 4 帧：暗色详情区均值 29.4→34.6→41.7 稳定在 41.6，浅色 251.3→248.3→247.9；`gedit-t200-dark.png` 里「完成后序列」往下依次变淡到几乎不可见，上半截已落位——错位直接看得见。
    - **踩坑**：`Cascade.In` 初版只 `BeginAnimation` 带未来 `BeginTime`，后排不会提前归 0，采样时反而「后排在 1、首行在半程」，单调性判反。照宿主补上 `BeginAnimation(null)` + `Opacity = 0` 预压才对——延迟靠 `From=0` 的前置期顶住，不靠事后压。
    - **验证**：SelfTest **490 → 493**。guardrail 地板 490→493。全链 0 错误 / 0 非 NU1900 警告 / `PASS=493 FAIL=0` / LINT-COMMENTS PASS 文件=133 / LINT-MOTION PASS / GUARDRAIL PASS。
  **滚动截图三层浮层入场错位（UI 质感轮十四）**
    - **动机**：滚动截图进流程时，取景层、预览卡、控制条在同一瞬间一起淡入。三件事同时出现读起来是「卡了一下」，不是「浮层依次落位」——这是全站最后一个多窗同刻入场的地方。
    - **改动**：`Fluent.FadeIn` 新增 `leadMs`（默认 0，其余八个调用点零影响）。延迟走动画 `BeginTime` 前置期：等待期内窗口停在 `Opacity = 0`，屏幕上什么都没有，因此不需要额外的定时器或手动压透明度。三个滚动窗构造参数 `entranceLeadMs`，由 `StartScrollLoop` 给出 0 / `StaggerMs` / `StaggerMs × 2`。
    - **取证**：断言 `T185b/c`（取景层起波时预览卡与控制条仍为 0；落位后三者全为 1）。新增取证模式 `SelfTest.exe scrollwave <前缀>`——`RenderTargetBitmap` 拍不到窗口级 `Opacity`（它只渲染内容视觉），所以该模式把三个**真实生产窗口**撑住 3s，由 `test-out/scrollshot.ps1` 屏采。进程侧 25ms 采样读数（真实窗口真实动画）：暗色 `0.42/0.00/0.00` → `1.00/0.57/0.00` → `1.00/1.00/0.55` → 全 `1.00`；浅色同形 `0.44` → `0.99/0.54` → `1.00/1.00/0.52`。屏采 PNG 里没拍到浮层——SelfTest 只泵分发循环、不跑 `Application.Run()`，窗口对象有真实 `Opacity` 却没进屏幕组合。改用生产进程（`FrameCapture.exe --scroll-hint`）屏采证实这类浮层确实上屏：控制条矩形内均值 37.4、同尺寸矩形外 25.8、矩形内最亮像素 255。本轮视觉判据因此取两条：生产进程证实浮层可上屏 + 真实窗口对象的 25ms 透明度轨迹。
    - **验证**：SelfTest **493 → 495**。guardrail 地板 493→495（本轮护栏在旧地板 493 下跑绿，新地板自下一轮起把关）。
  **悬停提示只留名称与快捷键（UI 质感轮十五）**
    - **背景**：UIAUDIT 两条待拍板项由用户定夺——帮助窗文案保持成段教程（其载入动效本就有逐项错位 + 翻页方向位移，动效侧无需改动）；tooltip 精简为名称 + 快捷键。
    - **改动**：编辑器 11 条悬停提示去掉说明性子句，`识别文字（复制到剪贴板）  Ctrl+O` → `识别文字 (Ctrl+O)`、`导出录制 (MP4 / WebM / GIF)  Ctrl+E` → `导出录制 (Ctrl+E)`、`生成胶片条（抽帧拼成一张逐帧图 → 剪贴板）` → `生成胶片条`、`确定：GIF 写入剪贴板 (Enter)` → `确定 (Enter)`，时间轴五段子句收成 `拖拽选片段 · [ / ] 设入出点`。撤销/重做的「可撤 N 步」是状态不是说明，保留并统一成 `撤销上一笔 · 可撤 3 步 (Ctrl+Z)`。
    - **护栏**：新增 `T186a-tooltips-lean`——遍历编辑器视觉树收集全部悬停提示，出现全角括号、全角冒号或超过两段 ` · ` 子句即失败。这条把「精简」从一次性编辑变成持续约束，否则下一次加按钮就会长回去。
    - **取证边界**：tooltip 要真实鼠标悬停才画得出来，仓库禁 OS 级鼠标自动化，故取证走视觉树枚举（实测 12+ 条全部达标）而非截图；内容变更的前后对照即 diff 本身。
    - **验证**：SelfTest **495 → 496**。guardrail 地板 495→496。
    - **流程教训**：向用户发选择题前必须先读代码确认选项含义。本轮把 UIAUDIT 里「文案精简 vs 保持成段」的 A/B 当成动效 A/B 发问，且帮助窗的分层入场早就在位。另外用户在上班，阻塞式提问会干等——取舍类判断自己拍板、在报告里标出来供否决。
  **编辑器轻提示抬起入场与世代化收起（UI 质感轮十六）**
    - **改动**：预览底部即时反馈条从纯淡入换成「下方 6px 抬起 + 淡入」（`HoverMs`/EaseOut），淡出（`DissolveMs`/EaseIn）走完才 `Collapsed` 并摘掉位移变换。显示中再次触发只换文字并续期计时，不重播入场。
    - **世代号**：收起回调按 `_snackGen` 判定，期间若有新提示，旧回调放弃收尾——否则新提示的变换与可见性会被上一次淡出收掉，表现为「第二次提示没有位移且不会收起」。
    - **取证**：`T187a-d` 四段状态断言（触发前 Collapsed / 半程透明度 0.02–0.99 且位移为正 / 满显示时再触发透明度不回落 / 收尾可见性+透明度+变换全清）。轻提示要真实触发 OCR/复制才出现，编辑器入口又要选区层，故取证走窗口对象断言而非屏采。
    - **验证**：SelfTest **496 → 500**。guardrail 地板 496→500。
  **护栏不再把用户设置留在 e2e 改写后的状态（验证链收口）**
    - **缺陷**：`scripts/guardrail.ps1` 的 e2e 段会改 `%APPDATA%\FrameCapture\settings.json`（主题、热键、手势配置都落在这个文件），跑完不回滚。表现是每轮护栏后 `Theme` 被留在 `dark`，得手工还原；下一轮取证又在非用户主题下起跑。此前这个副作用没写进任何文档，靠会话内手工补救。
    - **修法**：e2e 段开头按字节快照该文件，段尾原样写回；文件原本不存在则删掉护栏期间新建的那份。快照用字节而非 JSON 往返，避免格式化差异污染对照。
    - **验证**：实跑 `GUARDRAIL PASS (FAIL=0 SKIP=1 共 12 项)`，并打印「用户设置已还原（e2e 期间被改写）」——这行本身证明 e2e 确实改写过。`Theme` 跑前跑后都是 `system`。
    - **边界**：整份文件并非逐字节还原。护栏第 5 步「恢复稳态」会拉起一个新的 FrameCapture 实例，该实例按自身运行状态又存了一次设置（`LastTool` 一类运行期字段），这是应用正常行为。逐字节一致这条只在 e2e 段边界内成立，不宣称覆盖整个护栏生命周期。

## 20:30 截止时的确切收口（动效质感轮，轮十二至轮十七）

**最后状态**：HEAD `140c843`，工作树干净。最后一次全链验证 = 20:12 那次护栏（含编译与 SelfTest）：
编译 0 error、**SelfTest PASS=500 FAIL=0**、LINT-COMMENTS FAIL=0 WARN=3、LINT-MOTION FAIL=0、
**GUARDRAIL PASS (FAIL=0 SKIP=1 共 12 项)**。SKIP 是 focus-safety——前台是反外挂游戏时注入被拦，按设计记 SKIP 不记 FAIL。
SelfTest 地板从 478 一路提到 **500**（`scripts/guardrail.ps1` 与 GOAL.md 两处同步）。

**这六轮交付的动效与护栏**（每轮一个连贯改动，各自带断言与明/暗双主题取证）：
- `df2b7c7` 编辑器工具条逐项落定，`CascadeIn` 加 `leadMs` 延迟起步
- `7bf0fb4` 全站动效时长收进 `Fluent.Motion` 令牌 + lint 护栏（动效不许再写裸毫秒数）
- `d42943a` 播放条与工具条按阅读顺序错开；修掉 T179 的假阳性探针
- `b9814e2` / `ed6b37f` 编辑器与安装器接上 Mica 背板，能力门控走 `RtlGetVersion`
- `9186b65` 手势配置窗详情区逐项错位入场
- `9c25861` 滚动截图三层浮层按 `StaggerMs` 依次落位（`Fluent.FadeIn` 新增 `leadMs`）
- `551356e` 编辑器悬停提示只留名称与快捷键 + `T186a` 持续约束
- `54cd67b` 编辑器轻提示抬起入场 + 按 `_snackGen` 世代收起
- `140c843` 护栏跑完还原用户设置（验证链不再污染自己的取证环境）

**动图取证工具**（在 gitignore 的 `test-out/` 下，未入库）：`gifshot.ps1` 以「窗口首次上屏」为零点
在内存里密集连拍并按真实经过时间打帧标；`makegif.py` 拼成 90ms/帧、末帧停 900ms 的循环动图。
产物 `cascade-dark.gif` / `cascade-light.gif`（手势配置窗载入，明暗各一条）。
关键取证边界已写进 UIAUDIT：`RenderTargetBitmap` 拍不到窗口级 `Opacity`；SelfTest 只泵分发循环
不跑 `Application.Run()`，窗口有真实透明度却不进屏幕组合——所以淡入类动效必须真实屏采或生产进程验证。

**未完成的确切一项**：UIAUDIT L30「D5 选区层（吸附态/拖框态）」。它需要真实鼠标悬停与拖框，
仓库硬约束禁止 OS 级鼠标自动化，自动化路径到此为止；遮罩 + 虚线态已由 pick/pk2 覆盖。
建议下阶段二选一：人工配合跑一次验收，或把它从动效清单移到「人工验收项」单列。
UIAUDIT 其余 60 条已全部勾选并带证据。

**仍挂着的非动效事项**：`PresetQQ → PresetScopeFirst`（`0ba167a`）等用户否决；上 GitHub 前补 GPL2 `LICENSE`；
MSI 安装器已放弃、安装/卸载路径未实测；Qodana 清理暂停，等 Rider 重扫出 sarif 再对 1555 条基线做差。

## 宣传网页（site/，纯静态）

`site/` 是 FrameCapture 的宣传页：无构建步骤，浏览器直接打开 `index.html`（`file://` 即可，
脚本一律传统 `<script>`，绕开 `file://` 下 ES Module 的 CORS 限制）。

**与工具同源的设计令牌。** 配色逐条取自 `Core/Theme.cs`（含 alpha 语义：Chrome `0xD9` 最透、
Window `0xF2` 近不透、Card 全不透明），时长逐条取自 `Core/Fluent.cs` 的 `Motion` 常量，
圆角沿用窗 12 / 控件 10 / 正圆色块 13，图标只用 Segoe Fluent Icons 的 PUA 码位——
与工具内同一套字形，非 Windows 访客拿不到该字体，所以图标一律只做装饰、旁边必有文字。

**文件划分。** CSS 五份：`tokens`（令牌）/ `base`（重置、排版、Mica 式氛围背板）/ `layout`（骨架与断点）/
`components`（按钮、键帽、卡片、窗口模型、选区、标签页、令牌表）/ `motion`（关键帧与状态过渡）。
JS 七份：`tokens`（`FC.Motion` 数字形式 + `prefers-reduced-motion` 实时读取）/ `theme` / `reveal` /
`hero` / `tabs` / `pointer` / `main`。

**动效清单。** 首屏把图标里的「四角取景框 + 中心点」演成一次真实捕获：四角按 `Stagger` 依次落位、
中心点 `Spring` 弹入、选区 `Enter` 撑开、描边闪光 `Flash` 单向往落、轻提示 `Hover` 抬起 `Dissolve` 退场。
滚动入场抬 6px 用 `Enter`，同组逐项错位 `Stagger`；主题切换走 View Transitions 圆形扩散 `Theme`；
悬停 `Hover`、按压 `Press`（短于悬停）；卡片镜面高光与 4° 倾斜、氛围光跟随光标，全部节流到一帧一次。

**降级与兜底。** `prefers-reduced-motion` 下时长全部塌成 1ms、位移取消、内容一律可见；
观察器或脚本失效时，视口内内容在兜底时限后仍会显示——动效可以没有，内容不能没有。
`?static=1` 静态模式停在终态，供打印、抓取与截图比对。

**验证。** 7 份 JS 全部 `node --check` 通过；CSS 括号平衡；`index.html` 引用的 13 个资源全部存在。
真实 Edge 窗口连拍三帧抓到取景框逐角落位、轻提示抬起、轻提示停留三个不同阶段，动效确实在推进。
`--dump-dom` 冒烟：`body.is-ready` 已置位（7 份脚本跑完未抛错）、31 个 `data-reveal` 全部入场、
标题两行揭幕、进度条注入、取景框与选区就位、标签页接管 `tabIndex`。
明暗两版整页截图与 500/900 窄屏截图留存 `test-out/site/`；溢出探针显示各视口 `scrollWidth == clientWidth`，
唯一越界元素是 `.ambient__glow`，它在 `overflow:hidden` 的固定层内，不产生横向滚动。
## 双语探针 + 首眼走查轮（2026-10-18 夜间，83 笔提交）

**这一轮的主线不是加功能，是把「肉眼才会漏」的东西变成机器每次都查的东西，然后用截图把查不到的部分人肉过一遍。**
截图审查是本轮最高产的发现手段：录制手感说反了、欢迎卡英文标签被键位盖掉、键位列把组合键劈成两截，全是看出来的，
不是断言跑出来的。

**新增护栏 `lint:ux-bilingual`**（`tools/SelfTest/LayoutAudit.cs` + `scripts/ux-audit.ps1`）：22 个界面 × 中英 = 44 个组合，
每个组合独立进程离屏建窗，逐条文字量版面。四类死法分开查——**裁字/省略号**（祖先真的裁切才算，WPF 默认不裁，
溢出到空白处不算缺陷，设置窗左侧导航当年就是这样误报的）、**裁行**（少掉一整行 ≥6px）、**漏翻**（英文界面里的中文，
PUA 图标字形与用户自取的手势名豁免）、**叠字**（本轮新增：按字体属性另起 TextBlock 量出文字真正要多宽，
与邻居的框求交集，横压 4px、纵向过半即报）。叠字这条是被逼出来的：写死宽度的格子在 WPF 里量不出溢出，
`DesiredSize` 被自己的 `Width` 夹住，只有求交集诚实。拿写死 40px 复跑验证：英文报一条、中文不报，44 组合零误报。

**验证链不再碰用户设置。** 护栏起 `test-out/guardrail-settings`，用 `FC_SETTINGS_DIR`（`SettingsService.Dir` 认它，
`error.log` 跟着走）把整个设置目录搬走，起点是用户那份的副本；`lib-hotkey.ps1` 加 `Get-FcDir`/`Get-FcSettingsPath`/
`Get-FcLogPath`，11 个 e2e 脚本的路径全走这三个函数，备份/还原整段删除。证据：一整轮 `-AllowInjection` 全量护栏
跑前跑后用户 `settings.json` SHA256 一字未变。护栏的 e2e 段同时进 `try/catch`，中途抛异常记 `e2e:phase FAIL`，
汇总不会假装全跑过。

**补的手感**（都可一键否决，理由与证据见仓库根 `NIGHT-2026-10-18.md`）：贴图方向键 1px / Shift 10px；倒计时 `Esc` 取消
并把 `Esc` 标在卡片上；浏览态双击画布在 1:1 与适配间来回；暂停全局热键时托盘图标转灰；自动保存视频慢的那次给
「正在保存视频…」再换「已保存 文件名」（半秒内写完不吭声）；选区放大镜按 `C` 取过的色并进编辑器「最近用色」；
滚动截图完成后托盘出现「继续上次滚动」，基准帧就是完成时落盘的那张 PNG，不在内存里再留一份。

**说错话的地方**：帮助窗两处教「按住起录、松手停」，出厂是 Chord 按一下开始/再按一下停止；README 指了一条不存在的
「手势配置器」路径、默认键位整段是旧的、工具行漏了荧光笔 `H` 还把「圆圈」写成「椭圆」；英文键位表逗号不空格，
补空格后又暴露帮助窗键位列 128px 会在 `+` 上断行（给到 158）；首启欢迎卡英文标签 `Width=40` 被键位盖住；
设置页三条英文是压扁的直译。`AppSettings.Gestures` 的注释与 `DefaultQqSet` 实现不一致，按注释即缺陷处理。

**断言换位置一处（需要留意）**：`T178b` 原来在屏幕上量「首项已抬、末项未起」那一瞬间，离屏窗口的透明度多久更新一次
由合成器给，同一份代码一次过一次红——改判结构：`Fluent.CascadeStepMs` 算出的错位步长 > 0 且透明度动画确实挂在每个按钮上。
同时淡入与没挂动画照样红。

**状态**：SelfTest 615/0（下限抬到 615）、护栏 20 项 PASS（focus-safety 因用户侧有输入而 SKIP）、探针 36 组合 0 发现、
四个 lint 全绿、`dotnet build FrameCapture.slnx` 0 error 0 warning。版本 0.9.2，产物 `release/FrameCapture-Setup-0.9.2-x64.exe`
与 `FrameCapture-0.9.2-x64-offline.zip`（收口时会带完整校验重打一次，最终 SHA256 记在 `NIGHT-2026-10-18.md`）。
交付清单与人工验收清单在仓库根 `NIGHT-2026-10-18.md`。


> 本节数字在 10-21 按 git 复核：界面登记数取 LayoutAudit.Surfaces 在批次最后一笔的真实值（22），提交数取 git rev-list --since 计数（83）。

## 高级感与交互动效轮（2026-10-21 下午起，进行中）

**产品北极星（本轮定下，之后每条改动都按这两问验收）**：首要——连傻瓜都用得明白，并且傻瓜觉得比 QQ/微信截图好用得多；
次要——不傻的人也要觉得它非常灵活、功能强大。落地成两句：不懂软件的人第一眼看不看得懂下一步点哪；老手能不能在
**不增加表面复杂度**的前提下发现深度（键位、悬停提示带键位、右键、进阶设置）。深度一律不占用默认表面。

**暗色主题先补一条真实的层次台阶。** 量出来：卡片、绑定行、键位行、标题栏全是 `#2D2D2D`（亮度 45.0），内容区 27.7——
整页只有一次明度跳变，层次全靠 1px 描边撑着，看着是「一叠线框」而不是分层的表面。新增 `Theme.RowBg` / `Theme.RowHover`：
暗色行 45 → 54（一档），亮色 255 → 245；圆角定标：卡片 8、行 6（外圆包内圆）。护栏 `T219` 八条把台阶钉住：行比卡片亮、
hover 再亮一档、步长落在 4..24 之间（太糊与太跳都算失败）、暗底次要文字对比度 ≥4.5:1，深浅各四条。

**行有按压反馈了。** 快捷键行只有悬停没有按压，按下去要到状态真变才有回应。`HoverFill` 补按下/抬起四档：
静止 → 悬停 → 压到 `Mix(底色, Theme.Press)` → 抬起回到指针所在那一档；静止全透明的幽灵按钮拿悬停色当底，
否则按下去一片透明毫无变化。`BgHover` 里 140/160/120/70 四个裸时长回到 `Fluent.Motion` 令牌，动效护栏的助手名单
补 `AnimateBg`/`TintBg`（种一个 `TintBg(x, 999)` 验过会 FAIL）。`T220` 五条走完整条状态机。

**亮色主题的键帽不再压灰**：白卡上叠 20% 黑 = `#CCCCCC`，读起来像按不动的禁用件；改白面 + 底边加粗，
坐在行底色上对比反而更清楚。暗色保持往上提一档。

**编辑器工具条**：四根顶天立地的通高分隔线把工具条切成格子，改成居中 18 DIP 短细线 + 软描边色，分组靠断开与留白说话。
选中态的刷法在切换工具与换肤两处各写一遍，收成 `RefreshToolHighlight()`。`T221` 四条钉住选中语义：浏览态整条无高亮
（浏览是「没在画」这个状态，不是目录里的一把工具）、选一把只刷那一枚、再点同一枚回到浏览高亮清零。

**三条护栏型教训进 `DECISIONS.md`**：断言要写意图不写实现副作用（`T198b` 把「悬停不换底色」写成「没有底色」，
一给行垫色就红——按意图重写并同时要求底色等于 `RowBg`，是收紧不是放宽）；动画中的画刷 `ToString()` 印成 scRGB 浮点串，
颜色断言一律按字节比、容 ±2；`MouseEnter`/`MouseLeave` 必须喂 `MouseEventArgs`，喂 `MouseButtonEventArgs`
会在 raise 当场抛 InvalidCast。

**键位在全应用只有一副长相。** 首启欢迎卡与延时截图卡上的键位是「彩色文字」，读起来像强调，不像要按的键；
换成 Keycaps.Row 键帽，与设置页、帮助页同一套。T222 三条钉住：就绪卡里应有 4 枚键帽、键帽面色与编辑器一致
（把卡改回彩色文字会当场 FAIL，已种过验证）。

**浮起卡片补一条上缘打亮的描边。** 就绪提示、倒计时、通知三张浮在任意桌面内容上的卡片只有一条等宽半透明灰边：
暗色主题下投影几乎看不见，卡片是浮着的还是贴上去的全靠猜。Fluent.CardEdge() 给描边一条竖向渐变——上缘提亮、
中段回到 Stroke、下缘压暗；光从上方来，上缘就该比下缘亮。比加深投影克制，也不像整圈白边那样在浅色里发脏。

**倒计时卡片不跟着秒数变窄。** 卡片外壳是 SizeToContent，数字从三位掉到两位（100→99、10→9）那一下整张卡片
会跟着窄一截，120 秒的延时截图会抖两次。给数字留出三位的位置，卡片宽度由标题那行定。

**设置窗八个分区，探针以前只开第一个。** 其余七个分区的版面从来没被量过；现在由 cat 参数指定（走 _restoreCat，
ShowCat 是 Build 里的局部函数，反射拿不到），组合 44 → 56。同时把「扫到多少条文字」改判**真显示**：八个分区都挂在
树里，切走的那块只是父面板折叠，里面每条文字的 Visibility 仍是 Visible——原来数的是「树里有多少文字」，
不是「用户看到多少」，「0 发现 + 0 文字 = 探针没看到东西」这条被撑成一个永远不可能为 0 的大数。
这个计数当场照出 pin-menu 是空扫（ContextMenu 的条目不弹出就不 realize，从写下那天起一条也没扫过），
删掉这个界面与建窗函数——查不到的东西不该挂在护栏里冒充查过。
裁字判据也按它自己写的意图收紧：原来只要求祖先里存在一个 ClipToBounds 容器，现在要求那一格真的比文字右边缘
更早收口，并把那一格的类型与尺寸写进报告；窗口本身算裁人者，否则「被挤出窗口右沿」这一类反而查不出来。
拿「收口线放宽 400px」验过它是活的：截图页立刻报回两条，真实阈值下它们确实没被切。

**一量就量出三处真缺陷。** 「更改」被挤出窗口右沿（路径写死 MaxWidth=250 把整行撑爆，排在最后的它最先被裁，
24px 的格子要放 42px 的字），可点区域只有字那么大（24×15）；默认保存位置被裁成「图片\Fram…」——这一行回答的是
「存到哪去了」，裁断等于把问题原样还给用户，现在路径宽度由所在列给、宁可折行也不要省略号，与路径重复的
「保存位置」标签删掉把宽度还给路径。「试听」24 高、外观页强调色色片 20 高，都补到 28。
**就绪卡这一轮把"说真话"这件事量出来了（17:20 用户追加：去掉小字、加开关、验证用户配置后准不准）。**
底部那行灰字（选完区域即截图 · 录制键再按一下停止 · 双击贴图关闭 · 右键贴图 / 托盘查看更多）一句话交代三件事、
谁都没说清，删掉；键位列加开关「就绪提示列出键位」（系统页，与「启动气泡提示」成对：一个管要不要弹，一个管列不列）。
真正的收获在验证：就绪卡把整串键位当一个组合键切，而一个动作能绑多条按法、还能带触发后缀——
`DescribeKeysFor` 拼出 `Ctrl+Shift+X，F2 按下`，切完键帽裂成「X，F2 按下」。
新增 `Keycaps.ChordRow` / `LooksLikeKeys`（帮助页与就绪卡共用一份判法：按法之间按本地化逗号与带空格斜杠分，
后缀里的斜杠不带空格切不坏；组合键与后缀以第一个空格分界，后缀作暗色小字；占位文案不长成键帽）。
验证时抓到两个真缺陷：卡片上曾有一枚**写着「未设键」的键帽**（看着就像有个键叫这名字）、半角逗号不切。
卡片宽度原写死 372 是为那行小字撑的，小字一走右边空出大片——改 244–372 自适应，并在 `ContentRendered`
拿到实测宽高后重摆右下角。帮助页键位列同轮换成键帽。新增护栏 15 条，**每条都种过违规验它会响**
（「中文逗号切出 3 枚」「关掉开关后还剩 4 枚键帽」「键帽：… / F2 按下」）。
**状态（截至 18:58）**：SelfTest **674 PASS / 0 FAIL**（625 → 674，下限同步抬到 674）、探针 28 界面 × 中英 = 56 组合 0 发现、
四条 lint 全绿、文案 461 条孤儿键 0、`dotnet build FrameCapture.slnx` 0 error。本轮清单与真人验收清单见 `NIGHT-2026-10-21.md`（产物 SHA256 批次末补）。
