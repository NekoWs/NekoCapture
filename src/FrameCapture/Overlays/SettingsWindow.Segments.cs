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

// 本 partial 职责：快捷键折叠行上的小控件——启用开关与删除按钮。
/// <summary>
/// 快捷键行的小控件：折叠行里的启用开关与删除按钮。
/// 两个控件都只改这条快捷键自己的状态，落盘与热重载由调用方给的回调负责。
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

}