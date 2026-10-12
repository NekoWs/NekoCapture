# FrameCapture Todo

## 用户反馈（10-12 排队，按用户给定顺序做）
- [x] **新增「选中」工具（V）**（R52）：对象编辑（移动/改大小/删除）从选中工具进入。
- [x] **「圆」图标改线性**（R53）：图标与产物形状一致（GlyphProbe 验证线性环字形）。
- [x] **自实现 RGBA 调色板**（R54，R59 修游标钉死 bug）：DevTools 式自绘调色板替换系统选色器。
- [x] **笔宽「…」弹层消失 bug**（R55）：OpenPopSticky——按下松开不关，点别处才关。
- [x] **设置项：按下选区快捷键后是否显示旧选区**（R56）：ShowLastRegion 开关，默认开。
- [x] **设置页改版**（R59）：六分组小标题 + 自绘标题栏 + 强调色就地调色板。
- [x] **双击捕获键 = 开始/结束录制（免按住）**（R60）：Core.ToggleRecord 纯状态机（点按窗内引擎不停=零间隙）；
      REC 徽标常驻并写明「再连按两下结束」；录制中单按=无操作；关开关=旧语义零漂移（点按零延迟）。
- [x] **移除录制时长上限（MaxDurationMs）**（R61）：帧改磁盘 spool（FrameSpool 逐帧追加写 + FrameSpoolList
      惰性回读/Slice 零拷贝），编辑器位图改自适应 LRU，GIF 导出走 Slice——4K@60fps 纯内存 OOM 根除。
      删 `_maxTimer` 停止判定：长按录到松手、连按录到再连按，唯一停止=用户动作或磁盘护栏（剩余<1GB 自动收尾）。
      设置页删「最长时长」滑杆行；HelpWindow 文案改「不设时长上限」；MaxDurationMs 仅留 JSON 兼容。
      e2e：录 9.36s/186 帧（spool 383MB）秒开、拖时间轴惰性回读、GIF 4.43MB、关窗删 spool。SelfTest 197/197。
- [x] **设置窗口自定义标题栏**（R59）：QQ 式自绘标题栏+关闭钮；拖动改 WM_NCHITTEST→HTCAPTION 原生拖窗（根治拖不动+超调）。
- [ ] **设置滑杆控件修缝+美化**：缝隙已修（R57）；整体样式重做按用户「之后再美化」留后。
- [x] **托盘右键菜单两修**（R58）：失焦即关（锚窗激活+Deactivated 收菜单）+ 紧凑化对齐编辑器密度。
- [x] **帮助窗改紧凑布局** + **全盘 UI 布局整理**（R62）：编辑器为基准；标题栏抽成共享件 Fluent.TitleBar/
      InstallCaptionDrag（设置+帮助同款），帮助窗去大标题/悬浮关闭钮、分组小标题对齐设置窗 Group、行距内衬收紧
      （高 1143→1029）。帮助/设置/托盘三窗现共享同一套密度与视觉语言。
- [x] **编辑器时间轴悬停竖线改常驻**（R57）：常驻+内缘精确贴首尾。
- [x] **时间轴右侧数值文本占位**（R57）：MinWidth=210 右对齐，布局恒定。
- [x] **自动保存副本（AutoSaveCopy）**（R63）：opt-in 开关（默认关，关=零副作用）——捕获后除剪贴板外再往文件夹写一份文件，
      单帧截图存 PNG、多帧录制存 GIF（已烘焙标注/裁剪的最终产物）；「保存位置」路径行可弹 FolderBrowserDialog 选目录
      （空=图片\FrameCapture），时间戳命名 + 唯一去重绝不覆盖。SaveService 新建，SelfTest T101a-e，实机 e2e 双路径复验。
- [x] **编辑器记住工具/颜色/粗细（RememberEditorTools）**（R64）：默认开——关编辑器时记住最后用的标注工具/颜色/线宽，
      下次打开直接是它们（QQ 编辑器不记，差异化手感）。新增「编辑器」分组开关；Core/EditorPrefs 纯决策（颜色 ARGB 全等落槽、
      宽度命中预设、#AARRGGBB 格式），Closed 时写一次。SelfTest T102a-f，实机 e2e（按 A+点绿+点粗→✕→重开预选一致）。
