using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FrameCapture.Core;
using FrameCapture.Overlays;

static partial class SelfTest
{
    private static void SettingsPaletteChecks(SettingsWindow window, SettingsService settings, Action<string> snapshot)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var type = typeof(SettingsWindow);
        object? Field(string name) => type.GetField(name, flags)!.GetValue(window);
        object? Call(string name, params object?[] args) => type.GetMethod(name, flags)!.Invoke(window, args);
        string Key(object part) => (string)part.GetType().GetProperty("Key")!.GetValue(part)!;
        FrameworkElement Element(object part) => (FrameworkElement)part.GetType().GetProperty("Element")!.GetValue(part)!;
        var saved = settings.Current.Clone();
        try
        {
            Call("NavigateSettings", "palette", false, true); PumpMs(40);
            var bodies = (Dictionary<string, StackPanel>)Field("_settingsBodies")!;
            var swatch = Descendants(bodies["palette"]).OfType<Border>().Single(b => b.Tag as string == "settings-accent-swatch");
            swatch.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.MouseRightButtonUpEvent });
            Chk("palette-right-click-enables-system-accent", settings.Current.Accent == "system"
                && (bool)typeof(Theme).GetField("_accentFollowsSystem", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!);
            var reset = Descendants(bodies["palette"]).OfType<Button>().Single(b => b.Tag as string == "settings-accent-reset");
            reset.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Chk("palette-accent-reset-click-restores-default", settings.Current.Accent == "" && Theme.Accent == Theme.DefaultAccent);
            Call("NavigateSettings", "colors", false, true); PumpMs(50); window.UpdateLayout();
            var parts = ((IEnumerable)Field("_paletteParts")!).Cast<object>().ToArray();
            Chk("palette-preview-exposes-every-color", Theme.Tokens.All(t => parts.Any(p => Key(p) == t.Key)) && parts.Any(p => Key(p) == "Accent"));
            var stage = (Grid)Field("_paletteStage")!;
            double previewHeight = stage.ActualHeight;
            var wholeWindow = parts.First(p => Key(p) == "WindowBg");
            for (int i = 0; i < 8; i++) { Call("SelectPalettePart", wholeWindow); window.UpdateLayout(); }
            Chk("palette-window-highlight-does-not-grow-layout", Math.Abs(stage.ActualHeight - previewHeight) < 0.5
                && VisualTreeHelper.GetParent((Border)Field("_paletteHighlight")!) is Canvas);
            foreach (string key in new[] { "Stroke", "StrokeSoft", "StrokeField" })
            {
                object part = parts.First(p => Key(p) == key);
                var box = (Rect)Call("PalettePartBounds", part)!;
                var hit = Call("HitPalettePart", new Point(box.Left + 4, box.Top + box.Height / 2), stage);
                Chk("palette-expanded-edge-hit-" + key, hit != null && Key(hit) == key);
            }
            var dark = Descendants(bodies["colors"]).OfType<Button>().Single(b => b.Tag as string == "palette-mode-dark");
            var light = Descendants(bodies["colors"]).OfType<Button>().Single(b => b.Tag as string == "palette-mode-light");
            string actualTheme = settings.Current.Theme; bool actualDark = Theme.IsDark;
            dark.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); PumpMs(Fluent.Motion.QuickMs + 20); snapshot("colors-dark");
            light.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); PumpMs(Fluent.Motion.QuickMs + 20); snapshot("colors-light");
            Chk("palette-preview-mode-does-not-change-app-theme", !((bool)Field("_palettePreviewDark")!) && settings.Current.Theme == actualTheme && Theme.IsDark == actualDark);
            var cardPart = parts.First(p => Key(p) == "CardBg");
            Call("SelectPalettePart", cardPart);
            var highlight = (Border)Field("_paletteHighlight")!;
            Chk("palette-hover-highlights-selected-surface", highlight.Visibility == Visibility.Visible && highlight.Width > 20);
            var originalDark = Theme.ValueOf("CardBg", true);
            var target = Element(cardPart);
            Call("ClosePalettePickers");
            Call("BeginPalettePointer", cardPart);
            Chk("palette-press-does-not-open-picker", Field("_palPop") is not Popup { IsOpen: true });
            Call("EndPalettePointer", cardPart); PumpMs(30);
            var clickPopup = (Popup)Field("_palPop")!;
            Chk("palette-completed-click-keeps-picker-open", clickPopup.IsOpen && !clickPopup.StaysOpen);
            var root = (UIElement)PresentationSource.FromVisual(clickPopup.Child)!.RootVisual;
            root.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
            PumpMs(Fluent.Motion.EnterMs + 100);
            Chk("palette-outside-pointer-dismisses-picker", !clickPopup.IsOpen);
            Call("ClosePalettePickers");
            Call("BeginPalettePointer", cardPart); Call("EndPalettePointer", new object?[] { null }); PumpMs(30);
            Chk("palette-drag-away-does-not-open-picker", Field("_palPop") is not Popup { IsOpen: true });
            Call("OpenPaletteAccent", target);
            var accentPopup = (Popup)Field("_accentPop")!;
            Chk("palette-accent-picker-is-armed-for-outside-click", accentPopup.IsOpen && !accentPopup.StaysOpen);
            typeof(Window).GetMethod("OnDeactivated", flags)!.Invoke(window, new object[] { EventArgs.Empty });
            Chk("palette-owner-deactivation-dismisses-picker", !accentPopup.IsOpen);
            Call("OpenSwatchPop", target, "CardBg", false);
            var picker = (ColorPickView)Field("_palView")!;
            var custom = Color.FromRgb(0xED, 0xD5, 0xB8); picker.Color = custom; Call("PreviewSwatch", custom);
            Chk("palette-picker-updates-preview-live", Theme.ValueOf("CardBg", false) == custom
                && ((SolidColorBrush)((Border)target).Background).Color == custom && Theme.ValueOf("CardBg", true) == originalDark);
            Call("ClosePalettePickers"); PumpMs(20);
            Chk("palette-picker-persists-only-selected-side", settings.Current.Colors.GetValueOrDefault("light.CardBg") == HexColor.Format(custom),
                $"stored={settings.Current.Colors.GetValueOrDefault("light.CardBg")} picker={HexColor.Format(picker.Color)} expected={HexColor.Format(custom)}");
            Call("ResetPaletteSelection");
            Chk("palette-single-color-reset-preserves-other-side", !Theme.IsCustom("CardBg", false) && Theme.ValueOf("CardBg", true) == originalDark
                && !settings.Current.Colors.ContainsKey("light.CardBg"));
            string preset = settings.Current.ColorPreset;
            Call("OpenSwatchPop", target, "CardBg", false); ((Popup)Field("_palPop")!).IsOpen = false;
            Chk("palette-unchanged-picker-keeps-settings", !settings.Current.Colors.ContainsKey("light.CardBg") && settings.Current.ColorPreset == preset);
            Call("OpenSwatchPop", target, "CardBg", false);
            picker = (ColorPickView)Field("_palView")!; picker.Color = custom; Call("PreviewSwatch", custom);
            ((Popup)Field("_palPop")!).IsOpen = false; PumpMs(Fluent.Motion.EnterMs + 100);
            Chk("palette-dismissed-picker-commits-after-fade", settings.Current.Colors.GetValueOrDefault("light.CardBg") == HexColor.Format(custom));
            Call("ResetPaletteSelection"); PumpMs(Fluent.Motion.EnterMs + 100);
            Chk("palette-reset-survives-delayed-popup-close", !settings.Current.Colors.ContainsKey("light.CardBg"));
            snapshot("colors-selected");
        }
        finally
        {
            Call("ClosePalettePickers");
            settings.Mutate(s => { s.Accent = saved.Accent; s.Colors = saved.Colors; s.ColorPreset = saved.ColorPreset; });
            settings.Current.ApplyPalette();
        }
    }
}
