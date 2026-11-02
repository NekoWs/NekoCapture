using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using NekoCapture.Core;

namespace NekoCapture.Overlays;

/// <summary>页内分页条：只有文字，选中那一枚用一条滑动的强调色下划线标出来，不铺底色。
/// 与界面语言那条分段控件刻意不同：那条是「一条轨道加一枚实心胶囊」，这条是「裸文字加下划线」，
/// 两者摆在同一页上也不会被当成同一种控件。</summary>
public sealed partial class SettingsWindow
{
    /// <summary>分页条单行占的高度：一枚按钮加下边距。</summary>
    private const double TabStripHeight = 34;

    /// <summary>相邻两行的纵向间距：按钮 26 高，上下各留 2 像素。</summary>
    private const double TabRowPitch = 30;

    private readonly List<TabChip> _settingsTabChips = new();
    private List<string> _settingsTabIds = new();
    private string _paintedTabSelection = "";
    private double _tabStripApplied = TabStripHeight;
    private bool _tabIndicatorSliding;
    private string _tabIndicatorFor = "";
    private bool _tabPlacementPending;
    private WrapPanel _settingsTabs = null!;
    private Grid _settingsTabCanvas = null!;
    private Border _settingsTabIndicator = null!;
    private TranslateTransform _settingsTabIndicatorShift = null!;
    private Border _settingsTabHost = null!;

    /// <summary>一枚分段：裸文字加自己的墨色画刷，选中态由下划线表达。</summary>
    /// <param name="Id">这一枚对应的页标识。</param>
    /// <param name="Tab">可点的按钮本体。</param>
    /// <param name="Label">页名。</param>
    /// <param name="Ink">字色画刷，悬停与选中之间渐变。</param>
    private sealed record TabChip(string Id, Border Tab, TextBlock Label, SolidColorBrush Ink)
    {
        /// <summary>指针是否停在上面。</summary>
        public bool Hovered { get; set; }
    }

    /// <summary>当前该显示哪几枚分段：单页分组、详情页与搜索都不显示，交给标题与返回路径说明位置。</summary>
    /// <returns>按声明顺序排列的页标识；空表示整条收起。</returns>
    private List<string> SettingsTabIds()
    {
        if (SettingsSearchActive || IsDetailPage(_currentSettingsPage)) return new List<string>();
        string root = _settingsPages.TryGetValue(_currentSettingsPage, out var page) ? page.Root : "";
        if (!_settingsTabsOf.TryGetValue(root, out var ids) || ids.Count <= 1) return new List<string>();
        return ids;
    }

    /// <summary>按当前页刷新分页条：分组换了才重建按钮，同一分组内只让下划线滑到新位置。</summary>
    /// <param name="animate">false = 直接就位，用于改配色重建与首帧。</param>
    private void UpdateSettingsTabs(bool animate)
    {
        if (_settingsTabs == null || _settingsTabHost == null) return;
        var ids = SettingsTabIds();
        _tabPlacementPending = true;
        bool rebuilt = !ids.SequenceEqual(_settingsTabIds);
        if (rebuilt) BuildSettingsTabs(ids);
        bool strip = ids.Count > 1;
        // 重建那一帧按钮还没排版：字色直接就位，免得在淡入里再走一遍渐变
        PaintSettingsTabs(rebuilt ? false : animate);
        AnimateSettingsTabStrip(strip, animate);
        if (rebuilt) RevealSettingsTabs(strip, animate);
        PlaceTabIndicator(animate && !rebuilt);
    }

