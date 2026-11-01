using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NekoCapture.Core;

static partial class SelfTest
{
    private static void MenuLayoutChecks()
    {
        _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Theme.Apply("dark", null); Fluent.ApplyGlobalStyles();
        var resources = Fluent.MenuResources();
        var separatorStyle = (Style)resources[MenuItem.SeparatorStyleKey];
        var separator = new Separator { Style = separatorStyle };
        separator.ApplyTemplate();
        var line = separator.Template.LoadContent() as Border;
        Chk("menu-separator-overrides-system-style-with-soft-line", separator.OverridesDefaultStyle && line is
            { Height: 1, Margin.Left: 10, SnapsToDevicePixels: true }
            && line.Background is SolidColorBrush brush && brush.Color == Theme.StrokeSoft);
        var rows = new StackPanel { Width = 210, Background = Theme.CardBgBrush };
        MenuItem Item(Str label, string? glyph = null, bool checkable = false)
        {
            var item = new MenuItem { Header = Loc.T(label), IsCheckable = checkable,
                Style = (Style)resources[typeof(MenuItem)] };
            if (glyph != null) item.Icon = Fluent.Glyph(glyph, 15, Theme.TextDimBrush);
            rows.Children.Add(item); return item;
        }
        var plain = Item(K.Common.Settings);
        var icon = Item(K.Tray.CaptureNow, "\uE7A8");
        var check = Item(K.Common.StartWithWindows, checkable: true);
        var gesture = Item(K.Pin.Copy); gesture.InputGestureText = "Ctrl+C";
        var submenu = Item(K.Pin.Opacity); submenu.Items.Add(new MenuItem { Header = "100%" });
        var window = new Window { Content = rows, Width = 230, Height = 190, ShowInTaskbar = false };
        try
        {
            ShowOffscreen(window); PumpMs(100); window.UpdateLayout();
            FrameworkElement Part(MenuItem item, string name) => (FrameworkElement)item.Template.FindName(name, item);
            Chk("menu-plain-item-has-no-unused-icon-gutter", Part(plain, "leading").Visibility == Visibility.Collapsed
                && Part(plain, "header").TranslatePoint(new Point(), plain).X <= 11);
            Chk("menu-icon-item-reserves-only-icon-space", Part(icon, "leading").Visibility == Visibility.Visible
                && Part(icon, "header").TranslatePoint(new Point(), icon).X <= 34);
            Chk("menu-empty-shortcut-column-collapses", Part(plain, "gesture").Visibility == Visibility.Collapsed);
            Chk("menu-shortcut-label-remains-visible", Part(gesture, "gesture").Visibility == Visibility.Visible);
            Chk("menu-unchecked-item-retains-usable-check-space", Part(check, "leading").Visibility == Visibility.Visible);
            check.IsChecked = true; PumpMs(20);
            Chk("menu-checkmark-remains-visible", ((TextBlock)Part(check, "chk")).Text.Length == 1);
            Chk("menu-submenu-arrow-remains-visible", Part(submenu, "arrow").Visibility == Visibility.Visible);
            var bitmap = new RenderTargetBitmap(210, 150, 96, 96, PixelFormats.Pbgra32); bitmap.Render(rows);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string path = System.IO.Path.Combine(SettingsService.Dir, "menu-layout.png");
            System.IO.Directory.CreateDirectory(SettingsService.Dir);
            using var file = System.IO.File.Create(path); encoder.Save(file);
        }
        catch (Exception ex) { Chk("menu-layout-exception", false, ex.ToString()); }
        finally { window.Close(); }
    }
}
