using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using FrameCapture.Core;
using FrameCapture.Services;

namespace FrameCapture.Overlays;

// 本 partial 职责：快捷键行上的小控件——启用开关、删除按钮、简单/高级分段选择。
/// <summary>
/// 快捷键行的小控件：折叠行里的启用开关与删除按钮，以及正文里的简单/高级分段选择。
/// 三个控件都只改这条快捷键自己的状态，落盘与热重载由调用方给的回调负责。
/// </summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>折叠行上的小开关：这条快捷键是否参与触发。放在键帽前面，一眼看出哪条被关掉了。</summary>
    /// <param name="b">这条绑定。</param>
    /// <param name="onChanged">状态变了要重画的部分，比如折叠行的淡与不淡。</param>
    /// <returns>开关轨道。</returns>
    FrameworkElement EnableSwitch(GestureBindingDto b, Action onChanged)
    {
        var track = new Border
        {
            Width = 32,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(b.Enabled ? Theme.Accent : Theme.TrackBg),
            BorderBrush = new SolidColorBrush(b.Enabled ? Theme.Accent : Theme.Stroke),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            Cursor = Cursors.Hand,
            Focusable = true,   // 键盘可达——Tab 聚焦、空格/Enter 切换
            ToolTip = Loc.T(K.Settings.OffKeepsTheSetupBut),
        };
        var knob = new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = new SolidColorBrush(b.Enabled ? Fluent.OnAccentColor(Theme.Accent) : Colors.White),
            Stroke = new SolidColorBrush(Color.FromArgb(0x26, 0, 0, 0)),
            StrokeThickness = 1,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(3, 0, 3, 0),
            RenderTransform = new TranslateTransform(b.Enabled ? 14 : 0, 0),
        };
        track.Child = knob;
        bool on = b.Enabled;
        void Flip()
        {
            on = !on;
            b.Enabled = on;
            Fluent.SlideToggle(knob, track, on, 14);
            CommitHotkeys();
            onChanged();
        }
        // 点在开关上就是去改开关：这一击吃掉，别冒泡到胶囊去展开整行。
        track.MouseLeftButtonUp += (_, e) => { Flip(); e.Handled = true; };
        track.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Space or Key.Enter) { Flip(); e.Handled = true; }
        };
        return track;
    }

    /// <summary>折叠行上的删除按钮：只留垃圾桶图标，这句话交给 tooltip。</summary>
    /// <param name="b">这条绑定。</param>
    /// <returns>图标按钮。</returns>
    FrameworkElement DeleteIconButton(GestureBindingDto b)
    {
        var btn = new Border
        {
            Width = 26,
            Height = 24,
            CornerRadius = new CornerRadius(5),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = "\uE74D",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"),
                FontSize = 13,
                Foreground = Theme.TextDimBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            ToolTip = Loc.T(K.Settings.DeleteThisHotkey),
        };
        Fluent.ChipHover(btn);
        btn.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            DetachHotkeyAnimated(b);
        };
        return btn;
    }

    /// <summary>简单/高级分段选择：一块圆角底色在两段之间滑到选中那项，选中项的字反白。
    /// 切的是完成后与失败时这两行的出现与否。</summary>
    /// <returns>分段控件。</returns>
    FrameworkElement AdvancedSwitch()
    {
        var simple = SegLabel(Loc.T(K.Settings.Simple), Loc.T(K.Settings.ShowsOnlyWhereEachCase), false);
        var adv = SegLabel(Loc.T(K.Settings.Advanced), Loc.T(K.Settings.EditTheStepsThatRun), true);
        // 段宽由两段里较宽的那条量出来：写死一个数，换语言就会把「Advanced」裁在轨道边上。
        // 66 是最窄一档的呼吸感，中文两段用不满也不塌；量出来更宽就按更宽的给。
        var labels = new UniformGrid { Rows = 1, Columns = 2 };
        labels.Children.Add(simple);
        labels.Children.Add(adv);
        labels.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double seg = Math.Max(66, Math.Ceiling(labels.DesiredSize.Width / 2)), h = 26;

        var shift = new TranslateTransform(_hotAdvanced ? seg : 0, 0);
        var hi = new Border
        {
            Width = seg - 4,
            Height = h - 6,
            CornerRadius = new CornerRadius(5),
            Background = Theme.AccentBrush,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0),
            RenderTransform = shift,
            IsHitTestVisible = false,
        };

        var track = new Grid { Width = seg * 2, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 2) };
        var back = new Border
        {
            Height = h,
            CornerRadius = new CornerRadius(7),
            Background = Theme.TrackBgBrush,
            BorderBrush = Theme.StrokeBrush,
            BorderThickness = new Thickness(1),
        };
        track.Children.Add(back);
        track.Children.Add(hi);
        track.Children.Add(labels);

        void PaintLabels()
        {
            Tint(simple, _hotAdvanced ? Theme.TextDim : Fluent.OnAccentColor(Theme.Accent));
            Tint(adv, _hotAdvanced ? Fluent.OnAccentColor(Theme.Accent) : Theme.TextDim);
        }
        void Pick(bool to)
        {
            if (_hotAdvanced == to) return;
            _hotAdvanced = to;
            shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(to ? seg : 0,
                new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.StateMs)))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            PaintLabels();
            foreach (var sync in _hotModeSync) sync();
        }
        simple.MouseLeftButtonUp += (_, e) => { Pick(false); e.Handled = true; };
        adv.MouseLeftButtonUp += (_, e) => { Pick(true); e.Handled = true; };
        _hotModeSync.Add(PaintLabels);
        return track;
    }

    /// <summary>分段控件里的一段：可点文字，左右内边距把文字与段边界隔开，段宽由外层均分给两段。</summary>
    /// <param name="text">段名。</param>
    /// <param name="tip">tooltip，写明这一档管什么。</param>
    /// <param name="mode">这一段代表的形态：false = 简单，true = 高级。</param>
    /// <returns>段文字；点击由调用方接。</returns>
    TextBlock SegLabel(string text, string tip, bool mode)
    {
        bool selected = mode == _hotAdvanced;
        return new TextBlock
        {
            Text = text,
            FontSize = 12.5,
            Padding = new Thickness(12, 0, 12, 0),
            TextAlignment = TextAlignment.Center,
            // 高度交给文字本身：写死 26 会把行盒顶到分配框上沿，多出来的空隙全落在字下面，
            // 而 VerticalAlignment 居中的是这个已经和轨道一样高的元素——上下就差了这截。
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(selected ? Fluent.OnAccentColor(Theme.Accent) : Theme.TextDim),
            Cursor = Cursors.Hand,
            ToolTip = tip,
        };
    }

    /// <summary>把一段文字的颜色淡变到新值。</summary>
    /// <param name="t">段文字。</param>
    /// <param name="to">目标颜色。</param>
    static void Tint(TextBlock t, Color to)
    {
        if (t.Foreground is not SolidColorBrush old) { t.Foreground = new SolidColorBrush(to); return; }
        var br = new SolidColorBrush(old.Color);
        t.Foreground = br;
        br.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(to, new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.StateMs))));
    }
}
