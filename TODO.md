# FrameCapture Todo

## 用户反馈（10-12 排队，按用户给定顺序做）
- [ ] **新增「选中」工具（V）**：对象编辑（移动/改大小/删除）只从选中工具进入；撤掉「同工具 Ctrl+点击」方案。
      地基已就位：_textEls/_mosaicEls 元素↔标注映射 + 操作栈撤销（R50）。后续框/箭头等同款对象化。
- [ ] **「圆」图标改线性**：现在用实心圆 glyph（EA3B），但画出来是圆环——图标应与产物形状一致（找线性圆环字形，GlyphProbe 验证）。
- [ ] **自实现 RGBA 调色板**：系统选色器难用，换成类浏览器 DevTools 的自绘调色板（饱和度方块+色相条+alpha+RGBA/HEX 输入），替换「调色板取色 K」的弹窗。
- [ ] **笔宽「…」弹层消失 bug**：按下三个点、松开鼠标瞬间弹层即消失；改成按下后把鼠标移进滑杆小窗即不消失（悬停保持 + 移出/点击别处才关）。
- [ ] **设置项：按下选区快捷键后是否显示旧选区**（开关，默认开=沿用上次范围；关=每次全新框选）。
- [ ] **设置页改版**：按分类分组（分类标题 + 下面条目），不用图标，分类清楚即可；整体更好看。
- [ ] **双击捕获键 = 开始/结束录制（免按住）**：连按两下捕获热键开始录制（REC 徽标常驻），再连按两下结束并进编辑器。
      与现有「按住录制」并存；点按=截图不受影响（双击判定要避开 tap 语义，第二下间隔阈值内）。
- [ ] **移除录制时长上限（MaxDurationMs，现默认 6s/滑杆 2–30s）**：录制=一直录到用户停。
      前置：帧内存驻留改磁盘暂存（4K@60fps BGRA≈2GB/s，纯内存必 OOM）——与上一条同轮做，先 spool 后放开上限。
      涉及：AppSettings.MaxDurationMs、CaptureController 停止判定、SettingsWindow 滑杆、HelpWindow 文案。
- [ ] **设置窗口自定义标题栏**：现在无法拖动。画 QQ 风格自绘标题栏（不用 Windows 默认），自绘关闭按钮，拖标题栏移动窗口。
- [ ] **设置滑杆控件修缝+美化**：蓝色进度线与圆形滑块之间有空隙（修几何对齐），整体样式重做（现样式不好看）。
- [ ] **托盘右键菜单两修**：①菜单打开后失去焦点不自动消失（要自动关）；②菜单样式与新紧凑布局不搭，重做；
      顺带：设置界面整体也与紧凑布局不搭（与标题栏改版、分组改版合并做）。

## 用户反馈
- [x] **录制后的全屏黄色边框持续存在（10-04 已修复）**：根因 = **不是我们的代码**，是 Windows 11
  自带的 WGC 捕获提示边框（`#FFD11C`）。`WgcLiveCapture` 用 `GraphicsCaptureSession` 捕获**整台
  显示器**，而系统边框只按"被捕获对象"画 —— 于是必然全屏、与用户选的范围无关。
  取证：录制中桌面边缘像素 = `(220,195,29)`、停止后消失（`test-out/bugcheck/`，`scan_edges.py`/`verify_fix.py`）。
  修复：①用 `IGraphicsCaptureSession3::put_IsBorderRequired(false)` 关掉系统边框
  （IID `F2CDD966-22AE-5EA1-9596-3A289344C3BE`，槽 6/7，取自本机 SDK 10.0.26100.0 的
  `winrt\windows.graphics.capture.h`；钉住的 19041 投影没有这个成员，只能手写 vtable；
  必须从**原生** IUnknown QI，托管 CCW 会 E_NOINTERFACE）；
  ②改由自家 `RegionOutlineWindow` 按**真实范围**画描边，并用 `WDA_EXCLUDEFROMCAPTURE` 排除，
  保证只提示、不入镜（`ScopeProbe` 实测：同区域桌面 BitBlt 抓不到轮廓）。
  新增设置「录制时显示范围边框」（默认开）。SelfTest +T73（77/77）。
