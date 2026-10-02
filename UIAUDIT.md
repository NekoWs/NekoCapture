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
- [ ] B1 FloatBar 明暗双态（浅色=亚克力浅底+深色图标，同 Fluent 工具条）
- [x] B2 时间轴令牌化：轨道/拇指描边=Theme.TrackBg/Stroke，HUD 文字=Theme.Text，分隔线=Stroke，白色色块描边（light-bars2 实证，bars5 深色无损）；Editor_OnTheme 即时换色
- [x] B3 白色色块描边（第2轮）+ 绘制条三处分隔线补漏（第2轮 replace 打错文件未生效，本轮 3/3 断言修正为 Theme.Stroke，undo-off.png 实证）
- [x] B4 播放条现为整圈 1px 40% alpha accent 描边（FloatBar L43/77），bars5/light-bars2 双主题观感均成立，维持现状
- [x] B5 HUD 文本=Theme.Text（随 B2 一并完成）
### C. 状态指示与控件语义
- [ ] C1 选中工具= accent 实心 pill + 反白图标（现无选中态视觉）
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
- [ ] 每批次改动后：构建 0 error + SelfTest 全过 + e2e 捕获链路完好 + git commit

## 工作队列（按影响排序）
B1 主题化悬浮条 → C1/C2 状态语义 → A1/A2 入退场 → A3-A5 → B2-B5 → D 全档案
