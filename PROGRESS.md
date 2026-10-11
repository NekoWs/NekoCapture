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
