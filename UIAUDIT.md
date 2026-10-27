# UI/动画打磨审计（目标驱动，逐界面推进）

方法：`UICap region/burstregion` 桌面级连拍自家窗口 → 逐帧审查 → 改 → 复拍对比。
每完成一项在对应条目打 [x] 并附前后 commit。验收基线：明/暗双主题、统一缓动、可录发布视频品质。

## 基线发现（Round 48，e3/b3 连拍 + bars2/full5）
### A. 入场/退场动画（现状：几乎没有，全部生硬直现）
- [x] A1 编辑器：淡入 240ms cubic-out + 内容上浮 14px（Round 49，e5 连拍）
- [x] A2 悬浮条：首次=淡入190ms+上浮10px，播放条 delay110 错开；再次显示=纯淡入110ms不打扰；隐藏=淡出150ms+下沉5px（a2 连拍实证）
- [x] A3 设置/帮助窗：PopupEnter（淡入+0.96→1 放大 190ms）+ CloseWithFade（150ms cubic-in）；popup2/popup4 连拍实证双向渐变；修掉退场闪没与按键重复重入两 bug（da2cab3）
- [x] A4 选区层：遮罩 150ms 渐显 + 旧区域虚线滞后 90ms 淡入并 0.98→1 落位（pick/pk2/outline2 三组连拍实证）；吸附 hover 收束需真鼠标交互，留待人工核验（d084923）
- [x] A5 Fluent.Motion 令牌类（Enter190/EditorEnter240/Exit150/Stagger110/Hover110/Theme260），新动画一律引用（d084923）
- [x] A5b 散点清扫：开关/状态 150→Motion.StateMs、主题 260→ThemeMs、悬浮条退场→ExitMs、hover 色 120→HoverMs；交叉淡入 150+20 注明非对称节拍；圆角统一：编辑器 8→12、悬浮条 11→12（与设置/帮助窗 12 同语言，色块 13=正圆刻意保留，控件 10<容器 12 合规嵌套）（radius12.png 无瑕疵）
### B. 主题一致性（现状：浅色模式下悬浮条仍全黑，割裂）
- [x] B1 FloatBar 明暗双态（浅色=亚克力浅底+深色图标，同 Fluent 工具条）。**基线注记未勾但早已实现**：FloatBar.cs L42 背景=Theme.PanelBg、L66-78 OnTheme 按 ThemeMs 换肤重动画，图标前景随 Theme.Text——自 cafb068 基线即在（git log -S 实证）。本轮（HEAD）用 FC_THEME=light/dark 各起一实例 → F6 长按开编辑器 → UICap region "FrameCapture Bar" 现场复拍：**b1-bars-light.png**（浅亚克力浅底+深图标/深 HUD 文字/浅轨道，accent 拇指与 ✓ pill 醒目）/ **b1-bars-dark.png**（同构深底浅图标），与 D2 旧档 light-editor-bars.png/bars5.png 逐要素吻合，且含 Round 1 新选中语言（线宽 accent 圆点反白）无一旧主题残留
- [x] B2 时间轴令牌化：轨道/拇指描边=Theme.TrackBg/Stroke，HUD 文字=Theme.Text，分隔线=Stroke，白色色块描边（light-bars2 实证，bars5 深色无损）；Editor_OnTheme 即时换色
- [x] B3 白色色块描边（第2轮）+ 绘制条三处分隔线补漏（第2轮 replace 打错文件未生效，本轮 3/3 断言修正为 Theme.Stroke，undo-off.png 实证）
- [x] B4 播放条现为整圈 1px 40% alpha accent 描边（FloatBar L43/77），bars5/light-bars2 双主题观感均成立，维持现状
- [x] B5 HUD 文本=Theme.Text（随 B2 一并完成）
### C. 状态指示与控件语义
- [x] C1 选中工具= accent 实心 pill + 反白图标。**基线注记"现无选中态视觉"不成立**：cafb068 起 SetTool→HighlightBtn 已渲染 accent 底+黑图标（改前取证 c1-before-dark.png 实证可见）。本轮收掉截图暴露的语言缺口：①线宽点选器默认选中无任何高亮（RefreshWidths 构建时未调用，点了才亮）且无动画/反白 → 与工具统一走 HighlightBtn；②选中动画时长挂 HoverMs → 改 Motion.StateMs（这是状态切换非 hover）；③反白写死黑 → OnAccentColor 按 accent 亮度取黑/白（T72b 断言）；④编辑器开着换肤后工具/线宽前景残留旧主题色 → Editor_OnTheme 重打选中语言；⑤切换选中语义抽成纯逻辑 ToolToggle（T72a）。取证：c1-tool-dark.png / c1-tool-light.png（工具 pill 反白 + 线宽选中 + 色块环 + ✓ 同框），SelfTest 75/75
- [x] C2 ✓确定=accent 实心 pill（bars4.png）；✕保持幽灵（播放条+单帧绘制条两处）
- [x] C3 模板核查：hover 进入 110ms cubic-out / 退出 160ms，按下 scale0.92，均为动画非瞬变（Fluent.cs L75-81）
- [x] C4 撤销按钮禁用态：CanUndo=false 时 0.35 透明+禁点（110ms cubic-out 淡变），四个栈变更点全接 RefreshUndo；undo-off.png 实证变淡 ✓
### D. 逐界面截图档案（改前/改后成对留存 test-out/uiaudit/）
- [x] D1 编辑器明/暗双档案齐（light-editor.png / dark-editor.png）：浅色下 HUD/时间轴/描边/工具条全部可读，accent 边框一致；预览区无边界线属极简取向，维持
- [x] D2 悬浮条明/暗齐（light-editor-bars.png / bars5.png）：浅色=浅亚克力+深图标，白块描边/分隔线/撤销禁用态全部到位
- [x] D3 设置窗明/暗齐（light-settings.png / dark-settings.png）：双主题同构高水准；交叉淡入（260ms）原实现已在案
- [x] D4 帮助窗明/暗齐（light-help.png / dark-help.png）：中文速查版式双主题一致
- [x] D5 选区层（吸附态/拖框态）像素档案。**这条边界其实过得去**：OS 级鼠标注入确实禁（会搬用户真光标），
  但对自家窗口 PostMessage 鼠标消息不算——D8 弹托盘菜单走的就是这条路。Shot 加 `postmouse <标题> <x> <y> move|drag|down|up`
  （客户区物理像素；`drag` 带 `MK_LBUTTON`，WPF 才把一连串移动认成拖框而不是两次悬停）。
  第二道坎是选区窗自己：它无条件 `WDA_EXCLUDEFROMCAPTURE`，PrintWindow 与桌面 BitBlt 都只拿到全黑——
  不是没画，是排除对一切抓取 API 生效。于是给它加浮窗同一约定的 `FC_TOAST_NOEXCLUDE=1` 审查开关
  （生产默认不变；代价是此时放大镜可能取到遮罩自己的像素，读数不作数）。
  取证脚本 `scripts/cap-picker-states.ps1`：吸附态 `d5-picker-snap-{zh,en}-{dark,light}.png`（遮罩 + 十字虚线 +
  吸附窗高亮 + 放大镜读数「1900, 1100 / 1924×1234 / #151517」），拖框态 `d5-picker-drag-*`（实线选区 + 顶部尺寸读数
  「630 × 540」+ 角标坐标）。人眼复核：无裁切、无对比度问题。
