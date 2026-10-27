# 动作链正交化（方案 B）实施说明书

> 目标读者：接手实现的编码代理（GPT）。
> 本文件是这次改动的**唯一事实源**：实现细节与本文件冲突时，以本文件为准；本文件与仓库硬约束冲突时，以 `AGENTS.md` 为准。
> 交付前必须把本文档的状态改成「已实现」，并把与实现不符的段落删掉——留一份说谎的设计文档比没有更糟。

---

## 0. 一句话目标

把「选区」从「决定产物」里摘出来：**选区只产出一个区域值**，截静图、录制、滚动拼接、OCR、钉图都是独立的步骤，用户可以把它们按任意顺序拼成一条链。
今天「快捷键绑定的行为不符合选区开关」这类错位，在这个模型下不再是缺陷，而是「链里写的是什么就跑什么」。

---

## 1. 现状：为什么两处都在决定产物

| 位置 | 现在的行为 |
|---|---|
| `src/FrameCapture/Capture/CaptureController.cs:231` | `wantRecord = pk?.RecordMode ?? _recordAfterSelect;` —— 选区窗上的「截图/录制」开关是唯一活着的那份状态 |
| `src/FrameCapture/Core/GestureConfig.cs:152` | 默认截图键 = `[scope.select, capture.stored]`；默认录制键 = `[scope.select, record.toggle]` |
| `src/FrameCapture/Services/GestureInvoker.cs:97` | `LeadOf(名字)` 把链里第一个动作压成一个 `ScopedLead` 枚举（Still / Record / Scroll / None） |
| `src/FrameCapture/Capture/CaptureController.Scoped.cs:47` | `BeginScopedRun(lead, tail, onFail, run)` —— 产物由 `lead` 定，链里剩下的动作等产物收尾后才跑 |
| `GestureConfig.cs:27` + `CaptureController.cs:236` | `OnComplete` 折成 `_sessionAfter`，**是产物专属的**（默认录制键带 `edit.lastvideo`） |

两侧冲突的具体表现：

1. 录制链 `[select, record.toggle]` + 选区里切到「截图」→ 截了静图，但完成动作仍是 `edit.lastvideo`（去开上一个视频）。
2. 截图链 + 选区里切到「录制」→ 录完没有落点，因为链里根本没写录制专属的完成动作。
3. 链的能力上限被 `ScopedLead` 四值锁死：不能「先选区域、截一帧、再录一段」，也不能「选区域 OCR 后追加复制」。

根因一句话：**「选区」这个动作同时被当成「选什么产物」，产物与链纠缠在一起。**

---

## 2. 目标模型

### 2.1 运行上下文

```csharp
// src/FrameCapture/Capture/RunContext.cs（新建）
sealed class RunContext
{
    public RectI? Region { get; set; }        // select 产出
    public string DeviceName { get; set; }    // 区域所在显示器
    public CapturedFrame? Still { get; set; } // grab.still 产出
    public string? VideoPath { get; set; }    // record.stop 产出
    public bool Recording { get; set; }       // 录制进行中
    public bool FromSwitch { get; set; }      // 产物是「跟随开关」选出来的
}
```

一条链 = 一串步骤，跑在同一次运行上；每一步只通过 `RunContext` 交接。

### 2.2 动作表

分三类。现有动作名尽量不改，只补缺口；`scope.select` 保留为 `select` 的别名（迁移期读完就写回新名）。

**输入类（产出值）**

| 动作名 | 产出 | 说明 |
|---|---|---|
| `select` | `Region` | 打开选区窗，等用户拖框；取消 = 本次运行失败（走 `OnFail`）。`scope.select` 是它的旧名 |
| `grab.still` | `Still` | 有 `Region` 就抓该区域，没有就抓全屏 |
| `grab.window` | `Still` | 抓 `Region` 命中的窗口本体（原「锁定到窗口」的产物语义显式化） |

**产品类（产生产物，可与输入类同链）**