- [x] **钉到屏幕（PinWindow）**（R65）：编辑器点「钉到屏幕」按钮或 Ctrl+P，把当前帧（含标注/裁剪）作为无边框置顶小图钉在原捕获位置，
      可拖动/滚轮缩放(10%~600%)/双击或 Esc 关闭/右键复制，多张并存——补齐 Snipaste/QQ 招牌手感。PinWindow+PinZoom 新建，
      BakeCurrentFrame 复用烘焙。SelfTest T103a-e，实机 e2e（按钮+Ctrl+P 弹图、显示正确、Esc 关闭）。
- [x] **贴图增强：不透明度 + 右键菜单**（R66）：Ctrl+滚轮调贴图不透明度(20%~100%)，右键菜单 复制/不透明度▸(100·75·50·25)/关闭/关闭全部贴图(N)，
      贴图登记表支撑一键清空。Core/PinOpacity 新建。SelfTest T104a-e，实机 e2e（菜单+子菜单渲染、50% 生效、关闭全部清空）。
- [x] **识别文字（OCR）**（R69）：编辑器「识别文字」按钮(E8C1)或 Ctrl+O，用 Windows 内置 OCR（离线、中英文）识别当前帧印刷文字，
      纯文本写剪贴板 + 预览底部 snackbar 反馈「已识别 N 字 · 已复制」；无语言包优雅降级提示安装，绝不抛。
      Core/OcrService 新建（引擎优先用户语言回退任一已装；帧像素首 await 前同步拷入无 spool 竞态；Classify 纯函数）；
      新增两模式通用 snackbar 轻提示。tools/OcrProbe 探针确认本机 zh-Hans-CN 可用。SelfTest T105a-d，实机 e2e（记事本已知文字→剪贴板全中）。
- [x] **选区放大镜**（R70）：拖框/悬停选区时光标旁高倍像素放大镜（15×15 源像素→150 DIP 像素化）+ 中心像素标记 + 坐标/尺寸读数，
      像素级精确选区（QQ/Snipaste 招牌）。选区窗构造时抓干净桌面快照（遮罩未渲染→真实颜色），失败自动降级。
      设置项「选区放大镜」默认开（E721 字形）。Core/MagnifierMath 新建（Crop/Place 纯函数）。SelfTest T106a-g，实机 e2e（放大出清晰像素块、读数坐标精确）。
- [x] **放大镜取色器**（R71）：放大镜底部加色块 + 光标下像素 `#RRGGBB`，按 C 复制颜色 HEX——选区即取色，设计师/开发者刚需。
      构造时保留快照原始 BGRA 直读中心像素；MagnifierMath 加 PixelRgb/Hex 纯函数。SelfTest T107a-f，实机 e2e（色块+HEX 渲染、中心像素=HEX 内部一致、C 复制剪贴板全中）。
- [x] **OCR 结果预览窗**（R72）：设置「识别文字后显示结果窗」（默认关）开启时，OCR 成功另弹可编辑/可部分复制的结果窗（字数 + 复制全部）。
      OcrResultWindow 新建（复用 Fluent 标题栏/拖窗/淡出）；OcrService.Normalize 纯函数清理文本（剪贴板与结果窗共用）。SelfTest T108a-f，实机 e2e（弹窗、内容/字数/复制全部、Esc 非模态关）。
- [x] **序号标注工具**（R73）：按 N 点击放递增编号圆标（1、2、3…），步骤/教程标注刚需；删除后编号自动回退不重号。
      NumberBadge.Next 纯函数（max+1）；AnnKind.Number + GDI 烘焙（实心圆+居中白字）+ WPF 预览；ToolCatalog 加条目（Glyph E71D 定稿）工具条/帮助自动出现；T92 同步 9 工具。SelfTest T109a-f，实机 e2e（1/2/3 递增、删除回退、钉图烘焙进像素）。