- [x] D6 轮廓窗生命周期连拍实证：区域确认后 亮起(482/524)→450ms 衰减→≈1.1s 强调驻留→350ms 柔和淡出；修复 FlashOutline/FadeAway 死代码 + 步进两跳（outline2.*）；HUD/编码进度反馈审查待做
- [x] D6b HUD/编码反馈：后台编码 + HUD"导出 GIF… d/n"进度 + Esc 取消（在案）；本轮补 OK 主按钮 busy 0.55 淡变（编码期间，完成/取消/失败三路径复位）——编码期悬浮条自动隐藏致像素取证不确定，以接线审查+编译通过收账（该边界已由 D9 解除：进度/取消/失败/丢帧四态双主题像素档案在案）
- [x] D7 提交流程：OK/取消/Esc 全部改走 CloseWithFade（150ms cubic-in 淡出后真关闭+剪贴板写入不受影响）；CloseWithFade 退场渐变已在 popup4 连拍实证，本路径同一机制（trace: editor key=None+Escape→closed）
- [x] D8 托盘右键菜单明/暗双主题像素档案。**修正旧边界误注**：托盘菜单不是系统原生渲染——TrayService.ShowMenu 用 WPF ContextMenu + Fluent.MenuResources() 自绘并随主题着色，像素取证可行。取证机制（自动化边界内）：对自家 NotifyIcon 隐藏窗 PostMessage 托盘回调（.NET(8+) WinForms 协议：msg=WM_USER+1024=0x800，鼠标事件码在 **lParam**；.NET Framework 旧文档的 WM_USER+102 在此不生效——Round 4 遗留脚本因此弹不出菜单，本轮修好）→ MouseUp(Right) → 菜单在光标处弹出 → 按弹窗矩形裁切桌面。产物 d8-tray-menu-dark.png / d8-tray-menu-light.png（311×449 菜单，圆角/分隔线/文字全随主题换装，无一残留），脚本 scripts/cap-tray-menu.ps1 可复跑
- [x] D9 HUD 边界态明/暗双主题像素档案（丢帧警告 / 导出进度 / 取消恢复 / 强制失败）。机制（全在自动化边界内：hkm 热键合成 + 自家窗 keyw + 桌面 BitBlt，无 OS 级鼠标自动化）：临时抬 settings.MaxDurationMs=26000（先备份、finally 原样还原）+ 测试专用开关 FC_TEST_RING_BUDGET_MB=2500 钉帧预算（FrameRing，与 FC_TEST_NO_FG 同隐私/测试约定，用户环境永不置位）→ 长按 25s 真打爆预算（实测丢 ~36 帧）；Enter/Esc 背靠背同队列投递，保证 Esc 必落在编码窗口期；失败终态正常路径不可达，FC_TEST_ENCODE_FAIL=1 令编码任务必然抛错（EditorWindow.StartEncode）。**修好 Round 6 遗留选帧器致命伤**：悬浮条 SizeToContent，HUD 文本一变整组同名窗包围盒尺寸就变，旧版"尺寸全等才比"令 burst 全部帧被跳（dark 首跑 diffPixels=-1 即栽在此）——改为取全序列最小公共尺寸、逐帧中心对齐裁切后再差分。trace 断言按阶段字节偏移读增量防串档（"ring dropped"/"encode cancelled"/"gif encoded bytes="/"encode failed:"）。产物 8 张人眼复核通过：d9-hud-dropped-{dark,light}.png（"⚠内存预算已丢 N 帧"警告三角+文案随主题换装）、d9-hud-progress-{dark,light}.png（"导出 GIF… d/208" 进度态）、d9-hud-cancelled-{dark,light}.png 与同主题 dropped 张**逐像素相同**——这正是"取消恢复"的正确形态（取消分支 Show(_index) 显式还原 HUD，编码前后可见地无一丝残留），d9-hud-fail-{dark,light}.png（"导出失败"终态、编辑器仍存活、Esc 后正常关窗）。**D6b 旧注"编码期像素取证不确定，以接线审查收账"的边界就此解除**。脚本 scripts/cap-hud-states.ps1 双主题实跑 HUD-CAP PASS
- [x] D10 录制期间的范围边框：**推翻旧注"蓝框绝不能上屏"的结论**。旧做法（`BeginCapture` 里 `HideOutline`）导致录制期间屏幕上零范围提示，只剩 Windows 11 自带的 WGC 捕获提示边框（`#FFD11C`）——而它只按"被捕获对象"绘制，WGC 又只能整显示器捕获，于是**永远绕整屏、与所选范围无关**，用户报为"黄色边框一直存在/不按范围"。本轮：①后端 `IGraphicsCaptureSession3::put_IsBorderRequired(false)` 关掉系统边框（IID/槽序取自本机 SDK 10.0.26100.0 `winrt\windows.graphics.capture.h`；19041 投影无此成员故手写 vtable；必须从**原生** IUnknown QI——托管 CCW 只应答投影内接口，QI 必 `E_NOINTERFACE`；`put` 后**回读**确认，因为官方语义是"未授权时设置成功但被忽略，不抛异常"）；②`RegionOutlineWindow` 构造期 `EnsureHandle`+`WDA_EXCLUDEFROMCAPTURE`，使其**在屏可见但不进任何捕获**（BitBlt/WGC 均排除），据此按真实范围常亮描边；③新增设置「录制时显示范围边框」（默认开）。取证：录制中桌面边缘 yellow=0（改前实测 `(220,195,29)` 边缘 220px）、轮廓实测落在 `2566,474 258x39` 即所选范围（v3-region-zoom.png 人眼复核）、`ScopeProbe` 同区域桌面 BitBlt `accentPx=0` 证明排除生效；SelfTest +T73（77/77）
- [x] D11 **英文界面像素档案**（中文档案 D1–D10 的英文补齐，脚本 `scripts/cap-en-archive.ps1 -Lang en|zh -Theme dark|light` 一次跑齐 16 张，四套共 64 张）：
  `en-editor / en-settings / en-welcome / en-notice / en-ocr / en-countdown / en-help1..9 / en-tray-menu`（均带 `-dark`，
  `-Theme light` 可再跑一套）。机制全在自动化边界内：`FC_LANG=en` 起进程（语言在启动时定，改设置不会让已起实例换语言）、
  `FC_SETTINGS_DIR` 指向 `test-out/en-archive-settings`（起点是用户那份的副本，拍的是他真实键位）、
  `FC_TOAST_NOEXCLUDE=1` 让浮窗可被截到；界面全靠自家 IPC 开关叫出来（`--settings/--welcome/--notice/--ocr/--delay/--help`），
  帮助九页用 PostMessage 投 VK_RIGHT 在自家窗口翻页；编辑器要读样图，`--editor` 带参数不走转发，故单独作首个实例起；
  托盘菜单复用 D8 的 `cap-tray-menu.ps1`（分层窗只有 PrintWindow 拿得到内容）。
  **为什么值得单独一套**：双语探针查「装不装得下、漏没漏翻」，量不出版面好不好看、说法顺不顺——
  首启欢迎卡英文标签被键位盖掉那种缺陷，探针当时也查不出（后来补的「叠字」那条才查得出来）。
  本轮这套档案直接促成三处修改：欢迎卡标签列写死 40px 被键位盖住、帮助键位列 128px 在 `+` 上断行、
  设置页三条英文是压扁的直译。
