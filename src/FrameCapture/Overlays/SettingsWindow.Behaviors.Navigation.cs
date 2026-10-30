using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>行为路径复用未变化的前缀，只为新增路径段播放入场。</summary>
public sealed partial class SettingsWindow
{
    private WrapPanel? _behaviorBreadcrumbs;
    private readonly List<(string Key, Button Button, bool Selected)> _behaviorCrumbs = new();

    private void RenderBehaviorBreadcrumbs(int direction)
    {
        if (_behaviorDetails == null) return;
        if (_behaviorBreadcrumbs == null)
        {
            _behaviorDetails.Children.Clear();
            _behaviorDetails.Children.Add(new Border { Height = 1, Background = Theme.StrokeSoftBrush,
                Margin = new Thickness(2, 0, 2, 8) });
            _behaviorBreadcrumbs = new WrapPanel { Margin = new Thickness(0, 0, 0, 7) };
            _behaviorDetails.Children.Add(_behaviorBreadcrumbs);
            _behaviorCrumbs.Clear();
        }
        var cfg = _settings.Current.Gestures;
        var graph = cfg.Graph;
        var wanted = new List<(string Key, string Text, int Depth, bool Selected)>();
        if (graph.Flow(_behaviorFlow) is { } flow)
            wanted.Add(("flow:" + flow.Id, cfg.FlowName(flow), 1, false));
        for (int i = 0; i < _behaviorPath.Count; i++)
            wanted.Add(("node:" + _behaviorPath[i], GestureActions.LabelOf(graph.Node(_behaviorPath[i])!.Action),
                i + 1, i + 1 == _behaviorPath.Count));
        int common = 0;
        while (common < _behaviorCrumbs.Count && common < wanted.Count && _behaviorCrumbs[common].Key == wanted[common].Key)
        {
            var existing = _behaviorCrumbs[common];
            var next = wanted[common];
            existing.Button.Content = next.Text;
            if (existing.Selected != next.Selected)
            {
                existing.Button.BorderBrush = next.Selected ? Theme.AccentBrush : Brushes.Transparent;
                existing.Button.Template = BehaviorButtonTemplate(Theme.CardBg, next.Selected, Theme.Text);
                _behaviorCrumbs[common] = (existing.Key, existing.Button, next.Selected);
            }
            common++;
        }
        int keep = Math.Max(0, common * 2 - 1);
        if (_behaviorBreadcrumbs.Children.Count > keep)
            _behaviorBreadcrumbs.Children.RemoveRange(keep, _behaviorBreadcrumbs.Children.Count - keep);
        if (_behaviorCrumbs.Count > common) _behaviorCrumbs.RemoveRange(common, _behaviorCrumbs.Count - common);
        var entering = new List<FrameworkElement>();
        for (int i = common; i < wanted.Count; i++)
        {
            var item = wanted[i];
            if (i > 0)
            {
                var separator = new TextBlock { Text = "\uE76C", FontFamily = new FontFamily("Segoe Fluent Icons"),
                    Foreground = Theme.TextDimBrush, FontSize = 9, Margin = new Thickness(4, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center };
                _behaviorBreadcrumbs.Children.Add(separator); entering.Add(separator);
            }
            var button = BehaviorButton(item.Text, () => NavigateBehaviorPath(item.Depth), item.Selected);
            _behaviorCrumbs.Add((item.Key, button, item.Selected));
            _behaviorBreadcrumbs.Children.Add(button); entering.Add(button);
        }
        if (_behaviorBreadcrumbs.IsLoaded && _behaviorBreadcrumbs.IsVisible && entering.Count > 0)
            Fluent.CascadeIn(entering, risePx: 0, riseX: direction * 6, budgetMs: Fluent.Motion.EnterMs);
    }
}
