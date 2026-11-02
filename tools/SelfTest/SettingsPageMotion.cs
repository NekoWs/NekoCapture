using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using NekoCapture.Core;
using NekoCapture.Overlays;

static partial class SelfTest
{
    static void SettingsPageMotionChecks(SettingsWindow window)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(SettingsWindow);
        var pages = (Dictionary<string, StackPanel>)type.GetField("_settingsBodies", flags)!.GetValue(window)!;
        void Go(string page) => type.GetMethod("GotoPage", flags)!.Invoke(window, new object[] { page });
        FrameworkElement[] Modules(string index) => (FrameworkElement[])type.GetMethod("PageModules",
            BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { pages[index] })!;
        int settle = Fluent.Motion.QuickMs + Fluent.Motion.EnterMs + Fluent.Motion.StaggerMs * 3 + 100;
        Go("selection"); PumpMs(settle);
        Go("hotkeys"); PumpMs(Fluent.Motion.QuickMs / 3);
        // 退场是个过程：在退场窗口里采样，只要量到「旧页半透明、已不接点击、新页还没出来」就算数
        bool exiting = false;
        var exitClock = System.Diagnostics.Stopwatch.StartNew();
        while (!exiting && exitClock.ElapsedMilliseconds < 600)
        {
            PumpMs(5);
            exiting = pages["selection"].IsVisible && pages["selection"].Opacity is > 0 and < 1
                && !pages["selection"].IsHitTestVisible && !pages["hotkeys"].IsVisible;
        }
        Chk("settings-page-fast-exit-precedes-entry", exiting,
            $"旧页可见={pages["selection"].IsVisible} 不透明度={pages["selection"].Opacity:F2} 新页可见={pages["hotkeys"].IsVisible}");
        // 逐项入场同样是过程：量到「首项已亮、末项还没起」就算数，固定时刻取样在负载下会整段跑完
        bool staggered = false;
        var cards = Modules("hotkeys");
        var staggerClock = System.Diagnostics.Stopwatch.StartNew();
        while (!staggered && staggerClock.ElapsedMilliseconds < 900)
        {
            PumpMs(10);
            cards = Modules("hotkeys");
            staggered = cards.Length >= 3 && cards[0].Opacity > cards[^1].Opacity && cards[^1].Opacity < 0.01;
        }
        Chk("settings-hotkeys-modules-enter-top-to-bottom", pages["hotkeys"].IsVisible && !pages["selection"].IsVisible
            && staggered,
            $"可见={pages["hotkeys"].IsVisible} 旧页可见={pages["selection"].IsVisible} 模块 {cards.Length} 个，首尾不透明度 {cards.FirstOrDefault()?.Opacity:F2}/{cards.LastOrDefault()?.Opacity:F2}");
        PumpMs(settle); Go("presets"); PumpMs(Fluent.Motion.QuickMs);
        var presetCards = Modules("presets").OfType<Button>().Where(button => button.Tag is string id && BehaviorPresets.All.Contains(id)).ToArray();
        Chk("settings-presets-have-separate-entry-delays", presetCards.Length == 4
            && presetCards[0].Opacity >= presetCards[2].Opacity && presetCards[2].Opacity < 0.01);
        PumpMs(settle);
        Chk("settings-page-entry-restores-module-transforms", cards.All(card => card.Opacity > 0.99
            && (card.RenderTransform == null || card.RenderTransform.Value.IsIdentity)));
        Go("record-quality"); PumpMs(settle);
        var rows = Modules("record-quality");
        Go("selection"); PumpMs(settle); Go("record-quality"); PumpMs(Fluent.Motion.QuickMs + 45);
        Chk("settings-other-pages-stagger-settings-rows", rows.Length > 1
            && rows[0].Opacity > rows[^1].Opacity && rows[^1].Opacity < 0.01);
        Go("palette"); PumpMs(20); Go("hotkeys"); PumpMs(20); Go("startup"); PumpMs(settle);
        Chk("settings-rapid-switch-only-shows-final-page", pages.All(pair => pair.Value.IsVisible == (pair.Key == "startup")) && pages["startup"].IsHitTestVisible);
        Chk("settings-rapid-switch-cleans-hidden-page-state", pages.Values.All(page => page.Opacity > 0.99
            && (page.RenderTransform == null || page.RenderTransform.Value.IsIdentity)));
        Go("hotkeys"); PumpMs(20); Go("startup"); PumpMs(settle);
        Chk("settings-return-during-exit-stays-visible", pages["startup"].IsVisible && pages.Values.Count(page => page.IsVisible) == 1
            && Modules("startup").All(row => row.Opacity > 0.99));
        Go("hotkeys"); PumpMs(settle);
    }
}