### E. 回归护栏
- [x] 每批次改动后：构建 0 error + SelfTest 全过 + e2e 捕获链路完好 + git commit。**一条命令收口**：`scripts/guardrail.ps1`（构建 app/Shot/SelfTest 计 error → SelfTest 下限断言 → 注入可达性探针 → 5 项 e2e → 汇总；SKIP≠FAIL；全程转录落 test-out/guardrail-last-run.txt，退出码即闸门）。本轮同时修掉护栏的根因病灶：**回归脚本写死 Ctrl+Shift+G/R（vk 71/82），而当前设置是 F6/F7**——editor-keys 因此一直假 SKIP、tap-semantics/fuzz-keys/fuzz-statemachine/wait-then-run 会假 FAIL。新增 `scripts/lib-hotkey.ps1`（热键串→`hkm` 注入映射，与 HotkeySpec.Parse 同规：裸键仅 F1..F24，非法值直接 throw），上述脚本全部改为按设置派生；test-hotkey-e2e 的"恢复后"阶段改按恢复值注入（其 hk 71 断言"旧默认已失效"处保留并注释勿改）；test-focus-safety 因 Shot 的 `swallow` 内置 Ctrl+Shift+G，改为测试期间钉默认热键+全出口还原；test-tap-semantics 另钉 TapAlwaysShowsRegion=false 保持"阈值唯一变量"。**断言只增强未削弱**：editor-keys 新增 Enter 后真剪贴板三格式取证（CF_HDROP 在 + %TEMP%\FrameCapture 新增非空 capture-*.gif + CF_DIB/CF_BITMAP + FrameCapture.Gif 自定义格式），且 FAIL 改为一律 exit 1（原先打印 FAIL 仍退 0）。本轮实跑证据：GUARDRAIL PASS（FAIL=0 SKIP=1/9）——build×3 errors=0、SelfTest PASS=75 FAIL=0、editor-keys（clipboard hdrop/gifFile/bitmap/gifBytes 全 True，capture_hotkey=F6）、hotkey（旧键失效/新键 Ctrl+Alt+G 生效/恢复 F6 复现）、tap 四场景、fuzz 120 步存活 err_delta=0；唯一 SKIP 是 focus-safety 的前台保护（前台为用户浏览器时绝不抢焦点，by design，空闲时由 wait-then-run 值守补跑）
- [x] E 终局闸门收口记录（收口复跑，非新增改动）：在 D8/D9 全部归档后的 HEAD d78d872 上复跑 `pwsh -NoProfile -File scripts/guardrail.ps1` → **GUARDRAIL PASS（FAIL=0 SKIP=1/9，exit 0）**：build×3 errors=0、SelfTest PASS=75 FAIL=0（≥基线 73）、editor-keys 捕获→编辑器→Enter→剪贴板三格式取证全过、hotkey 改键/旧键失效/恢复、tap 四场景、fuzz 乱键存活 err_delta=0。唯一 SKIP 现场复核为前台保护本因（test-focus-safety 打印 `SKIP: 用户前台窗口在用 (FG=…Chrome)，避免抢占焦点`——by design，非第 3 轮修复前的假 SKIP 病灶复发；该项 PASS 证据链由第 27 轮手动等效取证覆盖）。至此本审计全部条目除 D5（已注明需真鼠标人工+理由）外均 [x] 并附证据

## 工作队列（按影响排序）
B1 主题化悬浮条 → C1/C2 状态语义 → A1/A2 入退场 → A3-A5 → B2-B5 → D 全档案

## 最终验收（第 9 轮）
- 暗色全流程一镜到底连拍（fin-dark.*，90ms）：桌面→遮罩渐显中间帧(118.3)→拾取态虚线(95/164)→确认轮廓满闪(164/164)→衰减驻留→录制前隐去→编辑器开——节拍与设计一致
- 浅色同流程连拍（fin-light.*）：亮度曲线逐拍吻合（119.3 中间帧→80.7 拾取→编辑器开），主题无关的遮罩语义正确
- 双主题档案 12 张齐：编辑器×2 / 悬浮条×2 / 设置×2 / 帮助×2 / 选区×2 + 轮廓生命周期连拍
- 全站令牌：Motion 7 常量 + 圆角 12（窗）/10（控件）/13（正圆色块）+ 主题色板 Theme.*；豁免项均有注释在案
- 功能零回归：每轮 SelfTest 70/70、e2e gif 链路、发布实例 1 个无异常
- 已知边界：D5 吸附/拖框态需真鼠标人工核验。（原注"托盘菜单为系统原生渲染"不成立，已收口为 D8：自绘 Fluent 菜单，双主题像素档案在案）

## 状态机与轮廓生命周期收口（2026-10-11 长跑 R1）
- [x] **S1 捕获入口守卫 + 轮廓生命周期**（新增 `Core/CaptureGuards.cs` 纯逻辑决策表，SelfTest T75a-d）
  证伪并修掉两处真实缺陷（决策表即回归护栏）：
  1. `BeginCapture()` 第一行无条件 `HideOutline()`、守卫在其后 —— "编辑器已开时再按捕获键"
     （RefocusEditor 路径）会把**常显范围轮廓永久弄没**，任何提前 return 的失败分支同样误隐藏。
     不变量：**只有 StartCapture 允许隐藏轮廓**（`MayHideOutline`）。
  2. `FinishCapture()` 注释写"常显轮廓由后续分支重新点亮"，但**没有任何分支点亮**：常显模式下一次
     录制就把轮廓淡出且不回来；且若「录制时显示范围边框」关着，录制期间根本没上屏，旧代码拿
     `_outline.IsVisible` 当前提更回不来。现由 `OnCaptureFinished` 明确决策（常显+有已存范围=带回，否则淡出）。
  取证：`scripts/test-outline-lifecycle.ps1`（hkm 热键合成 + 自家窗 keyw + 桌面 BitBlt，无 OS 级鼠标自动化）——
  桌面截图在"矩形边框带"上差分（`scripts/lib-band-diff.py`，忽略矩形内部）：录制中轮廓不在屏 → 录制结束后
  带回（band diff>=200）；编辑器开着再按捕获键轮廓不被隐藏（band diff~=0）+ trace 增量无 `outline: hide`。
  实跑 **OUTLINE-LIFECYCLE PASS**；SelfTest 81/81；EDITOR-KEYS PASS（剪贴板三格式）。
  产物 `test-out/outline-lifecycle/`（s0 启动/s1 录制中/s2 录制后/s3 再按键 四张桌面截图 + 静态预检）。

## 全局钩子零阻塞重构（2026-10-11 长跑 R2）
- [x] **S2 钩子回调不再占住全系统输入链**（新增 `Core/HookDecision.cs` 纯逻辑决策表，SelfTest T76a-d）
  `AppHost.Dispatch` 原用阻塞 `Dispatcher.Invoke`：钩子线程要等 UI 线程跑完 `BeginCapture`
  （WGC/D3D 初始化，trace 实测 ~130ms）才返回 —— 期间**整台机器键盘卡住**，且超过
  `LowLevelHooksTimeout` 会被系统**静默摘钩**（快捷键无声失效，无任何报错）。类注释写着
  "不做耗时操作（仅置状态 + 投递到 UI 线程）"，代码却是阻塞 Invoke —— 又一处文档与代码矛盾。
  修复：`Dispatch` 改 `BeginInvoke(Send)`（同优先级 FIFO，Begin/End 顺序保持）；
  watcher 的 `Changed` 同样改投递。钩子决策逻辑抽成 `HookDecision.Decide` 纯函数
  （捕获中主键吞掉不重复触发 / 所需修饰键松开才结束 / 范围键松开只复位 / 无关键放行），
  `HotkeyManager` 只做"查表→置状态→投递"，修饰键状态（IsKeyDown）懒查。
  看门狗：回调内耗时 >50ms 写 trace `hook slow`（旧阻塞指纹），回归脚本断言其永不出现。
  取证：OUTLINE-LIFECYCLE PASS（含 hook slow 断言）；SelfTest 85/85；EDITOR-KEYS PASS。

## 编辑器退场一致性（2026-10-11 长跑 R3）
- [x] **D7 关闭动效双标**：X 按钮走 `Fluent.CloseWithFade`（Exit150 淡出），Esc 与 Enter/Commit 却直接
  `Close()`——同一窗口两种退场手感，且与入场动效（EditorEnter240）不对称。三处统一为 CloseWithFade。
  取证：EDITOR-KEYS PASS（Enter→Commit→淡出后 window-not-found + 剪贴板三格式）；OUTLINE-LIFECYCLE PASS
  （Esc 淡出路径 + 捕获链路）；SelfTest 85/85。

## 全站 UI 现代化 + PCL2 式「QQ 弹弹」+ 简洁少字（2026-10-13 夜间长跑 R69–R82）
北极星「超过 QQ 截图、人人都爱用」。用户反馈「UI 太平、像很老的工具」→ 要现代感 + 到处弹性微交互；「要的 UI 风格是简洁少字的，防止老用户被括号里的各种字烦」。逐界面推进，每处=构建 0 错 + 截图核验 + 护栏 PASS（下限 405）。详细逐轮记录见 PROGRESS.md 末尾。