| 动作名 | 前置 | 说明 |
|---|---|---|
| `record.start` | `Region` | 对该区域起录；`Region` 缺失时拒绝执行 |
| `record.stop` | 无（要求 `Recording`） | 停止并产出 `VideoPath` |
| `record.toggle` | 无 | 在录 → 停，不在录 → 起（保留给「一个键管开关」的老习惯） |
| `scroll.capture` | `Region` | 步进拼接，产出长图静图到 `Still` |
| `capture.frame` | 无 | 立即定格全屏（忽略 `Region`） |

**终结类（消费产物）**

`copy.clipboard`（复制 `Still`，无则复制上一个）、`edit.last`（开编辑器）、`edit.lastvideo`、`pin.selection`、`ocr.selection`、`notify.toast`、`settings.open`、`help.open`、`save.png`（新，把 `Still` 存盘）。

> 命名沿用仓库既有风格：产品名用原点分隔的小写动词，与 `GestureActions` 现有常量并列。

### 2.3 执行语义

1. **顺序即语义**：按链序执行；异步步骤（`select`、录制）由宿主报告完成后继续。
2. **前置值缺失 = 该步失败**，走这条绑定的 `OnFail`（已有 `FallbackPolicy.Stop|Continue`，不动引擎策略）。
3. **没有任何产品步的链**（例如只有 `[select]`）保留「跟随选区开关」的行为：跑 `grab.still` 或 `record.start`，并把 `FromSwitch` 置位。这是**唯一的隐式行为**，且开关由设置项控制是否出现。
4. `OnComplete` 不再单独存在：迁移时**折叠进链尾**（顺序 = `Actions` 之后接 `OnComplete`）。之后用户想加完成动作，就往链尾加一步。
5. 一条链里允许出现多个产品步（`[select, grab.still, record.start]` 合法：先截一帧再开录）。

### 2.4 选区窗的开关

- 只有当链里**没有任何产品步**时，选区窗才显示「截图 / 录制」开关（因为此时产物由开关定）。
- 链里写了产品步时，开关**不显示**——产物已经写死在链里，再给一个能推翻它的开关就是这次的错位。
- 设置里「选区后先按开始」保持原义，但只在「开关或链选出的产物是录制」时生效。

---

## 3. 数据与迁移

### 3.1 存储形态

`GestureConfig` 增加一个整型 `ChainRevision`（默认 0），与 `AppSettings.ColorPreset` 那套「版本号 + 一次性迁移」同款：

```csharp
public int ChainRevision { get; set; }          // 0 = 旧链；2 = 正交链
public List<GestureBindingDto> Bindings { get; set; } = new();
```

`ChainRevision < 2` 时执行一次性映射，把 `OnComplete` 折叠进 `Actions`，然后把 `ChainRevision = 2` 并落盘。

### 3.2 映射表

| 旧 `Actions` | 旧 `OnComplete` | 新链 |
|---|---|---|
| `[scope.select]` | 空 | `[select]` |
| `[scope.select, capture.stored]` | 空 | `[select, grab.still]` |
| `[scope.select, record.toggle]` | `[edit.lastvideo]` | `[select, record.toggle, edit.lastvideo]` |
| `[scope.select, scroll.capture]` | 任意 | `[select, scroll.capture]` + 原 `OnComplete` |
| `[scope.select, X, …]`（其他） | 任意 | `[select, LeadToStep(X), …]`，其中 `LeadToStep`：`capture.stored→grab.still`、`record.*→record.*`、`scroll.capture→scroll.capture` |
| 不带 `scope.select` 的链 | 任意 | 原样 + 折叠 `OnComplete`（不涉及选区语义） |

迁移只做一次；`ToBinding()` 之后仍保留旧名别名解析，防止外部手改过的 settings.json 崩。

### 3.3 断言（必须新增，命名沿用 `Chk("T###-…")` 风格）

