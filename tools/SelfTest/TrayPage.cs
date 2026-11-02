using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using NekoCapture.Core;
using NekoCapture.Overlays;
using NekoCapture.Services;

static partial class SelfTest
{
    /// <summary>托盘页：行数、拖动改序、图标小窗与分页条的高度过渡。</summary>
    private static void TrayPageChecks()
    {
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var type = typeof(SettingsWindow);
        var settingsProperty = typeof(AppHost).GetProperty("Settings")!;
        object? prior = settingsProperty.GetValue(AppHost.Current);
        var service = new SettingsService();
        settingsProperty.SetValue(AppHost.Current, service);
        var window = new SettingsWindow();
        try
        {
            type.GetMethod("Build", flags)!.Invoke(window, null);
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -20000; window.Top = -20000; window.ShowInTaskbar = false;
            window.Show(); PumpMs(220);
            type.GetMethod("NavigateSettings", flags)!.Invoke(window, new object?[] { "tray", false, true });
            PumpMs(260);

            var list = (StackPanel?)type.GetField("_trayMenuList", flags)!.GetValue(window);
            Chk("T274-tray-rows", list != null && list.Children.Count >= 3, $"菜单行 {list?.Children.Count ?? 0} 条");
            if (list == null) return;

            var before = service.Current.Gestures.Bindings
                .Where(b => b.Source == TriggerSources.TrayMenu).Select(b => b.Id).ToList();
            // 视觉顺序换过之后松手，落盘顺序要跟着换：把第一行挪到末尾再走一次收尾
            var first = list.Children.OfType<FrameworkElement>().First();
            list.Children.Remove(first);
            list.Children.Insert(list.Children.Count, first);
            type.GetField("_trayDragging", flags)!.SetValue(window, true);
            type.GetField("_trayDragRow", flags)!.SetValue(window, first);
            type.GetMethod("EndTrayDrag", flags)!.Invoke(window, new object?[]
            {
                new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left),
            });
            PumpMs(140);
            var after = service.Current.Gestures.Bindings
                .Where(b => b.Source == TriggerSources.TrayMenu).Select(b => b.Id).ToList();
            Chk("T274-tray-drag-persists",
                before.Count == after.Count && after.Count > 1 && after[^1] == before[0] && after[0] == before[1],
                $"拖动后的顺序：{string.Join(",", after)}");

            // 图标小窗：点开要在位，格数是目录条目加一个「跟随行为」
            var rows = list.Children.OfType<FrameworkElement>().ToList();
            var binding = service.Current.Gestures.Bindings.First(b => b.Source == TriggerSources.TrayMenu);
            type.GetMethod("ShowTrayIconPopup", flags)!.Invoke(window, new object?[] { rows[0], binding });
            PumpMs(160);
            var popup = (Popup?)type.GetField("_trayIconPopup", flags)!.GetValue(window);
            var cells = new List<DependencyObject>();
            if (popup?.Child is DependencyObject content) CollectTyped(content, typeof(Button), cells);
            Chk("T274-tray-icon-popup",
                popup is { IsOpen: true } && cells.Count == TrayMenuIcons.All.Count + 1,
                $"小窗 {(popup?.IsOpen == true ? "开着" : "没开")}，格子 {cells.Count} 个");
            type.GetMethod("CloseTrayIconPopup", flags)!.Invoke(window, null);
            // 分页条：常显且定高，切分组时下面的内容不位移
            type.GetMethod("NavigateSettings", flags)!.Invoke(window, new object?[] { "selection", true, true });
            PumpMs(120);
            var host = (FrameworkElement?)type.GetField("_settingsTabHost", flags)!.GetValue(window);
            var scroller = (System.Windows.Controls.ScrollViewer?)type.GetField("_settingsScroller", flags)!.GetValue(window);
            double contentTop = scroller!.TransformToAncestor(window).Transform(new Point(0, 0)).Y;
            Chk("T274-tabs-fixed-height",
                host != null && host.Visibility == Visibility.Visible && Math.Abs(host.Height - 34) < 0.01,
                $"分页条常显且定高：{host?.Visibility} / {host?.Height}px");
            type.GetMethod("NavigateSettings", flags)!.Invoke(window, new object?[] { "tray", true, true });
            PumpMs(200);
            double trayTop = scroller.TransformToAncestor(window).Transform(new Point(0, 0)).Y;
            Chk("T274-tabs-no-content-shift",
                host != null && host.Visibility == Visibility.Visible && Math.Abs(host.Height - 34) < 0.01
                    && Math.Abs(trayTop - contentTop) <= 1.5,
                $"切到单页分组后分页条 {host?.Height}px，内容起点差 {Math.Abs(trayTop - contentTop):F1}px");
            var tabsPanel = (System.Windows.Controls.Panel?)type.GetField("_settingsTabs", flags)!.GetValue(window);
            Chk("T274-tabs-single-group", tabsPanel != null && tabsPanel.Children.Count == 1,
                $"单页分组上仍有一枚当前页胶囊：{tabsPanel?.Children.Count ?? -1} 枚");
        }
        catch (Exception e) { Chk("T274-tray-page", false, e.Message); }
        finally
        {
            window.Close();
            settingsProperty.SetValue(AppHost.Current, prior);
        }
    }
}