### 动效地基（Core/Fluent.cs）
- [x] `Motion` 令牌（SpringMs260/Enter/State/Exit/Hover/Theme）+ helper 族：`Spring`/`Elevate`（卡片投影）/`Springy`/`Lift`/`PopIn`/`ChipHover`/`HighlightBtn`/`IconButton`/`PopupCard`（浮层投影，外层透明 Padding 给 Popup 投影腾渲染空间）/`ToggleBorderHover`
- [x] 全局 `ScrollBarStyle`（10px 细、圆角滑块、无箭头、hover 加深）、`MenuResources`（ContextMenu 弹开 + MenuItem 快捷键右对齐列）、`TitleBar`（共享自绘标题栏，关闭钮 IconButton）

### 逐界面（hover/press 弹性 + 层次 + 简洁）
- [x] **设置窗**：切页交叉淡入+上浮 12→0、导航 accent 卡片+竖条滑动+辉光、4 组分段芯片 `ChipHover(1.06)`（取色格式/悬浮窗时长/延时截图/录制格式）、开关 `ToggleBorderHover`、强调色色片/试听/更改链接/保存路径 hover 反馈、7 页整体卡片化、可聚焦元素焦点环全覆盖
- [x] **编辑器**：工具条向上投影、三处浮层 `PopupCard` 投影、全部动作按钮（工具/撤销重做/保存复制/导出/钉图/OCR/播放/确定取消/置顶设置帮助）`IconButton`、色片与最近用色 `ChipHover`、选中工具 `HighlightBtn`（accent 底+反白，StateMs）、时间轴播放头改现代圆旋钮
- [x] **贴图**：右键菜单快捷键走 `InputGestureText` 右对齐、去解释性括号（保留「关闭全部贴图 (N)」计数）、菜单弹开
- [x] **托盘菜单**：删 4 处解释性括号（保留「延时截图（3 秒）」动态数值）、菜单弹开、`--tray-menu` 入口
- [x] **手势配置器**：章节标签/触发下拉/占位/校验语去括号、录键框 hover+focus 共用 accent 描边、下拉弹层弹性缩放入场、开关键盘可达+焦点环
- [x] **toast/欢迎/倒计时/滚动提示**：`PopIn` 弹入、截图浮窗 hover accent 描边亮起、倒计时/拼接段数数字 `BackEase` 弹入、按钮 `Springy`、`--welcome`/`--scroll-hint` 入口
- [x] **标题栏文字全不带** `FrameCapture` 前缀（设置「设置」/帮助「使用速查」/识别文字/手势配置；OS `Window.Title` 任务栏/Alt+Tab 保留前缀作品牌；首启欢迎「FrameCapture 已就绪」属问候语保留）

### 已定夺（用户拍板，2 项）
- [x] **帮助窗文案**：定为保持成段教程文档——读完即关、非常驻，不属「看久了被烦」的常驻文案。载入的逐项错位入场与翻页方向性位移已在位（`ContentRendered` → `CascadeRows(_body, risePx: 6)`，`ShowStep(animate, dir)` → `riseX: dir >= 0 ? 10 : -10`），动效侧无需改动。
- [x] **tooltip 只留名称与快捷键**：悬停提示里的说明性子句全部去掉——`识别文字（复制到剪贴板）  Ctrl+O` → `识别文字 (Ctrl+O)`、`导出录制 (MP4 / WebM / GIF)  Ctrl+E` → `导出录制 (Ctrl+E)`、`生成胶片条（抽帧拼成一张逐帧图 → 剪贴板）` → `生成胶片条`、`确定：GIF 写入剪贴板 (Enter)` → `确定 (Enter)`，时间轴那条五段子句收成 `拖拽选片段 · [ / ] 设入出点`。撤销/重做的「可撤 N 步」是状态不是说明，保留并统一成 `撤销上一笔 · 可撤 3 步 (Ctrl+Z)`。断言 `T186a-tooltips-lean` 遍历编辑器视觉树收集全部悬停提示，出现全角括号、全角冒号或超过两段 ` · ` 子句即失败。tooltip 要真实鼠标悬停才画得出来，仓库禁 OS 级鼠标自动化，取证走视觉树枚举而非截图。

