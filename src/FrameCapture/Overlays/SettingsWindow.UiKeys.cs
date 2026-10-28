using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FrameCapture.Core;
using FrameCapture.Services;

namespace FrameCapture.Overlays;

// 本 partial 职责：快捷键页的「界面键位」卡——确认选区、取消选区、截图浮窗的复制键，以及它们共用的胶囊外壳。
/// <summary>
/// 设置窗的界面键位部分：界面上每一处「按一下录一个键」的控件都归「快捷键」页，
/// 这几行跟快捷键列表是同一类东西，装在列表下面一张带小标题的卡里。
/// 一行只放一个键位：两个键位挤在一行，录左边那枚时右边那枚跟着错位，看起来像一对连体控件。
/// </summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>把一行键位包成胶囊：这一页的行都是这个形状，单独几行裸铺在卡片里会显得比别处挤。
    /// 内衬上下各 3、外边距各 1，行距与快捷键列表、编辑器键位那几张卡对得上。</summary>
    /// <param name="row">由 <c>KeyRow</c> 产出的一行。</param>
    /// <returns>带底色的胶囊容器。</returns>
    static Border UiKeyPill(FrameworkElement row)
    {
        row.Margin = new Thickness(0);   // 行自己的外边距搬进胶囊里会让胶囊比别处的行高出一档
        return new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderBrush = Theme.StrokeSoftBrush,
            BorderThickness = new Thickness(1),
            Background = Theme.RowBgBrush,
            Padding = new Thickness(4, 3, 10, 3),
            Margin = new Thickness(0, 1, 0, 1),
            Child = row,
        };
    }

    /// <summary>一行一个键位：左边是这项的名字，右边是键帽。名字不带图标——
    /// 这一页的行都只有名字，图标换来换去反而让每行看起来管着不同种类的东西。</summary>
    /// <param name="label">这枚键位管什么。</param>
    /// <param name="tip">行 tooltip，写明它在什么场合生效。</param>
    /// <param name="get">读当前键位串。</param>
    /// <param name="set">改定后的写回动作，内部负责落盘。</param>
    /// <param name="reinstall">改完要不要当场重装全局钩子。</param>
    /// <returns>可直接包进胶囊的一行。</returns>
    FrameworkElement KeyRow(string label, string tip, Func<string> get, Action<string> set, bool reinstall = false)
    {
        var g = Row();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new TextBlock
        {
            Text = label,
            FontSize = 13,
            Foreground = Theme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0),
        };
        Grid.SetColumn(text, 0);
        g.Children.Add(text);
        g.ToolTip = tip;
        var box = new ChordCaptureBox
        {
            Chord = get(),
            EmptyHint = Loc.T(K.Hotkeys.ClickToSet),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        // 录入期间暂停全局钩子（与快捷键列表同一套做法），否则正要录的那个组合键会被自家钩子先吞掉
        bool wasPaused = false;
        box.CaptureBegan += () => { wasPaused = HotkeyManager.Paused; HotkeyManager.Paused = true; };
        box.CaptureEnded += () => HotkeyManager.Paused = wasPaused;
        box.Picked += c =>
        {
            set(c);
            if (reinstall) AppHost.Current.InstallHotkeys();
        };
        Grid.SetColumn(box, 1);
        g.Children.Add(box);
        return g;
    }

    // 确认选区：可调整的框出现后，按这枚键才算数
    FrameworkElement ConfirmKeyRow() =>
        KeyRow(Loc.T(K.Capture.ConfirmSelection), Loc.T(K.Settings.ConfirmCancelKeysTip),
            () => _settings.Current.PickerConfirmChord,
            c => _settings.Mutate(s => s.PickerConfirmChord = c));

    // 取消选区：可调整的框出现后，按这枚键把这一框作废
    FrameworkElement CancelKeyRow() =>
        KeyRow(Loc.T(K.Common.CancelSelection), Loc.T(K.Settings.ConfirmCancelKeysTip),
            () => _settings.Current.PickerCancelChord,
            c => _settings.Mutate(s => s.PickerCancelChord = c));

    // 截图浮窗上「复制」的键位：浮窗活着的那几秒由全局钩子代收，改完当场生效
    FrameworkElement ToastCopyRow() =>
        KeyRow(Loc.T(K.Settings.CopyKey), Loc.T(K.Settings.TheToastAlsoAccepts),
            () => _settings.Current.ToastCopyChord,
            c => _settings.Mutate(s => s.ToastCopyChord = c), reinstall: true);
}