- `T27x-chain-migrate-still`：旧 `[scope.select, capture.stored]` + 空 `OnComplete` → `[select, grab.still]`
- `T27x-chain-migrate-record`：旧录制链 → `[select, record.toggle, edit.lastvideo]`（`OnComplete` 折进链尾）
- `T27x-chain-migrate-idempotent`：迁移跑两次结果相同，`ChainRevision` 停在 2
- `T27x-chain-legacy-alias`：`scope.select` 仍可解析（外部手改的旧串不炸）
- `T27x-chain-no-product-follows-switch`：`[select]` 在开关选录制时起录、选截图时截静图
- `T27x-chain-explicit-product-ignores-switch`：`[select, record.start]` 在开关为截图时仍然录制，且选区窗不显示开关
- `T27x-chain-multi-product`：`[select, grab.still, record.start]` 既出静图又起录
- `T27x-chain-missing-precondition`：`[record.stop]` 不在录制 → 该步失败、走 `OnFail`、其余步骤按 `FallbackPolicy` 处理
- `T27x-chain-editor-roundtrip`：编辑器里改序/增删后序列化再读回，链不变

> 命名说明：`T###` 只出现在 `Chk("…")` 的字符串里，**绝不写进代码注释**（`AGENTS.md` 规则 2）。

---

## 4. 代码落点

### 4.1 `src/FrameCapture/Core/GestureActions.cs`

- 新增动词常量：`Select`（值 `select`）、`GrabStill`（`grab.still`）、`GrabWindow`（`grab.window`）、`SavePng`（`save.png`）。
- `ScopeSelect` 保留常量，`IsValid`/`LabelOf`/`Describe` 都要认它（旧数据兼容）。
- 给每个动词补「类」（输入/产品/终结）与「需要 / 产出」的元数据，供 UI 校验与提示：

```csharp
public enum VerbClass { Input, Product, Terminal }
public readonly record struct VerbInfo(VerbClass Class, string Needs, string Produces);
public static VerbInfo InfoOf(string name) => …;
```

- 标签文案进 `Core/Strings/Hotkeys.cs`（界面文案只在 `Core/Strings/<界面>.cs` 声明，取词点写 `K.Hotkeys.<名字>`）。

### 4.2 `src/FrameCapture/Services/GestureInvoker.cs`

- `LeadOf` 与 `InvokePrelude` 里的 `lead/tail` 拆分**删掉**，换成「整条链交给控制器」：

```csharp
public bool InvokePrelude(IReadOnlyList<string> actions)
{
    var b = _binding;
    var chain = new List<string>(actions);
    if (b != null) chain.AddRange(b.OnComplete);          // 迁移后通常为空
    var onFail = b != null ? new List<string>(b.OnFail) : new List<string>();
    Post(() => _host.Controller.BeginChain(chain, onFail, RunNames));
    return true;
}
```

- `IsPrelude(name)` 的条件是 `name is Select or ScopeSelect`。
- `InvokeCore` 增加新动词分支；`record.start` 不再无条件乐观返回 `true`——它要求有 `Region`，没有就返回 `false`（让引擎走失败策略）。

### 4.3 `src/FrameCapture/Capture/CaptureController.Scoped.cs`

- `ScopedLead` 枚举**删除**，`BeginScopedRun` 换成 `BeginChain(chain, onFail, run)`：

```csharp
public void BeginChain(IReadOnlyList<string> chain, IReadOnlyList<string> onFail, Action<IReadOnlyList<string>> run)
```

- 执行循环（UI 线程）：

```text
ctx = new RunContext()
i = 0
while i < chain.Count:
    step = chain[i]
    switch StepKind(step):
        case Input when step is select:
            打开选区窗；结果回来时 ctx.Region/DeviceName 就位 → i++ → 继续
            取消 → 跑 onFail → 结束
        case Input when step is grab.still / grab.window:
            抓帧进 ctx.Still → i++
        case Product when step is record.start:
            起录 → 本次运行挂起，直到 StopRecording 回来 → ctx.VideoPath 就位 → i++ → 继续
        case Product when step is record.toggle:
            不在录 → 同 record.start；在录 → 停止，链继续
        case Product when step is scroll.capture:
            进入拼接循环 → 长图进 ctx.Still → i++
        default（终结类）:
            立即执行 → i++
    if 某步失败: 按 FallbackPolicy.Stop 结束 / Continue 跳到下一步
```

