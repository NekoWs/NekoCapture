# FrameCapture 宣传页

纯静态站点，无构建步骤：直接用浏览器打开 `index.html` 即可（`file://` 就能跑，
脚本一律传统 `<script>`，不用 ES Module，避免 `file://` 下的 CORS 限制）。

## 文件划分

| 文件 | 职责 |
| --- | --- |
| `css/tokens.css` | 设计令牌。配色逐条对应 `src/FrameCapture/Core/Theme.cs`，时长逐条对应 `Core/Fluent.cs` 的 `Motion` 常量 |
| `css/base.css` | 重置、排版、Mica 式氛围背板、滚动进度条 |
| `css/layout.css` | 页面骨架：顶栏、首屏、区块、栅格、断点 |
| `css/components.css` | 按钮、键帽、卡片、窗口模型、选区、标签页、令牌表 |
| `css/motion.css` | 关键帧与状态过渡，时长一律引用令牌 |
| `js/tokens.js` | `FC.Motion` 常量、`prefers-reduced-motion` 实时读取、节流与等待工具 |
| `js/theme.js` | 明暗切换（跟随系统 / 手动覆盖 / View Transitions 圆形扩散） |
| `js/reveal.js` | 滚动入场与逐项错位，含观察器失效时的兜底 |
| `js/hero.js` | 首屏捕获序列：取景框落位 → 撑开选区 → 描边闪光 → 轻提示 |
| `js/tabs.js` | 演示区标签页，含方向键可达 |
| `js/pointer.js` | 卡片镜面高光与轻微倾斜、氛围光跟随、滚动进度、导航当前项 |
| `js/main.js` | 装配 |
| `js/lang.js` | 浏览器语言与页面语言不符时补一行英文入口，关掉后记住；`?hint=1` 强制显示，供设计审查 |
| `en/index.html` | 英文页：与中文页同一套结构与样式，只换文案与资源路径 |

## 双语

- 中文页在站点根，英文页在 `en/`，两边互相 `hreflang` 声明；顶栏两个链接各用各自的语言写自己的名字。
- 不做自动跳转：把人正在读的那一页换掉是越权，语言由人点。
- 双文件方案的代价是加一块内容要写两份文案，只改一边会让另一种语言漏内容；换来的是不开 JS 也能读、爬虫能索引整页。
- **界面截图按语言分目录**：`assets/shots/` 是中文界面，`assets/shots/en/` 是同一批窗口的英文界面，英文页指后者。
  哪一窗在桌面端还没做英文，英文页就仍指中文截图——宁可两种语言共用一张图，也不放一张半中半英的图。
  出图一律用 `SelfTest` 离屏拍真实窗口，`FC_LANG=en` 控制语言，末尾 `@1.5` 是宣传页用的 150% 倍率
  （按 DPI 重绘，不是放大位图）：`rendersettings <路径> <类别>`、
  `rendereditor <路径>`、`renderpin <路径>`。后两者的画面内容是一段合成的屏幕画面——
  深色桌面加一个文本窗口，正文跟着界面语言换中英文，为的是让读者看出这是截来的画面而不是一片色块。

## 与桌面端同源（改一边必须改另一边）

- **配色**：`--win/--panel/--chrome/--card/--text/--text-dim/--stroke/--hover-bg/--press-bg/--track`
  对应 `Theme.cs` 的 `WindowBg/PanelBg/ChromeBg/CardBg/Text/TextDim/Stroke/Hover/Press/TrackBg`，
  alpha 按 `0xF2 / 0xE6 / 0xD9 / 0x55 / 0x22 / 0x15 / 0x33` 换算。
- **时长**：`--enter/--editor-enter/--exit/--stagger/--hover/--state/--theme/--spring/--press/--quick/--dissolve/--flash`
  对应 `Motion.Enter/EditorEnter/Exit/Stagger/Hover/State/Theme/Spring/Press/Quick/Dissolve/Flash`。
  `js/tokens.js` 里的 `FC.Motion` 是同一组值的数字形式。
- **圆角**：窗 12 / 控件 10 / 正圆色块 13。
- **图标**：只用 Segoe Fluent Icons 的 PUA 码位，与工具内同一套字形。
  非 Windows 访客拿不到该字体，所以图标一律只做装饰，旁边必有文字。

## 静态模式

地址加 `?static=1`：时长全部塌成 1ms，入场与首屏循环停在终态。
供打印、抓取与截图比对使用，真人访问不受影响。

## 无障碍与降级

- `prefers-reduced-motion: reduce` 下所有动画与位移取消，内容一律可见。
- 观察器或脚本失效时，视口内内容仍会在兜底时限后显示——动效可以没有，内容不能没有。
- 标签页支持方向键与 Home/End；按钮与链接都有 `:focus-visible` 描边。
