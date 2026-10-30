using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameCapture.Core;
using FrameCapture.Overlays;
using FrameCapture.Services;

static partial class SelfTest
{
    static void ComboSelectionMotionChecks()
    {
        var combo = new ComboBox { Margin = new Thickness(16) };
        Fluent.StyleCombo(combo);
        foreach (string text in new[] { "截图", "录制", "编辑" }) combo.Items.Add(new ComboBoxItem { Content = text });
        combo.SelectedIndex = 0;
        var window = new Window { Width = 300, Height = 120, Content = new StackPanel { Children = { combo } },
            Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show(); PumpMs(80);
            var strip = (Canvas)combo.Template.FindName("selectionRoll", combo);
            var viewport = (Grid)combo.Template.FindName("selectionViewport", combo);
            var label = (ContentPresenter)combo.Template.FindName("selectionLabel", combo);
            combo.SelectedIndex = 2; PumpMs(35);
            Chk("combo-caption-scrolls-up-through-intermediate-items", strip.Children.Count == 3 && label.Visibility == Visibility.Hidden
                && ((TranslateTransform)strip.RenderTransform).Y < 0 && Canvas.GetTop(strip.Children[1]) > 0);
            Chk("combo-caption-roll-has-visible-glyphs", strip.Children.OfType<Image>().All(row =>
            {
                var bitmap = (BitmapSource)row.Source; var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                return Enumerable.Range(0, pixels.Length / 4).Any(index => pixels[index * 4 + 3] > 0);
            }));
            var snapshot = typeof(Fluent).GetMethod("ComboRollSnapshot", BindingFlags.NonPublic | BindingFlags.Static)!;
            Directory.CreateDirectory("test-out/combo-motion");
            void Save(BitmapSource bitmap, string name)
            {
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine("test-out/combo-motion", name)); encoder.Save(file);
            }
            Save((BitmapSource)snapshot.Invoke(null, new object?[] { viewport, null })!, "scroll-up.png");
            PumpMs(Fluent.Motion.EnterMs + 60);
            Chk("combo-caption-restores-native-selection-after-scroll", strip.Visibility == Visibility.Collapsed && strip.Children.Count == 0
                && label.Visibility == Visibility.Visible && Equals(label.Content, combo.SelectionBoxItem));
            combo.SelectedIndex = 0; PumpMs(35);
            Chk("combo-caption-scrolls-down-for-earlier-item", ((TranslateTransform)strip.RenderTransform).Y > 0
                && Canvas.GetTop(strip.Children[^1]) < 0);
            var before = (BitmapSource)snapshot.Invoke(null, new object?[] { viewport, null })!;
            combo.SelectedIndex = 1;
            var after = (BitmapSource)((Image)strip.Children[0]).Source;
            byte[] Pixels(BitmapSource source)
            {
                var data = new byte[source.PixelWidth * source.PixelHeight * 4]; source.CopyPixels(data, source.PixelWidth * 4, 0); return data;
            }
            Chk("combo-caption-interruption-preserves-current-frame", before.PixelWidth == after.PixelWidth && Pixels(before).SequenceEqual(Pixels(after)));
            foreach (int index in new[] { 2, 0, 2, 1 }) combo.SelectedIndex = index;
            PumpMs(Fluent.Motion.EnterMs + 80);
            Chk("combo-caption-rapid-switch-settles-without-ghosts", strip.Children.Count == 0 && strip.Visibility == Visibility.Collapsed
                && label.Visibility == Visibility.Visible && Equals(label.Content, combo.SelectionBoxItem));
            var edited = new ComboBox { IsEditable = true };
            Fluent.StyleComboEditable(edited); edited.Items.Add("10"); edited.Items.Add("20");
            Chk("combo-editable-field-keeps-text-input-template", edited.Template.FindName("PART_EditableTextBox", edited) is TextBox
                && edited.Template.FindName("selectionRoll", edited) == null);
        }
        catch (Exception error) { Chk("combo-caption-motion", false, error.ToString()); }
        finally { window.Close(); }
    }

    static void OcrDeleteMotionChecks()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var hostProperty = typeof(AppHost).GetProperty("Settings")!;
        var sharedProperty = typeof(SettingsService).GetProperty("Shared")!;
        var previous = hostProperty.GetValue(AppHost.Current); var shared = SettingsService.Shared;
        SettingsWindow? window = null;
        try
        {
            var service = new SettingsService(); service.Current.OcrEngine = OcrService.SystemEngine;
            hostProperty.SetValue(AppHost.Current, service);
            window = new SettingsWindow { Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
            typeof(SettingsWindow).GetField("_restoreCat", flags)!.SetValue(window, 5);
            typeof(SettingsWindow).GetMethod("Build", flags)!.Invoke(window, null);
            window.Show(); PumpMs(Fluent.Motion.SpringMs + 60);
            var objects = new List<DependencyObject>(); CollectTyped((DependencyObject)window.Content, typeof(ComboBox), objects);
            var combo = objects.OfType<ComboBox>().Single(control => control.Items.OfType<ComboBoxItem>()
                .Any(item => item.Tag as string == OcrService.MediumEngine));
            objects.Clear(); CollectTyped((DependencyObject)window.Content, typeof(Grid), objects);
            var slot = objects.OfType<Grid>().Single(control => control.Tag as string == "ocr-model-delete-host");
            Chk("ocr-delete-motion-starts-fully-collapsed", slot.Visibility == Visibility.Collapsed && slot.Width == 0);
            combo.SelectedIndex = 1; PumpMs(35);
            Chk("ocr-delete-button-fades-and-expands-in", slot.IsVisible && slot.Opacity is > 0 and < 1
                && slot.Width is > 0 and < 36 && ((TranslateTransform)slot.RenderTransform).X < 0);
            double previousWidth = slot.Width;
            combo.SelectedIndex = 2; PumpMs(35);
            Chk("ocr-model-switch-keeps-delete-entry-continuous", slot.Width >= previousWidth);
            PumpMs(Fluent.Motion.EnterMs + 60);
            combo.SelectedIndex = 0; PumpMs(35);
            Chk("ocr-delete-button-fades-and-collapses-out", slot.Visibility == Visibility.Visible && slot.Opacity is > 0 and < 1
                && slot.Width is > 0 and < 36 && !slot.IsHitTestVisible);
            combo.SelectedIndex = 1; PumpMs(Fluent.Motion.EnterMs + 60);
            Chk("ocr-delete-button-can-reverse-exit", slot.IsVisible && slot.Opacity > 0.99 && Math.Abs(slot.Width - 36) < 0.1);
            combo.SelectedIndex = 0; PumpMs(Fluent.Motion.ExitMs + 60);
            Chk("ocr-delete-button-exit-cleans-up-layout", slot.Visibility == Visibility.Collapsed && slot.Width == 0 && slot.Opacity == 0);
        }
        catch (Exception error) { Chk("ocr-delete-motion", false, error.ToString()); }
        finally { window?.Close(); hostProperty.SetValue(AppHost.Current, previous); sharedProperty.SetValue(null, shared); }
    }
}
