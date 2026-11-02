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
            var samples = new List<double>();
            for (int i = 0; i < 60 && !grew; i++)
            {
                PumpMs(10);
                double height = host?.Height ?? 0;
                samples.Add(height);
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
            // 同一分组内换页：下划线从当前位置滑到新文字下面，中途要能量到中间位置——
            // 只查「有没有动画对象」会漏掉「入场时先被摆到终点、动画从终点走到终点」
            var indicator = (Border?)type.GetField("_settingsTabIndicator", flags)!.GetValue(window);
            var indicatorShift = (TranslateTransform?)type.GetField("_settingsTabIndicatorShift", flags)!.GetValue(window);
            var canvas = (Grid?)type.GetField("_settingsTabCanvas", flags)!.GetValue(window);
            window.UpdateLayout();
            var filesTab = tabsPanel?.Children.OfType<Border>().FirstOrDefault(tab =>
                (tab.Tag as string) == "settings-tab"
                && tab.Child is TextBlock labelText && labelText.Text == Loc.T(K.SettingsNavigation.Files));
            double fromX = indicatorShift?.X ?? 0;
            double filesX = filesTab?.Child is TextBlock filesLabel0 && canvas != null
                ? filesLabel0.TranslatePoint(new Point(0, 0), canvas).X
                : 0;
            type.GetMethod("NavigateSettings", flags)!.Invoke(window, new object?[] { "files", true, true });
            bool slid = false;
            var seats = new List<double>();
            for (int i = 0; i < 70 && !slid; i++)
            {
                PumpMs(10);
                double x = indicatorShift?.X ?? 0;
                seats.Add(x);
                slid = x > fromX + 2 && x < filesX - 2;
            }
            Chk("T274-tabs-indicator-slides", filesX > fromX + 4 && slid,
                $"下划线 {fromX:F0}→{filesX:F0}，中途采样 {string.Join(",", seats.Take(18).Select(v => v.ToString("F0")))}");
            PumpMs(320);
            var sameGroup = (Panel?)type.GetField("_settingsTabs", flags)!.GetValue(window);
            double at = filesX, want = filesTab?.Child is TextBlock filesLabel ? filesLabel.ActualWidth : 0;
            Chk("T274-tabs-indicator-follows",
                filesTab != null && indicator != null && indicatorShift != null && want > 0
                    && Math.Abs(indicatorShift.X - at) < 1 && Math.Abs(indicator.Width - want) < 1,
                $"下划线 x={indicatorShift?.X:F1}/宽 {indicator?.Width:F1}，目标 x={at:F1}/宽 {want:F1}");
            Chk("T274-tabs-same-group", sameGroup != null && sameGroup.Children.Count == chipCount,
                $"同组换页后仍是 {sameGroup?.Children.Count ?? -1} 枚");

            // 换回第一枚也要滑回来：终点在 0 不该被当成「没动」而瞬移
            double backFrom = indicatorShift!.X;
            type.GetMethod("NavigateSettings", flags)!.Invoke(window, new object?[] { "selection", true, true });
            bool back = false;
            var seatsBack = new List<double>();
            for (int i = 0; i < 70 && !back; i++)
            {
                PumpMs(10);
                double x = indicatorShift.X;
                seatsBack.Add(x);
                back = x < backFrom - 2 && x > 2;
            }
            Chk("T274-tabs-first-tab-animated", back,
                $"回到第一枚：{backFrom:F0}→，采样 {string.Join(",", seatsBack.Take(18).Select(v => v.ToString("F0")))}");

            // 两个都有分页条的分组之间切换：内容起点不能变，分页条也不能压到内容上
            PumpMs(320);
            var scroller2 = (System.Windows.Controls.ScrollViewer?)type.GetField("_settingsScroller", flags)!.GetValue(window);
            var host2 = (FrameworkElement?)type.GetField("_settingsTabHost", flags)!.GetValue(window);
            double topBefore = scroller2!.TransformToAncestor(window).Transform(new Point(0, 0)).Y;
            type.GetMethod("NavigateSettings", flags)!.Invoke(window, new object?[] { "record-start", true, true });
            PumpMs(700);
            double topAfter = scroller2.TransformToAncestor(window).Transform(new Point(0, 0)).Y;
            double stripBottom = host2!.TransformToAncestor(window).Transform(new Point(0, host2.ActualHeight)).Y;
            Chk("T274-tabs-groups-aligned",
                Math.Abs(topAfter - topBefore) < 1.5 && stripBottom <= topAfter + 0.5,
                $"切分组前后内容起点 {topBefore:F1}→{topAfter:F1}，分页条底 {stripBottom:F1}");
            // 换了一组按钮之后下划线也要落在新组的选中项上：重建那一刻还没排版，只能靠布局回调补位
            var rebuiltChips = ((Panel?)type.GetField("_settingsTabs", flags)!.GetValue(window))
                ?.Children.OfType<Border>().Where(tab => (tab.Tag as string) == "settings-tab").ToList();
            if (rebuiltChips is { Count: > 0 } && rebuiltChips[0].Child is TextBlock firstLabel && canvas != null)
            {
                window.UpdateLayout();
                double firstX = firstLabel.TranslatePoint(new Point(0, 0), canvas).X;
                Chk("T274-tabs-rebuilt-indicator",
                    indicatorShift != null && indicator != null
                        && Math.Abs(indicatorShift.X - firstX) < 1 && Math.Abs(indicator.Width - firstLabel.ActualWidth) < 1,
                    $"重建后下划线 x={indicatorShift?.X:F1}/宽 {indicator?.Width:F1}，目标 x={firstX:F1}/宽 {firstLabel.ActualWidth:F1}");
            }

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