### 载入与状态切换动效
- [x] **设置窗逐项错位入场**：`Fluent.CascadeIn`（首个消费 `Motion.StaggerMs` 的动效）。打开与切类别时各行淡入 + 8px 抬起、自上而下依次起跳，整段压在 330ms 内；容器不再同时位移。断言 `T171a/b/c`，逐帧量化上下带落差约 150ms，明暗双主题定稿见 `test-out/cascade3/`。
- [x] **帮助窗翻页逐项入场**：整块推 12px 改为每行淡入 + 顺翻页方向 10px 收拢（`CascadeIn` 新增 `riseX` 横向分量），逐行入场下沉为 `Fluent.CascadeRows`/`ResetCascade` 供两窗共用。断言 `T172a/b/c`，翻页逐帧上下带依次到位。
- [x] **分层窗与弹出层不再透桌面**：`Theme.WindowBgSolid`/`PanelBgSolid`（源色强制 alpha，RGB 不漂移）用于帮助窗、OCR 结果窗、胶片条、`PopupCard` 共 5 处；分层表面背后无模糊背板，任何 alpha 都是直接透桌面。断言 `T173a/b`；明暗双主题空白带标准差 0.00，均值精确等于 `0x20`/`0xF9`。
- [x] **逐项入场不破坏元素既有变换**：`CascadeIn` 把位移子变换套进 `TransformGroup`、收束摘回原对象，`ResetCascade` 只摘自己登记的位移（`ConditionalWeakTable` 标记）。图标按钮的 hover 缩放全程有效——覆盖 `RenderTransform` 会让只认 `ScaleTransform` 的缩放动画永久失灵。断言 `T174a/b/c`。
- [x] **`RenderTransform` 默认是单位 `MatrixTransform`**：判「变换空闲」统一走 `Fluent.HasNoTransform`；紧凑弹窗 0.96→1 入场放大与配套的 0.97 缩放退场从恒假的 `== null` 判断里解放出来。断言 `T175a/b`。
- [x] **设置窗 Mica 背板**（含 Win10 黑窗护栏 `Fluent.MicaSupported`）：`Fluent.MicaBackdrop`（`DWMWA_SYSTEMBACKDROP_TYPE`=2，22000 退 `DWMWA_MICA_EFFECT`，更早静默跳过；分层窗跳过）。`GlassFrameThickness=-1` 让位给背板，窗口与内容根底色改透明，`DWMWA_USE_IMMERSIVE_DARK_MODE` 随应用主题同步。断言 `T176a/b`（DWM 回读材质与色调）；浅色定稿 `test-out/mica-light.png` 可见壁纸渐变渗入。
- [x] **编辑器 Mica 背板**：`_mica = Fluent.MicaBackdrop(this)` 挂在 `BorderlessNative` 之后（前者把 `GlassFrameThickness` 设回 0，顺序反了背板让位失败）。窗底与两条停靠面板透明，`Editor_OnTheme` 在背板在场时**不**再把 `WindowBg` 动画上窗底——那等于用一层平涂盖掉背板，色调同步交给 `MicaBackdrop` 自己挂的 `Theme.Changed`。画布四周的 `viewHost` 保持不透明 `0x141414`：背板渗进画布会污染取色与裁切判断，这条是设置窗做法的边界。断言 `T182a`（DWM 回读材质=2 且窗底透明与能力一致）、`T182b`（画布四周底色 alpha=255）。屏幕取证 `test-out/editor-mica-light.png` / `editor-mica-dark.png`：停靠条 210–224（浅）与 66–101（暗）且蓝通道最高，是壁纸渗入而非平涂；画布四周两主题都精确 20.1。
- [x] **贴图窗不适用背板**：`PinWindow` 是 `AllowsTransparency=true` 的分层窗，`MicaBackdrop` 对分层窗直接返回 false（DWM 不在分层表面上画背板）；且它的工具条浮在截图上而不是桌面上，透出壁纸会把图片读成背景。保持 `Color.FromArgb(210,20,20,20)` 浮层，不改。
- [x] **安装器 Mica 背板**：`SetupWindow` 的 `_mica = Fluent.MicaBackdrop(this)` 排在 `BorderlessNative` 之后，窗底与标题条（`ChromeBg` 是 alpha 0xD9，叠在背板上等于两层半透明互相污染）按 `_mica` 走透明/平涂，状态卡片保持不透明——它是浮在材质页上的内容层。接上之后才暴露出真正的拦路虎：`Fluent.MicaSupported` 用 `Environment.OSVersion` 取版本号，而它在 net48 进程里谎报 6.2.9200，引导器里整套 Win11 特性一直是静默关闭状态（Windows PowerShell 5.1 有自己的配置，量不出这个谎报）。门控改走 `Native.OsVersion.BuildNumber`（ntdll `RtlGetVersion`），主程序与引导器各一份同名同命名空间的实现，链入的视觉语言源文件原样编译。断言 `T183a-rtl-build-number-sane`（探针本身要过 Win10 下限，结构体布局写错会返回 0 并悄悄关掉全部 Win11 特性）、`T177a` 的基准从谎报的那个 API 换成 Rtl 值。取证走真实进程（SelfTest 是 net8、Setup 是 net48，TFM 不兼容引用不进来）：`test-out/setupshot2.ps1` 读真实窗口 DWM 属性并屏采整窗，改动前 `背板38=0 暗色20=0`，改动后 `背板38=2 暗色20=1`（浅色下 `暗色20=0`）。浅色 A/B 同坐标取样：标题条 233.7→241.6、页面 235.8→242.8（改动后两处相等＝同一种材质，且都不等于两个平涂色），卡片 249.7→250.3（不透明层未受影响）。
- [x] **窗级淡入淡出「真实屏幕上不可见」是测量误差**：那条判定出自 25ms 设定的 PNG 连拍——编码一帧要 ~120ms，实际只跑到 ~7fps，150–240ms 的斜坡必然被量成一次阶跃。`scripts/probe-window-fade.ps1` 不落盘，`CopyFromScreen` 后在内存里算指标，实测 ~55fps（脚本自带采样率下限，慢于 40ms/帧直接判结论无效）。同一窗口退场在暗色与浅色下都是平滑衰减：暗色边缘能量 4.69→4.47→4.29→3.64→2.45→0.78→消失，浅色 4.34→4.24→4.11→3.98→3.65→2.36→1.23→消失。判据取边缘能量而非亮度均值——Mica 背板由 DWM 画、不参与窗级透明度，淡出时区域亮度几乎不动，只有内容结构在化掉。`Window.Opacity` 动画确实逐帧落屏，生产代码零改动。断言 `T180a-d`（入场半程/走完、退场半程/真关窗）。
- [x] **窗级透明度的通道边界**：WPF 顶层窗不是 Win32 分层窗——手工挂 `WS_EX_LAYERED` 留不住样式位、`SetLayeredWindowAttributes` 返回 false、`GetLayeredWindowAttributes` 连 `AllowsTransparency=true` 的窗都读不到值。窗级透明度只有 DComp 合成面这一条路，Win32 alpha 那条走不通；断言 `T181a/b` 把边界钉在测试里，别再往这条路上投入。
- [x] **编辑器工具条逐项落定**：`CascadeRows(_toolsBar, risePx: 6, itemMs: HoverMs, budgetMs: SpringMs+StaggerMs, leadMs: EditorEnterMs)`。`CascadeIn` 新增 `leadMs`（延迟靠 `BeginTime` 前置期，不靠手动压 Opacity——手动压会被 `SetGhost` 这类 `BeginAnimation` 抢写）。断言 `T178a-d`；逐帧取证 `test-out/wave-dark2.png` + `editorwave` 模式。
- [x] **编辑器播放条并入同一波**：播放条（四列 Grid）先、工具条后，错开一个 `Motion.StaggerMs`，两排各自左→右。断言 `T179a-c` 探被接管的列组容器（探按钮会假阳性，见 DECISIONS）；逐帧 `test-out/rows-dark.png` / `rows-light.png`。
- [x] **手势配置窗载入逐项错位入场**：详情区是「切一条绑定就整块重建」的状态切换，重建时整块闪现，现在逐项错位进场。`GestureEditor/Cascade.cs` 自带一份分层入场（本库不引用宿主，`GestureEditor/Motion.cs` 逐项对齐宿主数值，补 `StaggerMs=110`）：后排被 `From=0` 顶在全透明，首行起波后依次抬升，位移 6px，整段压在 `StaggerMs × 3` 预算内；行上已有变换时套 `TransformGroup` 只驱动自己那条位移，落位后摘掉自己。窗口本体 `ContentRendered` 淡入 + 0.96→1.0 缩放，详情区 `leadMs = QuickMs` 随后落位。断言 `T184a/b/c`（详情行数 ≥ 4、采样时刻错位单调且末行仍为 0、落位后透明度与变换全部复位）。逐帧取证 `test-out/gshot.ps1`：暗色详情区均值 29.4 → 34.6 → 41.7 稳定在 41.6，浅色 251.3 → 248.3 → 247.9；`gedit-t200-dark.png` 可见「完成后序列」往下依次变淡到几乎不可见。背板对该窗**不适用**：它为了让 `Window.Opacity` 动画真正驱动屏幕合成而强制 `WS_EX_LAYERED`（见 `Native.cs`），分层窗背后没有 DWM 背板。
- [x] **编辑器轻提示抬起入场 + 世代化收起**：预览底部的即时反馈条（OCR/复制）此前是纯淡入，且淡出后停在 `Visible` 的透明态。现在按全站语言从下方 6px 抬起淡入（`HoverMs` + EaseOut），淡出（`DissolveMs` + EaseIn）走完才 `Collapsed` 并摘掉位移。显示中再次触发只换文字并续期计时——重播会把刚看清的一行字压回全透明再抬一次。收起回调按 `_snackGen` 世代判定：期间又提示过一次，旧回调直接放弃，不会把新提示的变换和可见性收掉。断言 `T187a-d`（触发前 Collapsed / 半程透明度与正位移 / 满显示时再触发不回落 / 收尾可见性+透明度+变换三者全清）。轻提示在编辑器窗口内部，需真实触发 OCR/复制才出现，取证以窗口对象上的四段状态断言为准。
- [x] **滚动截图三层浮层入场错位**：取景层、预览卡、控制条同刻 `Show()`，三件事一起出现读起来是「卡了一下」，依次落位才读得出层次。`Fluent.FadeIn` 新增 `leadMs`（默认 0，其余窗口零影响；延迟走 `BeginTime` 前置期，等待期内窗口停在 `Opacity = 0`，屏幕上什么都没有），三层分别 0 / `StaggerMs` / `StaggerMs × 2`，构造参数 `entranceLeadMs` 由控制器给。断言 `T185b/c`（起波时刻预览卡与控制条仍为 0、落位后三者全 1）。取证模式 `SelfTest.exe scrollwave <前缀>` 用**真实生产窗口**在 25ms 采样下读出：暗色 `取景层0.42 预览卡0.00 控制条0.00` → `1.00/0.57/0.00` → `1.00/1.00/0.55` → 全 `1.00`；浅色同形（`0.44` → `0.99/0.54` → `1.00/1.00/0.52`）。屏采 PNG 在该模式下拍不到浮层：SelfTest 只泵分发循环、不跑 `Application.Run()`，窗口对象有真实 `Opacity` 却没进屏幕组合。用生产进程（`FrameCapture.exe --scroll-hint`）屏采证实这类浮层确实上屏——控制条矩形内均值 37.4、同尺寸矩形外 25.8、矩形内最亮像素 255。故本轮视觉判据取「生产进程证实浮层可上屏 + 真实窗口对象的 25ms 透明度轨迹」两条，缺一即不成立。
- [x] **其余界面同规则推广**：贴图窗淡入/0.94→1.0 弹入/退场与提示条、选区层淡入、捕获描边余晖全部改引令牌。托盘菜单是 WinForms 原生下拉，开合由系统「菜单显示动画」设定，不是本程序可控的动画表面。
- [x] **`SettingsWindow.cs` 内 8 处裸时长**已收敛到 `Fluent.Motion`：行显隐 `EnterMs`/`ExitMs`，导航胶囊位移与换色 `StateMs`（与开关滑块同速，胶囊 300→150ms 属有意的节奏统一），翻页淡出 `ExitMs`。
- [x] **全站动效时长令牌化 + 护栏**：28 处裸时长收进令牌——宿主 15 处引 `Fluent.Motion`，自包含库 `GestureEditor`（只依赖 GestureCore，不能反向引用宿主）新增本库 `Motion` 逐项对齐宿主数值。补 `PressMs/QuickMs/DissolveMs/FlashMs` 四个令牌承接按压下沉、即时过渡、提示退场、描边余晖四种既有节奏；hover 淡入 110 / 淡出 160 这类同义不同值统一为 `HoverMs`/`StateMs`。`scripts/lint-motion-tokens.ps1` 挂 guardrail 第 0 关：只认动画上下文，`DispatcherTimer.Interval` 这类逻辑定时器不在射程。
### 已知边界（非 bug）
- D5 选区吸附/拖框态需真鼠标人工核验（自动化边界外）
- 选区窗/悬浮 toast/编辑器实机：沙箱内合成热键触发不了（手势层低级钩子按安全惯例忽略注入输入；护栏 e2e 的应用内捕获正常）→ 已代码级复核 + R74/R77/R78 历史目视核验
- OCR 结果窗文本框圆角：WinRT OCR 在本沙箱载不了 `Microsoft.Windows.SDK.NET`、不可核验 → 按纪律不 ship，留待正常环境

