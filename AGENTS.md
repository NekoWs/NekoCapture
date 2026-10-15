# AGENTS.md — 仓库硬约束摘要

> 详规按需展开读，不要每轮都读全文。本文件只放**违反即回退**的条目。

## 注释（详规：[docs/COMMENT-STYLE.md](docs/COMMENT-STYLE.md)）

1. **只写当下事实。** 变更叙述是缺陷源：`此前/之前/原来/曾经/退役/弃用/改为/修掉/教训/历史` 为硬禁词，
   `旧/不再/回归/修复/坑/bug` 为歧义词需人工判定。护栏：`scripts/lint-comments.ps1`（已挂 guardrail 第 0 关）。
2. **注释里不写编号。** 禁止 `R###`（需求轮次）与 `T###`（SelfTest 用例）。测试可追溯性靠 `Chk("T145-…")`
   的名称字符串本身，需求可追溯性靠 `git log`。
3. **历史归 DECISIONS.md，代码里只留结论。** 护栏型教训（曾经错在哪 → 根因 → 现在靠什么防回归）一条一节；
   流水账直接删。**不要在代码注释里复述 DECISIONS.md**——复述即分叉，分叉即新的谎言。
4. **改代码必须同批改注释。** 注释承诺的行为必须有实现；注释与代码不一致按缺陷处理。
5. **`public`/`internal` 成员必须有 XML 文档。** 有参必写 `<param>`，有返必写 `<returns>`
   （`CS1591`+`CS1573`=error）。中文为主，代码符号与 Win32/WGC/GDI/LZW 等术语保持英文。

## 文案（界面 / 网页 / 提示 / 帮助）

6. **标签只说名字。** 补充说明并进句子，或交给界面上别处：键位交给键帽表与工具条图标，选项就是那一行的按钮。
   `录制（再按一下停止）` → `开始/停止录制`，`延时截图（3 秒）` → `延时截图`，`选中 (V)` → `选中`。
   括号只留在代码符号与记法里（`rgb(r, g, b)`、`*.ogg;*.wav` 过滤器、`Program Files (x86)`）。
   护栏：`scripts/lint-copy-parens.ps1`（已挂 guardrail 第 0 关），逐行豁免写 `// copyok`。

## 其余硬约束

见 [GOAL.md](GOAL.md) 的 Hard constraints 段（仓库内写文件、禁 OS 级鼠标自动化、不记录用户按键、
图标只用 Segoe Fluent Icons PUA 字形且先探测再选型、动效必须引用 `Fluent.Motion` 令牌、
构建前 `Stop-Process FrameCapture`、不得改弱断言）。
