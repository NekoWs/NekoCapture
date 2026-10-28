using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameCapture.Core;
using FrameCapture.Overlays;
using FrameCapture.Services;

static partial class SelfTest
{
    static void BehaviorMotionChecks(SettingsWindow settings, BehaviorNode selection)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(SettingsWindow);
        T Field<T>(string name) => (T)type.GetField(name, flags)!.GetValue(settings)!;
        PumpMs(Fluent.Motion.SpringMs + 80);
        var tabs = Field<TabControl>("_behaviorBranchTabs");
        var indicator = (Border)tabs.Template.FindName("selectionIndicator", tabs);
        double start = ((TranslateTransform)indicator.RenderTransform).X;
        tabs.SelectedIndex = 1; PumpMs(35);
        double middle = ((TranslateTransform)indicator.RenderTransform).X;
        var target = ((TabItem)tabs.SelectedItem).TranslatePoint(new Point(10, 0), tabs).X;
        var branch = Field<StackPanel>("_behaviorBranchContent");
        Chk("behavior-tab-indicator-glides", ReferenceEquals(tabs, Field<TabControl>("_behaviorBranchTabs"))
            && middle > Math.Min(start, target) && middle < Math.Max(start, target));
        Chk("behavior-tab-content-crossfades", branch.Children.OfType<FrameworkElement>().Any(child => child.Opacity is > 0 and < 1)
            && AdornerLayer.GetAdornerLayer(branch)?.GetAdorners(branch)?.Length > 0);
        foreach (int index in new[] { 3, 0, 2, 1 }) tabs.SelectedIndex = index;
        PumpMs(Fluent.Motion.SpringMs + 100);
        Chk("behavior-rapid-tab-switch-settles", tabs.SelectedIndex == 1 && double.IsNaN(branch.Height)
            && branch.Children.OfType<FrameworkElement>().All(child => child.Opacity > 0.99)
            && (AdornerLayer.GetAdornerLayer(branch)?.GetAdorners(branch)?.Length ?? 0) == 0);
        tabs.SelectedIndex = 0; PumpMs(Fluent.Motion.EnterMs + 50);
        var path = Field<List<string>>("_behaviorPath");
        path.Add(selection.Branches[BehaviorPorts.Still][0]);
        type.GetMethod("RenderBehaviorDetails", flags)!.Invoke(settings, null); PumpMs(35);
        var details = Field<StackPanel>("_behaviorDetails");
        Chk("behavior-enter-branch-animates", details.Children.OfType<FrameworkElement>().Any(child => child.Opacity is > 0 and < 1));
        type.GetMethod("NavigateBehaviorPath", flags)!.Invoke(settings, new object[] { 1 });
        PumpMs(Fluent.Motion.SpringMs + 100);
        Chk("behavior-branch-return-releases-height", path.SequenceEqual(new[] { selection.Id }) && double.IsNaN(details.Height)
            && details.Children.OfType<TabControl>().Any());
        var graph = AppHost.Current.Settings.Current.Gestures.Graph;
        var added = new BehaviorFlow(); graph.Flows.Add(added);
        type.GetField("_behaviorFlow", flags)!.SetValue(settings, added.Id);
        type.GetMethod("RenderBehaviors", flags)!.Invoke(settings, null); PumpMs(35);
        var content = Field<StackPanel>("_behaviorContent");
        Chk("behavior-new-card-animates", content.Children.OfType<FrameworkElement>().Any(child => child.Opacity is > 0 and < 1));
        graph.Flows.Remove(added);
        type.GetMethod("RenderBehaviors", flags)!.Invoke(settings, null); PumpMs(35);
        Chk("behavior-delete-card-keeps-exit-snapshot", AdornerLayer.GetAdornerLayer(content)?.GetAdorners(content)?.Length > 0);
        PumpMs(Fluent.Motion.SpringMs + 100);
        Chk("behavior-card-replacement-settles", double.IsNaN(content.Height)
            && (AdornerLayer.GetAdornerLayer(content)?.GetAdorners(content)?.Length ?? 0) == 0);
    }

    static void ChainMotionChecks()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(SettingsWindow).GetNestedType("ChainEditor", BindingFlags.NonPublic)!;
        var steps = new List<string> { GestureActions.Select, GestureActions.GrabStill, GestureActions.CopyLast };
        var editor = type.GetConstructor(new[] { typeof(Func<string[]>), typeof(Func<int, IEnumerable<GestureAction>>), typeof(Action<string[]>) })!
            .Invoke(new object[] { (Func<string[]>)(() => steps.ToArray()),
                (Func<int, IEnumerable<GestureAction>>)(_ => GestureActions.All), (Action<string[]>)(values => steps = values.ToList()) });
        var root = (StackPanel)type.GetProperty("Root")!.GetValue(editor)!;
        var window = new Window { Content = new AdornerDecorator { Child = root }, Width = 440, Height = 180,
            Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show(); PumpMs(100);
            void Render() => type.GetMethod("Render")!.Invoke(editor, null);
            var chips = (WrapPanel)type.GetField("_chips", flags)!.GetValue(editor)!;
            Chk("behavior-cards-have-pointer-cursor", chips.Children.OfType<System.Windows.Controls.Border>()
                .All(chip => chip.Cursor == System.Windows.Input.Cursors.Hand));
            var snapshot = (BitmapSource)typeof(SettingsWindow).GetMethod("CaptureSettingsVisual", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { chips.Children[1] })!;
            var center = new byte[4];
            snapshot.CopyPixels(new Int32Rect(snapshot.PixelWidth / 2, snapshot.PixelHeight / 2, 1, 1), center, 4, 0);
            Chk("chain-exit-snapshot-preserves-local-pixels", center[3] > 200);
            steps.Add(GestureActions.NotifyToast); Render(); PumpMs(35);
            var added = (Border)chips.Children[3];
            var scale = ((TransformGroup)added.RenderTransform).Children.OfType<ScaleTransform>().Single();
            Chk("chain-new-chip-fades-and-springs", added.Opacity is > 0 and < 1 && scale.ScaleX < 1);
            steps.RemoveAt(1); Render(); PumpMs(35);
            Chk("chain-delete-chip-animates-exit-and-layout", AdornerLayer.GetAdornerLayer(root)?.GetAdorners(root)?.Length > 0
                && chips.Children.OfType<FrameworkElement>().Any(chip => chip.RenderTransform is TransformGroup group
                    && group.Children.OfType<TranslateTransform>().Any(shift => Math.Abs(shift.X) > 1)));
            PumpMs(Fluent.Motion.SpringMs + 100);
            Chk("chain-chip-animation-releases-layout", double.IsNaN(root.Height)
                && (AdornerLayer.GetAdornerLayer(root)?.GetAdorners(root)?.Length ?? 0) == 0);
            type.GetMethod("OpenPicker", flags)!.Invoke(editor, null);
            type.GetMethod("ClosePicker", flags)!.Invoke(editor, null);
            type.GetMethod("OpenPicker", flags)!.Invoke(editor, null);
            PumpMs(Fluent.Motion.EnterMs + 100);
            var popup = (Popup)type.GetField("_pop", flags)!.GetValue(editor)!;
            Chk("chain-picker-reopen-survives-stale-close", popup.IsOpen && popup.Child != null);
            type.GetMethod("ClosePicker", flags)!.Invoke(editor, null); PumpMs(Fluent.Motion.ExitMs + 50);
        }
        finally { window.Close(); }
    }
}