- [x] **图标豆腐块**（10-02 已修复）：根因 = 工具/设置用了 BMP 符号（● ─ ○ ↗ ✎ ✂ ⌐ ≫ ⊞ ⚡ □ ★），图标字体链里不存在。用 GlyphProbe 逐字体探测 + 渲染矩阵人工选型后全部换成 Segoe Fluent Icons PUA 字形（点 E915 / 线 E949 / 圆 EA3F / 箭头 E74A / 手绘 E70F / 裁剪 E7A8 / 范围 E71A / 捕获 E7F4 / 阈值 E916 / FPS EC4A / 时长 E823 / 抽帧 E8FD / 胶片 E8B9 / 自启 E7E8）。SelfTest T10 字形回归断言已加（34/34 PASS），截图复验明/暗两主题均无方框。

## 用户反馈（续）
- [x] **UI 细节必须到位、美观（10-02）**：所有可交互元素的悬停样式（背景/描边反馈）、按下反馈、悬停气泡出现动画、窗口出入场、按钮/芯片切换等都要有平滑缓动（Fluent 风格 easing），间距/圆角/字阶统一。：已落地 Core/Fluent.cs —— 图标按钮统一模板（悬停圆角高亮 110ms 淡入 / 按下 0.92 回缩 + Press 色）、滑杆全新 Fluent 模板（细轨道 + 强调色已选段 + 圆形滑块悬停放大带光晕）、开关滑钮 BackEase 滑动 + 轨道/滑钮颜色渐变、主题芯片悬停渐亮、编辑器工具选中态背景淡入 + 前景反色、三个叠加窗（编辑器/设置/选区）入场淡入 120-160ms；SelfTest 新增 T11 模板解析回归（35/35 PASS）；明/暗两主题截图复验通过。悬停/缓动为动态效果，请上手体验一遍，不满意的点告诉我参数（时长/幅度）。

- [ ] 提权载体（elev 队列）已退出，如需继续用提权截图需重新批一次 UAC（scripts/launch-elevator.ps1）；日常验证已改用非提权本地 Shot 流程

## 编辑器前台焦点修复（第 26–27 轮）
- 第 36–37 轮收口（游戏退出后值守自动补跑成功）：**全套注入回归全绿** —— HOTKEY-E2E PASS / TAP-SEMANTICS PASS（1/16/1/70 帧语义）/ FUZZ PASS（120 步 err_delta=0）/ EDITOR-KEYS PASS / FOCUS-SAFETY 手动等效 PASS（零漏字符 + 捕获后 FG=FrameCapture Editor + Esc 归还原窗）。Shot `swallow` 修复一个自身 bug（WinForms Timer async 续体丢 key-up 注入 → 改纯计时阶段）。结果存 test-out/injection-tests.txt。仅差用户五项人肉确认。


- 第 31 轮：帮助速查补两行——主题切换（跟随系统/浅/深+强调色）与胶片条位置（%TEMP% 下 agent 目录，可关），"配置/教程在帮助文档"要点全覆盖（托盘/设置/快捷键/点按长按/吸附拖框/绘图键位/时间轴/右侧工具/剪贴板/胶片）。值守脚本 scripts/wait-then-run-injection-tests.ps1：游戏前台期间每 30s 探测，注入恢复后自动补跑 editor-keys + focus-safety，结果写 test-out/injection-tests.txt。SelfTest 67/67。


- 环境约束发现（第 30 轮）：《明日方舟》（反外挂）运行期间，系统级注入按键（keybd_event）被完全拦截——hook 收不到任何事件。影响：依赖注入热键的脚本（编辑器键矩阵/焦点安全等）在游戏前台时不可执行；两脚本已加检测并自动 SKIP（区别于 FAIL），游戏关闭后可完整跑。
- 新增 scripts/test-editor-keys.ps1：编辑器键盘矩阵 headless 测试（Space 播放/暂停、左右方向键单帧、Enter 提交；断言 trace editor key/play=True/gif encoded bytes）。配套 FC_TEST_NO_FG=1 环境开关：测试期抑制前台抢占（前台机制由第 27 轮证据覆盖）。


