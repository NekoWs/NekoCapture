using System.Collections.Generic;
using System.Linq;
using GestureCore;

namespace GestureEditor;

/// <summary>一条校验问题。<see cref="IsError"/>=true 必须修才能保存生效；false 为提示（可保存）。</summary>
public sealed record BindingProblem(string BindingId, string Message, bool IsError);

/// <summary>手势配置校验（纯逻辑、可单测）。编辑器实时调用，宿主也可在加载时调用。</summary>
public static class GestureValidation
{
    /// <summary>校验整份配置，返回每条绑定/全局的问题（空=全通过）。
    /// 规则：组合键非空、至少一个动作且动作名合法、时间参数在范围；同「组合键+触发」重复仅提示（引擎按优先级顺延，属合法但多半是误配）。</summary>
    public static List<BindingProblem> Validate(GestureDoc doc, IActionCatalog catalog, IKeyNamer namer)
    {
        var list = new List<BindingProblem>();
        var seen = new HashSet<(int mods, int key, TriggerKind trig)>();
        foreach (var b in doc.Bindings)
        {
            if (!b.Enabled) continue;   // 禁用的绑定不参与触发 → 不校验、不挡保存（A1）
            if (b.Key == 0 && b.Mods == ModifierKeys.None)
                list.Add(new BindingProblem(b.Id, "组合键为空：请设置一个组合键", true));

            if (b.Actions.Count == 0)
                list.Add(new BindingProblem(b.Id, "未选择任何动作", true));
            foreach (var a in b.Actions)
                if (!catalog.IsValid(a))
                    list.Add(new BindingProblem(b.Id, $"未知动作「{a}」", true));
            foreach (var a in b.OnComplete)
                if (!catalog.IsValid(a))
                    list.Add(new BindingProblem(b.Id, $"完成后序列含未知动作「{a}」", true));

            if (b.DoubleClickMs < 50 || b.DoubleClickMs > 2000)
                list.Add(new BindingProblem(b.Id, "双击间隔应在 50–2000ms", false));
            if (b.LongPressMs < 100 || b.LongPressMs > 5000)
                list.Add(new BindingProblem(b.Id, "长按阈值应在 100–5000ms", false));

            var k = ((int)b.Mods, b.Key, b.Trigger);
            if (!seen.Add(k))
                list.Add(new BindingProblem(b.Id, $"组合键+触发与另一条重复（{b.Chord.Format(namer)}）：会按优先级顺延，确认是有意为之", false));
        }
        return list;
    }

    public static bool HasErrors(IEnumerable<BindingProblem> problems) => problems.Any(p => p.IsError);
}