## 双语版面自动审计 + 首轮上手走查（2026-10-18 夜间长跑）

### 新增护栏
- [x] **双语版面探针** `tools/SelfTest/LayoutAudit.cs` + `scripts/ux-audit.ps1`（挂 guardrail 第 4.7 关 `lint:ux-bilingual`）：16 个界面 × 中英各起一进程，逐条文字量「装不装得下、有没有漏翻」，每条发现带窗口内坐标。判溢出要先确认祖先真的裁切——WPF 默认不裁，没裁的文本溢进空白看着没事；用户自取的手势名按原文显示，不算漏翻。基线 0 发现，每面另报「看到多少条文字」证明探针确实看到内容。
- [x] **测试进程不再写用户设置**：`FC_SETTINGS_DIR` 把整个设置目录搬走，error.log 与设置 watcher 跟着走；SelfTest 启动即复制一份到 `settings-scratch`。审计与自测会构造真编辑器窗，`SaveEditorPrefs` 在 Closed 时写盘——搬走之前它真的写掉了用户设置里的 `LastTool`。

### 补上的手感
- [x] 贴图方向键挪 1px、Shift+方向键 10px：跟屏幕上的既有界面对齐，鼠标拖不出这个精度
- [x] 倒计时浮窗 Esc 取消，卡片上标出 `Esc`：键盘本来就在手上，只给一个按钮等于让人去找鼠标
- [x] 双击画布在 1:1 与适配之间来回：只在浏览态认，选着工具时第二下是画图的起笔，不能抢
- [x] 暂停全局热键时托盘图标转灰：tooltip 早就写了「已暂停」，但托盘是余光扫一眼的地方
- [x] 自动保存视频的轻提示：慢的那次先「正在保存视频…」，写完换「已保存 文件名」；半秒内写完不吭声
- [x] 选区按 C 取过的色并进编辑器「最近用色」：同一份 `CustomColors`，同一条记色规则

### 文案与随包文档
- [x] 英文全表机械检查（458 条）：相邻两条菜单大小写对齐、两句逗号粘连改破折号；带首尾空格的条目逐个查过用法，都是拼接分隔符不是笔误
- [x] README.txt：删掉「设置 → 快捷键 → 手势配置器」这条已经没有的路径；默认键位按出厂种子改回 Ctrl+Alt+A / Ctrl+Alt+R，那段还写着 Ctrl+Shift+G 点按截图、长按录制——键是旧的，手感模型也是旧的

### 待人工核验
- 贴图方向键、倒计时 Esc、双击画布缩放、暂停托盘转灰、自动保存轻提示：都要真键盘真鼠标走一遍（自动化只能到窗口与状态这一层）

## 配色可自定义与改色即时生效（2026-10-26 上午）

### 这一轮看到的缺陷
- [x] **设置窗左侧导航选中项是一片脏色**：底色带 alpha，悬停再叠一层半透明白，落出来的颜色取决于窗口背后压着什么；选中项上再压一层悬停，两层染色互相污染。三态现在全部算在 `NavBg` 上（`AccentOn` / `HoverOn`），算出来都是不透明色；选中项文字按这一项真正的底色判（`Fluent.AccentTextOn`），读不清退回正文色。断言 `T241-mixes-opaque`、`T241-press-steps-past-hover`、`T241-accent-text-on-tint-light/dark`。
- [x] **改配色要重建窗口才看得见**：界面颜色是构造期取值的静态画刷。`Theme` 为每枚令牌持一支共享 `SolidColorBrush`，216 处 `new SolidColorBrush(Theme.X)` 指到 `Theme.XBrush`；取色器拖动逐帧落色。断言 `T241-brush-matches-token`、`T241-custom-lands-in-brush`、`T241-clear-restores-brush`。
- [x] **深浅互换是几十处同帧跳变**：`Fluent.MaskFade` 在 Adorner 层整窗盖一层换色那一刻的窗口底，按 `Motion.ThemeMs` 淡出后自摘；不进布局、不吃命中。新调色板在幕布后面就位，淡出期间看到的是同一块面在换色。退场色由 `Theme.Changing` 在重算之前取一个——`Changed` 广播时调色板已经换了。只在深浅档互换时盖，逐枚改色直接落色。断言 `T241-mask-on`、`T241-mask-color-outgoing`、`T241-mask-covers-window`、`T241-mask-gone`。
- [x] **悬停动画涂到全窗**：共享刷上挂元素级动画等于让每个用它的地方一起动画。`Fluent.BgHover` 先按 `Theme.IsPaletteBrush` 判出来再克隆私有副本；侧栏三态、快捷键胶囊描边、`HoverFill` 的静止/悬停底色各自私有，底色在事件发生时取，不在构造期抄死。
- [x] **写死的颜色**：选区描边、裁剪预览填充、托盘图标、ffmpeg 状态点、主题芯片选中/悬停色、帮助窗翻页芯片等 20 余处 ARGB 收进令牌。

### 新增界面
- [x] **设置 → 配色**：预设五套（芯片色带摆明暗两侧——浅色主题下五套预设只看当前侧几乎一模一样）→ 预览 → 按组分节的 17 枚令牌（行内明暗两枚色块各开各的取色弹层）→ 恢复出厂配色。断言 `T241-preset-applies`、`T241-preset-clears`、`T241-normalize-drops-junk`。

- [x] **改色改到读不清没人提醒**：预览卡最后一行把 `Text` / `TextDim` 压在窗口 / 面板 / 卡片 / 行四种底上的对比当场数出来，不足 4.5:1 的有几对报几对（注意色），一对都没有就说「正文在这四种底色上都读得清」；行内色块的十六进制同样在对比不足时转注意色并挂说明。断言 `T241-ink-note-clear`、`T241-ink-note-warn`。

- [x] **编辑器换色是几十处同帧跳变**：盖同一层换色幕布，且深浅互换这一档下窗底直接落定——幕布已经盖住整窗，窗底再自己淡一遍只是白跑一段动画。断言 `T243-editor-mask-on-swap`、`T243-editor-mask-gone`；取证 `scripts/cap-mask-editor.ps1`。

- [x] **换深浅档后语言三档的文字色不跟**：轨道底色是共享刷，当场就换；「系统默认 / 中文 / English」三段是构造期取色的私有画刷，没登记重画，翻档后字还停在旧档的灰度上，浅色底配深档的浅灰字就糊成一片。三段现在登记进本窗重画表，选中段按底色重新判字色。断言 `T244-lang-chip-found`、`T244-lang-chip-on-new-palette`、`T244-lang-chip-actually-moved`。

