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
