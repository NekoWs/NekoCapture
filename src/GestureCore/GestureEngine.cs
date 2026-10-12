using System;
using System.Collections.Generic;
using System.Linq;

namespace GestureCore;

/// <summary>
/// 手势引擎：喂入原始按键/鼠标事件（带毫秒时间戳）+ 绑定表 → 消歧 → 经 <see cref="IActionInvoker"/> 回调动作序列。
/// 与 UI/OS 完全解耦：键用不透明 int 标识、修饰键状态由宿主随事件传入（贴合 WH_KEYBOARD_LL 等低级钩子的取数方式）、
/// 时间由宿主传入（<see cref="Tick"/> 推进待决的点击消歧与长按判定），因此可确定性单测、可跨宿主复用。
///
/// 消歧规则：
///  • 组合键激活瞬间触发 KeyDown/Chord，失活瞬间触发 KeyUp（配对即「按住做 A、松手做 B」）。
///  • 组合键激活期间的鼠标动作按 Click / DoubleClick / TripleClick / LongPress 分类：
///    连击数达到该组合键已绑定的最高阶触发即立即触发；否则等 DoubleClickMs 无后续再触发当前阶。
///    若该组合键只绑了 Click（无更高阶），单击立即触发、零延迟。
///  • 长按：鼠标按下保持 ≥ LongPressMs 即触发 LongPress，并吞掉随后的抬起（不再计为单击）。
///  • 同一触发命中多条绑定按 <see cref="GestureBinding.Priority"/> 顺延；某步失败按该条 <see cref="GestureBinding.Fallback"/> 处理。
/// </summary>
public sealed class GestureEngine
{
    readonly IActionInvoker _invoker;
    List<GestureBinding> _bindings = new List<GestureBinding>();
    // 每个组合键的运行时状态（用引用键存，KeyChord 值相等即可命中）
    readonly Dictionary<KeyChord, ChordState> _state = new Dictionary<KeyChord, ChordState>(ChordEq.Instance);
    // 每个组合键已绑定的触发集合（决定消歧的最高阶 + 是否需等待）
    readonly Dictionary<KeyChord, HashSet<TriggerKind>> _triggers = new Dictionary<KeyChord, HashSet<TriggerKind>>(ChordEq.Instance);
    readonly HashSet<int> _keyDown = new HashSet<int>();
    // 独占吞键：本次按键事件是否命中「带具体键的组合键并成功触发」→ 宿主据此吞该键。
    bool _swallowThisEvent;
    // 按下时被吞的键：其抬起也要吞，避免前台程序看到「只抬起、没按下」的畸形键态。
    readonly HashSet<int> _swallowKeyUp = new HashSet<int>();
    List<ChordState> _pressCandidates = new List<ChordState>();   // 本次鼠标按下时处于激活态、且有鼠标触发的组合键

    sealed class ChordState
    {
        public KeyChord Chord = null!;
        public bool Active;
        public bool PressDown;
        public long PressTime;
        public bool LongPressFired;
        public int ClickCount;
        public long LastClickTime;
        public bool ClickPending;
        public int DoubleClickMs = 400;
        // 键盘点按/按住（Tap/Hold/HoldRelease）：本次按下的时序状态
        public long KeyDownTime;
        public bool KeyPending;
        public bool HoldFired;
    }

    sealed class ChordEq : IEqualityComparer<KeyChord>
    {
        public static readonly ChordEq Instance = new ChordEq();
        public bool Equals(KeyChord? a, KeyChord? b) => a != null && b != null && a.Equals(b);
        public int GetHashCode(KeyChord c) => c.GetHashCode();
    }

    public GestureEngine(IActionInvoker invoker)
    {
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
    }

