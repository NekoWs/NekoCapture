using FrameCapture.Core;
using GC = GestureCore;
using GE = GestureEditor;
using WinFormsKeys = System.Windows.Forms.Keys;

namespace FrameCapture.Services;

/// <summary>
/// 手势配置器的 FrameCapture 宿主适配（方案 C）。把 <see cref="GE.GestureEditorWindow"/> 库的四个注入点
/// ——动作清单 / 配置读写 / 键名 / 配色——接到 FrameCapture 上，并提供打开入口。
/// GestureEditor 库本身不认识 FrameCapture；所有耦合都收在这个文件里。
/// </summary>
public static class GestureEditorHost
{
    static GE.GestureEditorWindow? _open;

    /// <summary>打开（或聚焦）手势配置器。捕获组合键期间暂停全局钩子，否则要捕获的组合键会被自家钩子吞掉。</summary>
    public static void ShowOrFocus()
    {
        if (_open != null) { _open.Activate(); return; }
        var w = new GE.GestureEditorWindow(new FrameActionCatalog(), new FrameGestureConfigStore(), new FrameKeyNamer(), BuildPalette(), Loc.T);
        bool wasPaused = HotkeyManager.Paused;
        w.ChordCaptureBegan += () => HotkeyManager.Paused = true;
        w.ChordCaptureEnded += () => HotkeyManager.Paused = wasPaused;
        w.Closed += (_, _) => { _open = null; HotkeyManager.Paused = wasPaused; };
        _open = w;
        w.Show();
        w.Activate();
    }

    static GE.EditorPalette BuildPalette() => new()
    {
        WindowBg = Theme.WindowBg, PanelBg = Theme.PanelBg, CardBg = Theme.CardBg,
        Text = Theme.Text, TextDim = Theme.TextDim, Stroke = Theme.Stroke,
        Accent = Theme.Accent, Hover = Theme.Hover, Press = Theme.Press, TrackBg = Theme.TrackBg,
        IsDark = Theme.IsDark,
    };

    /// <summary>动作清单：直接暴露 <see cref="GestureActions"/>。新增功能只需在 GestureActions 加一条 + GestureInvoker 接上。</summary>
    sealed class FrameActionCatalog : GE.IActionCatalog
    {
        /// <summary>动作清单，来源是 <see cref="GestureActions.All"/>，每次调用现取一遍名字与说明。</summary>
        /// <returns>新数组：短名与说明按当前界面语言取词，因此切换语言后重开配置器就是新语言，
        /// 缓存成字段会把首次打开时的语言冻住。配置器只读，不回写。</returns>
        public IReadOnlyList<GE.ActionInfo> Actions() =>
            GestureActions.All.Select(a => new GE.ActionInfo(a.Name, a.Label, a.Description, a.IsCapture)).ToArray();

        /// <summary>动作名是否为本 app 真实支持的动作。</summary>
        /// <param name="name">动作名，与 <see cref="GestureActions"/> 表内原文逐字比对（区分大小写）。</param>
        /// <returns>命中返回 true；null、空串、未知名返回 false——配置器据此拒绝写入该动作。</returns>
        public bool IsValid(string name) => GestureActions.IsValid(name);
    }

    /// <summary>键名：VK → WinFormsKeys 枚举名（与 <see cref="HotkeySpec"/> 的键名一致，保证显示与存储串一致）。</summary>
    sealed class FrameKeyNamer : GC.IKeyNamer
    {
        /// <summary>虚拟键码 → 配置器显示与 chord 里用的键名。</summary>
        /// <param name="key">Windows 虚拟键码（VK_*），不做合法性校验。</param>
        /// <returns><c>System.Windows.Forms.Keys</c> 的枚举名；表外 VK 返回其十进制数字串，不抛异常。</returns>
        public string Name(int key) => ((WinFormsKeys)key).ToString();
    }

