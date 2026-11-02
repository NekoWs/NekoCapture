using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using NekoCapture.Core;

namespace NekoCapture.Overlays;

/// <summary>页内分页条：同一根下的兄弟页横向排开，选中态与整条的出现、退场都走动画。</summary>
public sealed partial class SettingsWindow
{
    /// <summary>分页条展开后的高度：一枚胶囊加下边距。</summary>
    private const double TabStripHeight = 34;

    private WrapPanel _settingsTabs = null!;
    private Border _settingsTabHost = null!;
    private readonly List<TabChip> _settingsTabChips = new();
    private List<string> _settingsTabIds = new();

    /// <summary>一枚分页胶囊：底色与字色都是本窗持有的可动画画刷，换状态时渐变而不是瞬切。</summary>
    /// <param name="Id">这一枚对应的页标识。</param>
    /// <param name="Tab">可点的胶囊本体。</param>
    /// <param name="Label">页名。</param>
    /// <param name="Backing">底色画刷。</param>
    /// <param name="Ink">字色画刷。</param>
    private sealed record TabChip(string Id, Border Tab, TextBlock Label, SolidColorBrush Backing, SolidColorBrush Ink)
    {
        /// <summary>指针是否停在上面。</summary>
        public bool Hovered { get; set; }
    }

    /// <summary>当前该显示哪几枚胶囊：单页分组、详情页与搜索都不显示，交给标题与返回路径说明位置。</summary>
    /// <returns>按声明顺序排列的页标识；空表示整条收起。</returns>
    private List<string> SettingsTabIds()
    {
        if (SettingsSearchActive || IsDetailPage(_currentSettingsPage)) return new List<string>();
        string root = _settingsPages.TryGetValue(_currentSettingsPage, out var page) ? page.Root : "";
        if (!_settingsTabsOf.TryGetValue(root, out var ids) || ids.Count <= 1) return new List<string>();
        return ids;
    }

    /// <summary>按当前页刷新分页条：分组换了才重建胶囊，同一分组内只渐变选中态。</summary>
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
        if (rebuilt) RevealSettingsTabs(strip, animate);
        else if (strip && _settingsTabs.Opacity < 0.999) _settingsTabs.Opacity = 1;   // 上一次淡入被切页打断，补回不透明
    }

    /// <summary>重建胶囊：每枚自己持有底色与字色画刷，事件只改状态、不换画刷实例。</summary>
    /// <param name="ids">这一组要显示的页标识。</param>
    private void BuildSettingsTabs(List<string> ids)
    {
        _settingsTabs.Children.Clear();
        _settingsTabChips.Clear();
        _settingsTabIds = ids;
        foreach (string id in ids)
        {
            if (!_settingsPages.TryGetValue(id, out var page)) continue;
            var backing = new SolidColorBrush(Colors.Transparent);
            var ink = new SolidColorBrush(Theme.TextDim);
            var label = new TextBlock
            {
                Text = page.Label, FontSize = 12.5, Foreground = ink, VerticalAlignment = VerticalAlignment.Center,
                Tag = "settings-tab-label",
            };
            var tab = new Border
            {
                Child = label, CornerRadius = new CornerRadius(6), Padding = new Thickness(11, 5, 11, 5),
                Margin = new Thickness(0, 0, 4, 0), Tag = "settings-tab", Focusable = true,
                Cursor = Cursors.Hand, Background = backing,
            };
            var chip = new TabChip(id, tab, label, backing, ink);
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

    /// <summary>把每枚胶囊刷成当前状态：选中一枚、其余按指针位置取悬停色。</summary>
    /// <param name="animate">false = 颜色直接就位。</param>
    private void PaintSettingsTabs(bool animate)
    {
        int ms = animate ? Fluent.Motion.HoverMs : 0;
        foreach (var chip in _settingsTabChips)
        {
            bool selected = chip.Id == _currentSettingsPage;
            Color backing = selected ? Fluent.AccentOn(Theme.CardBg, 0.18) : chip.Hovered ? Theme.Hover : Colors.Transparent;
            TintColor(chip.Backing, backing, ms);
            TintColor(chip.Ink, selected ? Theme.Accent : Theme.TextDim, ms);
        }
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

    /// <summary>整条的高度过渡：出现时从 0 长到定高，收起时缩回 0，下面内容的位移跟着这条曲线走。</summary>
    /// <param name="visible">当前分组是否有分页条。</param>
    /// <param name="animate">false = 直接就位。</param>
    private void AnimateSettingsTabStrip(bool visible, bool animate)
    {
        var host = _settingsTabHost;
        double target = visible ? TabStripHeight : 0;
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

    /// <summary>分组换了以后胶囊整排淡入：上一组已经退场，这一组从上方 6 像素落到位。</summary>
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