    /// <summary>替换绑定表（按优先级预排序）。会重置所有运行时状态。</summary>
    public void SetBindings(IEnumerable<GestureBinding> bindings)
    {
        _bindings = (bindings ?? Enumerable.Empty<GestureBinding>())
            .Select(b => b.Clone())
            .OrderBy(b => b.Priority)
            .ToList();
        _state.Clear();
        _triggers.Clear();
        _keyDown.Clear();
        _swallowKeyUp.Clear();
        _swallowThisEvent = false;
        _pressCandidates.Clear();
        foreach (var b in _bindings)
        {
            if (!_triggers.TryGetValue(b.Chord, out var set))
                _triggers[b.Chord] = set = new HashSet<TriggerKind>();
            set.Add(b.Trigger);
        }
    }

    static bool ChordSatisfied(KeyChord c, ModifierKeys mods, HashSet<int> keyDown) =>
        (c.Mods & ~mods) == 0 && (c.Key == 0 || keyDown.Contains(c.Key));

    ChordState State(KeyChord c)
    {
        if (!_state.TryGetValue(c, out var s))
            _state[c] = s = new ChordState { Chord = c };
        return s;
    }

    /// <summary>重算所有已知组合键的激活态，触发新激活(KeyDown/Chord)/新失活(KeyUp)绑定。mods=事件时刻修饰键状态。
    /// triggerKey=本次按键事件的键，用于独占吞键判定（仅当激活的组合键带具体键且等于 triggerKey 才算候选）。</summary>
    void RefreshActivation(ModifierKeys mods, int triggerKey = 0, long nowMs = 0)
    {
        foreach (var kv in _triggers)
        {
            var st = State(kv.Key);
            bool active = ChordSatisfied(kv.Key, mods, _keyDown);
            if (active && !st.Active)
            {
                st.Active = true;
                bool f1 = FireTrigger(kv.Key, TriggerKind.KeyDown);
                bool f2 = FireTrigger(kv.Key, TriggerKind.Chord);
                // 独占吞键候选：组合键带具体键(非纯修饰键)、正是本次事件键。
                bool claim = kv.Key.Key != 0 && kv.Key.Key == triggerKey;
                // 确有 KeyDown/Chord 动作成功触发 → 吞。
                if ((f1 || f2) && claim) _swallowThisEvent = true;
                // 键盘点按/按住：按下即认领（吞键，前台收不到），tap/hold 待抬起或阈值判定。
                if (HasTapHold(kv.Key))
                {
                    st.KeyDownTime = nowMs; st.KeyPending = true; st.HoldFired = false;
                    if (claim) _swallowThisEvent = true;
                }
            }
            else if (!active && st.Active)
            {
                st.Active = false;
                FireTrigger(kv.Key, TriggerKind.KeyUp);
                // 点按/按住消歧：抬起时按本次按住时长判定（≥阈值=HoldRelease，否则=Tap）。
                if (st.KeyPending)
                {
                    st.KeyPending = false;
                    bool isHold = nowMs - st.KeyDownTime >= BindingHoldMs(kv.Key);
                    FireTrigger(kv.Key, isHold ? TriggerKind.HoldRelease : TriggerKind.Tap);
                    st.HoldFired = false;
                }
                // 失活即清空该组合键的鼠标/点击累积
                st.PressDown = false; st.ClickCount = 0; st.ClickPending = false; st.LongPressFired = false;
            }
        }
    }

    /// <summary>喂入按键按下。返回是否应吞掉本键（命中带具体键的组合键并成功触发）——宿主结合 Exclusive 开关决定是否吞。</summary>
    public bool OnKeyDown(int key, ModifierKeys mods, long now)
    {
        _swallowThisEvent = false;
        bool repeat = _keyDown.Contains(key);   // 已在按下集合=系统自动重复（按住不放连发）
        _keyDown.Add(key);
        RefreshActivation(mods, key, now);
        if (_swallowThisEvent) { _swallowKeyUp.Add(key); return true; }
        // 自动重复：该键首次按下已被认领（在 _swallowKeyUp 里）→ 重复也吞，
        // 免得按住录制期间重复键漏给前台（旧 HookDecision 在捕获期吞主键的等价行为）。
        if (repeat && _swallowKeyUp.Contains(key)) return true;
        return false;
    }

