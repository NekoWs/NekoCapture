using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FrameCapture.Core;
using FrameCapture.Overlays;
using FrameCapture.Services;

static partial class SelfTest
{
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr UxSendMessage(IntPtr hwnd, uint message, IntPtr wp, IntPtr lp);

    private static void UxFixesChecks()
    {
        _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var defaults = new AppSettings();
        Chk("ux-defaults", defaults.Fps == 60 && defaults.SnapDwellMs == 25 && !defaults.ShowLastRegion
            && !defaults.WindowCaptureEnabled && !defaults.WindowCaptureShadow);
        var host = typeof(AppHost).GetProperty("Settings")!; var prior = host.GetValue(AppHost.Current);
        var settings = new SettingsService(); host.SetValue(AppHost.Current, settings);
        settings.Mutate(s => { s.SnapDwellMs = 50; s.ShowLastRegion = false; s.WindowCaptureEnabled = false; s.WindowCaptureShadow = false; });
        var window = new SettingsWindow();
        var type = typeof(SettingsWindow);
        object? Field(string name) => type.GetField(name, flags)!.GetValue(window);
        object? Call(string name, params object?[] args) => type.GetMethod(name, flags)!.Invoke(window, args);
        void Go(string page) { Call("NavigateSettings", page, false, true); PumpMs(40); window.UpdateLayout(); }
        try
        {
            Call("Build"); ShowOffscreen(window); PumpMs(100); Go("selection");
            var bodies = (Dictionary<string, StackPanel>)Field("_settingsBodies")!;
            var combo = Descendants(bodies["selection"]).OfType<ComboBox>().Single(c => c.Tag as string == "settings-duration-snap");
            combo.IsDropDownOpen = true;
            PumpMs(30);
            var chosen = combo.Items.Cast<ComboBoxItem>().Single(item => (double)item.Tag == 25);
            var source = Descendants(chosen).OfType<TextBlock>().FirstOrDefault() as DependencyObject ?? chosen;
            bool ownsPopupItem = (bool)type.GetMethod("SettingsInputContains", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { combo, source })!;
            Chk("ux-combo-popup-item-remains-inside-input-boundary", ownsPopupItem);
            var layer = (Canvas)combo.Template.FindName("numberRoll", combo);
            var nativeText = (TextBox)combo.Template.FindName("PART_EditableTextBox", combo);
            var snapshot = typeof(Fluent).GetMethod("ComboRollSnapshot", BindingFlags.Static | BindingFlags.NonPublic)!;
            var oldPicture = (System.Windows.Media.Imaging.BitmapSource)snapshot.Invoke(null, new object?[] { nativeText.Parent, null })!;
            combo.SelectedItem = chosen;
            var heldPicture = layer.Children.OfType<Image>().SingleOrDefault()?.Source as System.Windows.Media.Imaging.BitmapSource;
            bool SamePixels(System.Windows.Media.Imaging.BitmapSource first, System.Windows.Media.Imaging.BitmapSource second)
            {
                if (first.PixelWidth != second.PixelWidth || first.PixelHeight != second.PixelHeight) return false;
                int stride = first.PixelWidth * 4;
                var a = new byte[stride * first.PixelHeight]; var b = new byte[a.Length];
                first.CopyPixels(a, stride, 0); second.CopyPixels(b, stride, 0); return a.AsSpan().SequenceEqual(b);
            }
            Chk("ux-numeric-choice-holds-old-picture-before-deferred-animation", nativeText.Opacity == 0
                && layer.Visibility == Visibility.Visible && heldPicture is { IsFrozen: true } && SamePixels(oldPicture, heldPicture));
            combo.IsDropDownOpen = false; PumpMs(35);
            Chk("ux-numeric-choice-applies", settings.Current.SnapDwellMs == 25 && combo.Text == "25");
            var digits = layer.Children.OfType<Grid>().Select(cell => cell.Children.OfType<Canvas>().Single()).ToArray();
            Chk("ux-numeric-reels-show-continuous-digits", layer.Visibility == Visibility.Visible && digits.Length == 2
                && digits.All(tape => tape.Children.Count > 3 && ((TranslateTransform)tape.RenderTransform).HasAnimatedProperties)
                && Descendants(digits[1]).OfType<TextBlock>().Any(text => text.Text == "9"));
            combo.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, combo, "37"))
                { RoutedEvent = TextCompositionManager.PreviewTextInputEvent });
            combo.Text = "37";
            combo.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, combo, null) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            Chk("ux-numeric-typed-input-has-no-animation", settings.Current.SnapDwellMs == 37 && layer.Visibility == Visibility.Collapsed);
            var shadow = AllText(bodies["selection"]).Single(t => t.Text == Loc.T(K.Settings.IncludeTheShadowInWindow));
            Chk("ux-shadow-hidden-until-window-capture-enabled", !shadow.IsVisible);
            var bodyText = AllText(bodies["selection"]).Single(t => t.Text == Loc.T(K.Settings.ClickAWindowToCapture));
            var row = (Grid)VisualTreeHelper.GetParent(bodyText);
            void Toggle() => row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
            Toggle(); PumpMs(Fluent.Motion.EnterMs + 100);
            Chk("ux-window-capture-reveals-shadow", settings.Current.WindowCaptureEnabled && shadow.IsVisible);
            Toggle(); PumpMs(Fluent.Motion.ExitMs + 100);
            Chk("ux-window-capture-hides-shadow", !settings.Current.WindowCaptureEnabled && !shadow.IsVisible);
            Go("behaviors");
            var add = (Button)Field("_settingsAddBehavior")!;
            Chk("ux-behavior-add-shares-title-row", add.IsVisible
                && ReferenceEquals(VisualTreeHelper.GetParent(add), VisualTreeHelper.GetParent((TextBlock)Field("_settingsHeader")!)));
            var graph = new BehaviorGraph(); var select = graph.Add(GestureActions.Select);
            var still = graph.Add(GestureActions.GrabStill); var record = graph.Add(GestureActions.RecordStart);
            select.Branches[BehaviorPorts.Still].Add(still.Id); select.Branches[BehaviorPorts.Record].Add(record.Id);
            var flow = new BehaviorFlow { Steps = new() { select.Id } }; graph.Flows.Add(flow);
            var a = new GestureBindingDto { Id = Guid.NewGuid().ToString("N"), Chord = "Ctrl+Alt+A", EntryId = flow.Id };
            var b = new GestureBindingDto { Id = Guid.NewGuid().ToString("N"), Chord = "Ctrl+Alt+R", EntryId = flow.Id };
            settings.Mutate(s => { s.Gestures.Graph = graph; s.Gestures.Bindings = new() { a, b }; });
            foreach (var (binding, mode) in new[] { (a, BehaviorPorts.Still), (b, BehaviorPorts.Record) })
            {
                Call("OpenSettingsBinding", binding); PumpMs(Fluent.Motion.QuickMs + Fluent.Motion.EnterMs + 80);
                var page = bodies["binding:" + binding.Id];
                var modeRow = Descendants(page).OfType<FrameworkElement>().Single(e => e.Tag as string == "settings-binding-entry-mode");
                var choice = Descendants(modeRow).OfType<ComboBox>().Single();
                choice.SelectedItem = choice.Items.Cast<ComboBoxItem>().Single(i => i.Tag as string == mode);
            }
            Chk("ux-dual-shortcuts-created-without-preset", a.EntryMode == BehaviorPorts.Still && b.EntryMode == BehaviorPorts.Record
                && a.EntryId == b.EntryId && !select.RecordMode);
            var bindingPage = bodies["binding:" + b.Id];
            var trigger = Descendants(bindingPage).OfType<ComboBox>().Single(c => c.Items.OfType<ComboBoxItem>().Any(i => i.Tag as string == "Hold"));
            trigger.SelectedItem = trigger.Items.OfType<ComboBoxItem>().Single(i => i.Tag as string == "Hold"); PumpMs(35);
            var triggerRoll = (Canvas)trigger.Template.FindName("selectionRoll", trigger);
            Chk("ux-trigger-choice-keeps-animated-combo", b.Trigger == "Hold" && Descendants(bindingPage).Contains(trigger)
                && trigger.IsVisible && triggerRoll.Visibility == Visibility.Visible && triggerRoll.Children.Count > 1);
            PumpMs(Fluent.Motion.EnterMs + 100);
            Chk("ux-trigger-choice-shows-hold-timing", AllText(bindingPage).Any(t => t.Text == Loc.T(K.Behaviors.HoldMs) && t.IsVisible));
            var holdTiming = AllText(bindingPage).Single(t => t.Text == Loc.T(K.Behaviors.HoldMs));
            trigger.SelectedItem = trigger.Items.OfType<ComboBoxItem>().Single(i => i.Tag as string == "Tap"); PumpMs(35);
            Chk("ux-trigger-keeps-unchanged-timing-row", Descendants(bindingPage).Contains(holdTiming) && holdTiming.IsVisible);
            trigger.SelectedItem = trigger.Items.OfType<ComboBoxItem>().Single(i => i.Tag as string == "KeyUp");
            PumpMs(Fluent.Motion.EnterMs + 100);
            Chk("ux-trigger-choice-removes-timing-without-rebuild", b.Trigger == "KeyUp" && Descendants(bindingPage).Contains(trigger)
                && !AllText(bindingPage).Any(t => t.Text == Loc.T(K.Behaviors.HoldMs) && t.IsVisible));
            UxSendMessage(new System.Windows.Interop.WindowInteropHelper(window).Handle, 0x00A3, new IntPtr(2), IntPtr.Zero);
            Chk("ux-title-double-click-keeps-window-size", window.WindowState == WindowState.Normal);
            UxEditorFixesChecks(settings);
            UxCurrentControlChecks();
        }
        catch (Exception ex) { Chk("ux-fixes-exception", false, ex.ToString()); }
        finally { window.Close(); host.SetValue(AppHost.Current, prior); }
    }

    private static void UxCurrentControlChecks()
    {
        var progress = Fluent.Ui(new ProgressBar { Minimum = 0, Maximum = 100, Value = 40, Height = 4 });
        var check = Fluent.Ui(new CheckBox { Content = "", IsChecked = true });
        var window = new Window { Width = 240, Height = 100,
            Content = new StackPanel { Children = { progress, check } } };
        try
        {
            ShowOffscreen(window); PumpMs(40); window.UpdateLayout();
            var track = (FrameworkElement)progress.Template.FindName("PART_Track", progress);
            var fill = (FrameworkElement)progress.Template.FindName("PART_Indicator", progress);
            Chk("ux-current-progress-template-reflects-value", Math.Abs(fill.ActualWidth / track.ActualWidth - 0.4) < 0.01);
            progress.IsIndeterminate = true; PumpMs(40); window.UpdateLayout();
            Chk("ux-current-progress-template-shows-busy-state", fill.ActualWidth > 0 && fill.HasAnimatedProperties);
            var mark = (TextBlock)check.Template.FindName("mark", check);
            Chk("ux-current-checkbox-template-reflects-value", mark.IsVisible && check.FocusVisualStyle == null);
            check.IsChecked = false;
            Chk("ux-current-checkbox-template-clears-check", mark.Visibility == Visibility.Collapsed);
        }
        finally { window.Close(); }
    }
}