- [x] **贴图磁边**（R74）：拖动贴图松手自动吸附所在屏幕工作区边缘（Snipaste 式停靠四边/四角）；设置「拖动贴图自动磁边」（默认开）。
      PinSnap 纯函数（阈值=短边6%夹12–40；左右/上下边互斥吸附）；PinWindow 松手后 SnapToEdge（中心定位所在屏、工作区物理÷DpiScale 转 DIP）+ Motion.StateMs 平滑滑入；新增「贴图」设置组。SelfTest T110a-f，实机 e2e（底边精确吸附到工作区底 2088、齐平任务栏）。
- [x] **贴图 OCR 入口**（R75）：贴图右键菜单加「识别文字」，就地 OCR 贴图里的印刷文字（中英）→ 写剪贴板，开「识别后显示结果窗」则弹结果窗，否则贴图内浮层提示。QQ 无 OCR，差异化能力。
      OcrService.Feedback 纯函数（编辑器/贴图共用措辞）；PinWindow 内容改 Grid + Flash 浮层（淡入停留淡出）；OcrFrame 复用 RecognizeAsync(_frame)/Normalize/OcrResultWindow。SelfTest T111a-c，实机 e2e（右键→识别文字→结果窗中英正确+剪贴板）。
- [x] **选中已有标注改颜色/粗细**（R76）：V 选中一条标注后点色板/线宽，就地改这条的颜色/粗细（文字/序号粗细即字号），Ctrl+Z 可撤销；未选中时改的是后续新标注默认值。通用工作流修复（改错不用删了重画）。
      Annotation.WithStyle 纯函数（副本，null 保持原值）；EditorWindow.RestyleSelected 用 UndoRedo.Replace（Edit op）原位替换 + 无变化不刷撤销守卫；挂钩色板/最近用色/取色器关闭/3 挡线宽即时改，自定义滑杆关闭时一次性应用。SelfTest T112a-d，实机 e2e（红箭头→蓝→撤销回红；粗→细→撤销回粗，像素采样+截图）。
- [x] **贴图对齐到同屏其他贴图**（R77）：磁边从「只贴屏幕四边」升级为「屏幕四边 + 同屏其他贴图的边/中线」，拖动贴图松手自动与相邻贴图对齐（Snipaste 式排布手感）。
      PinSnap.SnapToLines 纯函数（left/right 贴屏幕边+贴图线，center 只贴图线避免半出屏，每轴取最近、超阈值不动）；SnapToEdge 收集同屏其他贴图边/中线（跨屏 DPI 不同仅同屏参与）。SelfTest T113a-h，实机 e2e（两图同位→右拖 261px 不吸、左拖回 6px 内吸附对齐 Left=1500、竖直下拖左缘仍对齐，EnumWindows 读真实矩形 + trace + 截图）。
- [x] **托盘「编辑上次截图」**（R78）：静态截图确认浮窗 3.2s 自动消失后，仍可从托盘把最近那张重新拉回编辑器标注（不用重截）。
      Capture/LastStill 纯状态持有（只留最新一帧，Set 覆盖/Clear/TryGet）；控制器 HasLastStill + EditLastStill（复用 OpenEditorForFrame，Idle 才开，编辑器关闭不释放该帧可反复找回）；托盘条件加项；CLI `--edit-last` 同入口（脚本可用 + 可自动化验证）。SelfTest T114a-d，实机 e2e（截图→toast 消失→--edit-last→编辑器打开且内容正确）。
- [x] **贴图旋转 90°**（R79）：贴图就地顺时针转正（横拍/竖拍图不用回编辑器），R 键或右键菜单，转 4 次回原图，Snipaste 有 QQ 无。
      Core/PinRotate 纯函数（Rotate90Cw 无损重排 old(x,y)→new(H-1-y,x)；Rotate steps 取模、0 同实例）；PinWindow.Rotate 换帧+重建 Source+重算 base+中心锚回中。SelfTest T115a-d，实机 e2e（703x403→R→403x703 中心不变、内容转 90°、trace pin-rotate）。