- 自动化收编（第 29 轮）：`scripts/test-focus-safety.ps1`（Shot `swallow` 模式自注入热键 + 文本框零漏字符断言 + 编辑器出现断言；检测到游戏/浏览器前台时**自动 SKIP** 不抢焦点——当前机器游戏在前台，本轮验证 SKIP 路径正确，PASS 证据链已由第 27 轮手工取证覆盖：probe 零漏字符 + FG=Editor + Esc 归还）。
- `_prevFg` 守卫：前台是本进程自家窗时置空，避免焦点乒乓。胶片条（AgentStrip）确认：SelfTest 单元级生成断言 + Commit 接线（设置 `AgentStripEnabled` 默认开，产物 `%TEMP%\FrameCapture\agent\strip-*.png`）。



- 用户实测指出：**编辑器弹出后没有键盘焦点**（前台锁：托盘后台进程 + 注入按键不授予前台权限，`SetForegroundWindow` 被拒）→ 快捷键打到用户正在用的窗口上。
- 修复（CaptureController）：`ForegroundNow` 链 = BringWindowToTop → SetForegroundWindow → **AttachThreadInput(前台线程↔UI线程) 经典链** → SwitchToThisWindow 兜底；编辑器/选区窗弹出即调用，**关闭后焦点归还**捕获前的窗口（`_prevFg`）。
- 开档 1 秒内有限重试（3×350ms）：应对部分游戏/应用窗口弹出后立刻抢回前台；之后不再强抢（Topmost 仍视觉置顶，点击可获焦点），避免与游戏发生焦点战。
- 证据（Shot 新增 `fg` 模式，打印前台窗标题/类名）：捕获前 FG=Chrome → 捕获后 FG=**FrameCapture Editor** → Esc 后 FG=**归还 Chrome**；零 `foreground denied`。SelfTest 67/67、模糊/热键/点按回归全 PASS。
- Shot 新增 `probe` 模式：焦点探针窗（自窗+文本框，不抢前台），与全局热键并发验证文本框 `ABCDEF` 零漏字符（hook 吞键证据）。
- 零帧安全网：WGC 理论 0 帧（极短点按/全静止）时 GDI 补抓 1 帧，保证“点按必有产物”（trace `zero-frame safety net`）。静止桌面实测点按稳定产出 8–12 帧。

## WGC 后端落地（第 25–26 轮）

- **捕获后端切换为 Windows.Graphics.Capture（WGC）**，GDI 保留为自动回退：任何初始化异常 → `wgc init failed: … → 回退 GDI`，Trace 标记 `capture backend=wgc|gdi`。设置项 `UseWgc`（默认开）。
- 离线投影方案：TFM `net8.0-windows10.0.19041.0` + NuGet 缓存 `microsoft.windows.sdk.net.ref/10.0.19041.57` 显式 `<Reference>`（本机无投影 targeting pack，SDK 10.0.400 不自动接线）。
- 手写最小 interop（全部 GUID 从本机 SDK 头文件 `Windows.Graphics.Capture.Interop.h` / cppwinrt base.h 核准）：
  - `RoGetActivationFactory`（HSTRING 手动经 combase）取 `IGraphicsCaptureItemInterop` = {3628E81B-3CAC-4C60-B7F4-23CE0E0C3356}，**CreateForWindow 是槽 0、CreateForMonitor 槽 1**；
  - 设备走 d3d11.dll 官方 free function `CreateDirect3D11DeviceFromDXGIDevice`（免 WinRT 工厂，`Direct3D11Device` 类不可激活）；
  - 指针→投影接口：`WinRT.ComWrappersSupport.GetObjectReferenceForInterface` + `WinRT.CastExtensions.As<IDirect3DDevice>`；
  - 像素：`SoftwareBitmap` → `LockBuffer` → `IMemoryBufferByteAccess` {5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D}（GetBuffer 在 vtable **槽 3**，前 3 槽是 IUnknown）。
- 两个隐蔽坑的修复（均有证据）：
  1. **apphost 探测上下文加载不了 `Microsoft.Windows.SDK.NET.dll`**（SelfTest 等普通 exe 正常）→ `WgcLiveCapture` 静态构造显式 `Assembly.LoadFrom` 预加载；first-chance 日志定位（FileNotFoundException），修复后 e2e `capture backend=wgc`。
  2. **WPF UI(STA) 线程直接做 WinRT 激活死锁** → 初始化移到独立线程（`WgcInit`），`done.Wait(5000)` 限时。
