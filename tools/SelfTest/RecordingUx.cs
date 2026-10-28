using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Overlays;
using FrameCapture.Services;
using GM = GestureCore.ModifierKeys;

static partial class SelfTest
{
    static void PresetFlickerChecks(SettingsWindow window, Dictionary<string, Button> cards)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(SettingsWindow).GetMethod("GotoPage", flags)!.Invoke(window, new object[] { "hotkeys" });
        PumpMs(Fluent.Motion.SpringMs + 150);
        var templates = cards.ToDictionary(pair => pair.Key, pair => pair.Value.Template);
        var list = (StackPanel)typeof(SettingsWindow).GetField("_hotList", flags)!.GetValue(window)!;
        bool opaque = true;
        bool hiddenClean = true;
        foreach (var id in new[] { BehaviorPresets.Dual, BehaviorPresets.Single, BehaviorPresets.Separate, BehaviorPresets.Dual })
        {
            cards[id].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); PumpMs(25);
            opaque &= list.IsVisible && list.Children.OfType<FrameworkElement>().All(row => row.Opacity > 0.99);
            var hidden = (StackPanel)typeof(SettingsWindow).GetField("_behaviorContent", flags)!.GetValue(window)!;
            hiddenClean &= !hidden.IsVisible && double.IsNaN(hidden.Height)
                && (AdornerLayer.GetAdornerLayer(hidden)?.GetAdorners(hidden)?.Length ?? 0) == 0;
        }
        Chk("preset-switch-keeps-card-templates", cards.All(pair => ReferenceEquals(pair.Value.Template, templates[pair.Key])));
        Chk("preset-switch-keeps-binding-cards-opaque", opaque);
        Chk("preset-switch-does-not-animate-hidden-behavior-page", hiddenClean);
        PumpMs(Fluent.Motion.SpringMs + 100);
        Chk("preset-switch-has-no-ghost-and-releases-height", list.ClipToBounds && double.IsNaN(list.Height)
            && (AdornerLayer.GetAdornerLayer(list)?.GetAdorners(list)?.Length ?? 0) == 0);
        string theme = Theme.ModeName;
        try
        {
            Theme.Apply("dark", "");
            Theme.Apply("light", ""); Fluent.ApplyGlobalStyles(); PumpMs(Fluent.Motion.StateMs + 100);
            Chk("preset-theme-refreshes-owned-backgrounds", cards.All(pair => ((SolidColorBrush)pair.Value.Background).Color
                == (pair.Key == BehaviorPresets.Dual ? Fluent.AccentOn(Theme.CardBg, 0.14) : Theme.CardBg)));
            var rows = (Dictionary<string, FrameworkElement>)typeof(SettingsWindow).GetField("_hotRows", flags)!.GetValue(window)!;
            Chk("theme-refreshes-existing-hotkey-row-backgrounds", rows.Count > 0 && rows.Values.Cast<Border>()
                .All(row => ((SolidColorBrush)((Border)((StackPanel)row.Child).Children[0]).Background).Color == Theme.RowBg));
            var keyBoxes = (Dictionary<string, ChordCaptureBox>)typeof(SettingsWindow).GetField("_hotBoxes", flags)!.GetValue(window)!;
            var keyTexts = new List<DependencyObject>();
            foreach (var box in keyBoxes.Values) CollectTyped(box, typeof(TextBlock), keyTexts);
            Chk("theme-refreshes-existing-keycap-ink", keyTexts.Count > 0 && keyTexts.Cast<TextBlock>()
                .All(text => ((SolidColorBrush)text.Foreground).Color == Theme.Text));
            Chk("preset-focus-uses-rounded-themed-outline", Application.Current.Resources[SystemParameters.FocusVisualStyleKey] is Style style
                && style.Setters.OfType<Setter>().First(s => s.Property == Control.TemplateProperty).Value is ControlTemplate template
                && template.LoadContent() is Border { CornerRadius.TopLeft: 10, BorderThickness.Left: 2 } border
                && ((SolidColorBrush)border.BorderBrush).Color == Theme.Accent);
            var viewers = new List<DependencyObject>(); CollectTyped(window, typeof(ScrollViewer), viewers);
            Chk("settings-content-scroller-does-not-take-keyboard-focus", viewers.OfType<ScrollViewer>()
                .Where(viewer => viewer.Content is Grid).All(viewer => !viewer.Focusable));
        }
        finally { Theme.Apply(theme, ""); Fluent.ApplyGlobalStyles(); }
    }

    static void RecordingReadyChecks(SettingsService service)
    {
        string confirm = service.Current.PickerConfirmChord, cancel = service.Current.PickerCancelChord;
        var previous = HotkeyManager.ArmedRecordKey;
        var hotkeysProperty = typeof(AppHost).GetProperty("Hotkeys")!;
        var previousHotkeys = hotkeysProperty.GetValue(AppHost.Current);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        try
        {
            hotkeysProperty.SetValue(AppHost.Current, null);
            service.Current.PickerConfirmChord = "Ctrl+Enter"; service.Current.PickerCancelChord = "Shift+Esc";
            var monitor = Monitors.All().First();
            var region = new RectI(monitor.Bounds.X + 150, monitor.Bounds.Y + 150, 480, 320);
            int started = 0, stopped = 0;
            RecordPanelWindow.ShowArmed(region, monitor.DeviceName,
                () => { started++; RecordPanelWindow.Current!.SwitchToRecording(); }, () => stopped++);
            PumpMs(Fluent.Motion.EnterMs + 100);
            var panel = RecordPanelWindow.Current!;
            var texts = new List<DependencyObject>(); CollectTyped(panel, typeof(TextBlock), texts);
            var visible = texts.OfType<TextBlock>().Where(text => text.IsVisible).Select(text => text.Text).ToList();
            Chk("record-ready-shows-status-region-without-hint-paragraphs", visible.Contains(Loc.T(K.Capture.RecordReady))
                && visible.Contains(Loc.F(K.Capture.Region, 480, 320)) && visible.Count == 4);
            var startButton = (Border)typeof(RecordPanelWindow).GetField("_start", flags)!.GetValue(panel)!;
            var stopButton = (Border)typeof(RecordPanelWindow).GetField("_stop", flags)!.GetValue(panel)!;
            Chk("record-ready-configured-keys-in-tooltips", (string)startButton.ToolTip == Loc.F(K.Capture.RecordStartKey, "Ctrl+Enter")
                && (string)stopButton.ToolTip == Loc.F(K.Capture.RecordCancelKey, "Shift+Esc"));
            var work = (Rect)typeof(RecordPanelWindow).GetField("_workDip", flags)!.GetValue(panel)!;
            var boundsMethod = typeof(RecordPanelWindow).GetMethod("PanelBounds", BindingFlags.Static | BindingFlags.NonPublic)!;
            var regions = new[] { new Rect(work.Left, work.Top, 120, 100), new Rect(work.Right - 120, work.Top, 120, 100),
                new Rect(work.Left, work.Bottom - 100, 120, 100), new Rect(work.Right - 120, work.Bottom - 100, 120, 100) };
            Chk("record-ready-placement-fits-work-area", regions.All(selected => work.Contains((Rect)boundsMethod.Invoke(null,
                new object[] { selected, work, new Size(panel.Width, panel.Height) })!)));
            var callback = HotkeyManager.ArmedRecordKey!;
            Chk("record-ready-preserves-unrelated-keys", !callback(0x0D, GM.None) && !callback(0x41, GM.Control));
            var heights = new List<double>();
            var managedHeights = new List<double>();
            var panelHwnd = new System.Windows.Interop.WindowInteropHelper(panel).Handle;
            EventHandler frame = (_, _) =>
            {
                if (started == 0) return;
                GetWindowRect(panelHwnd, out var rectangle);
                heights.Add((rectangle.B - rectangle.T) / VisualTreeHelper.GetDpi(panel).DpiScaleY);
                managedHeights.Add(panel.ActualHeight);
            };
            CompositionTarget.Rendering += frame;
            try { callback(0x0D, GM.Control); callback(0x0D, GM.Control); PumpMs(Fluent.Motion.StateMs + 100); }
            finally { CompositionTarget.Rendering -= frame; }
            Chk("record-ready-confirm-starts-once", started == 1 && RecordPanelWindow.Current == panel);
            Chk("record-active-releases-ready-key-session", HotkeyManager.ArmedRecordKey == null
                && ((FrameworkElement)typeof(RecordPanelWindow).GetField("_readyDetails", flags)!.GetValue(panel)!).Visibility == Visibility.Collapsed);
            double activeHeight = panel.Height;
            Chk("record-active-window-height-has-no-size-animation", !DependencyPropertyHelper.GetValueSource(panel, Window.HeightProperty).IsAnimated
                && Math.Abs(activeHeight - ((Border)typeof(RecordPanelWindow).GetField("_card", flags)!.GetValue(panel)!).DesiredSize.Height) < 1,
                $"height={activeHeight}, card={((Border)typeof(RecordPanelWindow).GetField("_card", flags)!.GetValue(panel)!).DesiredSize.Height}, animated={DependencyPropertyHelper.GetValueSource(panel, Window.HeightProperty).IsAnimated}");
            PumpMs(Fluent.Motion.StateMs + 50);
            Chk("record-active-window-keeps-correct-height", Math.Abs(panel.Height - activeHeight) < 0.1);
            Chk("record-active-height-is-correct-from-first-render", heights.Count > 0
                && heights.Concat(managedHeights).All(height => Math.Abs(height - activeHeight) < 1),
                $"expected={activeHeight}, native={string.Join(',', heights)}, managed={string.Join(',', managedHeights)}");
            RecordPanelWindow.Dismiss();
            RecordPanelWindow.ShowArmed(region, monitor.DeviceName, () => started++, () => stopped++);
            HotkeyManager.ArmedRecordKey!(0x1B, GM.Shift); PumpMs(80);
            Chk("record-ready-cancel-closes-without-start", stopped == 1 && started == 1 && RecordPanelWindow.Current == null
                && HotkeyManager.ArmedRecordKey == null);
            using var manager = new HotkeyManager();
            var route = typeof(HotkeyManager).GetMethod("RouteArmedRecordKey", flags)!;
            bool Route(bool down) => (bool)route.Invoke(manager, new object[] { (uint)0x0D, down, GM.None })!;
            int claims = 0; HotkeyManager.ArmedRecordKey = (_, _) => { claims++; return true; };
            bool down = Route(true), repeat = Route(true); HotkeyManager.ArmedRecordKey = null;
            bool paired = down && repeat && claims == 1 && Route(false) && !Route(false);
            var sink = HotkeyManager.CaptureSink;
            var packet = Marshal.AllocHGlobal(24);
            try
            {
                HotkeyManager.ArmedRecordKey = (_, _) => true; Route(true); HotkeyManager.ArmedRecordKey = null;
                Marshal.Copy(new byte[24], 0, packet, 24); Marshal.WriteInt32(packet, 0, 0x0D); Marshal.WriteInt32(packet, 8, 0x80);
                HotkeyManager.CaptureSink = (_, _, _, _) => true;
                typeof(HotkeyManager).GetMethod("HandleKey", flags)!.Invoke(manager, new object[] { packet });
            }
            finally { HotkeyManager.CaptureSink = sink; Marshal.FreeHGlobal(packet); }
            Chk("record-ready-swallows-repeat-and-paired-release", paired && !Route(false));
        }
        finally
        {
            RecordPanelWindow.Dismiss(); HotkeyManager.ArmedRecordKey = previous;
            hotkeysProperty.SetValue(AppHost.Current, previousHotkeys);
            service.Current.PickerConfirmChord = confirm; service.Current.PickerCancelChord = cancel;
        }
    }
}
