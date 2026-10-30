using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using FrameCapture.Core;
using FrameCapture.Overlays;
using FrameCapture.Services;

static partial class SelfTest
{
    static void BehaviorNavigationUiChecks()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var previousSync = SynchronizationContext.Current;
        var property = typeof(AppHost).GetProperty("Settings")!;
        var previous = property.GetValue(AppHost.Current);
        SettingsWindow? window = null;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var service = new SettingsService();
            var graph = new BehaviorGraph();
            var selection = graph.Add(GestureActions.Select);
            selection.Branches[BehaviorPorts.Still].Add(graph.Add(GestureActions.GrabStill).Id);
            selection.Branches[BehaviorPorts.Record].Add(graph.Add(GestureActions.RecordStart).Id);
            graph.Flows.Add(new BehaviorFlow { Steps = new() { selection.Id } });
            service.Current.Gestures.Graph = graph; service.Current.Gestures.GraphRevision = 1;
            property.SetValue(AppHost.Current, service);
            window = new SettingsWindow { Left = -20000, Top = -20000, ShowInTaskbar = false, ShowActivated = false };
            typeof(SettingsWindow).GetField("_restoreSettingsPage", flags)!.SetValue(window, "flow");
            typeof(SettingsWindow).GetMethod("Build", flags)!.Invoke(window, null);
            window.Show(); PumpMs(Fluent.Motion.SpringMs + 100);
            BehaviorNavigationChecks(window, selection);
            BehaviorMotionChecks(window, selection);
            ChainMotionChecks();
        }
        catch (Exception ex) { Chk("behavior-navigation-ui", false, ex.ToString()); }
        finally
        {
            window?.Close();
            property.SetValue(AppHost.Current, previous);
            SynchronizationContext.SetSynchronizationContext(previousSync);
        }
    }

    static void BehaviorNavigationChecks(SettingsWindow settings, BehaviorNode selection)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(SettingsWindow);
        var path = (List<string>)type.GetField("_behaviorPath", flags)!.GetValue(settings)!;
        path.Add(selection.Branches[BehaviorPorts.Still][0]);
        type.GetMethod("RenderBehaviorDetails", flags)!.Invoke(settings, null);
        var details = (StackPanel)type.GetField("_behaviorDetails", flags)!.GetValue(settings)!;
        var breadcrumb = details.Children.OfType<WrapPanel>().Single();
        breadcrumb.Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        PumpMs(Fluent.Motion.HoverMs + Fluent.Motion.ExitMs);
        Chk("behavior-breadcrumb-keeps-root-editor", path.SequenceEqual(new[] { selection.Id })
            && details.Children.OfType<TabControl>().Any() && details.Children.Count > 4);
        var tabs = details.Children.OfType<TabControl>().Single();
        Chk("behavior-branches-use-selected-tabs", tabs.Items.Count == GestureActions.PortsOf(selection.Action).Length
            && tabs.SelectedItem is TabItem { IsSelected: true });
        tabs.SelectedIndex = 1;
        tabs = details.Children.OfType<TabControl>().Single();
        Chk("behavior-branch-selection-survives-render", tabs.SelectedIndex == 1);
        tabs.SelectedIndex = 0;
        var objects = new List<DependencyObject>(); CollectTyped(details, typeof(ComboBox), objects);
        var combo = objects.OfType<ComboBox>().Single();
        combo.UpdateLayout();
        DependencyObject? hit = combo.InputHitTest(new Point(14, combo.ActualHeight / 2)) as DependencyObject;
        while (hit != null && hit is not ToggleButton) hit = VisualTreeHelper.GetParent(hit);
        if (hit is ToggleButton toggle)
            typeof(ToggleButton).GetMethod("OnClick", flags)!.Invoke(toggle, null);
        PumpMs(Fluent.Motion.EnterMs);
        Chk("combo-text-hit-opens-dropdown", hit is ToggleButton && combo.IsDropDownOpen);
        combo.IsDropDownOpen = false;
        double compact = combo.Width;
        combo.SelectedIndex = 1; PumpMs(Fluent.Motion.QuickMs);
        Chk("combo-selection-keeps-longest-option-width", compact < 110 && combo.Width == compact);
        var longer = new ComboBoxItem { Content = "Longest configurable capture behavior" };
        combo.Items.Add(longer); PumpMs(Fluent.Motion.QuickMs);
        double expanded = combo.Width;
        combo.Items.Remove(longer); PumpMs(Fluent.Motion.QuickMs);
        Chk("combo-content-changes-resize-field", expanded > compact + 80 && combo.Width == compact);
    }
}
