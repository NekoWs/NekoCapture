using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FrameCapture.Core;
using FrameCapture.Native;
using FrameCapture.Services;

namespace FrameCapture.Overlays;

/// <summary>按功能逐层导航的设置窗口，支持搜索和共享行为编辑。</summary>
public sealed partial class SettingsWindow : Window
{
    readonly SettingsService _settings;
    int _cat;           // 当前分页索引
    /// <summary>索引形式的兼容导航入口。</summary>
    Action<int, bool>? _gotoCat;

    int _restoreCat = -1;    // 重建（改主题/强调色）时要恢复的分页索引；-1 = 没指定，走默认第一页


    /// <summary>
    /// 设置窗的窗口属性：无边框 + WindowChrome 原生拖窗区与缩放边框、固定起始尺寸、不进任务栏、不总在最前。
    /// 必须在 UI 线程调用；此处只取 <c>SettingsService</c> 引用，内容装配在 <see cref="Build"/>。
    /// </summary>
    public SettingsWindow()
    {
        // 文本输入控件按需启用输入法。
        InputMethod.SetIsInputMethodEnabled(this, false);
        _settings = AppHost.Current.Settings;
        _settings.Changed += OnPresetSettingsChanged;
        Closed += (_, _) => _settings.Changed -= OnPresetSettingsChanged;
        Closed += (_, _) => { _pageMotionGeneration++; ResetPageModules(); };
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;   // 可拖边缩放（WindowChrome 的 6px 缩放边框）
        // WindowChrome 原生缩放边框 + 原生圆角/阴影（与编辑器同款）；不用 AllowsTransparency——整窗会被推到软件渲染。
        Fluent.BorderlessNative(this, resizeBorder: 6);
        // 标题条 = WindowChrome 原生 CaptionHeight 拖窗区（比 WM_NCHITTEST 钩子更稳，避免与 WindowChrome 命中测试抢序）；
        // 关闭按钮在 Build 里标 IsHitTestVisibleInChrome 让位给 WPF 命中。
        var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
        if (chrome != null) chrome.CaptionHeight = Fluent.TitleBarHeight;
        // 窗口底就是「窗口底色」这枚令牌的实底：非分层窗背后没有模糊背板，带 alpha 的色会直接透成黑，
        // 所以平涂它的不透明版。Mica 只在窗口自己不画像素的地方显形，铺上实底等于把它盖掉——
        // 留着背板，「窗口底色」这一项怎么改都看不出变化。
        Background = Theme.WindowBgSolidBrush;
        SizeToContent = SizeToContent.Manual;   // 分页后每页很短，固定舒适尺寸起步，可拖边缩放
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Width = 760; Height = Math.Min(660, SystemParameters.WorkArea.Height - 120);
        MinWidth = 680; MinHeight = 460;
        ShowInTaskbar = true;   // 普通工具窗：任务栏要有它，从别的应用切回来才找得到
        Topmost = false;   // 普通工具窗：不该压住用户正在截/录的画面
        Title = "FrameCapture Settings";
        // 拖窗区只有标题条那一条（CaptionHeight = Fluent.TitleBarHeight）：整窗背景不承担拖拽——
        // 整窗拖拽会被 ScrollContentPresenter 吞掉命中（滚动画布里拖不动），还会与标题栏的拖拽双路径叠加出现位移超调。
        Fluent.PopupEnter(this);
    }

    /// <summary>
    /// 打开设置窗；已有实例时只激活它并切到请求的那一页，全进程同时最多一个设置窗。
    /// 必须在 UI 线程调用（遍历 <c>Application.Current.Windows</c> 找现存实例）。
    /// </summary>
    /// <param name="page">开局停在哪一页：页名或下标；空或认不出回第一页。提权交接回来时用它停到「系统」。</param>
    public static void ShowOrFocus(string? page = null)
    {
        if (AppHost.Current.Controller?.UpdateReserved == true) return;
        var existing = Find();
        // 已经开着一扇窗：只把它激活，并按这次请求切到那一页（提权交接回来说的就是「系统」）
        if (existing != null) { existing.Activate(); existing.GotoPage(page); return; }
        var w = new SettingsWindow { _restoreCat = ParsePage(page), _restoreSettingsPage = page };
        w.Build();
        w.Show();
        w.Activate();
    }