- [x] **贴图「另存为」**（R80）：贴图右键有复制却无保存，补标准另存为（存的是当前帧，含旋转后状态）。
      PinWindow.SaveAs：SaveFileDialog（默认「图片\FrameCapture」+ `pin-<时间戳>.png`）→ BgraImage.SavePng（复用编辑器路径）→ Flash「已保存」+ trace pin-save；右键菜单「另存为…」+ Ctrl+S。实机 e2e（Ctrl+S→另存为对话框→回车→落盘 700x400 正确）。附带查清「关编辑器贴图消失」是全局 SendInput 漏键到前台贴图的测试假象，非产品 bug。
- [x] **编辑器图标语义修正**（R81，实机走查发现）：招牌「钉到屏幕」原用通用图片图标 E91B（看不出是"钉"），改实心图钉 E840；「总在最前」改置顶箭头 E898（开关态靠高亮底色），两按钮名副其实互不混淆。
      GlyphProbe matrix 肉眼选字形验证；PinBtn 单点定义两工具条同生效。实机 e2e（放大工具条截图：钉到屏幕=图钉、总在最前=置顶箭头）。
- [x] **首次启动欢迎卡**（R82）：新用户装完只见托盘图标、不知按什么键——首启弹不抢焦点卡片点破两热键+关键手感+「查看帮助」入口，`FirstRunHintShown` 看过一次永不再打扰。复用 CaptureToast 骨架（WDA 排除/悬停暂停/12s 自动消失/Esc/关闭 X）。
      查清 CopyFromScreen 抓「无边框+Topmost+透明背景」WPF 窗恒黑是 GDI/DWM 怪癖（CaptureToast 同款骨架同黑，PrintWindow+WindowFromPoint 双证用户可见），视觉审查改 PrintWindow。实机 e2e：首启渲染正确、重启不再弹、关闭 X 收卡、查看帮助开帮助窗。
- [x] **「截图后」工作流三选项**（R83）：截图完成后干什么是根本工作流分叉（QQ 强制进编辑器 / Snipaste 只复制），做成显式三选一兼容三类人群——弹提示（默认，浮窗点一下/Enter 进编辑器）· 进编辑器（复制后直接开标注，QQ 式）· 只复制（不弹窗不开编辑器，最快，轮廓闪一下作反馈）。三态都仍写剪贴板+托盘可找回编辑器。
      `AfterCapture` 设置 + `Core/AfterCapture.Resolve` 纯决策（脏值回落 toast，T116a-d，SelfTest 276→280）；设置页文字芯片三选行就地更新选中态。实机 e2e 三态各验（editor 开编辑器无浮窗 / copy 无窗但剪贴板有图 / toast 浮窗现）。
- [x] **贴图「更新内容」原地重抓**（R84）：Snipaste F3 招牌、QQ 缺位——贴图常用来盯梢会变的画面（进度/数值/直播），此前是静态快照，源画面变了只能重截再钉。做成按贴图来源区域原地重抓，位置/缩放/旋转全保持。
      `PinWindow` 记源区域 + 旋转步数；`Refresh()` 先 Hide 避免自捕获→200ms 后 Grab→按角度重放→中心锚定换图→Flash「已更新」。F3 键 + 右键菜单双入口。实机 e2e PrintWindow 前后像素对比（桌面↔设置 UI 双向，diff 15~20%，trace `pin-refresh`）。坑：Ctrl+P 不关编辑器抢焦点，F3 需贴图聚焦（创建即 Activate/点一下即聚焦，真实用法天然满足）。
- [x] **荧光笔 / 马克笔高亮**（R85）：QQ 招牌标注工具、我们缺位——在截图上划半透明高亮圈重点又不遮字。此前只能画不透明框/手绘，都盖字。
      复用 Freehand 的 Polyline 预览 + DrawCurve 烘焙双路径（只改半透明/加粗/平头帽三参数，WYSIWYG 风险最低）；`AnnKind.Highlight` + `Annotation.HighlightAlpha=90`（alpha 是「类型」属性统一施加，标注颜色仍存不透明色→改色/调色板照常，预览与烘焙同取常量保证所见即所得）；编辑器 `Tool.Highlight`（H 键，线宽挡位×2.5，平头帽）；`ToolCatalog` 加 "H"（图标 E7E6 马克笔 GlyphProbe 定稿，帮助符号非 PUA ▨）。SelfTest 280→**282**（T117a 半透明红 over 白透出白 / T117b 对照不透明 G/B≈0），T92 计数 9→10。实机 e2e（PrintWindow+像素采样）：工具条马克笔图标现、H 选中蓝底、拖出平头半透明红条、笔画 (96,20,27) vs 背景 (21,21,23) 证实透出、帮助窗 ▨ 正常渲染。

