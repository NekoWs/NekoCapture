using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FrameCapture.Core;
using FrameCapture.Services;

namespace FrameCapture.Overlays;

// 本 partial 职责：快捷键页的「界面键位」卡——确认与取消、截图浮窗的复制键，以及它们共用的胶囊外壳。
/// <summary>
/// 设置窗的界面键位部分：界面上每一处「按一下录一个键」的控件都归「快捷键」页，
/// 这两行跟快捷键列表是同一类东西，装在列表下面一张带小标题的卡里。
/// </summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>把一行键位包成胶囊：这一页的行都是这个形状，单独两行裸铺在卡片里会显得比别处挤。
    /// 内衬上下各 3、外边距各 1，行距与快捷键列表、编辑器键位那几张卡对得上。</summary>
    /// <param name="row">由 <c>ChipRow</c> 产出的一行。</param>
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

    // 确认/取消选区：可调整的框出现后生效，右侧两个键位框点一下就能录
    FrameworkElement PickerKeysRow()
    {
        var g = ChipRow("\uE8C8", Loc.T(K.Settings.ConfirmCancelKeysTip), Loc.T(K.Settings.ConfirmCancelKeysHint));
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(row, 2);
        row.Children.Add(PickerKeyBox(_settings.Current.PickerConfirmChord, "confirm"));
        row.Children.Add(PickerKeyBox(_settings.Current.PickerCancelChord, "cancel"));
        g.Children.Add(row);
        return g;
    }

    // 单个键位框：录入期间暂停全局钩子（与快捷键列表同一套做法），改完当场落盘
    ChordCaptureBox PickerKeyBox(string chord, string which)
    {
        var box = new ChordCaptureBox
        {
            Chord = chord,
            EmptyHint = Loc.T(K.Hotkeys.ClickToSet),
            Margin = new Thickness(8, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        bool wasPaused = false;
        box.CaptureBegan += () => { wasPaused = HotkeyManager.Paused; HotkeyManager.Paused = true; };
        box.CaptureEnded += () => HotkeyManager.Paused = wasPaused;
        box.Picked += c => _settings.Mutate(s =>
        {
            if (which == "confirm") s.PickerConfirmChord = c; else s.PickerCancelChord = c;
        });
        return box;
    }

    // 截图浮窗上「复制」的键位：浮窗活着的那几秒由全局钩子代收，改完当场生效。
    FrameworkElement ToastCopyRow()
    {
        var g = ChipRow("\uE8C8", Loc.T(K.Settings.TheToastAlsoAccepts), Loc.T(K.Settings.CopyKey));
        var box = new ChordCaptureBox
        {
            Chord = _settings.Current.ToastCopyChord,
            EmptyHint = Loc.T(K.Hotkeys.ClickToSet),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        // 录入期间暂停全局钩子，否则正要录的那个组合键会被自家钩子先吞掉（与快捷键列表同一套做法）
        bool wasPaused = false;
        box.CaptureBegan += () => { wasPaused = HotkeyManager.Paused; HotkeyManager.Paused = true; };
        box.CaptureEnded += () => HotkeyManager.Paused = wasPaused;
        box.Picked += c =>
        {
            _settings.Mutate(s => s.ToastCopyChord = c);
            AppHost.Current.InstallHotkeys();   // 钩子里那份键位串跟着换，不必重启
        };
        Grid.SetColumn(box, 2);
        g.Children.Add(box);
        return g;
    }
}