    /// <summary>配置读写：settings.json 的 gestures 段 ↔ 宿主无关的 <see cref="GE.GestureDoc"/>。
    /// 组合键串复用 <see cref="HotkeySpec"/> 解析/重建（不重造键表）。Save 走 Mutate → 触发热重载。</summary>
    sealed class FrameGestureConfigStore : GE.IGestureConfigStore
    {
        /// <summary>读当前设置的手势段，翻成宿主无关的编辑文档。</summary>
        /// <returns>新建的文档（与设置对象不共享引用，改文档不会即时影响运行时）。
        /// chord 解析不出来的绑定保留但键位归零（mods=None、key=0），由用户在配置器里重设；
        /// trigger / fallback 字符串认不出来时分别按 Chord / Stop 兜底，不整条丢弃。</returns>
        public GE.GestureDoc Load()
        {
            var cfg = AppHost.Current.Settings.Current.Gestures;
            var doc = new GE.GestureDoc { Enabled = cfg.Enabled, Exclusive = cfg.Exclusive };
            foreach (var d in cfg.Bindings)
            {
                var spec = HotkeySpec.Parse(d.Chord);
                doc.Bindings.Add(new GE.BindingEdit
                {
                    Id = d.Id,
                    Enabled = d.Enabled,
                    Mods = spec == null ? GC.ModifierKeys.None : ModsOf(spec),
                    Key = (int)(spec?.VirtualKey ?? 0),
                    Trigger = Enum.TryParse<GC.TriggerKind>(d.Trigger, true, out var t) ? t : GC.TriggerKind.Chord,
                    Actions = d.Actions.ToList(),
                    OnComplete = d.OnComplete.ToList(),
                    AfterCapture = d.AfterCapture ?? "",
                    CopyToClipboard = d.CopyToClipboard,
                    Fallback = Enum.TryParse<GC.FallbackPolicy>(d.Fallback, true, out var f) ? f : GC.FallbackPolicy.Stop,
                    Priority = d.Priority,
                    DoubleClickMs = d.DoubleClickMs,
                    LongPressMs = d.LongPressMs,
                });
            }
            return doc;
        }

        /// <summary>把编辑文档整体写回 settings.json 的手势段，并当场让手势引擎与键盘钩子重载。</summary>
        /// <param name="doc">配置器当前文档。<b>整体替换</b>现有配置：文档里没有的绑定即被删除，不做增量合并。</param>
        public void Save(GE.GestureDoc doc)
        {
            var cfg = new GestureConfig { Enabled = doc.Enabled, Exclusive = doc.Exclusive };
            foreach (var b in doc.Bindings)
            {
                var spec = new HotkeySpec
                {
                    Ctrl = (b.Mods & GC.ModifierKeys.Control) != 0,
                    Alt = (b.Mods & GC.ModifierKeys.Alt) != 0,
                    Shift = (b.Mods & GC.ModifierKeys.Shift) != 0,
                    Win = (b.Mods & GC.ModifierKeys.Win) != 0,
                    Key = (WinFormsKeys)b.Key,
                };
                cfg.Bindings.Add(new GestureBindingDto
                {
                    Id = b.Id,
                    Enabled = b.Enabled,
                    Chord = spec.ToString(),
                    Trigger = b.Trigger.ToString(),
                    Actions = b.Actions.ToArray(),
                    OnComplete = b.OnComplete.ToArray(),
                    AfterCapture = b.AfterCapture,
                    CopyToClipboard = b.CopyToClipboard,
                    Fallback = b.Fallback.ToString(),
                    Priority = b.Priority,
                    DoubleClickMs = b.DoubleClickMs,
                    LongPressMs = b.LongPressMs,
                });
            }
            AppHost.Current.Settings.Mutate(s => s.Gestures = cfg);
            // 立即热重载引擎：Mutate 是本进程自写盘，文件 watcher 会因 RecentlySelfWritten 跳过，
            // 不自己触发 InstallHotkeys，引擎就继续持保存前的绑定表——完成后序列/启用状态要等下次启动才生效。
            AppHost.Current.InstallHotkeys();
        }

        static GC.ModifierKeys ModsOf(HotkeySpec s) =>
              (s.Ctrl ? GC.ModifierKeys.Control : GC.ModifierKeys.None)
            | (s.Alt ? GC.ModifierKeys.Alt : GC.ModifierKeys.None)
            | (s.Shift ? GC.ModifierKeys.Shift : GC.ModifierKeys.None)
            | (s.Win ? GC.ModifierKeys.Win : GC.ModifierKeys.None);
    }
}
