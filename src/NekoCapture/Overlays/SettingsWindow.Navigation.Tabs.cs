using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using NekoCapture.Core;

namespace NekoCapture.Overlays;

/// <summary>页内分页条：一条轨道，选中那块是一枚滑到位置上的强调色胶囊，与界面语言同一套分段控件。</summary>
public sealed partial class SettingsWindow
{
    /// <summary>分页条展开后的高度：30 的轨道加 4 的下边距。</summary>
    private const double TabStripHeight = 34;

    private readonly List<TabChip> _settingsTabChips = new();
    private List<string> _settingsTabIds = new();
    private string _paintedTabSelection = "";
    private double _tabStripApplied = TabStripHeight;
    private WrapPanel _settingsTabs = null!;
    private Grid _settingsTabTrack = null!;
    private Border _settingsTabTrackBorder = null!;
    private Border _settingsTabPill = null!;
    private TranslateTransform _settingsTabPillShift = null!;
    private Border _settingsTabHost = null!;

    /// <summary>一枚分段：只有文字与自己的墨色画刷，底色由滑动的胶囊负责。</summary>
    /// <param name="Id">这一枚对应的页标识。</param>
    /// <param name="Tab">可点的分段本体。</param>
    /// <param name="Label">页名。</param>
    /// <param name="Ink">字色画刷，选中与未选中之间渐变。</param>
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

    /// <summary>按当前页刷新分页条：分组换了才重建分段，同一分组内只让胶囊滑到新位置。</summary>
    /// <param name="animate">false = 直接就位，用于改配色重建与首帧。</param>
    private void UpdateSettingsTabs(bool animate)
    {
        if (_settingsTabs == null || _settingsTabHost == null) return;
        var ids = SettingsTabIds();
        bool rebuilt = !ids.SequenceEqual(_settingsTabIds);
        if (rebuilt) BuildSettingsTabs(ids);
        bool strip = ids.Count > 1;
        PaintSettingsTabs(animate);
        AnimateSettingsTabStrip(strip, animate);
        if (!rebuilt) { PlaceTabPill(animate); return; }
        RevealSettingsTabs(strip, animate);
        PlaceTabPill(false);
        DeferTabPlacement();
    }

    /// <summary>重建分段：每枚自己持有字色画刷，事件只改状态、不换画刷实例。</summary>
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
                Child = label, Height = 24, CornerRadius = new CornerRadius(5),
                Padding = new Thickness(9, 0, 9, 0), Margin = new Thickness(0, 1, 2, 1),
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

    /// <summary>刷字色：选中那一枚落在强调色上写字，其余按指针位置取灰阶。
    /// 换选中时字色等半拍再变——胶囊还在路上就先写白字，看着像闪了一下。</summary>
    /// <param name="animate">false = 颜色直接就位。</param>
    private void PaintSettingsTabs(bool animate)
    {
        bool selectionChanged = _paintedTabSelection != _currentSettingsPage;
        _paintedTabSelection = _currentSettingsPage;
        int ms = animate ? Fluent.Motion.HoverMs : 0;
        int delay = animate && selectionChanged ? Fluent.Motion.StateMs / 2 : 0;
        foreach (var chip in _settingsTabChips)
        {
            bool selected = chip.Id == _currentSettingsPage;
            Color ink = selected ? Fluent.OnAccentColor(Theme.Accent) : chip.Hovered ? Theme.Text : Theme.TextDim;
            TintColor(chip.Ink, ink, ms, delay);
        }
        // 轨道与胶囊都取主题共享画刷：换色时指回新的那一份
        if (_settingsTabTrackBorder != null)
        {
            _settingsTabTrackBorder.Background = Theme.TrackBgBrush;
            _settingsTabTrackBorder.BorderBrush = Theme.StrokeBrush;
        }
        if (_settingsTabPill != null) _settingsTabPill.Background = Theme.AccentBrush;
    }