- [x] **截图提示音**（R86）：QQ 招牌听觉反馈、我们缺位——截图完成「嘀」一声确认「截好了」（尤其只复制模式画面无变化时）。但后台工具默认响会吵，故做成**默认关的开关 + 「试听」按钮**，兼容所有受众。
       `Core/CaptureSound.cs` 运行时合成（不引外部音频文件）：`Samples()` 柔和短促「嘀」（44.1kHz/16bit/42ms/1000Hz 正弦/指数衰减短尾/2ms 起音防爆音/峰值仅 0.18 满量程）→ `MakeWav()` 封 PCM WAV（纯函数）→ `Play()` 用 Win32 `PlaySound(SND_MEMORY|SND_ASYNC)` 异步播放（避开 SoundPlayer 立即 Dispose 截断坑），异常静默忽略。设置项 `CaptureSound`（默认 false），`CaptureController` 静态图捕获成功后接线（三态都响，关=零副作用）；设置窗「截图」组 `CaptureSoundRow()`（扬声器图标 E767 + 试听 chip + 开关），帮助窗补说明。SelfTest 282→**286**（T118a WAV 头/RIFF size 自洽 · T118b 时长 30–60ms · T118c 峰值 0.1–0.5 满量程 · T118d 末样本衰减<0.02）。实机 e2e（PrintWindow+trace）：F6 截图 trace `capture sound played=True bytes=3748`、设置行渲染正常、点试听再响一条、开关持久化 False + OFF 视觉确认。

- [~] **滚动截图**（R87 算法已落地 / UI 待 R88）：QQ 最大缺口、我们完全没有——长网页/聊天记录/代码一屏截不下，滚动自动拼长图。多轮特性。
       R87 已做核心拼接 `Core/ScrollStitch.cs`（纯函数）：`FindOverlap` 灰度行签名从最大重叠往下扫命中即返回（偏好最大重叠、滚动小则快），搜索窗口界定 `min(accH,bH)`+候选上限防卡顿；`Append` BlockCopy 拼新内容。SelfTest 286→**290**（T119a-d 合成帧：找重叠/拼接/无重叠/抗噪）。真实像素取证 `SelfTest stitch` 模式：同区域下移 200px 两帧→overlap=800 正解、stitched.png 肉眼无缝。踩坑：早退均值 bug（加 bad 标志）、测试帧灰度须用雪崩哈希（线性序列偏移 d 只差近恒定小值会假重叠）。**R88 待做**：捕获循环 + 控制浮窗（滚动后按空格拼接/Enter 完成/Esc 取消）+ 完成后进剪贴板与 AfterCapture 流；真实滚动阈值调优。

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
- [ ] 待查：error.log 偶发 `(Dispatcher) ObjectDisposedException: Cannot access a closed file`（10-12 03:45 单次，已被 e.Handled 兜住未崩）——疑某处对已关闭流写盘（设置保存/trace/临时图？），若复现再定位

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

