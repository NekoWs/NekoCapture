using System.Windows;
using System.Windows.Controls;
using NekoCapture.Core;

namespace NekoCapture.Overlays;

/// <summary>按行为中的动作直接呈现开放项，调用目标变化时保留未变的设置行。</summary>
public sealed partial class SettingsWindow
{
    private FrameworkElement BindingParameterOverrides(GestureBindingDto binding)
    {
        var root = new StackPanel();
        var cfg = _settings.Current.Gestures;
        var fields = new Dictionary<(string Node, string Parameter), (FrameworkElement Row, Action Sync)>();
        var groups = new Dictionary<string, (StackPanel Panel, TextBlock Title)>();
        void Reconcile()
        {
            var graph = cfg.BindingGraph(binding);
            var nodes = BehaviorParameters.Reachable(graph, binding.EntryId)
                .Select(node => cfg.Graph.Node(node.Id)!).Where(node => BehaviorParameters.Exposed(node, cfg.Bindings).Any()).ToArray();
            var locations = BehaviorParameters.Locations(graph, binding.EntryId);
            var active = new HashSet<(string, string)>();
            var panels = new List<UIElement>();
            foreach (var node in nodes)
            {
                if (!groups.TryGetValue(node.Id, out var group))
                {
                    var title = SectionHeader(""); title.Margin = new Thickness(0, 14, 0, 2);
                    group = (new StackPanel(), title); groups[node.Id] = group;
                }
                string label = GestureActions.LabelOf(node.Action);
                group.Title.Text = nodes.Count(other => other.Action == node.Action) > 1
                    ? Loc.F(K.Common.LabelWithContext, label, locations.GetValueOrDefault(node.Id, "")) : label;
                var rows = new List<UIElement> { group.Title };
                foreach (var parameter in BehaviorParameters.Exposed(node, cfg.Bindings))
                {
                    var key = (node.Id, parameter.Id); active.Add(key);
                    if (!fields.TryGetValue(key, out var field))
                        fields[key] = field = BindingParameterRow(binding, node, parameter, Reconcile);
                    field.Sync(); rows.Add(field.Row);
                }
                ReconcileParameterChildren(group.Panel, rows); panels.Add(group.Panel);
            }
            ReconcileParameterChildren(root, panels);
            foreach (var key in fields.Keys.Where(key => !active.Contains(key)).ToArray()) fields.Remove(key);
            foreach (string id in groups.Keys.Where(id => !nodes.Any(node => node.Id == id)).ToArray()) groups.Remove(id);
        }
        Reconcile(); return root;
    }

    private static void ReconcileParameterChildren(Panel panel, IReadOnlyList<UIElement> children)
    {
        for (int i = panel.Children.Count - 1; i >= 0; i--)
            if (!children.Contains(panel.Children[i])) panel.Children.RemoveAt(i);
        for (int i = 0; i < children.Count; i++)
        {
            if (i < panel.Children.Count && ReferenceEquals(panel.Children[i], children[i])) continue;
            panel.Children.Remove(children[i]); panel.Children.Insert(i, children[i]);
        }
    }
}
