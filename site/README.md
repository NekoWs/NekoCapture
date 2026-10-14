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