- `_captureAfterSelect` / `_recordAfterSelect` / `_scrollAfterSelect` 三个布尔**删除**，由 ctx 承载。
- 「跟随开关」分支只出现在「整条链没有产品步」这一种情况：跑 `grab.still` 或 `record.start`，`ctx.FromSwitch = true`。
- `CaptureController.cs:231` 的 `wantRecord = pk?.RecordMode ?? …` 随之改写成「开关值写进 ctx；链里有产品步时忽略开关」。
- `_sessionAfter` 与 `AfterCapture.Resolve` 一整套**删除**：完成动作已是链上的步骤。

### 4.4 `src/FrameCapture/Core/GestureConfig.cs`

- `GestureBindingDto` 保留 `Actions` / `OnFail`；`OnComplete` 标 `[Obsolete]` 但**继续反序列化**（迁移读得到），迁移后清空。
- 新增 `ChainRevision` 与 `Migrate()`（见 §3）。
- `DefaultQqSet` 直接产出新链：截图 `[select, grab.still]`、录制 `[select, record.toggle, edit.lastvideo]`。
- `UpgradeStockDefaults` 里的字符串匹配同步换新名。

### 4.5 快捷键页（`src/FrameCapture/Overlays/SettingsWindow.Chain.cs`，现 105 行）

把「chip 列表 + 加号」升级成序列编辑器：

- 每步一枚 chip，显示 `动词标签` + 一枚小徽标表示「产出什么 / 需要什么」（`VerbInfo`）。
- 排序：chip 左右各一枚 24×24 图标按钮（`\uE76B`/`\uE76C`），不做拖拽——拖拽要接管鼠标捕获，与页内滚动/焦点逻辑冲突，收益不抵成本。
- 增：链尾一枚「添加步骤」下拉，列出 `VerbInfo` 里允许的动词；参数不足的动词在选择时给出**当行提示**，不弹窗。
- 删：chip 上的 `\uE711`，沿用现有的 `DeleteIconButton`。
- 链为空时给一句说明（`K.Hotkeys.ChainEmptyHint`）。
- 高度节奏与现有卡片一致：`ChipHeight = 27` 已定义在 `SettingsWindow.Chain.cs`，新按钮同高。
- 文件超 700 行就拆 `SettingsWindow.Chain.Edit.cs` / `SettingsWindow.Chain.Paint.cs`（`AGENTS.md`：文件 ≤700 行）。

### 4.6 帮助与文案

- `Core/Strings/Hotkeys.cs` 增：`ChainTitle`、`ChainEmptyHint`、`AddStep`、`MoveLeft`、`MoveRight`、每个新动词的标签（若复用 `GestureActions.LabelOf`，则不重复建字段——**同一句中文全表只能有一个字段**）。
- `Core/Strings/Capture.cs` 若产品名要显示在选区窗（开关提示），沿用既有 `K.Capture.*`，不要新建同义字段。
- `Help.cs` 里「选区」那一节补一句：**选区之后做什么由链决定；链里没写产品时才由选区开关决定**。

---

## 5. 执行顺序（建议按 PR 切）

1. **PR1 词表与元数据**：`GestureActions` 新动词 + `VerbInfo`；不动行为，SelfTest 全绿。
2. **PR2 运行上下文与链执行器**：新增 `RunContext`，`BeginChain` 落地；`ScopedLead` 与三个 `_xAfterSelect` 删除；老 UI 仍用旧链名映射（此 PR 内先保留 `LeadOf` 兼容层，PR3 删）。
3. **PR3 迁移**：`ChainRevision` + `Migrate()` + 别名解析 + 迁移断言。到此为止旧数据全部变新链。
4. **PR4 选区开关语义**：有产品步就不显示开关；「跟随开关」只在无产品步时生效。
5. **PR5 序列编辑器 UI**：`SettingsWindow.Chain*` 重写；文案与帮助同步。
6. **PR6 清理与文档**：删兼容层、`OnComplete`/`_sessionAfter`/`AfterCapture`、`ScopedLead`；更新 `PROGRESS.md`、`UIAUDIT.md`、`DECISIONS.md`（一条：为什么产物必须与链分离）、`AGENTS.md` 若新增了跨模块约束。