    /// <summary>重建按钮：每枚自己持有字色画刷，事件只改状态、不换画刷实例。</summary>
    /// <param name="ids">这一组要显示的页标识。</param>
    private void BuildSettingsTabs(List<string> ids)
    {
        _settingsTabs.Children.Clear();
        _settingsTabChips.Clear();
        _settingsTabIds = ids;
        foreach (string id in ids)
        {
            if (!_settingsPages.TryGetValue(id, out var page)) continue;
            var ink = new SolidColorBrush(Theme.TextDim);
            var label = new TextBlock
            {
                Text = page.Label, FontSize = 12.5, Foreground = ink, VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center, Tag = "settings-tab-label",
            };
            var tab = new Border
            {
                Child = label, Height = 26, CornerRadius = new CornerRadius(5),
                Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(0, 2, 2, 2),
                Tag = "settings-tab", Focusable = true, Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
            };
            var chip = new TabChip(id, tab, label, ink);
            void Open() { if (id != _currentSettingsPage) NavigateSettings(id, true); }
            tab.MouseLeftButtonUp += (_, e) => { Open(); e.Handled = true; };
            tab.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { Open(); e.Handled = true; } };
            tab.MouseEnter += (_, _) => { chip.Hovered = true; PaintSettingsTabs(true); };
            tab.MouseLeave += (_, _) => { chip.Hovered = false; PaintSettingsTabs(true); };
            System.Windows.Automation.AutomationProperties.SetName(tab, page.Label);
            _settingsTabChips.Add(chip);
            _settingsTabs.Children.Add(tab);
        }
    }

    /// <summary>刷字色：选中的与指针停着的写正文色，其余写灰字。分页条没有底色，字色变化不区分时机。</summary>
    /// <param name="animate">false = 颜色直接就位。</param>
    private void PaintSettingsTabs(bool animate)
    {
        _paintedTabSelection = _currentSettingsPage;
        int ms = animate ? Fluent.Motion.QuickMs : 0;
        foreach (var chip in _settingsTabChips)
        {
            bool lit = chip.Id == _currentSettingsPage || chip.Hovered;
            TintColor(chip.Ink, lit ? Theme.Text : Theme.TextDim, ms);
        }
        if (_settingsTabIndicator != null) _settingsTabIndicator.Background = Theme.AccentBrush;
    }

    /// <summary>把一枚画刷渐变到目标色。动画攥着颜色属性时读到的是当前帧，
    /// 先读再交还属性才能从中途接着走，而不是先跳回底值再渐变。</summary>
    /// <param name="brush">目标画刷。</param>
    /// <param name="to">终点色。</param>
    /// <param name="ms">渐变时长；0 表示立刻就位。</param>
    private static void TintColor(SolidColorBrush brush, Color to, int ms)
    {
        Color from = brush.Color;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        if (ms <= 0) { brush.Color = to; return; }
        brush.Color = from;
        brush.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(to, new Duration(TimeSpan.FromMilliseconds(ms))));
    }

    /// <summary>把下划线摆到选中那一枚的文字下面：横纵位置与宽度一起走，换行时跟着换行。</summary>
    /// <param name="animate">true = 从当前位置滑到新位置；false = 直接就位。</param>
    private void PlaceTabIndicator(bool animate)
    {
        if (_settingsTabIndicator == null || _settingsTabCanvas == null) return;
        var chip = _settingsTabChips.FirstOrDefault(item => item.Id == _currentSettingsPage);
        if (chip == null || chip.Label.ActualWidth <= 0) return;
        var at = chip.Label.TranslatePoint(new Point(0, 0), _settingsTabCanvas);
        var tab = chip.Tab.TranslatePoint(new Point(0, 0), _settingsTabCanvas);
        double width = chip.Label.ActualWidth;
        _tabIndicatorFor = chip.Id;
        _tabPlacementPending = false;
        double top = tab.Y + chip.Tab.ActualHeight - 4;   // 文字下沿再落 4 像素，不压到字
        _settingsTabIndicator.BeginAnimation(WidthProperty, null);
        _settingsTabIndicatorShift.BeginAnimation(TranslateTransform.XProperty, null);
        _settingsTabIndicatorShift.BeginAnimation(TranslateTransform.YProperty, null);
        if (!animate || !_settingsTabIndicator.IsLoaded)
        {
            _tabIndicatorSliding = false;
            _settingsTabIndicator.Width = width;
            _settingsTabIndicatorShift.X = at.X;
            _settingsTabIndicatorShift.Y = top;
            return;
        }
        double fromWidth = _settingsTabIndicator.Width;
        double fromX = _settingsTabIndicatorShift.X, fromY = _settingsTabIndicatorShift.Y;
        _settingsTabIndicator.Width = fromWidth;
        _settingsTabIndicatorShift.X = fromX;
        _settingsTabIndicatorShift.Y = fromY;
        var duration = new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.StateMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        // 下划线在滑的这段时间里，排版回调不许再摆一次，否则会被按回终点、看着像没动画
        _tabIndicatorSliding = true;
        var slide = new DoubleAnimation(at.X, duration) { EasingFunction = ease };
        slide.Completed += (_, _) => _tabIndicatorSliding = false;
        _settingsTabIndicatorShift.BeginAnimation(TranslateTransform.XProperty, slide);
        _settingsTabIndicatorShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(top, duration) { EasingFunction = ease });
        _settingsTabIndicator.BeginAnimation(WidthProperty, new DoubleAnimation(width, duration) { EasingFunction = ease });
    }

    /// <summary>分段排成几行：按它们的纵坐标有几档算，宽度不够时 <see cref="WrapPanel"/> 会换行。</summary>
    /// <returns>行数，至少一行。</returns>
    private int TabLines()
    {
        if (_settingsTabCanvas == null) return 1;
        var tops = new HashSet<double>();
        foreach (var chip in _settingsTabChips)
            tops.Add(Math.Round(chip.Tab.TranslatePoint(new Point(0, 0), _settingsTabCanvas).Y));
        return Math.Max(1, tops.Count);
    }

    /// <summary>分页条该占的高度：单行定高，每多一行加一行行距。</summary>
    /// <returns>当前应占的像素高度。</returns>
    private double TabStripTarget() => TabStripHeight + (TabLines() - 1) * TabRowPitch;

    /// <summary>行数变了才跟着改高度：宽度压到换行时整条长高，退回一行再收回去。
    /// 换页进行中不动高度：那时按钮还是上一组的，按新一组的页数去算会把同高的两条分页条顶得一上一下。</summary>
    private void SyncTabStripHeight()
    {
        if (!SettingsTabIds().SequenceEqual(_settingsTabIds)) return;
        double target = SettingsTabIds().Count > 1 ? TabStripTarget() : 0;
        if (Math.Abs(target - _tabStripApplied) < 0.5) return;
        AnimateSettingsTabStrip(target > 0, false);
    }

    /// <summary>整条的高度过渡：出现时从 0 长到定高，收起时缩回 0。内容跟着这条曲线上下让位，
    /// 没有底色跟着一起缩，看着就是「一行按钮被收进表头里」。</summary>
    /// <param name="visible">当前分组是否有分页条。</param>
    /// <param name="animate">false = 直接就位。</param>
    private void AnimateSettingsTabStrip(bool visible, bool animate)
    {
        var host = _settingsTabHost;
        double target = visible ? TabStripTarget() : 0;
        _tabStripApplied = target;
        host.BeginAnimation(HeightProperty, null);
        double from = host.Height;
        if (double.IsNaN(from)) from = host.ActualHeight;
        if (!animate || !host.IsLoaded || Math.Abs(from - target) < 0.5)
        {
            host.Height = target;
            host.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        host.Height = from;
        host.Visibility = Visibility.Visible;
        var grow = new DoubleAnimation(from, target, new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.EnterMs)))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        grow.Completed += (_, _) =>
        {
            host.BeginAnimation(HeightProperty, null);
            host.Height = target;
            if (!visible) host.Visibility = Visibility.Collapsed;
        };
        host.BeginAnimation(HeightProperty, grow);
    }

    /// <summary>分组换了以后按钮轻轻淡入：整条宽度可能跟着变，位移会把新的一组甩得很突然。</summary>
    /// <param name="visible">当前分组是否有分页条。</param>
    /// <param name="animate">false = 直接就位。</param>
    private void RevealSettingsTabs(bool visible, bool animate)
    {
        var panel = _settingsTabs;
        panel.BeginAnimation(OpacityProperty, null);
        if (!visible || !animate || !panel.IsLoaded)
        {
            panel.Opacity = 1;
            return;
        }
        panel.Opacity = 0.15;
        panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0.15, 1,
            new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.QuickMs))));
    }
}
