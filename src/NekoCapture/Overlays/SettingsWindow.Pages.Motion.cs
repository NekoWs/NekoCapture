using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using NekoCapture.Core;

namespace NekoCapture.Overlays;

/// <summary>分页的快速退场、模块错峰入场与中断清理。</summary>
public sealed partial class SettingsWindow
{
    int _pageMotionGeneration;
    readonly List<(FrameworkElement Element, Transform? Prior)> _pageMotionModules = new();

    void ResetPageModules()
    {
        foreach (var (element, prior) in _pageMotionModules)
        {
            element.BeginAnimation(OpacityProperty, null); element.Opacity = 1;
            element.RenderTransform = prior;
        }
        _pageMotionModules.Clear();
    }

    void HoldPageModules()
    {
        foreach (var (element, _) in _pageMotionModules)
        {
            double opacity = element.Opacity;
            element.BeginAnimation(OpacityProperty, null); element.Opacity = opacity;
            element.RenderTransform = element.RenderTransform?.CloneCurrentValue();
        }
    }

    static FrameworkElement[] PageModules(Panel page)
    {
        var modules = new List<FrameworkElement>();
        void Add(FrameworkElement element)
        {
            if (element.Visibility != Visibility.Visible) return;
            if (element is StackPanel stack)
            {
                foreach (var child in stack.Children.OfType<FrameworkElement>()) Add(child);
            }
            else if (element is Grid { Tag: "settings-preset-grid" } grid)
            {
                foreach (var child in grid.Children.OfType<FrameworkElement>().OrderBy(Grid.GetRow).ThenBy(Grid.GetColumn)) Add(child);
            }
            else modules.Add(element);
        }
        var children = page.Children.OfType<FrameworkElement>().Where(child => child.Visibility == Visibility.Visible).ToArray();
        if (children is [Border { Child: StackPanel rows }])
            foreach (var row in rows.Children.OfType<FrameworkElement>()) Add(row);
        else foreach (var child in children) Add(child);
        return modules.ToArray();
    }

    void SwitchSettingsPage(IReadOnlyList<StackPanel> pages, int index, bool animate,
        ScrollViewer? scroller, TextBlock header, string title, double scrollOffset = 0, Action? prepareChrome = null)
    {
        int generation = ++_pageMotionGeneration;
        var incoming = pages[index];
        var outgoing = pages.FirstOrDefault(page => page.IsVisible && !ReferenceEquals(page, incoming));
        double outgoingOpacity = outgoing?.Opacity ?? 1;
        double outgoingShift = (outgoing?.RenderTransform as TranslateTransform)?.Y ?? 0;
        double headerOpacity = header.Opacity;
        foreach (var page in pages)
        {
            CancelContentTransitions(page);
            if (!ReferenceEquals(page, outgoing)) page.Visibility = Visibility.Collapsed;
            page.BeginAnimation(OpacityProperty, null);
            page.RenderTransform = null;
            page.IsHitTestVisible = false;
        }
        HoldPageModules();
        if (outgoing != null) outgoing.Opacity = outgoingOpacity;
        header.BeginAnimation(OpacityProperty, null);
        header.Opacity = headerOpacity;

        void Reveal()
        {
            if (generation != _pageMotionGeneration) return;
            foreach (var page in pages)
            {
                page.Visibility = Visibility.Collapsed;
                page.BeginAnimation(OpacityProperty, null); page.Opacity = 1;
                page.RenderTransform = null;
            }
            ResetPageModules();
            prepareChrome?.Invoke();
            scroller?.ScrollToVerticalOffset(scrollOffset);
            Dispatcher.BeginInvoke(() =>
            {
                if (generation == _pageMotionGeneration) scroller?.ScrollToVerticalOffset(scrollOffset);
            }, System.Windows.Threading.DispatcherPriority.Loaded);
            header.Text = title; header.Opacity = 1;
            incoming.IsHitTestVisible = true;
            if (!animate) { incoming.Visibility = Visibility.Visible; return; }
            incoming.Opacity = 0;
            var modules = PageModules(incoming);
            foreach (var module in modules) _pageMotionModules.Add((module, module.RenderTransform));
            Fluent.CascadeIn(modules, risePx: 10, itemMs: Fluent.Motion.EnterMs,
                budgetMs: Fluent.Motion.EnterMs + Fluent.Motion.StaggerMs * 3);
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(Fluent.Motion.QuickMs));
            incoming.BeginAnimation(OpacityProperty, fade);
            incoming.Visibility = Visibility.Visible;
            header.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1,
                TimeSpan.FromMilliseconds(Fluent.Motion.QuickMs)));
        }

        if (!animate || outgoing == null) { Reveal(); return; }
        ExitSettingsBreadcrumbs();
        var shift = new TranslateTransform(); outgoing.RenderTransform = shift;
        var duration = TimeSpan.FromMilliseconds(Fluent.Motion.QuickMs);
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(outgoingShift, -6, duration)
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        var exit = new DoubleAnimation(outgoing.Opacity, 0, duration)
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        exit.Completed += (_, _) => Reveal();
        outgoing.BeginAnimation(OpacityProperty, exit);
        header.BeginAnimation(OpacityProperty, new DoubleAnimation(header.Opacity, 0, duration));
    }
}