- [x] **删快捷键是两拍，中间有一段时间纹丝不动**：原先先淡出右滑（`QuickMs` 120ms）、再把这行退出布局并让下面几行在 150ms 里滑上来——滑走那 120ms 下面几行一动不动，收尾那一下就是一个跳。现在改成一拍：淡出右滑的同时把这行的高度收到 0、外边距同时收到 0，下面几行被布局连着顶上来，收完再退出列表。断言 `T245-hotkey-delete-shrinks`（被删那行在起步后就矮了）、`T245-hotkey-delete-follows-during`（45ms 时下面那行已经在往上走）、`T245-hotkey-delete-gone`、`T245-hotkey-delete-committed`。

- [x] **侧栏那枚滑条不在选中项的中线上**：落点原先按「页序 × 行距 + 写死的行顶偏移 + 半个行高」算，行顶偏移那个数一和真实布局对不上，滑条整体偏出中线六像素（量出来药丸中线 230.5、滑条中线 240）。现在取选中项在画布里的真实行位算中线，行高、边距、外层内衬怎么改都跟着走。断言 `T246-nav-indicator-centred`（第 1 页）、`T246-nav-indicator-follows`（第 4 页），量的都是滑条中线与行中线之差 ≤ 1px。

### 取证
`scripts/cap-palette.ps1 -Page <页> -Themes <dark|light>` 拍设置页；拍前把窗提到 TOPMOST，否则被别的窗压住，量到的是桌面。
`scripts/cap-mask.ps1` 换色连拍：本进程逐帧截屏（另起进程一帧的开销比整段动画还长，采不到淡开的那几帧），
逐帧量窗口中心与远角——两条曲线越过两档中点只差 0–1 帧、各有 6–7 帧过渡值，判 `FADE UNIFORM`；
`-From light -To dark` 反着翻同样成立。换色后的外观页（`test-out/mask/mask-060.png`）里语言三档的字色跟着新档走。
逐页走查：快捷键 / 截图 / 产物 / 系统四页各拼一张（`test-out/pages/montage-dark.png`、`montage-light.png`），
明暗两侧导航选中态、卡片、开关、键帽、芯片一致，无残留脏色。
- [x] **配色自带的几套看着不对，强调色的入口也找不到**：预设只动了表面与描边，强调色还是上一套那一抹蓝，选中胶囊与开关不跟着换；强调色那行（`AccentRow`）写完没有挂到任何一页上，用户在界面上根本找不到它。现在预设是六套各有性格的整套配色，每套自带强调色并一起落；强调色在「配色」页单独一张卡（色块 + 十六进制 + 恢复出厂 + 一句用法）。断言 `T247-preset-applies-accent`、`T247-preset-default-resets-accent`、`T247-preset-fill-ink-*`（强调色上的字 ≥ 4.5:1）、`T248-accent-entry-present`、`T248-accent-entry-shows-hex`、`T248-accent-not-on-appearance`。

- [x] **改「窗口底色」窗口不动**：设置窗的窗底原先让给了 Mica 背板（窗口自身底色透明），背板只在窗口自己不画像素的地方显形——铺什么令牌颜色都等同被它盖掉。现在窗底平涂「窗口底色」这枚令牌的不透明版（共享画刷），背板让位，改它就当场整窗换色。断言 `T249-window-bg-is-token`、`T249-window-bg-follows`、`T249-window-bg-restores`；像素取证见 `PROGRESS.md` 这一轮的「窗口底与卡片都跟着令牌走」。

- [x] **快捷键展开收起连点会掉帧**：展开每次都把限高放到无限再 `Measure` 一遍整棵正文，半路折返时内容还会被重新按回透明再淡一次。现在每个容器记「第几代开关 + 展开到位的自然高度」：自然高度只在第一次展开时量，往后直接当动画目标；半路折返从当前高度与当前透明度接着走；动画回调对不上代数就不落地。断言 `T250-fold-open-after-rapid`、`T250-fold-content-visible`、`T250-fold-stays-open`、`T250-fold-caches-height`、`T250-fold-reuses-height`、`T250-fold-closed-after-rapid`。

- [x] **确认与取消、复制键散在截图页上**：键位录入是同一类东西，却在两页上各摆一份，找键位要挨个翻页。现在两行移进快捷键页的「界面键位」卡，截图页一个键位框都不剩。规则写进 `AGENTS.md` 第 8 条。断言 `T251-ui-keys-on-hotkeys-page`、`T251-keys-not-on-capture-page`、`T251-ui-keys-labelled`。

- [x] **时长是「五枚芯片 + 一枚输入框」两套交互**：延时截图与悬浮窗存在时间各占一行却有六个可点的东西，输入框还只在值不落在档位上时才高亮。现在各是一枚可输入下拉框：候选档 + 直接打字共用一种写法（「3 秒」），选档 / 回车 / 移开焦点落盘，Esc 作废，条目逐条淡入。断言 `T252-duration-boxes`、`T252-duration-editable`、`T252-duration-items`、`T253-duration-commits-typed`、`T253-duration-clamps`、`T253-duration-resident`、`T253-duration-decimals`、`T253-duration-escape-reverts`。

- [x] **用了预设之后侧栏悬停是一块刺眼的青，字全糊**：预设表把 8 位色值按 `#AARRGGBB` 写，而 `HexColor` 读的是 `#RRGGBBAA`——`#33FFFFFF`（20% 白）被读成不透明的亮青，悬停把整行刷满，白字压上去只有一倍多对比。描边、按下层、轨道底色是同一个错，所以「边框扎眼」与「悬停看不清」是一件事。现在 60 条 8 位色值按解析器认的顺序重写；守卫是用例：叠加层与描边必须半透明（`T258-overlays-translucent`）、悬停一步的对比度 ≤1.9（`T258-hover-is-a-step`）、描边压到底上 1.08–2.0（`T256-*`）。

- [x] **设置文件里那份预设副本带着错位的字节**：`Colors` 是套用那一刻抄下来的字符串，光改代码不会改到它——用户不重新点一遍预设就一直在读错。现在 `AppSettings.PresetRevision` 记副本版本，落后于 `ColorPresets.Revision` 时只换读错位的那几条（存下来不透明、表里本是半透明），别的手改颜色与出厂预设下的自定义色都留得住（`T259-*`）。

- [x] **侧栏选中项的悬停把色相冲淡**：选中 + 悬停原是「强调色淡底上再压一层通用悬停色」，两层染色落出来的颜色取决于强调色多亮。现在悬停是提强调色浓度（26% → 36%），字色按真实底色挑，最后一档按底色明暗取黑或白（`Fluent.NavInk`）。`T254-nav-ink-readable` 量六套预设 × 明暗两侧 × 四态。

- [x] **描边只有两档，键帽芯片全挤在「标准」上**：调细一点就把卡片边界一起调了；键帽还借卡片底、未选中的芯片借半透明悬停色（压在什么底上读出什么色）。现在多一档「控件描边」，另加「键帽底色」「芯片底色」两枚令牌，配色页 17 → 20 枚。断言 `T257-keycap-uses-token`、`T257-chip-uses-token`。

- [x] **预设的亮侧几乎看不出换过**：石墨、午夜蓝、森林的亮侧表面只离出厂几个色阶，用户会以为「亮色根本没做」。现在亮侧给足色相与台阶：石墨 #F0F0F0 中性灰、午夜蓝 #E9EFFA 蓝灰、暖沙 #F8F1E4 纸黄、森林 #ECF3EA 灰绿。断言 `T255-preset-visible-*`（每套两侧都改动了至少 4 枚令牌）。

- [x] **「恢复出厂配色」与卡片不是一套样式**：它原先是 `RoundButton`——无边框、无投影的 7 圆角灰条，紧挨着一排带描边和投影的卡片，看着像另一个家族。现在圆角 8、1px 描边、同档投影、悬停走 `RowHover`。断言 `T261-restore-looks-like-card`。

- [x] **预览卡里中间那枚芯片没有右间距**：`chipOff` 少了 `Margin`，与「侧栏底色」贴在一起。三枚现在都是右间距 6（最后一枚不需要）。断言 `T261-preview-chips-gapped`。

- [x] **快捷键页的行距与别处不同**：界面键位那两行裸铺在卡片里，间距比上下两张卡紧一档；编辑器键位那几组的标题下面只留 2px，顶着第一枚胶囊。两行现在包成同一种胶囊（行外侧边距收进胶囊内），标题下面留 6px。