- 语义说明：WGC 仅在画面变化时产帧（系统行为）；静止画面录制帧数偏少属正常，帧时间戳真实、GIF 延时按实际时间计算。
- 新回归证据：SelfTest **T20a/b/c**（真实取帧 ≥3 帧/尺寸格式正确、Stop 后停流、会话可重建；配合同进程动画探针窗制造画面变化）；**PASS=67 FAIL=0**。
- e2e 全链：hk→编辑器（WGC 19–20 帧）→Enter→`gif encoded bytes=6854767`→编辑器关闭；剪贴板格式 `Bitmap/BitmapSource/FileDrop/FileNameW/PersistentObject/FrameCapture.Gif` 全在。
- 焦点安全证据（回应“对另一个窗口按快捷键”质疑）：Shot `probe` 模式（自窗+文本框，不抢前台）与全局热键并发——**文本框保持 `ABCDEF` 零漏字符**，捕获照常（hook 命中即 `return 1` 吞键，见 HotkeyManager）；`keyw/ctrlw` 为 PostMessage 直投自有窗口，本就不依赖前台。
- 全套回归在 WGC 后端下重跑：模糊 120 步 PASS、HOTKEY-E2E PASS、TAP-SEMANTICS PASS、err_delta=0。

## 点按/长按语义端到端（第 24 轮）
- [x] scripts/test-tap-semantics.ps1 四场景（读 fc-trace「open editor frames=N」断言）：阈值400+按100ms→1帧；阈值0+按400ms→16帧（0=全部长按）；阈值1000+按500ms→1帧（延迟可配置）；阈值1000+按1300ms→72帧（超阈值恢复长按）→ TAP-SEMANTICS PASS，配置自动恢复
- [!] 校准注记：keybd_event 注入到 hook 实测按住时长有 ~150ms 抖动，断言需避开阈值边界（durMs≈frames×33，默认 30fps）

## 自定义热键端到端（第 23 轮）
- [x] Shot 新增 hkm（任意 Ctrl/Shift/Alt/Win 组合按住）；scripts/test-hotkey-e2e.ps1：改 CaptureHotkey=Ctrl+Alt+G、ScopeHotkey=Ctrl+Alt+R → 旧 Ctrl+Shift+G 确认失效、新捕获键出编辑器、新选区键出全屏选区窗，恢复默认后回归通过，error.log 零增量 → HOTKEY-E2E PASS
- 结论：目标项「全局快捷键可自定义」首次拿到端到端证据（此前仅解析单测 + 设置 UI）

## 状态机全链路模糊（第 22 轮）
- [x] scripts/fuzz-statemachine.ps1：随机全局热键（71/82/未注册187）+ 四窗口乱键 + 热键轰炸（含录制中重入），收尾多层 Esc 后断言：进程存活、error.log 零增量、收尾后捕获热键仍能开编辑器；seed 98765/20260215（120/160 步）双通过
- [x] 抓到测试假象根因：picker 的 WPF Title 被 SourceInitialized 中 SetWindowTextW("FrameCapture Region") 覆盖 → 键鼠注入打不中窗口、状态合法停在 SelectingScope；已把 WPF Title 统一为 "FrameCapture Region"，脚本改打正确标题
- [x] 结论：录制中/选区中再按捕获热键被正确忽略（BeginCapture 守卫），无错误日志、无卡死残留

## 右侧工具快捷键与统一异步导出（第 21 轮）
- [x] 右侧竖排四个工具加快捷键：Ctrl+S 保存单帧 / Ctrl+C 复制单帧 / Ctrl+E 保存动图 / Ctrl+H 视觉暂留，tooltip 同步标注
- [x] SaveGif 复用从 Commit 抽出的 StartEncode（后台烘焙+编码+HUD 进度+取消，防重入）——消除保存动图 ~1s UI 冻结；死代码 EncodeGif 删除
- [x] 回归：SelfTest 64/64；Enter 提交（编辑器关闭+GIF 生成 10.3MB）与编码中 Esc 取消两场景复测通过，error.log 零增量
- [!] Shot 扩展 ctrlw（PostMessage 组合键）实测：WPF 修饰键读真实键盘状态，PostMessage 的 Ctrl 不被识别 → 组合键无法在允许工具内验证（不为可测性扭曲应用）；对话框默认目录 Pictures\FrameCapture 逻辑为共享代码

