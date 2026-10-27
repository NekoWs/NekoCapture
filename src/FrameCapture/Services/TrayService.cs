using System.Drawing;
using System.Windows.Controls;
using FrameCapture.Core;
using WinForms = System.Windows.Forms;

namespace FrameCapture.Services;

/// <summary>托盘常驻：图标 + WPF Fluent 右键菜单（随主题着色、悬停高亮、可勾选）。</summary>
public sealed class TrayService : IDisposable
{
    private readonly AppHost _host;
    private readonly WinForms.NotifyIcon _icon;
    private System.Windows.Window? _menuAnchor;
    private ContextMenu? _menu;
    // 两枚图标一次画好备用：暂停切换时只是换个引用，不反复造 GDI 句柄。
    // 强调色改动会重画它们——图标是位图，画出来就定死了，不重画会停在旧强调色上。
    private Icon _iconOn, _iconPaused;
    private readonly Action _onPalette;

    /// <summary>建托盘图标并订阅设置变更以刷新悬停提示。图标此刻 <c>Visible=false</c>，由 <see cref="Show"/> 点亮。</summary>
    /// <param name="host">宿主，用于把菜单项动作转给 <see cref="CaptureController"/> 与设置服务；须已完成装配。</param>
    public TrayService(AppHost host)
    {
        _host = host;
        _iconOn = MakeIcon(paused: false);
        _iconPaused = MakeIcon(paused: true);

        _icon = new WinForms.NotifyIcon
        {
            Icon = _iconOn,
            Visible = false,
            Text = "FrameCapture",
        };
        _icon.MouseUp += (_, e) =>
        {
            // 左键单击=选区截图，右键=菜单。
            // 双击会连发两次 MouseUp，但 BeginScopeSelection 有重入保护（已在选区只顶前不重开），不会截两次。
            if (e.Button == WinForms.MouseButtons.Left) Dispatch(() => _host.Controller.BeginScopeSelection());
            else if (e.Button == WinForms.MouseButtons.Right) Dispatch(ShowMenu);
        };
        _icon.DoubleClick += (_, _) => Dispatch(() => _host.Controller.BeginScopeSelection());
        // 悬停提示动态反映当前热键/范围/fps：改键或换范围后无需打开菜单即可确认状态
        _host.Settings.Changed += () => Dispatch(UpdateTip);
        _onPalette = () => Dispatch(RebuildIcons);
        Theme.Changed += _onPalette;
        UpdateTip();
    }

    /// <summary>按当前强调色重画两枚托盘图标，再把托盘切到新引用上。</summary>
    void RebuildIcons()
    {
        var prevOn = _iconOn;
        var prevPaused = _iconPaused;
        _iconOn = MakeIcon(paused: false);
        _iconPaused = MakeIcon(paused: true);
        UpdateTip();          // 先让 _icon.Icon 指向新画的那枚，旧句柄才谈得上释放
        prevOn.Dispose();
        prevPaused.Dispose();
    }

    void UpdateTip()
    {
        var s = _host.Settings.Current;
        if (Services.HotkeyManager.Paused) // 暂停态一眼可见，避免用户以为热键失灵
        {
            // 图标整枚转灰：余光扫过托盘就知道热键让位了，不用把鼠标停上去读提示
            _icon.Icon = _iconPaused;
            _icon.Text = Loc.T(K.Tray.FramecapturePausedHotkeysReleased);
            return;
        }
        _icon.Icon = _iconOn;
        string region = s.RegionW >= 16 ? Loc.F(K.Tray.RegionFps, s.RegionW, s.RegionH, s.Fps) : Loc.T(K.Tray.NoRegionDoubleClick);
        string tip = Loc.F(K.Tray.FramecaptureGestures, s.Gestures.Bindings.Count, region);
        _icon.Text = tip.Length <= 63 ? tip : "FrameCapture"; // NotifyIcon.Text 上限 63 字符
    }

    /// <summary>让托盘图标可见。构造时故意不可见，宿主装配完再调本方法——避免图标先出现、菜单还点不动的窗口期。</summary>
    public void Show() => _icon.Visible = true;

    /// <summary>
    /// 提示气泡：托盘动作的轻量反馈（如「剪贴板里没有图片」）。走 <see cref="Overlays.NoticeToast"/>，
    /// 与截屏浮窗同一套样式、圆角与动效；系统托盘气泡的样式不受主题控制，也不在程序自己的掌控里。
    /// </summary>
    /// <param name="msg">气泡正文，单行短文本；停留 4 秒后自动消失，不阻塞调用。</param>
    public void Notify(string msg) => Dispatch(() => Overlays.NoticeToast.Show(msg));

