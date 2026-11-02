using System.Windows;
using System.Windows.Controls;
using NekoCapture.Core;

namespace NekoCapture.Overlays;

/// <summary>在动作内配置允许各触发单独调整的设置。</summary>
public sealed partial class SettingsWindow
{
    private FrameworkElement BehaviorExposureSettings(BehaviorNode node)
    {
        var root = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
        var open = Fluent.RoundButton(Loc.T(K.Triggers.Parameters), Theme.CardBg, Theme.TextDim, 12, false);
        open.HorizontalAlignment = HorizontalAlignment.Left; open.Tag = "settings-action-exposure";
        var content = new StackPanel();
        bool expanded = false;
        open.Click += (_, _) =>
        {
            expanded = !expanded;
            TransitionContent(content, () =>
            {
                content.Children.Clear();
                if (!expanded) return;
                var cfg = _settings.Current.Gestures;
                content.Children.Add(UiNote(Loc.T(K.Triggers.ParametersHint)));
                var exposed = BehaviorParameters.Exposed(node, cfg.Bindings).Select(parameter => parameter.Id).ToHashSet();
                foreach (var parameter in BehaviorParameters.For(node))
                {
                    var check = Fluent.Ui(new CheckBox { Content = Loc.T(parameter.Label), IsChecked = exposed.Contains(parameter.Id),
                        ToolTip = Loc.T(parameter.Hint), Foreground = Theme.TextBrush, FontSize = 13,
                        MinHeight = 36, Margin = new Thickness(0, 3, 0, 3), Tag = "settings-action-expose:" + parameter.Id });
                    void Save(bool enabled) { BehaviorParameters.SetExposure(cfg, node, parameter.Id, enabled); CommitBehaviorGraph(); }
                    check.Checked += (_, _) => Save(true); check.Unchecked += (_, _) => Save(false);
                    content.Children.Add(check);
                }
            });
        };
        root.Children.Add(open); root.Children.Add(content); return root;
    }
}