## 编辑器顺手增强（第 20 轮）
- [x] 播放倍速：＋/－ 键 ×0.25–×8 步进（0 或 × 复位），HUD 实时显示「×4」；实机验证（按两次＋ → HUD 截图 l7-speed-crop.png 显示 ×4）
- [x] 重做：Core.UndoRedo<T> 纯类（新增清空重做栈、LIFO 重做）接入标注历史；快捷键 Ctrl+Y 或裸 Y；SelfTest T19a-d → 64/64 PASS
- [x] 帮助文档同步新增两行（倍速、重做）

## GIF 编码性能与不阻塞 UI（第 19 轮）
- [x] 基准：4K×30 帧编码 4.7s（UI 冻结风险）→ 编码器重构为逐帧独立编码 + Parallel.For 并行 + IProgress/CancellationToken → 0.95s（约 5×）；FC_BENCH=1 可复跑基准（不计入 PASS/FAIL）
- [x] Commit 移到后台线程：HUD 实时「导出 GIF… n/N」，编码中再按 Enter 忽略、按 Esc 取消并恢复 HUD；保存对话框路径沿用同步（模态下可接受）
- [x] SelfTest T18a/b：并行输出有效性 + 取消抛 OperationCanceledException → 60/60 PASS
- [x] 端到端实测：Enter 提交 → 编辑器关闭 + GIF 写入 %TEMP%\FrameCapture（10.3MB，剪贴板 CF_HDROP 源）；Enter 后 50ms Esc → 编辑器存活（取消生效）；error.log 零增量

## 交互逻辑下沉可测（第 18 轮）
- [x] timeline 拖范围状态机抽为 Core.RangeDragState（新拖/端点抓取±8px/反向拖/单击清除/索引钳制），UI 仅转发事件；窗口吸附命中抽为 Core.SnapPick（z 序取先、跳过 <4px）
- [x] SelfTest T16a-i + T17a-d：13 项断言全过 → 58/58 PASS；「拖范围手感」「窗口吸附」两项人工清单降级为逻辑已测+体验复核；重构后编辑器冒烟正常（error.log 零增量）

## 工作区与剪贴板证据（第 17 轮）
- [x] 工作区清理：删除死 xunit 脚手架 tests/（0 个源文件）与一次性工具 probe/、plist/、.probe/、根目录 nul；scripts/ 下 100+ 临时 png/gif/py 补丁归档至 scripts/_archive/（保留 elevate-run/launch-elevator/fuzz-keys/gditest-local/smoke 脚本）
- [x] ClipboardService 重构出 BuildDataObject（纯组装，可测）；SelfTest 新增 T15a/b：FileDrop + CF_DIB(Bitmap) + 自定义 FrameCapture.Gif 三格式齐备、GIF 字节逐字相等、路径正确 → 45/45 PASS（不占用真实剪贴板）

## 4K 内存预算（第 16 轮）
- [x] FrameRing 固定 512MB 预算 → 自适应：可用物理内存×40%，夹在 [512MB, 2.5GB]；4K 长按不再 15 帧就丢
- [x] 实机抓到真 bug：MEMORYSTATUSEX.dwLength 手填 68（实际 64）→ GlobalMemoryStatusEx 一直失败、静默回落 512MB（修复前 1.2s 长按丢 22 帧仅剩 16；修复后 38 帧零丢弃）→ 改用 Marshal.SizeOf<>，SelfTest T14e 守护
- [x] 丢弃透明化：FrameRing.Dropped 计数 → CapturedSequence.DroppedFrames → 编辑器 HUD「⚠内存预算已丢 N 帧」+ Trace 日志
- [x] SelfTest 新增 T14a-e（驱逐顺序保首帧 / Dropped 计数 / Snapshot 副本 / 自适应区间 / 内存查询真实生效），43/43 PASS

## 健壮性证据（第 15 轮）
- [x] T13 同类多标注烘焙回归：3 个不同颜色椭圆 + 3 个点全部像素级可见 → 当年 draw7「第三笔丢失」为工具态问题（当时处于 Browse 态所画无效），烘焙层无丢失 BUG，该疑虑关闭
- [x] 乱按键盘 fuzz 脚本 scripts/fuzz-keys.ps1：编辑器窗口 PostMessage 300 个随机键（Space/工具键/Del/方向/Home/End/Tab），进程存活 + 窗口在 + error.log 零新增 → PASS；可重复执行（固定种子）

