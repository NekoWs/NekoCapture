using System;
using System.Collections.Generic;
using System.Linq;
using FrameCapture.Core;
using GC = GestureCore;
using GE = GestureEditor;
using WinFormsKeys = System.Windows.Forms.Keys;

namespace FrameCapture.Services;

/// <summary>
/// 手势配置器的 FrameCapture 宿主适配（R122 方案 C）。把 <see cref="GE.GestureEditorWindow"/> 库的四个注入点
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
        var w = new GE.GestureEditorWindow(new FrameActionCatalog(), new FrameGestureConfigStore(), new FrameKeyNamer(), BuildPalette());
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
        static readonly GE.ActionInfo[] All =
            GestureActions.All.Select(a => new GE.ActionInfo(a.Name, a.Label, a.Description, a.IsCapture)).ToArray();
        public IReadOnlyList<GE.ActionInfo> Actions() => All;
        public bool IsValid(string name) => GestureActions.IsValid(name);
    }

    /// <summary>键名：VK → WinFormsKeys 枚举名（与 <see cref="HotkeySpec"/> 的键名一致，保证显示与存储串一致）。</summary>
    sealed class FrameKeyNamer : GC.IKeyNamer
    {
        public string Name(int key) => ((WinFormsKeys)key).ToString();
    }

    /// <summary>配置读写：settings.json 的 gestures 段 ↔ 宿主无关的 <see cref="GE.GestureDoc"/>。
    /// 组合键串复用 <see cref="HotkeySpec"/> 解析/重建（不重造键表）。Save 走 Mutate → 触发热重载。</summary>
    sealed class FrameGestureConfigStore : GE.IGestureConfigStore
    {
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
                    Fallback = Enum.TryParse<GC.FallbackPolicy>(d.Fallback, true, out var f) ? f : GC.FallbackPolicy.Stop,
                    Priority = d.Priority,
                    DoubleClickMs = d.DoubleClickMs,
                    LongPressMs = d.LongPressMs,
                });
            }
            return doc;
        }

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
                    Fallback = b.Fallback.ToString(),
                    Priority = b.Priority,
                    DoubleClickMs = b.DoubleClickMs,
                    LongPressMs = b.LongPressMs,
                });
            }
            AppHost.Current.Settings.Mutate(s => s.Gestures = cfg);
            // 立即热重载引擎：Mutate 是本进程自写盘，文件 watcher 会因 RecentlySelfWritten 跳过，
            // 不主动 Apply 的话引擎还拿着旧绑定（新增的完成后序列/启用状态要等下次重启才生效）。
            AppHost.Current.InstallHotkeys();
        }

        static GC.ModifierKeys ModsOf(HotkeySpec s) =>
              (s.Ctrl ? GC.ModifierKeys.Control : GC.ModifierKeys.None)
            | (s.Alt ? GC.ModifierKeys.Alt : GC.ModifierKeys.None)
            | (s.Shift ? GC.ModifierKeys.Shift : GC.ModifierKeys.None)
            | (s.Win ? GC.ModifierKeys.Win : GC.ModifierKeys.None);
    }
}
