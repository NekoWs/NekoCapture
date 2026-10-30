using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FrameCapture.Core;
using FrameCapture.Overlays;

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
        Go("capture"); PumpMs(settle);
        Go("hotkeys"); PumpMs(Fluent.Motion.QuickMs / 3);
        Chk("settings-page-fast-exit-precedes-entry", pages["capture"].IsVisible && pages["capture"].Opacity is > 0 and < 1
            && !pages["capture"].IsHitTestVisible && !pages["hotkeys"].IsVisible);
        PumpMs(Fluent.Motion.QuickMs);
        var cards = Modules("hotkeys");
        Chk("settings-hotkeys-modules-enter-top-to-bottom", pages["hotkeys"].IsVisible && !pages["capture"].IsVisible
            && cards.Length >= 7 && cards[0].Opacity > cards[^1].Opacity && cards[^1].Opacity < 0.01);
        PumpMs(settle); Go("presets"); PumpMs(Fluent.Motion.QuickMs);
        var presetCards = Modules("presets").OfType<Button>().Where(button => button.Tag is string id && BehaviorPresets.All.Contains(id)).ToArray();
        Chk("settings-presets-have-separate-entry-delays", presetCards.Length == 4
            && presetCards[0].Opacity >= presetCards[2].Opacity && presetCards[2].Opacity < 0.01);
        PumpMs(settle);
        Chk("settings-page-entry-restores-module-transforms", cards.All(card => card.Opacity > 0.99
            && (card.RenderTransform == null || card.RenderTransform.Value.IsIdentity)));
        Go("record-quality"); PumpMs(settle);
        var rows = Modules("record-quality");
        Go("capture"); PumpMs(settle); Go("record-quality"); PumpMs(Fluent.Motion.QuickMs + 45);
        Chk("settings-other-pages-stagger-settings-rows", rows.Length > 1
            && rows[0].Opacity > rows[^1].Opacity && rows[^1].Opacity < 0.01);
        Go("palette"); PumpMs(20); Go("hotkeys"); PumpMs(20); Go("system"); PumpMs(settle);
        Chk("settings-rapid-switch-only-shows-final-page", pages.All(pair => pair.Value.IsVisible == (pair.Key == "system")) && pages["system"].IsHitTestVisible);
        Chk("settings-rapid-switch-cleans-hidden-page-state", pages.Values.All(page => page.Opacity > 0.99
            && (page.RenderTransform == null || page.RenderTransform.Value.IsIdentity)));
        Go("hotkeys"); PumpMs(20); Go("system"); PumpMs(settle);
        Chk("settings-return-during-exit-stays-visible", pages["system"].IsVisible && pages.Values.Count(page => page.IsVisible) == 1
            && Modules("system").All(row => row.Opacity > 0.99));
        Go("hotkeys"); PumpMs(settle);
    }
}
