using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using NekoCapture.Capture;
using NekoCapture.Core;
using NekoCapture.Overlays;
using NekoCapture.Services;

static partial class SelfTest
{
    private static void SettingsPolishChecks(SettingsWindow window, SettingsService settings, Action<string> snapshot)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var type = typeof(SettingsWindow);
        object? Field(string name) => type.GetField(name, flags)!.GetValue(window);
        void Go(string id, bool animate = false) => type.GetMethod("NavigateSettings", flags)!.Invoke(window, new object[] { id, animate, true });
        int settle = Fluent.Motion.QuickMs + Fluent.Motion.EnterMs + 80;
        Go("capture"); PumpMs(settle);
        var crumbs = (Border)Field("_settingsCrumbHost")!;
        var bodies = (Dictionary<string, StackPanel>)Field("_settingsBodies")!;
        double before = bodies["capture"].TranslatePoint(new Point(), window).Y;
        Go("selection", true); PumpMs(Fluent.Motion.QuickMs / 3);
        double during = bodies["capture"].TranslatePoint(new Point(), window).Y;
        Chk("settings-path-does-not-push-outgoing-page", crumbs.Visibility == Visibility.Collapsed && during <= before + 0.5);
        PumpMs(Fluent.Motion.QuickMs);
        var trail = (WrapPanel)Field("_settingsCrumbs")!;
        double enteringHeight = crumbs.ActualHeight, enteringOpacity = trail.Opacity;
        double enteringTrailHeight = trail.ActualHeight;
        PumpMs(settle);
        Chk("settings-path-enters-with-height-and-opacity", crumbs.IsVisible && enteringHeight > 0
            && enteringHeight < crumbs.ActualHeight && enteringOpacity is > 0 and < 1,
            $"entering={enteringHeight:F2} settled={crumbs.ActualHeight:F2} opacity={enteringOpacity:F2}");
        Chk("settings-path-text-retains-full-height", Math.Abs(enteringTrailHeight - trail.ActualHeight) < 0.5);
        Go("capture", true); PumpMs(Fluent.Motion.QuickMs / 3);
        Chk("settings-path-exits-before-releasing-space", crumbs.IsVisible && crumbs.ActualHeight > 0 && trail.Opacity is > 0 and < 1);
        PumpMs(settle);
        Chk("settings-path-completes-collapse", crumbs.Visibility == Visibility.Collapsed);
        Go("selection", true); PumpMs(20); Go("record", true); PumpMs(20); Go("capture", true); PumpMs(settle);
        Chk("settings-path-rapid-navigation-discards-stale-height", crumbs.Visibility == Visibility.Collapsed
            && (string)Field("_currentSettingsPage")! == "capture");
        var binding = settings.Current.Gestures.Bindings[0];
        type.GetMethod("OpenSettingsBinding", flags)!.Invoke(window, new object[] { binding }); PumpMs(settle);
        var detail = bodies["binding:" + binding.Id];
        var cards = detail.Children.OfType<Border>().Select(card => card.Tag as string).ToArray();
        Chk("settings-binding-separates-input-and-behavior", cards.SequenceEqual(new[] { "settings-binding-input", "settings-binding-behavior" }));
        snapshot("shortcut-detail");

        var updateProperty = typeof(AppHost).GetProperty("Updates", flags)!;
        object? previousUpdate = updateProperty.GetValue(AppHost.Current);
        using var controller = new CaptureController(settings);
        var updateType = typeof(SettingsWindow).Assembly.GetType("NekoCapture.Updates.UpdateService")!;
        var update = (IDisposable)Activator.CreateInstance(updateType, flags, null, new object[] { settings, controller }, null)!;
        updateProperty.SetValue(AppHost.Current, update);
        try
        {
            if (bodies.Remove("updates", out var old)) ((Grid)Field("_settingsHost")!).Children.Remove(old);
            Go("updates"); PumpMs(settle);
            var stageType = typeof(SettingsWindow).Assembly.GetType("NekoCapture.Updates.UpdateStage")!;
            void Publish(string stage) => updateType.GetMethod("Publish", flags)!.Invoke(update, new[] { Enum.Parse(stageType, stage) });
            Publish("Current"); PumpMs(settle);
            var panel = bodies["updates"];
            var resultHost = Descendants(panel).OfType<Border>().Single(border => border.Tag as string == "settings-update-result");
            Publish("Idle"); PumpMs(Fluent.Motion.QuickMs / 2);
            Chk("settings-current-version-fades-with-text-intact", resultHost.IsVisible && resultHost.Opacity is > 0 and < 1
                && AllText(resultHost).Any(text => text.Text == Loc.T(K.Updates.Current)));
            PumpMs(settle);
            Chk("settings-current-version-removes-space-after-exit", resultHost.Visibility == Visibility.Collapsed);
            Publish("Current"); PumpMs(settle); Publish("Idle"); PumpMs(20); Publish("Current"); PumpMs(settle);
            Chk("settings-current-version-reentry-survives-old-exit", resultHost.IsVisible && resultHost.Opacity > 0.99,
                $"visible={resultHost.Visibility} opacity={resultHost.Opacity:F2} max={resultHost.MaxHeight:F2} height={resultHost.ActualHeight:F2}");
        }
        finally { update.Dispose(); updateProperty.SetValue(AppHost.Current, previousUpdate); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(System.Windows.Media.VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