## 用户反馈（UI 细节·第二轮 10-02）
- [x] 全局 ToolTip 主题化：默认白底黑框与暗色主题脱节 → App 级隐式 ToolTip 样式（卡片底 + 圆角 8 + 投影 + 140ms 淡入），随主题着色（Fluent.ApplyGlobalStyles，启动与主题切换时重挂）
- [x] 托盘右键菜单：WinForms 灰菜单 → WPF ContextMenu（圆角卡片 + 投影 + 悬停圆角高亮 + 勾选字形 E73E + 分隔线主题化），随主题重建；开机自启项可勾选
- [x] 编辑器 timeline：默认 Slider → Fluent 细轨道（编辑器固定暗色 #3A3A40 轨道 + 强调色填充 + 圆形滑块）
- [x] 颜色芯片：悬停 1.18 放大 + 按下 0.9 回缩（弹性缓动）
- [x] SelfTest 新增 T12 全局样式/菜单资源解析回归（36/36 PASS）；timeline 截图复验通过（细轨道 + 蓝色圆滑块 + HUD「1/16 0.05s」）
- [x] 帮助窗滚动条主题化：默认亮色粗滚动条 → 全局细滚动条（10px 透明槽 + 圆角滑条，悬停加深）；期间踩坑：外层 ControlTemplate.Trigger 不能引用嵌套 Thumb 模板内 x:Name（KeyNotFound 'tb'）→ 悬停 Trigger 移入 Thumb 模板；T12 重构为直接解析 ToolTipStyle()/ScrollBarStyle()/MenuResources()（原先 Application.Current 为空时静默跳过，没起到回归作用）
- [x] 快捷键录入框悬停加深（Theme.Press 120ms 渐变）、timeline 拖范围条悬停增亮 + SizeAll 光标；帮助窗实机截图复验通过（暗色 + 强调色分节 + 细滚动条）

## 进行中
- [x] 明/暗双主题视觉验证：浅色主题此前从未真正生效——settings.json 从不落盘导致切换脚本静默失败（已修复：首启自动写默认配置），现明/暗均截图复验通过（含开关图标列 + 右对齐胶囊布局修复）

## 待办
- [ ] 时间轴拖范围 / 窗口吸附的实机确认（手动清单）
- [ ] 保存对话框（保存单帧/动图/视觉暂留）流程确认——默认目录已改为「图片\FrameCapture」

## Round 40 — 用户 9 条 bug 清单 + 悬浮工具条重构（已修，待用户复测）
1. 选取层不总在最前 → Deactivated 自动 re-topmost + 重复按 Ctrl+Shift+R 重新顶前
2. 蓝框不消失 → FinishCapture 后 2.2s 自动淡出（常显轮廓设置仍生效）
3. 蓝框被录进画面 → BeginCapture 前强制 HideOutline()
4. 播放只走一帧 → Show() 设 slider 加 _sliderGuard；播放键 E768▶/E769⏸ 切换（E768 经 glyphprobe 验证）
5. 编辑器无法拖动 → 窗口空白处 MouseLeftButtonDown→DragMove（控件自吞事件不受影响）
6. 顶部白条 → WindowStyle=None 默认白边，设 BorderBrush #323236
7. 气泡豆腐块 → ToolTip 样式显式 FontFamily=雅黑（此前继承按钮的图标字体）
8. 窗口只展示预览：工具条拆为两个独立置顶悬浮小窗 FloatBar（顶部=绘制，底部=播放+时间轴+保存），鼠标离开 500ms 自动淡出、回到预览即重新浮现；修复了 owner 构造期设 Owner 导致的 UI 线程死锁
9. 单帧截图无录制工具：单帧时仅顶部绘制条（含 保存帧/复制帧/✓/✗/设置/帮助），无底部条 — QQ 式
验证：SelfTest 67/67；e2e 长按捕获→play 持续 1.2s+→Right 步进→Enter 提交 GIF(20KB) ✓