    /// <summary>喂入按键抬起。返回是否应吞掉本键（其按下曾被吞 → 抬起也吞，保持前台键态一致）。</summary>
    public bool OnKeyUp(int key, ModifierKeys mods, long now)
    {
        _swallowThisEvent = false;
        _keyDown.Remove(key);
        RefreshActivation(mods, key, now);
        if (_swallowKeyUp.Remove(key)) return true;
        return false;
    }

    public void OnMouseDown(int button, ModifierKeys mods, long now)
    {
        RefreshActivation(mods, 0, now);   // 鼠标事件也刷新（修饰键可能在鼠标事件间隙变化）
        _pressCandidates.Clear();
        foreach (var kv in _triggers)
        {
            var st = State(kv.Key);
            if (!st.Active) continue;
            var trig = kv.Value;
            bool hasMouse = trig.Contains(TriggerKind.Click) || trig.Contains(TriggerKind.DoubleClick)
                            || trig.Contains(TriggerKind.TripleClick) || trig.Contains(TriggerKind.LongPress);
            if (!hasMouse) continue;
            st.PressDown = true; st.PressTime = now; st.LongPressFired = false;
            st.DoubleClickMs = BindingDoubleClickMs(kv.Key);
            _pressCandidates.Add(st);
        }
    }

    public void OnMouseUp(int button, ModifierKeys mods, long now)
    {
        foreach (var st in _pressCandidates)
        {
            if (!st.PressDown) continue;
            st.PressDown = false;
            if (st.LongPressFired) { st.LongPressFired = false; continue; }  // 长按已吞掉本次抬起
            if (!st.Active) continue;                                        // 抬起时组合键已失活 → 不计击
            st.ClickCount++;
            st.LastClickTime = now;
            Classify(st);
        }
        _pressCandidates.Clear();
    }

    int BindingDoubleClickMs(KeyChord c)
    {
        foreach (var b in _bindings)
            if (b.Chord.Equals(c) && (b.Trigger == TriggerKind.DoubleClick || b.Trigger == TriggerKind.TripleClick))
                return b.DoubleClickMs;
        foreach (var b in _bindings)
            if (b.Chord.Equals(c)) return b.DoubleClickMs;
        return 400;
    }

    /// <summary>抬起后判定当前连击是否已达该组合键已绑定的最高阶触发；达到即触发，否则挂起等后续连击。</summary>
    void Classify(ChordState st)
    {
        var trig = _triggers[st.Chord];
        int highest = trig.Contains(TriggerKind.TripleClick) ? 3
                    : trig.Contains(TriggerKind.DoubleClick) ? 2
                    : trig.Contains(TriggerKind.Click) ? 1 : 0;
        if (highest == 0) { st.ClickCount = 0; return; }
        if (st.ClickCount >= highest)
        {
            FireTrigger(st.Chord, TriggerForCount(st.ClickCount, trig));
            st.ClickCount = 0; st.ClickPending = false;
        }
        else
        {
            st.ClickPending = true;   // 等 DoubleClickMs 看是否还有下一击
        }
    }

    static TriggerKind TriggerForCount(int count, HashSet<TriggerKind> avail)
    {
        if (count >= 3 && avail.Contains(TriggerKind.TripleClick)) return TriggerKind.TripleClick;
        if (count >= 2 && avail.Contains(TriggerKind.DoubleClick)) return TriggerKind.DoubleClick;
        return TriggerKind.Click;
    }

