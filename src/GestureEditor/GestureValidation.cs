using GestureCore;

namespace GestureEditor;

/// <summary>一条校验问题。<see cref="IsError"/>=true 必须修才能保存生效；false 为提示（可保存）。</summary>
/// <param name="BindingId">问题归属的绑定 Id，界面据此把该条卡片标红。</param>
/// <param name="Message">面向用户的文案，已按 <see cref="GestureValidation.Validate"/> 注入的取词函数取词。</param>
/// <param name="IsError">true=错误，挡住保存；false=提示，可照常保存。</param>
/// <returns>按给定值构造的不可变记录。</returns>
public sealed record BindingProblem(string BindingId, string Message, bool IsError);

/// <summary>手势配置校验（纯逻辑、可单测）。编辑器实时调用，宿主也可在加载时调用。</summary>
public static class GestureValidation
{
    /// <summary>校验整份配置，返回每条绑定的问题（空=全通过）。
    /// 规则：组合键非空、至少一个动作且动作名在目录内、时间参数在范围；
    /// 同「组合键+触发」重复只提示不报错——引擎按优先级顺延，属合法配置但多半是误配。
    /// 禁用的绑定整条跳过：不参与触发，也就不校验、不挡保存。</summary>
    /// <param name="doc">待校验的整份配置（全局开关 + 全部绑定）。</param>
    /// <param name="catalog">动作目录，用于判定主动作与完成后序列里的动作名是否存在。</param>
    /// <param name="namer">键名显示，只用于重复提示里回显组合键。</param>
    /// <param name="loc">取词函数，宿主传自己的界面语言；不传即原样返回中文。
    /// 带参的文案先取整句模板再填参数——语序由译文决定，不能先填词再翻。</param>
    /// <returns>问题列表，顺序与 <c>doc.Bindings</c> 一致；全通过时为空列表，不为 null。</returns>
    public static List<BindingProblem> Validate(GestureDoc doc, IActionCatalog catalog, IKeyNamer namer,
        Func<string, string>? loc = null)
    {
        Func<string, string> L = loc ?? (s => s);
        var list = new List<BindingProblem>();
        var seen = new HashSet<(int mods, int key, TriggerKind trig)>();
        foreach (var b in doc.Bindings)
        {
            if (!b.Enabled) continue;   // 禁用的绑定不参与触发 → 不校验、不挡保存
            if (b.Key == 0 && b.Mods == ModifierKeys.None)
                list.Add(new BindingProblem(b.Id, L("组合键为空：请设置一个组合键"), true));

            if (b.Actions.Count == 0)
                list.Add(new BindingProblem(b.Id, L("未选择任何动作"), true));
            foreach (var a in b.Actions)
                if (!catalog.IsValid(a))
                    list.Add(new BindingProblem(b.Id, string.Format(L("未知动作「{0}」"), a), true));
            foreach (var a in b.OnComplete)
                if (!catalog.IsValid(a))
                    list.Add(new BindingProblem(b.Id, string.Format(L("完成后序列含未知动作「{0}」"), a), true));

            if (b.DoubleClickMs < 50 || b.DoubleClickMs > 2000)
                list.Add(new BindingProblem(b.Id, L("双击间隔应在 50–2000ms"), false));
            if (b.LongPressMs < 100 || b.LongPressMs > 5000)
                list.Add(new BindingProblem(b.Id, L("长按阈值应在 100–5000ms"), false));

            var k = ((int)b.Mods, b.Key, b.Trigger);
            if (!seen.Add(k))
                list.Add(new BindingProblem(b.Id,
                    string.Format(L("组合键 {0} 与另一条重复：会按优先级顺延，确认是有意为之"), b.Chord.Format(namer)),
                    false));
        }
        return list;
    }

    /// <summary>问题集中是否存在错误级条目——保存按钮可用性与保存入口的唯一判据。</summary>
    /// <param name="problems">待判定的问题集合，通常来自 <see cref="Validate"/>；空集合返回 false。</param>
    /// <returns>只要有一条 <see cref="BindingProblem.IsError"/> 为 true 即 true。</returns>
    public static bool HasErrors(IEnumerable<BindingProblem> problems) => problems.Any(p => p.IsError);
}