    /// <summary>把一枚画刷渐变到目标色。动画攥着颜色属性时读到的是当前帧，
    /// 先读再交还属性才能从中途接着走，而不是先跳回底值再渐变。</summary>
    /// <param name="brush">目标画刷。</param>
    /// <param name="to">终点色。</param>
    /// <param name="ms">渐变时长；0 表示立刻就位。</param>
    /// <param name="delayMs">起跳前的等待，换选中时让胶囊先动。</param>
    private static void TintColor(SolidColorBrush brush, Color to, int ms, int delayMs = 0)
    {
        Color from = brush.Color;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        if (ms <= 0) { brush.Color = to; return; }
        brush.Color = from;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to,
            new Duration(TimeSpan.FromMilliseconds(ms))) { BeginTime = TimeSpan.FromMilliseconds(delayMs) });
    }

    /// <summary>把胶囊摆到选中那一枚下面：横向滑到新格，宽度同时收到这一枚的宽度；分段换行时纵向也跟着走。</summary>
    /// <param name="animate">true = 从当前位置滑到新格；false = 直接就位。</param>
    private void PlaceTabPill(bool animate)
    {
        if (_settingsTabPill == null || _settingsTabPillShift == null || _settingsTabTrack == null) return;
        var chip = _settingsTabChips.FirstOrDefault(item => item.Id == _currentSettingsPage);
        if (chip == null || chip.Tab.ActualWidth <= 0)
        {
            _settingsTabPill.Width = 0;
            return;
        }
        var at = chip.Tab.TranslatePoint(new Point(0, 0), _settingsTabTrack);
        double width = chip.Tab.ActualWidth;
        _settingsTabPill.BeginAnimation(WidthProperty, null);
        _settingsTabPillShift.BeginAnimation(TranslateTransform.XProperty, null);
        _settingsTabPillShift.BeginAnimation(TranslateTransform.YProperty, null);
        if (!animate || !_settingsTabPill.IsLoaded)
        {
            _settingsTabPill.Width = width;
            _settingsTabPillShift.X = at.X;
            _settingsTabPillShift.Y = at.Y;
            return;
        }
        double fromWidth = _settingsTabPill.Width, fromX = _settingsTabPillShift.X, fromY = _settingsTabPillShift.Y;
        _settingsTabPill.Width = fromWidth;
        _settingsTabPillShift.X = fromX;
        _settingsTabPillShift.Y = fromY;
        var duration = new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.StateMs));
        // 宽度只收放不过冲：过冲会把轨道撑宽一瞬，整条跟着抖
        _settingsTabPill.BeginAnimation(WidthProperty, new DoubleAnimation(width, duration)
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        _settingsTabPillShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(at.X, duration)
        { EasingFunction = Fluent.Spring(0.5) });
        _settingsTabPillShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(at.Y, duration)
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    /// <summary>排版跑完再摆一次：重建那一刻分段还没量出宽度与行数，胶囊和整条高度只能等下一轮布局。</summary>
    private void DeferTabPlacement()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsLoaded) return;
            SyncTabStripHeight();
            PlaceTabPill(false);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>分段之间的行距：分段 24 高，上下各留 1 像素。</summary>
    private const double TabsLinePitch = 26;

    /// <summary>分段排成几行：按它们的纵坐标有几档算，宽度不够时 <see cref="WrapPanel"/> 会换行。</summary>
    /// <returns>行数，至少一行。</returns>
    private int TabLines()
    {
        if (_settingsTabTrack == null) return 1;
        var tops = new HashSet<double>();
        foreach (var chip in _settingsTabChips)
            tops.Add(Math.Round(chip.Tab.TranslatePoint(new Point(0, 0), _settingsTabTrack).Y));
        return Math.Max(1, tops.Count);
    }

    /// <summary>分页条该占的高度：单行定高，每多一行加一行行距。</summary>
    /// <returns>当前应占的像素高度。</returns>
    private double TabStripTarget() => TabStripHeight + (TabLines() - 1) * TabsLinePitch;

    /// <summary>行数变了才跟着改高度：宽度压到换行时整条长高，退回一行再收回去。</summary>
    private void SyncTabStripHeight()
    {
        double target = SettingsTabIds().Count > 1 ? TabStripTarget() : 0;
        if (Math.Abs(target - _tabStripApplied) < 0.5) return;
        AnimateSettingsTabStrip(target > 0, true);
    }

    /// <summary>整条的高度过渡：出现时从 0 长到定高，收起时缩回 0，下面内容的位移跟着这条曲线走。</summary>
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

    /// <summary>分组换了以后整条淡入：上一组已经退场，这一组从上方 6 像素落到位。</summary>
    /// <param name="visible">当前分组是否有分页条。</param>
    /// <param name="animate">false = 直接就位。</param>
    private void RevealSettingsTabs(bool visible, bool animate)
    {
        var panel = _settingsTabs;
        panel.BeginAnimation(OpacityProperty, null);
        if (panel.RenderTransform is not TranslateTransform shift)
        {
            shift = new TranslateTransform();
            panel.RenderTransform = shift;
        }
        shift.BeginAnimation(TranslateTransform.YProperty, null);
        if (!visible || !animate || !panel.IsLoaded)
        {
            panel.Opacity = visible ? 1 : 0;
            shift.Y = 0;
            return;
        }
        panel.Opacity = 0;
        shift.Y = -6;
        panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1,
            new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.StateMs))));
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-6, 0,
            new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.EnterMs)))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
}