每个 PR 都必须：`dotnet build FrameCapture.slnx -c Debug -v q --nologo` 零错误 → `SelfTest.exe` 全绿 → `pwsh -NoProfile -File scripts/guardrail.ps1 -SkipE2e` 通过。

---

## 6. 验收

**自动**

- SelfTest 全绿，且用例数不低于改动前（护栏里有 floor，硬性）。
- 新增用例见 §3.3，另加 UI 侧：
  - `T27x-chain-editor-verbs`：编辑器下拉列出全部合法动词，顺序稳定
  - `T27x-chain-editor-warn`：缺前置值的步骤在编辑器里带警示标记
  - `T27x-readme-chain-table`：文档里那张「旧链 → 新链」表与 `Migrate()` 的实现一致（用同一份映射数据，避免文档与代码分叉）

**手动（在真机上过一遍）**

1. 默认截图键：拖框 → 静图；再把选区开关切到录制（链里无产品步时才有开关）→ 录制。
2. 把截图键的链改成 `[select, grab.still, copy.clipboard]` → 拖框后静图直接进剪贴板，不弹提示浮窗。
3. 改成 `[select, record.start]` 再拖框 → 直接起录，选区窗**没有**开关。
4. 改成 `[select, grab.still, record.start]` → 既存静图又起录。
5. 老 settings.json（改动前的备份）导入 → 所有键位行为与改动前一致。

---

## 7. 禁止事项（照抄 `AGENTS.md`，违反即回退）

1. 注释只写当下事实：禁词 `此前/之前/原来/曾经/退役/弃用/改为/修掉/教训/历史`，歧义词 `旧/不再/回归/修复/坑/bug` 需人工判定。
2. 注释里不写 `R###` / `T###` 编号。
3. 界面文案只在 `Core/Strings/<界面>.cs`，取词点写 `K.<界面>.<名字>`；一句中文全表只能有一个字段。
4. 标签只说名字，补充说明并进句子；括号只留在代码符号与记法里。
5. `public`/`internal` 成员必须有 XML 文档（`<param>` / `<returns>`），否则 `CS1591` 直接是 error。
6. 文件 ≤700 行，超了拆 partial。
7. 动效必须引用 `Fluent.Motion` 令牌。
8. 构建前 `Stop-Process -Name FrameCapture`；不得改弱任何断言。
9. 只有 `Core/Strings/` 之下的中文才算文案；日志、`Trace`、异常消息不进文案表。

---

## 8. 风险与已知取舍

| 风险 | 说明与对策 |
|---|---|
| 旧设置被一次性改写 | 迁移前把 `settings.json` 复制成 `settings.json.bak.<ChainRevision>`，失败可回滚；断言里覆盖「迁移幂等」 |
| `record.start` 从乐观受理改成要求 `Region` | 会改变「按一下就录全屏」的老行为；用 `capture.frame` + `record.start`（无 `select`）表达「录全屏」，并在迁移表里为这种老链留一条映射 |
| 链里多个产品步的收尾顺序 | 录制是长任务：录制期间的链**挂起**，停止后才继续后面的步骤；文档与 UI 都要写明 |
| 编辑器复杂度 | 用「左右移动 + 增删」而不是拖拽，理由见 §4.5 |
| 与「选区后先按开始」设置的关系 | 只在产物为录制时生效；设置项的说明文案同步改（`K.Settings.StartRecordingAfterSelectingTip`） |

---

## 9. 交付清单

- [ ] 代码：§4 全部落点
- [ ] 迁移：§3，含 `settings.json.bak` 备份
- [ ] 测试：§3.3 + §6 新增用例
- [ ] 文档：`PROGRESS.md`（做了什么的结论）、`UIAUDIT.md`（界面变化）、`DECISIONS.md` 新增一节「为什么产物必须与链分离」、本文件标记「已实现」
- [ ] 版本号：`src/FrameCapture/FrameCapture.csproj` 的 `<Version>` 递增（唯一来源）
- [ ] 发布：`pwsh -NoProfile -File scripts/publish-release.ps1`（先 commit，ProductVersion 里带 git hash）