    /// <summary>切换「暂停全局热键」。托盘菜单项与 `--pause-toggle` IPC 共用；返回切换后的状态。</summary>
    /// <returns>切换后的暂停态：true = 已暂停（钩子不喂手势、不吞任何键），false = 恢复正常工作。</returns>
    public bool TogglePause()
    {
        bool on = !Services.HotkeyManager.Paused;
        Services.HotkeyManager.Paused = on;
        UpdateTip();
        Notify(on ? Loc.T(K.Tray.PausedCaptureHotkeysAreReleased) : Loc.T(K.Tray.ResumedCaptureHotkeysAreActive));
        return on;
    }

    /// <summary>
    /// 在鼠标处弹出托盘菜单。菜单项每次重建，因此始终反映当前设置（延时秒数、自启勾选、是否已提权）。
    /// 同一时刻只保留一份菜单与其锚窗：再次右键会先关掉上一份。
    /// </summary>
    public void ShowMenu()
    {
        Trace.Log("tray menu open");
        if (_menu != null) _menu.IsOpen = false; // 再次右键：先关掉已开着的菜单，连带回收它的锚窗
        var menu = new ContextMenu { Resources = Fluent.MenuResources() };
        _menu = menu;

        void Item(string text, Action act, bool checkable = false, bool isChecked = false)
        {
            var mi = new MenuItem { Header = text, IsCheckable = checkable, IsChecked = isChecked };
            mi.Click += (_, _) => { menu.IsOpen = false; act(); };
            menu.Items.Add(mi);
        }
        void Sep() => menu.Items.Add(new Separator());

        Item(Loc.T(K.Tray.SetCaptureRegion), () => _host.Controller.BeginScopeSelection());
        Item(Loc.T(K.Tray.ClearCaptureRegion), () => _host.Controller.ClearStoredRegion());
        Sep();
        Item(Loc.T(K.Tray.CaptureNow), () => _host.Controller.CaptureStoredStill());
        // 静态截图的确认浮窗会自动消失，留一个入口把最近那张重新拉回编辑器标注
        if (_host.Controller.HasLastStill)
            Item(Loc.T(K.Tray.EditLastScreenshot), () => _host.Controller.EditLastStill());
        Item(Loc.T(K.Common.ScrollingCapture), () => _host.Controller.BeginScrollCapture());
        // 滚早了头不必从头再来：只有手里真留着半张长图时这一条才出现
        if (_host.Controller.HasScrollResume)
            Item(Loc.T(K.Tray.ContinueTheLastScroll), () => _host.Controller.ResumeScrollCapture());
        Item(Loc.T(K.Common.DelayedCapture), () => _host.Controller.BeginDelayedCapture());
        Item(Loc.T(K.Tray.PinFromClipboard), () =>
        {
            if (!_host.Controller.PinFromClipboard())
                Notify(Loc.T(K.Tray.NoImageOnTheClipboard));
        });
        // 开了自动保存后，用户常想知道文件落到哪——直接打开保存目录
        if (_host.Settings.Current.AutoSaveCopy)
            Item(Loc.T(K.Tray.OpenSaveFolder), () =>
            {
                try
                {
                    var dir = SaveService.ResolveFolder();
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
                }
                catch (Exception ex) { Trace.Log($"open folder failed: {ex.Message}"); Notify(Loc.T(K.Tray.CouldNotOpenTheSave)); }
            });
        Sep();
        Item(Loc.T(K.Tray.Settings), OpenSettings);
        // 就绪卡只在首启弹一次；老手想再看一眼自己现在绑的是哪个键，从托盘直接叫出来，
        // 不必为一行键位去开帮助窗。
        Item(Loc.T(K.Tray.ShowKeyHints), () =>
        {
            var g = _host.Settings.Current.Gestures;
            Overlays.WelcomeToast.Show(g.CaptureKeyText(), g.RecordKeyText());
        });
        Item(Loc.T(K.Tray.Help), OpenHelp);
        Item(Loc.T(K.Common.StartWithWindows), () =>
        {
            _host.Settings.Mutate(s => s.RunAtStartup = !s.RunAtStartup);
            StartupService.Apply(_host.Settings.Current.RunAtStartup, _host.Settings.Current.RunAsAdmin);
        }, checkable: true, isChecked: _host.Settings.Current.RunAtStartup);
        // 暂停全局热键——游戏/演示/与其他软件热键冲突时临时让位，键完整落到前台应用。
        // 运行时状态（不持久化），重启自动恢复工作。
        Item(Loc.T(K.Tray.PauseGlobalHotkeys), () => TogglePause(),
            checkable: true, isChecked: Services.HotkeyManager.Paused);
        // 按需提权：解决被更高权限窗口（管理员终端/游戏）压住、快捷键盲区的问题
        if (AppHost.IsElevated)
            Item(Loc.T(K.Tray.RunningAsAdministrator), () => { }, checkable: true, isChecked: true); // 勾选标记由 checkable 呈现，文本里不重复打勾
        else
            Item(Loc.T(K.Tray.RunAsAdministrator), () =>
            {
                // 托盘这一下与设置里那枚开关是同一件事：都记成「要提权」，再换成提权实例
                _host.Settings.Mutate(s => s.RunAsAdmin = true);
                _host.RestartAsAdmin();
            });
        Sep();
        Item(Loc.T(K.Tray.Exit), () =>
        {
            Dispose();
            System.Windows.Application.Current?.Shutdown();
        });

        // 透明锚点窗：保证菜单在鼠标处弹出、并能在关闭时回收。
        // 必须激活：应用没有任何激活窗口时，ContextMenu 收不到"失焦"，点别处菜单也不消失。
        _menuAnchor?.Close();
        var anchor = new System.Windows.Window
        {
            Width = 1,
            Height = 1,
            WindowStyle = System.Windows.WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = true,
            Topmost = true,
            Left = WinForms.Cursor.Position.X,
            Top = WinForms.Cursor.Position.Y,
        };
        _menuAnchor = anchor;
        anchor.Show();
        anchor.Activate();
        anchor.Deactivated += (_, _) => { menu.IsOpen = false; }; // 点其他应用/切窗 → 菜单即收
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_menu, menu)) _menu = null;
            if (ReferenceEquals(_menuAnchor, anchor)) _menuAnchor = null;
            anchor.Close();
        };
        menu.PlacementTarget = anchor;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    static void Dispatch(Action a)
    {
        var app = System.Windows.Application.Current;
        if (app == null) { a(); return; }
        app.Dispatcher.Invoke(a);
    }

    void OpenSettings() => Overlays.SettingsWindow.ShowOrFocus();

    void OpenHelp() => Overlays.HelpWindow.ShowOrFocus();

    /// <summary>
    /// 托盘图标：与 <c>scripts/make-icon.py</c> 同一枚取景框标记，比例常量取自那份源图
    /// （边距 14.65% / 臂长 24.41% / 线宽 9.38% / 中心点直径 29.30%），透明底、无砖面。
    /// </summary>
    /// <remarks>64px 画布绘制后交给外壳缩到托盘尺寸；虚线角标在 16px 下会糊成一团，故用实角。</remarks>
    /// <param name="paused">true 画暂停版：整枚转灰。暂停时热键让位给别的软件，托盘图标是唯一的余光信号。</param>
    /// <returns>64px 透明底的托盘图标。</returns>
    static Icon MakeIcon(bool paused)
    {
        const int C = 64;                                  // 画布边长
        const float inset = 0.1465f * C, arm = 0.2441f * C, stroke = 0.0938f * C, dot = 0.2930f * C;
        using var bmp = new Bitmap(C, C);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var accent = paused ? Color.FromArgb(255, 0x9A, 0xA0, 0xA6)
                            : Color.FromArgb(255, Theme.Accent.R, Theme.Accent.G, Theme.Accent.B);
        using var pen = new Pen(accent, stroke)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Flat,
            EndCap = System.Drawing.Drawing2D.LineCap.Flat,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
        };
        float lo = inset, hi = C - inset, mid = C / 2f;
        foreach (var (cx, cy, dx, dy) in new[] { (lo, lo, 1f, 1f), (hi, lo, -1f, 1f), (lo, hi, 1f, -1f), (hi, hi, -1f, -1f) })
        {
            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddLines(new[]
            {
                new PointF(cx + dx * arm, cy), new PointF(cx, cy), new PointF(cx, cy + dy * arm),
            });
            g.DrawPath(pen, path);
        }
        using var fill = new SolidBrush(accent);
        g.FillEllipse(fill, mid - dot / 2, mid - dot / 2, dot, dot);
        IntPtr hicon = bmp.GetHicon();
        return Icon.FromHandle(hicon);
    }

    /// <summary>
    /// 隐藏并释放托盘图标，同时关闭还开着的菜单锚窗。宿主 Shutdown 与托盘「退出」菜单项都走这里。
    /// </summary>
    /// <remarks>调用后实例不可再用：<see cref="WinForms.NotifyIcon"/> 已释放，再调 <see cref="Show"/> 会抛异常。</remarks>
    public void Dispose()
    {
        Theme.Changed -= _onPalette;
        _icon.Visible = false;
        _icon.Dispose();
        _menuAnchor?.Close();
    }
}
