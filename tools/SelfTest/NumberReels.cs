using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NekoCapture.Core;

static partial class SelfTest
{
    private static void NumberReelChecks()
    {
        _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var combo = new ComboBox { Text = "1000", HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12) };
        combo.Items.Add("5"); combo.Items.Add("25"); Fluent.StyleComboEditable(combo);
        var root = new Grid { Background = Theme.WindowBgSolidBrush, Children = { combo } };
        var window = new Window { Width = 250, Height = 85, Content = root, ResizeMode = ResizeMode.NoResize };
        var prepare = typeof(Fluent).GetMethod("PrepareComboNumber", BindingFlags.Static | BindingFlags.NonPublic)!;
        var roll = typeof(Fluent).GetMethod("RollComboNumber", BindingFlags.Static | BindingFlags.NonPublic)!;
        void Snapshot(string name)
        {
            root.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string folder = System.IO.Path.Combine(SettingsService.Dir, "previews"); System.IO.Directory.CreateDirectory(folder);
            using var stream = System.IO.File.Create(System.IO.Path.Combine(folder, name + ".png")); encoder.Save(stream);
        }
        try
        {
            ShowOffscreen(window); PumpMs(80);
            foreach (var (before, after, name) in new[] { ("1000", "5", "shorten"), ("5", "1000", "grow"), ("1.25", "5", "fraction") })
            {
                combo.Text = before; PumpMs(25); window.UpdateLayout();
                double firstWidth = combo.ActualWidth;
                prepare.Invoke(null, new object[] { combo });
                combo.Text = after;
                var watch = Stopwatch.StartNew(); roll.Invoke(null, new object[] { combo, before, after });
                var layer = (Canvas)combo.Template.FindName("numberRoll", combo);
                var text = (TextBox)combo.Template.FindName("PART_EditableTextBox", combo);
                var ones = layer.Children.OfType<Grid>().Single(c => c.Tag as string == "number-reel:0");
                int Count(string value) => value.Replace(".", "").Length;
                int places = Math.Max(Count(before), Count(after));
                int duration = Fluent.Motion.NumberRollMs + (places - 1) * Fluent.Motion.NumberStaggerMs;
                PumpMs(duration / 2); double middleWidth = combo.ActualWidth; Snapshot("number-" + name + "-middle");
                int remaining = Math.Max(0, duration - Fluent.Motion.PressMs - (int)watch.ElapsedMilliseconds);
                PumpMs(remaining); root.UpdateLayout();
                double wheelX = ones.TranslatePoint(new Point(), root).X;
                int unit = after.Contains('.') ? after.IndexOf('.') - 1 : after.Length - 1;
                double target = text.TranslatePoint(new Point(text.GetRectFromCharacterIndex(unit).Left, 0), root).X;
                Chk("number-reel-lands-on-native-character-" + name, Math.Abs(wheelX - target) < 1);
                PumpMs(Fluent.Motion.PressMs + Fluent.Motion.EnterMs); root.UpdateLayout();
                double nativeX = text.TranslatePoint(new Point(text.GetRectFromCharacterIndex(unit).Left, 0), root).X;
                Chk("number-reel-handoff-does-not-jump-" + name, text.Opacity == 1 && layer.Visibility == Visibility.Collapsed && Math.Abs(wheelX - nativeX) < 1);
                double lastWidth = combo.ActualWidth;
                Chk("number-reel-width-changes-during-roll-" + name, Math.Abs(firstWidth - lastWidth) < 1
                    || (middleWidth > Math.Min(firstWidth, lastWidth) + 0.2 && middleWidth < Math.Max(firstWidth, lastWidth) - 0.2));
                Snapshot("number-" + name + "-settled");
            }
        }
        catch (Exception ex) { Chk("number-reel-exception", false, ex.ToString()); }
        finally { window.Close(); }
    }
}
