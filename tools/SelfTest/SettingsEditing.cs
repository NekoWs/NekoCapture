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
        var binding = new GestureBindingDto { Id = "settings-editing", Name = "自定义名称", Trigger = "Tap", Chord = "F8" };
        var row = (Border)typeof(SettingsWindow).GetMethod("HotkeyRow", flags)!.Invoke(settings, new object[] { binding })!;
        var header = (Grid)((Border)((StackPanel)row.Child).Children[0]).Child;
        var nameCell = (Grid)header.Children[1];
        var fixture = new Window { Content = row, Width = 440, Height = 130, Left = -20000, Top = -20000,
            ShowActivated = false, ShowInTaskbar = false };
        void Click(UIElement target) => target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        try
        {
            fixture.Show(); PumpMs(80);
            var name = nameCell.Children.OfType<TextBlock>().First();
            var hint = nameCell.Children.OfType<TextBlock>().Last();
            var measure = new TextBlock { Text = name.Text, FontSize = name.FontSize, FontWeight = name.FontWeight };
            measure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double gap = hint.TranslatePoint(new Point(), row).X - name.TranslatePoint(new Point(), row).X - measure.DesiredSize.Width;
            Chk("hotkey-trigger-hint-follows-name", gap is >= 0 and < 14 && hint.Text == Loc.T(GestureConfig.TriggerLabel("Tap")));
            Click(nameCell.Children.OfType<Border>().Single()); PumpMs(80);
            var host = nameCell.Children.OfType<Grid>().Single();
            var editor = host.Children.OfType<TextBox>().Single(); editor.ApplyTemplate();
            Chk("rename-field-has-rounded-content-and-readable-selection", editor.Template.LoadContent() is Border { CornerRadius.TopLeft: 10 }
                && editor.Template.FindName("PART_ContentHost", editor) is ScrollViewer && editor.MinHeight >= 32
                && editor.SelectionOpacity < 0.5 && InputMethod.GetIsInputMethodEnabled(editor)
                && editor.SelectionLength == editor.Text.Length);
            editor.Text = new string('W', 80); fixture.UpdateLayout();
            Chk("rename-field-stays-inside-name-column", editor.ActualWidth <= nameCell.ActualWidth + 1
                && editor.ActualWidth > 60 && header.Children.OfType<ChordCaptureBox>().Single().ActualWidth > 20);
            Click(editor);
            Chk("rename-field-click-does-not-expand-hotkey", ((StackPanel)row.Child).Children[1].Visibility == Visibility.Collapsed);
            editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor), 0, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            fixture.UpdateLayout();
            Chk("rename-escape-preserves-name-and-trigger", binding.Name == "自定义名称"
                && nameCell.Children.OfType<TextBlock>().Last().Text == hint.Text);
            int savedNames = 0, unchangedNames = 0;
            var unchanged = (Grid)typeof(SettingsWindow).GetMethod("RenameBox", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { "默认名称", (Action<string>)(_ => savedNames++), (Action)(() => unchangedNames++) })!;
            fixture.Content = unchanged; fixture.UpdateLayout();
            var unchangedEditor = unchanged.Children.OfType<TextBox>().Single();
            unchangedEditor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(fixture), 0, Key.Enter)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Chk("rename-unchanged-name-does-not-save-preset", savedNames == 0 && unchangedNames == 1);
        }
        finally { fixture.Close(); typeof(SettingsWindow).GetMethod("RebuildHotkeys", flags)!.Invoke(settings, null); }

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