## Round 41 — 用户复测后新 8 条清单（已修，已重新发布，待用户复测）
1. 选取窗口仍在其他窗口之下 → picker 400ms 巡检 SetWindowPos(HWND_TOPMOST, NOMOVE|NOSIZE|NOACTIVATE)（WPF Topmost 翻转不够，直接 Win32 重插置顶带）；FloatBar 同样加 500ms 巡检
2. 工具条默认在预览外侧下方 + 可拖 → 绘制条 Top=编辑器底部+10（外侧常驻）；FloatBar 空白处 DragMove，拖后置 ManualPos 不再被重排
3. 录制仍含蓝框 → 根因是 BeginCapture 结束时 FlashOutline（轮廓在录制期间上屏被 WGC 抓走）→ 彻底删除捕获起点的 FlashOutline/FadeAway，录制全程轮廓不上屏
4. 进度条填充与圆分离 → 弃用 WPF Slider（模板在部分 DPI 下 fill/thumb 错位），改单画布自绘时间轴（底轨+accent 填充+拇指圆同一坐标系绘制，不可能分离）；拖空白=跳转，拖两端 accent 把手=调裁剪范围（SetA/SetB 保持≥2 帧），悬停把手处光标变 SizeWE
5. 布局调整 → 播放条=预览画面下缘内侧（离开 500ms 自动隐藏，仅它自动隐藏）；绘制条=外侧下方常驻
6. 帮助窗无关闭钮 → 右上角 E711 + 空白可拖
7. 设置窗无法拖 → 空白处 DragMove
8. 切换主题闪一下 → Theme.Changed 事件（实际变化才触发）：设置窗原地交叉淡入淡出（旧 170ms 淡出→关，新 260ms 淡入，同位置）；帮助窗同样交叉重建；编辑器/FloatBar/轮廓窗订阅 Changed 即时换色（底色 260ms 渐变、轮廓用 Theme.Accent）
验证：SelfTest 67/67；e2e 长按 19 帧→播放持续→Esc 关闭；单帧 tap 仅 1 条绘制条且在编辑器底缘外侧 15px ✓；已重新 publish+zip（70MB），干净实例已启动
备注：调试中发现构建会因运行中实例锁 exe 而静默失败（MSB3027），构建前必须先 Stop-Process FrameCapture

## Round 43-44 — 自审补丁（时间轴命中测试 bug）
- 发现：_rangeCanvas 无 Background → WPF 中 null Background 不参与命中测试，轨道空白区点选/拖动失效（画布只有画出来的矩形可点）。改为 Background=Transparent。
- 清理死代码：CropSel 类、picker._dpiScale 字段（消掉 2 条编译警告）。
- 验证：SelfTest 67/67；e2e 捕获→编辑器+播放条→Esc 正常。已重新 publish+zip(70MB)，干净实例已重启。

## Round 45-46 — 生产版全链路验收 + 自检扩充
- 对发布实例 e2e：捕获→Enter→GIF(869KB/17帧)→剪贴板三格式（Image/FileDrop/自定义 FrameCapture.Gif）+ 胶片条 ✓；FileDrop 指向 %TEMP%\FrameCapture\capture-*.gif 真实存在 ✓
- 乱键+编辑中重复捕获压力：状态机正确回收（Idle↔Editing），进程始终 Responding ✓
- 新增自检 T68(SetA/SetB 钳位)/T69(Theme.Changed 只在实际变化时触发)/T70(时间轴映射边界+单调) → PASS=70 FAIL=0
- README.txt（发布包内+仓库根）更新为新布局说明（绘制条外侧常驻、播放条内侧、时间轴拖把手裁剪、悬浮条可拖）；zip 已重打包（二进制未变）

## Round 47 — 隐私修复：全局钩子不再记录用户按键（重要）
- 发现：HotkeyManager 每次键击都 Trace.Log("hook key=VK ...")，等同磁盘键盘记录（旧日志已含历史键码）
- 修复：逐键记录改为环境变量 FC_TRACE_KEYS=1 才启用（调试注入链路用）；状态标记（BeginCapture/FinishCapture/open editor/editor key=）保留
- 清理：删除所有含历史键码的 fc-trace.log
- 验证：新包运行后 trace 无任何 hook key= 行、功能标记完整；SelfTest 70/70；已重发布+重打包+实例重启

## 工程约定
- git 仓库已建立（master），**每轮至少提交一次**，重要改动即时提交，防重大回退

## 状态机与轮廓生命周期（2026-10-11 长跑 R1）
- [x] **常显范围轮廓在"编辑器开着再按捕获键"后永久消失**：根因 = `BeginCapture` 第一行无条件 `HideOutline()`，
  状态守卫在其后。修复 = 抽出 `Core/CaptureGuards.cs` 决策表，只有 StartCapture 才隐藏轮廓（不变量 `MayHideOutline`）。
