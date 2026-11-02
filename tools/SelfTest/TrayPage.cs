using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using NekoCapture.Core;
using NekoCapture.Overlays;
using NekoCapture.Services;

static partial class SelfTest
{
    /// <summary>托盘页：行数、拖动改序、图标小窗、逐行入场与分页条的出现/收起。</summary>
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

            var first = list.Children.OfType<FrameworkElement>().First();
            double step = first.ActualHeight + 4;
            int count = list.Children.Count;

            // 落点几何：一格一步，越界夹在两端——落点算错就是「拖不动」的来源
            var dropIndex = type.GetMethod("TrayDropIndex", flags)!;
            int Slot(double y) => (int)dropIndex.Invoke(window, new object?[] { y, first })!;
            bool slots = Slot(step * 0.4) == 0 && Slot(step * 1.6) == 1 && Slot(-40) == 0 && Slot(step * 99) == count - 1;
            Chk("T274-tray-drop-slots", slots,
                $"落点：0.4 格→{Slot(step * 0.4)}，1.6 格→{Slot(step * 1.6)}，越界→{Slot(-40)}/{Slot(step * 99)}");

            // 让位：落点换格时其余行先按一格滑开，被拖的那一行留在原处
            type.GetField("_trayDragRow", flags)!.SetValue(window, first);
            type.GetField("_trayDragTarget", flags)!.SetValue(window, count - 1);
            type.GetMethod("ShiftTrayRows", flags)!.Invoke(window, null);
            PumpMs(240);
            var shifted = RowsOf(list).Skip(1).Select(ShiftOf).ToList();
            bool letWay = shifted.Count > 0 && shifted.All(y => Math.Abs(y + step) < 1.5) && Math.Abs(ShiftOf(first)) < 0.5;
            Chk("T274-tray-let-way", letWay,
                $"让位位移：{string.Join(",", shifted.Select(y => y.ToString("F1")))}（一格 {step:F1}，被拖行 {ShiftOf(first):F1}）");
            type.GetField("_trayDragRow", flags)!.SetValue(window, null);
            type.GetField("_trayDragTarget", flags)!.SetValue(window, -1);

            var before = service.Current.Gestures.Bindings
                .Where(b => b.Source == TriggerSources.TrayMenu).Select(b => b.Id).ToList();
            // 视觉顺序换过之后松手，落盘顺序要跟着换：把第一行挪到末尾再走一次收尾
            type.GetField("_trayDragging", flags)!.SetValue(window, true);
            type.GetField("_trayDragRow", flags)!.SetValue(window, first);
            type.GetField("_trayDragTarget", flags)!.SetValue(window, list.Children.Count - 1);
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

            // 落位动画走完后子元素才真正换序，换的是同一批控件，不重建
            PumpMs(320);
            var settled = list.Children.OfType<FrameworkElement>().Select(row => row.Tag as string).ToList();
            Chk("T274-tray-drag-reorders", settled.Count == after.Count
                && (settled[^1] ?? "").EndsWith(after[^1], StringComparison.Ordinal),
                $"落位后的视觉顺序：{string.Join(",", settled)}");

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

            // 逐项入场：行各自从 0.86 倍弹回原大，错开起跳；整列同时出现会是 1.0
            type.GetMethod("PlayTrayMenuEntrance", flags)!.Invoke(window, new object?[] { true });
            PumpMs(30);
            var entering = RowsOf(list).Select(ScaleOf).ToList();
            Chk("T274-tray-row-entrance",
                entering.Count > 0 && entering.All(scale => scale < 0.99),
                $"入场起跳时的缩放：{string.Join(",", entering.Select(scale => scale.ToString("F2")))}");
            PumpMs(760);
            var landed = RowsOf(list).Select(ScaleOf).ToList();
            Chk("T274-tray-row-entrance-lands",
                landed.Count > 0 && landed.All(scale => Math.Abs(scale - 1) < 0.02),
                $"入场落定后的缩放：{string.Join(",", landed.Select(scale => scale.ToString("F2")))}");

