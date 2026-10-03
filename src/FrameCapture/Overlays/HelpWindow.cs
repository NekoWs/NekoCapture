using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FrameCapture.Core;
using FrameCapture.Native;
using FrameCapture.Services;

namespace FrameCapture.Overlays;

/// <summary>快捷键与用法速查（教程文档，允许成段文字）。</summary>
public sealed class HelpWindow : Window
{
    public static void ShowOrFocus()
    {
        foreach (Window w in Application.Current.Windows)
            if (w is HelpWindow h) { h.Activate(); return; }
        var w2 = new HelpWindow();
        w2.Build();
        w2.Show();
        w2.Activate();
    }

    void OnThemeRebuild()
    {
        var oldw = this;
        var nw = new HelpWindow();
        nw.Build();
        nw.Left = oldw.Left; nw.Top = oldw.Top;
        nw.Opacity = 0;
        nw.Show();
        var fo = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(FrameCapture.Core.Fluent.Motion.ExitMs + 20)); // 交叉淡入的非对称节拍
        fo.Completed += (_, _) => oldw.Close();
        oldw.BeginAnimation(OpacityProperty, fo);
        nw.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(FrameCapture.Core.Fluent.Motion.ThemeMs)));
    }

    void Build()
    {
        // 覆盖层不需要文本输入：禁用输入法，避免拼音拦截快捷键
        System.Windows.Input.InputMethod.SetIsInputMethodEnabled(this, false);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        Title = "FrameCapture Help";
        Width = 640; Height = 620;

        var root = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Theme.WindowBg),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(22),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 2, Opacity = 0.5 },
        };
        var panel = new StackPanel();
        void H(string t) => panel.Children.Add(new TextBlock
        {
            Text = t, FontSize = 14, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Theme.Accent), Margin = new Thickness(0, 12, 0, 4),
        });
        void KV(string k, string v)
        {
            // 两列栅格：键位列定宽，说明列对齐（此前空格拼接导致错位）
            var g = new Grid { Margin = new Thickness(10, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var kt = new TextBlock
            {
                Text = k, FontSize = 13,
                Foreground = new SolidColorBrush(Theme.Text),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 14, 0),
            };
            var vt = new TextBlock
            {
                Text = v, FontSize = 13,
                Foreground = new SolidColorBrush(Theme.Text),
                TextWrapping = TextWrapping.Wrap,
            };
            g.Children.Add(kt);
            Grid.SetColumn(vt, 1);
            g.Children.Add(vt);
            panel.Children.Add(g);
        }
        string HK(bool scope)
        {
            var s = SettingsService.Shared?.Current;
            var hk = scope ? s?.ScopeHotkey : s?.CaptureHotkey;
            return string.IsNullOrEmpty(hk) ? (scope ? "Ctrl+Shift+R" : "Ctrl+Shift+G") : hk;
        }

        var title = new TextBlock { Text = "FrameCapture 使用速查", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Theme.Text) };
        panel.Children.Add(title);

        H("捕获");
        KV(HK(true), "设置捕获范围（拖框 / 悬停吸附窗口，Enter 确认，Esc 取消）——范围沿用");
        KV(HK(false) + " 长按", "录制按下到放开期间的画面（可在设置里改上限时长）");
        KV(HK(false) + " 点按", "只抓一帧（点按阈值可调，设为 0 = 永远长按）");

        H("编辑器 · 播放");
        KV("空格", "播放 / 暂停");
        KV("← →", "上一帧 / 下一帧");
        KV("Shift + ← →", "往前 / 往后一秒");
        KV("时间轴", "拖动滑块跳转；下方细条可拖选一段范围（导出只含该段），单击清除");
        KV("＋ / －", "播放倍速 ×0.25–×8（0 或 × 复位）");

        H("编辑器 · 绘图（再点当前工具 = 退出绘制）");
        KV("● D", "标记点");
        KV("─ L", "直线");
        KV("○ O", "圆圈");
        KV("↗ A", "箭头");
        KV("✎ P", "手绘");
        KV("✂ C", "裁剪（拖框后松开即裁）");
        KV("Ctrl+Z / U / Del", "撤销上一笔");
        KV("Ctrl+Y / Y", "重做上一笔");

        H("完成");
        KV("Enter / ✓", "生成 GIF 写入剪贴板（可直接粘贴到聊天工具），并自动生成 Agent 胶片条");
        KV("Esc / ✕", "放弃本次捕获");
        KV("右侧竖排", "保存当前帧 / 复制当前帧 / 保存 GIF / 保存视觉暂留合成图");

        H("其他");
        KV("托盘图标", "右键：设置 / 帮助 / 退出；修改快捷键后全局即时生效");
        KV("主题", "设置 → 外观：跟随系统 / 浅色 / 深色 + 强调色，全应用即时生效");
        KV("胶片条", "确定后在 %TEMP%\\FrameCapture\\agent\\ 生成逐帧横排长图（含帧号/时间戳，可在设置关闭）");
        var tip = new TextBlock
        {
            Text = "小提示：所有按钮悬停都有说明气泡。",
            FontSize = 12, Foreground = new SolidColorBrush(Theme.TextDim), Margin = new Thickness(0, 14, 0, 0),
        };
        panel.Children.Add(tip);

        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var shell = new Grid();
        shell.Children.Add(scroll);
        var closeBtn = FrameCapture.Core.Fluent.IconButton("", "关闭 (Esc)", 34, 14);
        closeBtn.Click += (_, _) => FrameCapture.Core.Fluent.CloseWithFade(this);
        closeBtn.HorizontalAlignment = HorizontalAlignment.Right;
        closeBtn.VerticalAlignment = VerticalAlignment.Top;
        closeBtn.Margin = new Thickness(0, 10, 12, 0);
        shell.Children.Add(closeBtn);
        root.Child = shell;
        Content = root;
        Theme.Changed += OnThemeRebuild;
        Closed += (_, _) => Theme.Changed -= OnThemeRebuild;
        MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is not Control) DragMove(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) FrameCapture.Core.Fluent.CloseWithFade(this); };
        FrameCapture.Core.Fluent.PopupEnter(this);
        SourceInitialized += (_, _) =>
        {
            var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Win32.SetRoundedCorners(h);
            Win32.SetWindowTextW(h, "FrameCapture Help");
        };
        Loaded += (_, _) =>
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Left + (wa.Width - Width) / 2;
            Top = wa.Top + (wa.Height - Height) / 2;
        };
    }
}