- [x] **常显模式下一次录制把轮廓淡出且不回来**：根因 = `FinishCapture` 无条件 `FadeAway`，注释谎称"由后续分支重新点亮"。
  修复 = `OnCaptureFinished` 决策（常显+有已存范围 → 带回轮廓，否则淡出）。SelfTest +T75（81/81）；新增 `scripts/test-outline-lifecycle.ps1`。

## 全局钩子零阻塞（2026-10-11 长跑 R2）
- [x] **钩子回调阻塞等 UI 线程（全系统键盘卡 ~130ms + 静默摘钩风险）**：`Dispatch` 阻塞 `Invoke` →
  改 `BeginInvoke(Send)`（FIFO 保序）；钩子决策抽成 `Core/HookDecision.cs` 纯逻辑（SelfTest T76，85/85）；
  慢回调看门狗 `hook slow` 进 trace，回归脚本断言不出现。

## 编辑器退场一致性（2026-10-11 长跑 R3）
- [x] **Esc/Enter 绕过 CloseWithFade（与 X 按钮双标）**：三处统一 Exit150 淡出（8deb809）。

## 待办（2026-10-11 长跑交接，06:15）
- [x] **重新发布 release**（2026-10-11 12:27 完成）：先杀实例 → publish exit=0 → staging 替换 win-x64（exe 时间戳 12:27:10）
  → 实例 pid 78292 启动 trace "Startup done"；二进制内确认含 R1 字符串 "outline: persistent show"、R2 "hook slow"、R3 CloseWithFade 符号。
- [x] 裁剪销毁标注：Core/CropRemap 决策表——框内标注坐标重映射保留、跨界丢弃（T78 含像素级往返，90/90）。
- [x] 裁剪升级为视图状态（R8/R9）：CropState 求交栈 + ActionOrder 双流撤销——裁剪可撤销/可重做、二次裁剪=缩窗、帧数据永不破坏、导出瞬间一次性折算（T78c 像素等价）；T80a-f/T81a-c，103/103，guardrail PASS。
- [x] 编辑器重做按钮：E7A6 字形（GlyphProbe 取证）+ SetGhost 统一禁用淡出，初始 0.35 禁用态截图确认。
- [x] Fps 钳制不一致：已收口为 AppSettings.FpsMin/FpsMax 常量单一事实源（滑条/Normalize/编辑器×5/引擎×2 全引用，T77，87/87）。
- [x] 编辑器缩放/1:1 视图。（R7：Core/ZoomMath + Ctrl滚轮/PageUp/Down/Home/End + 拖拽平移 + HUD 倍率；T79a-d + e2e:zoom 入闸，94/94）
- [x] Esc 分层（R10）：绘制态 Esc 先退工具不丢捕获，浏览态才关窗（Core/EscStep + T82a + e2e:esc-layers 入闸，104/104）。
## 用户打磨清单（2026-10-11 13:20 提出，R12 进行中）
- [x] **点工具没有意义**（R12 已删）*：手绘一下就是点，删 Dot 工具（ToolOrder/决策表/帮助/README/SelfTest T72 同步）。
- [x] **颜色不能自定义**（R13 调色板+最近用色）：色板加"自定义"入口（取色弹层，含最近用色）。
- [x] **线宽不能自定义**（R13 滑杆 1-24px）：细/中/粗 外加自定义数值。
- [x] **播放控件常驻 + 移到预览内容下方**（R12 停靠面板）：录制（多帧）时常驻显示，停靠预览画面下方（不再悬浮在内容里/不再自动隐藏）。
- [x] **PR 式带刻度小进度条**（R12 落地）：逐帧刻度+整秒标尺+裁剪把手+播放头，替换现 320px 圆点进度条。
- [x] **面性图标 + 更紧凑**（R12 定稿）：GlyphProbe 全区扫描定稿面性集（F5B0▶/E769⏸/F08C◀/F08E▶/F8AC⏮/F8AD⏭/E105存/F413复制/E786GIF/F78C✓/E9CE?/EA3B○/EDD7✎/F0AF→），按钮尺寸收小。
- [ ] **每个组件都过一遍改好看**（进度条被点名）。