            // 分页条：多页分组展开到定高，单页分组整条收起；高度是走出来的，不是一步跳到位
            type.GetMethod("NavigateSettings", flags)!.Invoke(window, new object?[] { "selection", true, true });
            var host = (FrameworkElement?)type.GetField("_settingsTabHost", flags)!.GetValue(window);
            bool grew = false;
            for (int i = 0; i < 60 && !grew; i++)
            {
                PumpMs(10);
                double height = host?.Height ?? 0;
                grew = height > 0.5 && height < 33.5;
            }
            Chk("T274-tabs-height-animated", grew, $"分页条高度过渡中量到中间值：{(grew ? "有" : "没有")}");
            PumpMs(360);
            var tabsPanel = (Panel?)type.GetField("_settingsTabs", flags)!.GetValue(window);
            Chk("T274-tabs-multi-group",
                host != null && host.Visibility == Visibility.Visible && Math.Abs(host.Height - 34) < 0.01
                    && tabsPanel != null && tabsPanel.Children.Count > 1,
                $"多页分组的分页条：{host?.Visibility} / {host?.Height}px / {tabsPanel?.Children.Count ?? -1} 枚");
            int chipCount = tabsPanel?.Children.Count ?? 0;
            // 同一分组内换页：胶囊滑到选中那一枚下面，条目数不变
            var pill = (Border?)type.GetField("_settingsTabPill", flags)!.GetValue(window);
            var pillShift = (TranslateTransform?)type.GetField("_settingsTabPillShift", flags)!.GetValue(window);
            var track = (Grid?)type.GetField("_settingsTabTrack", flags)!.GetValue(window);
            type.GetMethod("NavigateSettings", flags)!.Invoke(window, new object?[] { "files", true, true });
            PumpMs(240);
            bool sliding = pill != null && pillShift != null && pill.HasAnimatedProperties && pillShift.HasAnimatedProperties;
            Chk("T274-tabs-pill-animated", sliding, $"胶囊与位移{(sliding ? "都带" : "没有")}动画");
            PumpMs(320);
            var sameGroup = (Panel?)type.GetField("_settingsTabs", flags)!.GetValue(window);
            var filesTab = sameGroup?.Children.OfType<Border>().FirstOrDefault(tab =>
                (tab.Tag as string) == "settings-tab"
                && tab.Child is TextBlock labelText && labelText.Text == Loc.T(K.SettingsNavigation.Files));
            double at = 0, want = 0;
            if (filesTab != null && track != null)
            {
                window.UpdateLayout();
                at = filesTab.TranslatePoint(new Point(0, 0), track).X;
                want = filesTab.ActualWidth;
            }
            Chk("T274-tabs-pill-follows",
                filesTab != null && pill != null && pillShift != null
                    && Math.Abs(pillShift.X - at) < 1 && Math.Abs(pill.Width - want) < 1,
                $"胶囊 x={pillShift?.X:F1}/宽 {pill?.Width:F1}，目标 x={at:F1}/宽 {want:F1}");
            Chk("T274-tabs-same-group", sameGroup != null && sameGroup.Children.Count == chipCount,
                $"同组换页后仍是 {sameGroup?.Children.Count ?? -1} 枚");

            type.GetMethod("NavigateSettings", flags)!.Invoke(window, new object?[] { "tray", true, true });
            PumpMs(520);
            var trayHost = (FrameworkElement?)type.GetField("_settingsTabHost", flags)!.GetValue(window);
            var trayTabs = (Panel?)type.GetField("_settingsTabs", flags)!.GetValue(window);
            Chk("T274-tabs-single-group",
                trayHost != null && trayHost.Visibility == Visibility.Collapsed && Math.Abs(trayHost.Height) < 0.01
                    && trayTabs != null && trayTabs.Children.Count == 0,
                $"单页分组整条收起：{trayHost?.Visibility} / {trayHost?.Height}px / {trayTabs?.Children.Count ?? -1} 枚");
        }
        catch (Exception e) { Chk("T274-tray-page", false, e.Message); }
        finally
        {
            window.Close();
            settingsProperty.SetValue(AppHost.Current, prior);
        }
    }

    /// <summary>行容器里的子行。</summary>
    /// <param name="list">承载行的面板。</param>
    /// <returns>按视觉顺序排列的行。</returns>
    private static List<FrameworkElement> RowsOf(Panel list) => list.Children.OfType<FrameworkElement>().ToList();

    /// <summary>读一行当前的缩放倍率；没有缩放变换时按 1 算。</summary>
    /// <param name="row">目标行。</param>
    /// <returns>横向缩放倍率。</returns>
    private static double ScaleOf(FrameworkElement row) => row.RenderTransform is TransformGroup group
        && group.Children.OfType<ScaleTransform>().FirstOrDefault() is { } scale
        ? scale.ScaleX
        : 1;

    /// <summary>读一行当前的纵向位移；没有位移变换时按 0 算。</summary>
    /// <param name="row">目标行。</param>
    /// <returns>相对排版位置的纵向位移。</returns>
    private static double ShiftOf(FrameworkElement row) => row.RenderTransform is TransformGroup group
        && group.Children.OfType<TranslateTransform>().FirstOrDefault() is { } shift
        ? shift.Y
        : 0;
}
