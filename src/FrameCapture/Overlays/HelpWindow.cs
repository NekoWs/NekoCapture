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

    /// <summary>SelfTest renderhelp 离屏取证用：构建但不 Show——不占任务栏、不抢焦点、不碰热键，与运行实例互不干扰。</summary>
    public static HelpWindow CreateForCapture()
    {
        var w = new HelpWindow();
        w.Build();
        return w;
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
        Width = 640;
        // 条目随功能增长（R13 起 806 装不下，末条被裁一半）：高度自适应内容，超高封顶后滚动
        SizeToContent = SizeToContent.Height;
        // R49：封顶值跟随工作区（固定 1040 在 2K 屏白白滚动藏底，1080p 屏又可能超屏）
        MaxHeight = Math.Max(600, SystemParameters.WorkArea.Height - 80);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var root = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Theme.WindowBg),
            BorderBrush = new SolidColorBrush(Theme.Stroke),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 2, Opacity = 0.5 },
        };
        var dock = new DockPanel { LastChildFill = true };

        // R62 紧凑化：与设置窗同款自绘标题栏（共享件 Fluent.TitleBar），去掉旧版 18px 大标题 + 悬浮关闭钮；
        // 分组小标题/行距/内衬全部对齐设置窗同一套密度与视觉语言。
        var (bar, closeBtn) = FrameCapture.Core.Fluent.TitleBar("\uE9CE", "FrameCapture 使用速查");
        closeBtn.Click += (_, _) => FrameCapture.Core.Fluent.CloseWithFade(this);
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);

        var panel = new StackPanel { Margin = new Thickness(16, 6, 16, 14) };
        void H(string t) => panel.Children.Add(new TextBlock
        {
            Text = t, FontSize = 11, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Theme.TextDim), Margin = new Thickness(2, 12, 0, 3),
        });
        void KV(string k, string v)
        {
            // 两列栅格：键位列定宽（各行同宽才对齐），说明列自适应换行
            var g = new Grid { Margin = new Thickness(2, 1, 0, 1) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(172) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var kt = new TextBlock
            {
                Text = k, FontSize = 12.5,
                Foreground = new SolidColorBrush(Theme.Text),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 12, 0),
            };
            var vt = new TextBlock
            {
                Text = v, FontSize = 12.5,
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

        H("捕获");
        KV(HK(true), "设置捕获范围（拖框 / 悬停吸附窗口，Enter 确认，Esc 取消）——范围沿用；虚线旧选区可在设置关闭");
        KV("选区放大镜", "拖框/悬停时光标旁显示高倍像素放大镜 + 坐标/尺寸读数 + 光标下像素颜色，像素级精确选区/取色；按 C 复制颜色（格式 HEX/RGB/HSL 可在设置选，默认 HEX），选区窗内按 F 即时循环切换格式（读数立刻变，偏好会记住）");
        KV(HK(false) + " 长按", "录制按下到放开期间的画面，范围角上亮 REC 徽标；录到松手为止，不设时长上限");
        KV(HK(false) + " 连按两下", "免按住录制：REC 徽标常驻，再连按两下结束进编辑器（可在设置关闭）");
        KV("录制帧率 / 高刷", "设置「录制」组：帧率滑杆 10–240，或开「跟随显示器刷新率录满」（如 160Hz 屏就按 160fps 录，忽略手动值）。实际帧率受内容变化率与系统捕获投递限制——静态画面不会凭空产帧；要真正录满高刷，需被录内容本身以刷新率变化（全屏游戏/视频等）");
        KV("录制格式 MP4/WebM/GIF", "设置「录制格式」决定「导出录制」(Ctrl+E) 与「自动保存副本」的默认容器：MP4(libx264)/WebM(vp9) 支持高帧率、体积小，需本机装 FFmpeg（软件自动探测，未装则视频项提示安装、GIF 始终可用）；GIF 免依赖到处能贴但封顶约 50fps 且体积大。Enter 写剪贴板始终是 GIF（聊天工具可直接粘贴）");
        KV("录制包含鼠标指针", "设置「录制」组可关：默认录出的画面带鼠标光标；关掉后成片干净无光标，适合做教程/演示时后期自己加高亮，或不想让光标乱入");
        KV("导出视频需要 FFmpeg", "MP4/WebM 导出靠本机 ffmpeg（软件不内置，运行时自动探测 PATH 与常见安装目录）。没装时视频项会提示、GIF 仍可导出。安装任选其一：① 命令行 `winget install Gyan.FFmpeg`（Win11 自带 winget，装完重开软件即识别）；② 到 ffmpeg.org 下载解压后把其 bin 目录加入 PATH；③ 已装过但没进 PATH，可把 ffmpeg.exe 所在目录加进系统 PATH。装好后无需重启设置，下次导出即生效");
        KV(HK(false) + " 点按", "截一帧进剪贴板（开启「先显示选区」则先弹选区）；浮窗上点一下或 Enter 继续标注");
        KV("截图后", "设置里可选：弹提示（默认，浮窗点一下/Enter 进编辑器）· 进编辑器（复制后直接开标注，QQ 式）· 只复制（不弹窗不开编辑器，最快）· 贴图（复制后直接把图钉在原捕获位置，Snipaste 式，可多张并存、拖动/缩放/标注；仅对静图/滚动/延时等单帧捕获生效，录制仍进编辑器）");
        KV("截图提示音", "设置「截图」组可开：静态截图完成放一声柔和短促的「嘀」作听觉确认（默认关）；旁边「试听」可先听再决定");
        KV("滚动截图（长图）", "托盘「滚动截图（长图）」→ 拖选区 → 只管滚动目标窗口，自动找重叠拼接长图（进度实时显示）；点「完成」出图，按 Esc 随时取消（QQ 式）。网页/聊天记录/代码一屏截不下时用");
        KV("延时截图", "托盘「延时截图（N 秒）」→ 倒计时期间去打开右键菜单/悬停提示/下拉框，到点自动截当前范围（QQ 式延时）。秒数在设置「截图」组选（关/1/3/5/10）；倒计时浮窗不抢焦点、不入镜，点「取消」可中止");
        KV("从剪贴板贴图", "复制任意图片（截图 / 图片文件 / 网页图）后，托盘「从剪贴板贴图」把它钉在光标处作参考（Snipaste 式粘贴即贴图）；剪贴板没图片会气泡提示");

        H("编辑器 · 播放");
        KV("空格", "播放 / 暂停");
        KV("← →", "上一帧 / 下一帧");
        KV("Shift + ← →", "往前 / 往后一秒");
        KV("时间轴", "点/拖跳转；拖两端把手选一段范围（导出只含该段），单击清除");
        KV("[ / ]", "裁剪起点/终点 = 当前帧（Backspace 清除范围）");
        KV("＋ / －", "播放倍速 ×0.25–×8（0 或 × 复位）");

        H("编辑器 · 缩放");
        KV("Ctrl + 滚轮", "以光标为锚放大 / 缩小");
        KV("PageUp / PageDown", "放大 / 缩小（同 Ctrl + ＋ / －）");
        KV("Home 或 Ctrl+0", "回到适配视图");
        KV("End 或 Ctrl+1", "逐像素 1:1（HUD 显示当前倍率）");
        KV("拖 拽", "放大后：浏览态左键拖，或任意工具下按住中键平移");

        H(FrameCapture.Core.ToolCatalog.HelpHeading);
        // 工具条目从目录生成：加工具不会再漏帮助（R48，T92 配套）
        foreach (var t in FrameCapture.Core.ToolCatalog.Tools)
            KV(t.Sym == t.Key ? t.Key : $"{t.Sym} {t.Key}", t.Help); // Sym 是普通 Unicode：帮助窗默认字体渲染，图标字形(Glyph)会出豆腐块
        KV("调色板 K", "自绘 RGBA 调色板：色相/饱和/明度/透明度拖动即时生效，HEX 与 R/G/B/A 可直填；最近用色钉在旁侧，点一下即用");
        KV("⋯ 宽度 W", "自定义线宽：滑杆 1–24px，实时预览");
        KV("Ctrl+Z / U / Del", "撤销上一笔（标注与裁剪按发生顺序统一撤销）");
        KV("V 选中后改色/宽", "选中已有标注后点色板或线宽，就地改这条标注的颜色/粗细（文字/序号的粗细即字号），Ctrl+Z 可撤销；未选中时改的是后续新标注的默认值");
        KV("Ctrl+Y / Y", "重做上一笔");

        H("完成");
        KV("Enter / ✓", "生成 GIF 写入剪贴板（可直接粘贴到聊天工具），并自动生成 Agent 胶片条");
        KV("F1", "打开本帮助（编辑器/设置窗内均可）");
        KV("Esc / ✕", "放弃本次捕获（绘制态下 Esc 先退出工具，不会丢捕获；再按才放弃）");
        KV("底部工具条", "保存当前帧 Ctrl+S / 复制当前帧 Ctrl+C / 钉到屏幕 Ctrl+P / 识别文字 Ctrl+O / 导出录制 Ctrl+E（MP4/WebM/GIF 三选一）/ 暂留合成图 Ctrl+H");
        KV("Ctrl+P / 图片钮", "钉到屏幕：把当前帧（含标注）作为置顶小图钉在原位置，可拖动/滚轮缩放/双击或 Esc 关闭/R 键旋转 90°/F3 原地重抓源区域刷新内容（盯梢会变的画面免重截，Snipaste 式）/Ctrl+S 另存为/Ctrl+C 复制回剪贴板/Ctrl+O 识别文字/右键菜单（复制·另存为·识别文字·旋转·更新内容·不透明度·关闭），多张并存；拖动松手自动磁边贴屏幕边缘，也会对齐到同屏其他贴图的边/中线（可在设置关闭）");
        KV("Ctrl+O / 识别钮", "识别文字：用 Windows 内置 OCR 识别当前帧里的印刷文字（中英文），纯文本写入剪贴板；无语言包时提示安装，不联网。设置「识别文字后显示结果窗」开启时另弹可编辑/部分复制的结果窗");
        KV("图钉", "编辑器总在最前（默认关）：钉上后盖着其他窗口也看得见，选择偏好会记住");

        H("其他");
        KV("托盘图标", "右键：设置 / 帮助 / 退出；修改快捷键后全局即时生效");
        KV("暂停全局热键", "托盘「暂停全局热键（游戏/演示时）」勾选后，截图/选区/录制等热键全部让位给前台软件（不拦截、不误触），适合打游戏、投屏演示、或与其他软件热键撞车时临时避让；托盘图标提示会变「已暂停」，随时回来取消勾选即恢复。暂停期间托盘菜单里的「立即捕获」等仍可用（只让位热键，不影响托盘操作）；重启软件自动恢复工作");
        KV("主题", "设置 → 外观：跟随系统 / 浅色 / 深色 + 强调色，全应用即时生效");
        KV("胶片条", "确定后在 %TEMP%\\FrameCapture\\agent\\ 生成逐帧横排长图（含帧号/时间戳，可在设置关闭）");
        var tip = new TextBlock
        {
            Text = "小提示：所有按钮悬停都有说明气泡。",
            FontSize = 11.5, Foreground = new SolidColorBrush(Theme.TextDim), Margin = new Thickness(2, 14, 0, 0),
        };
        panel.Children.Add(tip);

        var scroll = new ScrollViewer
        {
            Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        DockPanel.SetDock(scroll, Dock.Bottom); // LastChildFill 填满剩余
        dock.Children.Add(scroll);
        root.Child = dock;
        Content = root;
        Theme.Changed += OnThemeRebuild;
        Closed += (_, _) => Theme.Changed -= OnThemeRebuild;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) FrameCapture.Core.Fluent.CloseWithFade(this); };
        FrameCapture.Core.Fluent.PopupEnter(this);
        // 标题栏 = 原生 HTCAPTION（共享件，与设置窗同款）；旧版整窗 DragMove 废除（内容区不该拖窗）
        FrameCapture.Core.Fluent.InstallCaptionDrag(this, closeBtn);
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