    /// <summary>推进时间：结算挂起的连击消歧与长按判定。宿主应在事件循环里周期性调用（或按需）。</summary>
    public void Tick(long now)
    {
        foreach (var st in _state.Values)
        {
            if (st.PressDown && !st.LongPressFired && _triggers.TryGetValue(st.Chord, out var t) && t.Contains(TriggerKind.LongPress))
            {
                int lp = BindingLongPressMs(st.Chord);
                if (now - st.PressTime >= lp)
                {
                    st.LongPressFired = true;
                    FireTrigger(st.Chord, TriggerKind.LongPress);
                }
            }
            // 键盘按住：保持 ≥ 阈值即触发 Hold（确认按住才开始；抬起时再触发 HoldRelease）。
            if (st.KeyPending && !st.HoldFired && _triggers.TryGetValue(st.Chord, out var tk) && tk.Contains(TriggerKind.Hold))
            {
                if (now - st.KeyDownTime >= BindingHoldMs(st.Chord))
                {
                    st.HoldFired = true;
                    FireTrigger(st.Chord, TriggerKind.Hold);
                }
            }
            if (st.ClickPending && now - st.LastClickTime >= st.DoubleClickMs)
            {
                st.ClickPending = false;
                if (st.ClickCount > 0)
                {
                    FireTrigger(st.Chord, TriggerForCount(st.ClickCount, _triggers[st.Chord]));
                    st.ClickCount = 0;
                }
            }
        }
    }

    int BindingLongPressMs(KeyChord c)
    {
        foreach (var b in _bindings)
            if (b.Chord.Equals(c) && b.Trigger == TriggerKind.LongPress) return b.LongPressMs;
        return 500;
    }

    static bool IsTapHold(TriggerKind t) =>
        t == TriggerKind.Tap || t == TriggerKind.Hold || t == TriggerKind.HoldRelease;

    /// <summary>该组合键是否绑了键盘点按/按住类触发（按下即认领吞键、抬起/阈值判定 tap 或 hold）。</summary>
    bool HasTapHold(KeyChord c) =>
        _triggers.TryGetValue(c, out var s) &&
        (s.Contains(TriggerKind.Tap) || s.Contains(TriggerKind.Hold) || s.Contains(TriggerKind.HoldRelease));

    /// <summary>键盘按住阈值(ms)：取该组合键 Tap/Hold/HoldRelease 绑定的 LongPressMs；无则 250。</summary>
    int BindingHoldMs(KeyChord c)
    {
        foreach (var b in _bindings)
            if (b.Chord.Equals(c) && IsTapHold(b.Trigger)) return b.LongPressMs;
        return 250;
    }

    /// <summary>触发某组合键的某类触发：按优先级依次尝试绑定，成功即止；失败按该条 Fallback 决定是否顺延下一条。
    /// 返回是否确有动作成功触发（供宿主判断独占吞键）。</summary>
    bool FireTrigger(KeyChord chord, TriggerKind trigger)
    {
        for (int i = 0; i < _bindings.Count; i++)
        {
            var b = _bindings[i];
            if (!b.Chord.Equals(chord) || b.Trigger != trigger) continue;
            bool ok = RunActions(b);
            if (ok) return true;
            if (b.Fallback == FallbackPolicy.NextBinding) continue;   // 失败自动下一个
            return false;                                              // Stop / SkipStep 已在本条内消化
        }
        return false;   // 该触发无绑定
    }

    /// <summary>顺序执行一条绑定的动作序列。SkipStep 跳过失败步继续；Stop/NextBinding 遇失败即中止返回 false。
    /// 主序列成功后再跑「完成后序列」OnComplete（A3）——尽力而为，其失败不改变本条结果。</summary>
    bool RunActions(GestureBinding b)
    {
        foreach (var action in b.Actions)
        {
            bool ok;
            try { ok = _invoker.Invoke(action); }
            catch { ok = false; }
            if (ok) continue;
            if (b.Fallback == FallbackPolicy.SkipStep) continue;   // 跳过这步，继续后续
            return false;                                          // Stop / NextBinding：中止本条
        }
        if (b.OnComplete != null)                                  // 完成后序列：主序列成功后才跑
            foreach (var action in b.OnComplete)
                try { _invoker.Invoke(action); } catch { }
        return true;
    }
}
