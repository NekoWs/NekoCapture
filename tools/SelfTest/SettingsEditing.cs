using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NekoCapture.Core;
using NekoCapture.Overlays;

static partial class SelfTest
{
    static void SettingsEditingChecks(SettingsWindow settings, SettingsService service)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        // 行内展开编辑器已经换成「概览卡 + 触发详情页」（那部分覆盖在 SettingsOverview），
        // 这里只留改名弹框自己的契约：模板、选中、没改就不落盘
        int savedNames = 0, unchangedNames = 0;
        var fixture = new Window
        {
            Width = 440, Height = 130, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false,
        };
        try
        {
            var rename = (Grid)typeof(SettingsWindow).GetMethod("RenameBox", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { "默认名称", (Action<string>)(_ => savedNames++), (Action)(() => unchangedNames++) })!;
            fixture.Content = rename; fixture.Show(); PumpMs(80);
            var editor = rename.Children.OfType<TextBox>().Single(); editor.ApplyTemplate();
            Chk("rename-field-has-rounded-content-and-readable-selection", editor.Template.LoadContent() is Border { CornerRadius.TopLeft: 10 }
                && editor.Template.FindName("PART_ContentHost", editor) is ScrollViewer && editor.MinHeight >= 32
                && editor.SelectionOpacity < 0.5 && InputMethod.GetIsInputMethodEnabled(editor)
                && editor.SelectionLength == editor.Text.Length);
            editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor), 0, Key.Enter)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Chk("rename-unchanged-name-does-not-save-preset", savedNames == 0 && unchangedNames == 1,
                $"落盘 {savedNames} 次，取消回调 {unchangedNames} 次");
        }
        finally { fixture.Close(); }
        var saved = service.Current.Clone();
        var cache = typeof(CaptureSound).GetField("_cached", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previousCache = cache.GetValue(null);
        try
        {
            service.Current.CaptureSound = false; service.Current.CustomSoundPath = "";
            var sound = (Grid)typeof(SettingsWindow).GetMethod("CaptureSoundRow", flags)!.Invoke(settings, null)!;
            var controls = sound.Children.OfType<StackPanel>().Single();
            var preview = controls.Children.OfType<Border>().Single();
            var combo = controls.Children.OfType<ComboBox>().Single();
            cache.SetValue(null, null);
            preview.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
            Chk("muted-preview-does-not-play-built-in-sound", cache.GetValue(null) == null && !preview.IsEnabled && preview.Opacity < 0.6);
            combo.SelectedIndex = 1;
            Chk("chime-selection-enables-preview", service.Current.CaptureSound && preview.IsEnabled && preview.Opacity > 0.99);
            combo.SelectedIndex = 0;
            preview.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(settings), 0, Key.Space)
                { RoutedEvent = Keyboard.KeyDownEvent });
            Chk("muted-preview-keyboard-does-not-play-sound", !service.Current.CaptureSound && !preview.IsEnabled && cache.GetValue(null) == null);
        }
        finally { cache.SetValue(null, previousCache); service.Save(saved); }
    }
}