    /// <summary>把已开着的窗切到某一页；页名为空或认不出就不动。</summary>
    /// <param name="page">页名或下标，与 <c>FC_SETTINGS_PAGE</c> 同一套写法。</param>
    void GotoPage(string? page)
    {
        if (!string.IsNullOrWhiteSpace(page) && IsLoaded) NavigateSettings(ResolveSettingsPage(page), true, canonical: true);
    }

    /// <summary>页名或下标 → 分页序号；认不出返回 -1。</summary>
    /// <param name="page">形如 <c>system</c> 或 <c>8</c> 的串。</param>
    /// <returns>分页序号，认不出为 -1。</returns>
    static int ParsePage(string? page)
    {
        if (string.IsNullOrWhiteSpace(page)) return -1;
        var raw = page.Trim().ToLowerInvariant();
        string[] names = { "hotkeys", "behaviors", "capture", "record", "output", "editor", "pin", "appearance", "palette", "system", "about" };
        int at = Array.IndexOf(names, raw);
        if (at >= 0) return at;
        return int.TryParse(raw, out var n) && n >= 0 && n < names.Length ? n : -1;
    }

    static SettingsWindow? Find()
    {
        foreach (Window w in Application.Current.Windows)
            if (w is SettingsWindow s) return s;
        return null;
    }

    /// <summary>把「允许截到自己」这次改动应用到已经开着的窗口：程序性重设一遍 WDA，不必重启才生效。</summary>
    /// <param name="w">要重设的窗口；还没建句柄的直接跳过，它建成时会按设置走。</param>
    static void ApplyCaptureAffinity(Window w)
    {
        var h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
        if (h == IntPtr.Zero) return;
        if (Core.Fluent.CaptureSelfAllowed)
        {
            try { Win32.SetWindowDisplayAffinity(h, Win32.WDA_NONE); } catch { }
            return;
        }
        Core.Fluent.ExcludeFromCapture(h);
    }

    /// <summary>审查脚本用 <c>FC_SETTINGS_PAGE</c> 把窗口直接开到某一页截图（页名或下标）。
    /// 只认这一个环境变量：不给就回第一页，改主题/强调色重建时仍按 <c>_restoreCat</c> 停在原页。</summary>
    /// <returns>分页下标；没设或认不出返回 -1，由调用方兜到第一页。</returns>
    static int StartupPage()
    {
        var raw = Environment.GetEnvironmentVariable("FC_SETTINGS_PAGE");
        if (string.IsNullOrWhiteSpace(raw)) return -1;
        raw = raw.Trim().ToLowerInvariant();
        string[] names = { "hotkeys", "behaviors", "capture", "record", "output", "editor", "pin", "appearance", "palette", "system", "about" };
        int at = Array.IndexOf(names, raw);
        return at >= 0 ? at : int.TryParse(raw, out var n) ? n : -1;
    }

    void Build()
    {
        InitializeSettingsCatalog();
        Content = BuildSettingsShell();
        _repaintHandler = () => { foreach (var paint in _repaint.ToArray()) paint(); };
        Closed += (_, _) => ClosePalettePickers();
        Theme.Changed += _repaintHandler;
        Closed += (_, _) => { if (_repaintHandler != null) Theme.Changed -= _repaintHandler; };
        _gotoCat = (index, animate) => NavigateSettings(LegacySettingsPage(index), animate, canonical: true);
        string initial = _restoreSettingsPage ?? Environment.GetEnvironmentVariable("FC_SETTINGS_PAGE")
            ?? LegacySettingsPage(_restoreCat >= 0 ? _restoreCat : StartupPage());
        NavigateSettings(initial, false);
        Theme.Changed += OnPaletteChanged;
        Theme.Changing += OnPaletteChanging;
        PreviewMouseDown += OnSettingsBackgroundMouseDown;
        Closed += (_, _) => { Theme.Changed -= OnPaletteChanged; Theme.Changing -= OnPaletteChanging; };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (InCapturingBox(e.OriginalSource as DependencyObject) || e.OriginalSource is TextBox) return;
                if (_settingsTrail.Count > 1) NavigateSettings(_settingsTrail[^2], true);
                else Fluent.CloseWithFade(this);
                e.Handled = true;
            }
            else if (e.Key == Key.F1) { HelpWindow.ShowOrFocus(); e.Handled = true; }
        };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Win32.SetRoundedCorners(hwnd);
            Win32.SetWindowTextW(hwnd, "FrameCapture Settings");
        };
        Closed += (_, _) => AppHost.Current.InstallHotkeys();
    }
}
