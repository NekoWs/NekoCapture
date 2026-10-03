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
- [ ] D5 选区层（吸附态/拖框态）— 需真鼠标悬停/拖框，自动化边界外；遮罩+虚线态已由 pick/pk2 覆盖
- [x] D6 轮廓窗生命周期连拍实证：区域确认后 亮起(482/524)→450ms 衰减→≈1.1s 强调驻留→350ms 柔和淡出；修复 FlashOutline/FadeAway 死代码 + 步进两跳（outline2.*）；HUD/编码进度反馈审查待做
- [x] D6b HUD/编码反馈：后台编码 + HUD"导出 GIF… d/n"进度 + Esc 取消（在案）；本轮补 OK 主按钮 busy 0.55 淡变（编码期间，完成/取消/失败三路径复位）——编码期悬浮条自动隐藏致像素取证不确定，以接线审查+编译通过收账
- [x] D7 提交流程：OK/取消/Esc 全部改走 CloseWithFade（150ms cubic-in 淡出后真关闭+剪贴板写入不受影响）；CloseWithFade 退场渐变已在 popup4 连拍实证，本路径同一机制（trace: editor key=None+Escape→closed）
### E. 回归护栏
- [x] 每批次改动后：构建 0 error + SelfTest 全过 + e2e 捕获链路完好 + git commit。**一条命令收口**：`scripts/guardrail.ps1`（构建 app/Shot/SelfTest 计 error → SelfTest 下限断言 → 注入可达性探针 → 5 项 e2e → 汇总；SKIP≠FAIL；全程转录落 test-out/guardrail-last-run.txt，退出码即闸门）。本轮同时修掉护栏的根因病灶：**回归脚本写死 Ctrl+Shift+G/R（vk 71/82），而当前设置是 F6/F7**——editor-keys 因此一直假 SKIP、tap-semantics/fuzz-keys/fuzz-statemachine/wait-then-run 会假 FAIL。新增 `scripts/lib-hotkey.ps1`（热键串→`hkm` 注入映射，与 HotkeySpec.Parse 同规：裸键仅 F1..F24，非法值直接 throw），上述脚本全部改为按设置派生；test-hotkey-e2e 的"恢复后"阶段改按恢复值注入（其 hk 71 断言"旧默认已失效"处保留并注释勿改）；test-focus-safety 因 Shot 的 `swallow` 内置 Ctrl+Shift+G，改为测试期间钉默认热键+全出口还原；test-tap-semantics 另钉 TapAlwaysShowsRegion=false 保持"阈值唯一变量"。**断言只增强未削弱**：editor-keys 新增 Enter 后真剪贴板三格式取证（CF_HDROP 在 + %TEMP%\FrameCapture 新增非空 capture-*.gif + CF_DIB/CF_BITMAP + FrameCapture.Gif 自定义格式），且 FAIL 改为一律 exit 1（原先打印 FAIL 仍退 0）。本轮实跑证据：GUARDRAIL PASS（FAIL=0 SKIP=1/9）——build×3 errors=0、SelfTest PASS=75 FAIL=0、editor-keys（clipboard hdrop/gifFile/bitmap/gifBytes 全 True，capture_hotkey=F6）、hotkey（旧键失效/新键 Ctrl+Alt+G 生效/恢复 F6 复现）、tap 四场景、fuzz 120 步存活 err_delta=0；唯一 SKIP 是 focus-safety 的前台保护（前台为用户浏览器时绝不抢焦点，by design，空闲时由 wait-then-run 值守补跑）

## 工作队列（按影响排序）
B1 主题化悬浮条 → C1/C2 状态语义 → A1/A2 入退场 → A3-A5 → B2-B5 → D 全档案

## 最终验收（第 9 轮）
- 暗色全流程一镜到底连拍（fin-dark.*，90ms）：桌面→遮罩渐显中间帧(118.3)→拾取态虚线(95/164)→确认轮廓满闪(164/164)→衰减驻留→录制前隐去→编辑器开——节拍与设计一致
- 浅色同流程连拍（fin-light.*）：亮度曲线逐拍吻合（119.3 中间帧→80.7 拾取→编辑器开），主题无关的遮罩语义正确
- 双主题档案 12 张齐：编辑器×2 / 悬浮条×2 / 设置×2 / 帮助×2 / 选区×2 + 轮廓生命周期连拍
- 全站令牌：Motion 7 常量 + 圆角 12（窗）/10（控件）/13（正圆色块）+ 主题色板 Theme.*；豁免项均有注释在案
- 功能零回归：每轮 SelfTest 70/70、e2e gif 链路、发布实例 1 个无异常
- 已知边界：D5 吸附/拖框态需真鼠标人工核验；托盘菜单为系统原生渲染