- [x] **时长框里把单位一起写着，右半边空出一大片**：`3 秒` 逼着框宽按三个汉字取。现在框里只放数字，单位在框外；特殊档由单位那一格改口（`[0] 关` / `[0] 常驻`）。「截图后边框显示时间」也从滑杆换成同一枚控件。断言 `T252-duration-unit-outside`、`T260-duration-outline-row`、`T260-duration-outline-commits`。

- [x] **同一页上两张卡的小标题字号不一样**：「界面键位」用共用的 `SectionHeader`（11），「绘制工具」各写各的（12），看上去像两张不同的卡；顺带「绘制工具」那张卡因为「这一节没有标题」的老理由还多留了 10px 上边距。现在全站节标题只有一处定义（12 号、上 4 下 6），编辑器键位那几组改成调它；卡与卡之间统一 2+2。断言沿用 `T251-ui-keys-labelled`（标题在不在），字号与留白靠截图复核。

- [x] **快捷键卡片偏高，还挂着一个简单/高级开关**：折叠行内衬从 7 收到 4、行外边距 4 收到 2、卡片内衬也收一档；「简单 / 高级」分段开关整块删掉，完成后与失败时两行始终在。断言 `T191d-overrides-always-shown`。

- [x] **拖动芯片时加号那一格会变矮**：芯片与加号各自按内容定高（25 与 24），一行里只剩加号时行高就掉下来，卡片跟着一缩一伸。现在两者共用 `ChainEditor.ChipHeight = 27`。

- [x] **界面键位把确认与取消挤在一行**：两枚键帽并排加一枚图标，录左边那枚时右边跟着错位，看起来像连体控件。拆成「确认选区 / 取消选区 / 截图后的复制键」三行，行首图标去掉；标签复用 `K.Capture.*` 那两条。断言 `T251-ui-keys-on-hotkeys-page`。

- [x] **下拉框点文字那一截不开候选**：模板里只有箭头是开关，点正文只放光标。现在给 `PART_EditableTextBox` 挂一枚抬起处理器，弹层关着就打开；断言 `T252-duration-click-text-opens`。

- [x] **内存探针在并行护栏下偶发「编辑器没出现」**：固定等 2.1 秒窗口不出来就判失败。改成最多重试 16 次、每次 500ms；判据不变（编辑器必须出现）。

- [x] **选区的去向只能由手势决定**：同一次拖框，想截图还是想录制取决于把你叫起来的是哪个键。现在可调整态多一枚两段开关（截图 / 录制），只活这一次、不落盘；断言 `T262-picker-mode-initial`、`T262-picker-mode-switchable`。

- [x] **切到录制就立刻开录**：范围定完往往还要摆窗口、清桌面。现在选区旁边先摆一条「开始录制 / 取消」（`RecordStartWindow`，不抢焦点），点下去才起录；可在 `设置 · 录制 · 选区后先按开始` 关掉。断言 `T264-record-start-bar-shows`、`T264-record-start-bar-starts`、`T263-startclick-toggle-on-record-page`。

- [x] **选区层只能用 Esc 取消**：鼠标在手的用户得腾出一只手回键盘。现在右键等同于 Esc（`设置 · 截图 · 右键关闭选区`，默认开，可关）；断言 `T262-rightclick-closes`、`T262-rightclick-can-be-off`。

- [x] **单键 PrtSc 绑不上**：全局快捷键模型把裸键一律拒收（只放行 F1–F24），`PrtSc` 录得进去、存不下来——`ToBinding()` 解析失败，那条绑定被静默丢掉。现在裸键放行 F1–F24 与 `PrintScreen`，其余裸字母仍拒收；键帽与设置串统一写 `PrtSc`。断言 `T265-*`。

- [x] **Win 组合键与提权**：`Win+Shift+S` 这类组合模型本来支持（Win 位一直在），但从没测过；系统先接手的键与焦点在高权限窗口上的键都需要进程已提权。现在系统页有「以管理员身份运行」+ 当前权限状态，开启时换一次提权实例（UAC 一次），同时开自启则建最高权限计划任务、登录不再弹 UAC。运行时实测项列在 `TODO.md` 的「待实测」一节。

- [x] **「以管理员身份运行」开着却显示未提权**：状态那行是构造期算一次的死文案；提权又只在「刚点开关」那一下发生，手动启动的实例从来不会自己提权。现在状态行随开关改口（已开启但未提权 → 写明重启后生效），启动时若开着就先换提权实例（带 `--elevated-attempt` 防来回弹 UAC），开关本身也不再先弹自己的确认框。

- [x] **Win 组合键在录制时弹出了 Windows 截图工具**：引擎只在动作真的触发时吞键，动作被守卫挡下（正在录制）就放行，系统于是收到 Win+Shift+S。现在绑了键盘触发的组合键按下即吞，守卫只决定我们自己的动作跑不跑。

- [x] **截图/录制开关看不到**：它原先只在「可调整选区」的勾叉那一排出现。现在常显在屏幕上方居中，压在压暗遮罩之上。

- [x] **护栏 e2e 会被「杀不掉的实例」整段带红**：脚本新起的进程撞上单实例互斥，会把参数转发给还在跑的旧实例再退出，旧实例没带测试开关，于是每一项都表现为「编辑器没出现」。现在 e2e 开跑前先清场；若仍有实例杀不掉（多半是提权起的），如实记 SKIP 并写明原因，不让被测代码替环境背锅。
- [x] **启动自动提权会拖垮脚本**：`RunAsAdmin` 开着时，护栏/冒烟每起一次进程就弹一次 UAC，宿主卡在等子进程上——10 项 e2e 全红。现在脚本启动（`FC_NO_ELEVATE=1`、`FC_TEST_NO_FG=1`、或用独立设置目录）一律不提权。

- [x] **录新快捷键时 Win 组合抓不到、字母变成 ImeProcessed**：录入框只听窗口键盘事件——Win+Shift+S 被系统先接走，中文输入法下 Shift+字母的 `e.Key` 是 `ImeProcessed`。现在录入态把全局钩子的键汇（`HotkeyManager.CaptureSink`）接到录入框，先于暂停与手势层、命中即吞；窗口事件保留兜底并补上 `System`/`ImeProcessed`/`DeadCharProcessed` 取键。

- [x] **选区录制没有「当前时间 + 停止」的地方**：现在选区确认后范围框保持显示，上方面板从「开始录制」原地变成「● 计时 + 停止录制」，右上角 REC 徽标在这段录制里不亮（REC 归面板说）。

- [x] **开启管理员身份后仍显示未提权**：去掉那行灰字；开关一开就直接换提权实例，UAC 被取消则把开关关回去。

- [x] **设置里开提权后托盘仍不勾、也没有提权实例**：交接只做了一半——起了提权实例却让旧进程继续活着，新实例撞上单实例互斥、把参数转发给正在退出的旧进程再退掉。现在与托盘同一条交接：先起新的、再退旧的；提权实例带 `--settings --page system`，回来后停在系统页（`T266-page-arg-*`）。托盘那条也会把 `RunAsAdmin` 记进设置。

- [x] **录键连 Ctrl+任意键都失效**：吞掉 Ctrl 之后它不进系统输入队列，`GetAsyncKeyState` 读到「没按」，组合键被记成裸键；另外收尾（落盘 + 重装钩子）发生在钩子回调里，`Install()` 先卸后装会打断它自己。现在录入框自记修饰键账、开始时读一次真实键态；收尾改成 `Dispatcher.BeginInvoke` 回 UI 线程。断言 `T267-capture-*`。

- [x] **单字母能录不能触发**：`HotkeySpec.Parse` 只放行带修饰键与 F1–F24，裸字母在存进绑定表时被判非法、绑定静默消失。现在裸键一律收（只拒修饰键本身）——绑 `A` 就是让 `A` 在全局顶替本来的用途，任何软件都收不到它。断言 `T5c-parse-bare-letter` / `T265-bare-letter-accepted` / `T131b-modifier-only-rejected`。