## 滚动截图（长图）R87–R88（2026-10-12 上午）
- [x] **核心拼接算法**（R87）：`Core/ScrollStitch.cs` 纯函数 `FindOverlap`/`Append`；T119a-d，290 PASS；真实像素 `SelfTest stitch` overlap=800 精确无缝。
- [x] **自动捕获流程 + 控制条**（R88）：`ScrollHintWindow`（不抢焦点、WDA 排除、进度/完成/取消）+ `CaptureController` 定时器自动拼接（稳定判定）+ 托盘项 + `--scroll` IPC + `Shot wheel`。实机 2840px 长图行号 009→250 连续无缝。
- [x] **全局 Esc 取消**（R90）：控制条不抢焦点致 Esc 漏给目标窗——全局钩子在会话期吞 Esc 触发取消，标志绑定控制条生命周期（Show 置位/Closed 兜底/Done·Cancel 即时清零）防泄漏。实机 Esc 取消 + 二次 Esc 不误触（无泄漏）。
- [ ] **均匀内容局限**（已知边界，非 bug）：行间差异过小（如整页相同文本/相同表格行）时 `FindOverlap` 可能误判重叠致跳号——重叠拼接固有，QQ 同款。真实网页/聊天/文档不受影响。若将来要硬扛：可加「行签名用更宽采样 + 提高 thresh + 找不到可靠重叠宁可整帧追加并提示」的兜底，或引入特征点匹配（重）。暂不做。
- [ ] **滚动截图手感打磨候选**（R89+）：① 平滑滚动（触控板）下稳定判定阈值/间隔调参；② 吸顶/吸底固定表头页会破坏重叠（可检测「顶部 N 行恒定」并忽略）；③ 超长图（>1万 px）编辑器缩放/内存；④ 完成后 toast 上直接给「继续滚动」入口；⑤ 阈值/最小新增行数做成设置项（当前 3px 硬编码）。
- [x] **延时截图**（R91）：独立托盘动作 + `CountdownWindow` 倒计时浮窗（不抢焦点、WDA 排除、取消按钮）+ 设置 `DelayCaptureSeconds`（关/1/3/5/10 秒 chip）+ `--delay` IPC。截右键菜单/悬停提示/下拉框这类一动就消失的界面。实机倒计时递减 + 到点出图 + 取消中止全验。
- [ ] **录制倒计时**（仍缓）：触碰 R60 ToggleRecord 状态机，风险高，单独评估。
- [ ] **贴图鼠标穿透**（仍缓）：开了没法用新全局热键关，是个坑，需先设计退出路径。

## 延时截图 R91（2026-10-12 上午）
- [x] **倒计时截当前范围**：`CountdownWindow` + `BeginDelayedCapture`（取已存范围/全屏，0 秒直截）+ 完成路径复用 ScrollDone + 托盘项 + `--delay` IPC + 帮助。护栏 PASS，实机视觉/递减/fire/取消全验。
- [ ] **延时截图候选**（后续）：① 全局热键触发延时截图（当前走托盘，加键需进快捷键设置，避免堆键先不做）；② 倒计时期间也支持 Esc 取消（复用 R90 全局钩子模式，加 `CountdownActive` 标志——但倒计时窗可点「取消」，优先级低）；③ 延时截图前把倒计时窗自动挪开避免挡住目标菜单（当前顶部居中，一般不挡）。

## 从剪贴板贴图 R92（2026-10-12 上午）
- [x] **粘贴即贴图**：`BgraImage.FromSource`（Bgra32 建帧，往返无损，T121a-c，301 PASS）+ `CaptureController.PinFromClipboard`（光标处落点、钳进屏内）+ 托盘「从剪贴板贴图」+ 无图气泡提示 + `--pin-clip` IPC + 帮助。护栏 PASS，实机有图/无图/PrintWindow 内容一致全验。
- [ ] **贴图候选增强**（后续）：① 全局热键「粘贴即贴图」（Snipaste 是 F3，但我们 F3 已用于贴图重抓；需另择键并进快捷键设置，避免堆键先不做）；② 剪贴板是 GIF/多帧时的处理（当前 `GetImage` 只取首帧静态图，够用；将来可考虑钉动图）；③ 贴图「复制回剪贴板」已有（右键菜单），可加 Ctrl+C 快捷键。

## 编辑器工具栏裁切修复 R93（2026-10-12 上午，手感审计）
- [x] **窄图工具栏两端裁切**：`FitMinWidthToToolbar` 让 MinWidth 跟随工具条实测宽（夹 `[812,工作区宽]`，未显示时重居中，收敛保护）+ 修 guardrail 探针接受选区窗（`TapAlwaysShowsRegion` 开时点按弹选区窗，旧探针误判拦截致 e2e 全 SKIP）。护栏 PASS，实机小图工具栏两端全可见。
- [ ] **编辑器布局候选**（后续）：① 极窄屏（<工具条宽）下工具条仍会裁——可考虑溢出收纳进「⋯」或换两行，当前工作区宽夹取已覆盖常见屏；② 多帧（录制）编辑器底部有播放面板，工具条+面板总高在小图时是否需同样自适应高度（本轮只处理宽）；③ 工具条可拖拽/可配置顺序（重度需求，暂缓）。

## 暂停全局热键 R94（2026-10-12 上午，兼容受众）
- [x] **一键让位**：`HotkeyManager.Paused`（运行时、不持久化）+ `HookDecision.Decide(paused)` 纯决策（暂停时放行不吞、进行中捕获仍收尾，T122a-d，305 PASS）+ 托盘可勾选项 + tooltip「已暂停」+ 气泡 + `--pause-toggle` IPC（可绑 Windows 快捷方式当暂停热键，免堆键）+ 帮助。护栏 PASS，实机基线/暂停/恢复三段全验。
- [ ] **暂停候选增强**（后续）：① 可选「暂停 N 分钟后自动恢复」定时器（演示场景友好，需 UI 选时长，暂缓避免堆数值）；② 暂停时托盘图标换灰显变体（当前仅 tooltip 文字，图标变体需再出一套图标资源）；③ 只暂停部分热键（如只停录制不停截图）的细粒度开关（需求不普遍，暂缓）。

## 贴图窗快捷键补全 R95（2026-10-12 上午）
- [x] **Ctrl+C 复制回剪贴板 / Ctrl+O 识别文字**：`PinWindow.Copy()`（右键菜单与快捷键共用）+ OnKeyDown 两分支 + 帮助/注释同步。护栏 PASS，实机贴图 Ctrl+C 逐像素核对复制内容与贴图一致。
- [ ] **贴图候选增强**（后续）：① 贴图窗也支持 Ctrl+S 已有，可加「空格=播放/暂停」用于钉住的 GIF/多帧（当前贴图是单帧静态，需先支持多帧贴图）；② 贴图方向键微调位置（1px，Shift=10px）像素级对齐；③ 贴图组批量对齐/等距分布（重度需求，暂缓）。

## 选区窗内按 F 循环取色格式 R96（2026-10-12 上午）
- [x] **免进设置切格式**：`_colorFormat` 可变 + `_lastRgb` + `F` 键循环 Hex→RGB→HSL（就地刷新放大镜读数 + `AppHost.Current.Settings.Mutate` 持久化）+ 帮助。护栏 PASS，实机三格式 + 持久化全验。
- [ ] **取色候选增强**（后续）：① 放大镜读数对超长格式（hsl）的换行/宽度自适应（当前单行够用）；② 取色历史（最近 N 色，编辑器调色板已有最近用色，可打通）；③ 选区窗内也支持 Shift+F 反向循环（小便利，暂缓）。

## 编辑器小图默认 1:1 不再放大变糊 R97（2026-10-12 上午，手感审计）
- [x] **FitScale 封顶 1.0**：小图默认 1:1 清晰居中、大图仍缩小适配；`OneToOneZoom`/`ZoomFit`/`ZoomAt`/`ClampPan` 自洽。SelfTest +T123a-d（309），护栏 PASS，实机小图 1:1 居中核对。
- [ ] **缩放候选增强**（后续）：① 小图四周留白偏多——工具栏溢出收纳成「⋯」后窗口可随小图收窄（R93 候选①，一并解决）；② 「适配」与「1:1」在小图时同值，可让适配键对极小图仍铺满（用户显式要求放大，需与默认区分，暂缓）；③ 双击画布在 1:1↔适配间切换（便利，暂缓）。

## 取色复制格式 R89（2026-10-12 上午）
- [x] **HEX/RGB/HSL 可选**：`Core/ColorFormat.cs` 纯格式化 + 设置项 `ColorCopyFormat`（默认 hex）+ 选区放大镜读数/按 C 复制 + 设置窗 chip 行 + 帮助。T120a-h，298 PASS；实机 rgb/hsl 剪贴板 + 读数 + 设置行渲染全验。
- [ ] **取色候选增强**（后续）：① 选区窗内按某键**循环切换**格式（不用进设置）；② 取色历史（最近 N 色，编辑器调色板已有最近用色，可打通）；③ 放大镜读数对超长格式（hsl）的换行/宽度自适应（当前单行够用）。

