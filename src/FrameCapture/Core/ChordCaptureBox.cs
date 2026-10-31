using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using K = FrameCapture.Core.K;

namespace FrameCapture.Core;

/// <summary>
/// 键位录入框：点一下，按下新键即改键，按 Esc 取消这个键位。
/// 常态显示键帽，录入态显示提示，边框在录入态转成强调色。
/// 录入期间吞掉所有按键，窗口自己的 Esc 关闭与 Tab 焦点移动都不会被触发。
/// 绑的是全局热键时，宿主订阅 <see cref="CaptureBegan"/> 在录入期间暂停全局钩子，
/// 否则正要录的那个组合键会被自家钩子先吞掉。
/// </summary>
public sealed partial class ChordCaptureBox : Border
{
    readonly Grid _host = new();
    bool _capturing;
    string _chord = "";

    /// <summary>录入期间自己记的修饰键状态。吞键之后 Ctrl/Shift/Win 不会进系统的输入队列，
    /// 读 <c>GetAsyncKeyState</c> 会读到「没按」——组合键于是被记成裸键。这里在钩子里自己记账。</summary>
    GestureCore.ModifierKeys _heldMods;
    string _emptyHint = Loc.T(K.Hotkeys.ClickToSet);

    /// <summary>键位改定：参数是新的键位串，空串表示解绑。</summary>
    public event Action<string>? Picked;

    /// <summary>录入中途失焦：既没改键也没解绑，这一笔不算。新加的键位框据此把自己收回去。</summary>
    public event Action? Cancelled;

    /// <summary>进入录入态。全局热键的宿主借此暂停键盘钩子。</summary>
    public event Action? CaptureBegan;

    /// <summary>离开录入态，无论改键、解绑还是失焦。与 <see cref="CaptureBegan"/> 成对。</summary>
    public event Action? CaptureEnded;

    /// <summary>是否正在等键。宿主窗口的「Esc 关窗」挂在前置（隧道）事件上，比本控件先收到按键，
    /// 需要据此放行：正在等键时这一记 Esc 是解绑这个键位，不是关窗。</summary>
    /// <value>录入态为 true。</value>
    public bool Capturing => _capturing;

    /// <summary>当前键位串，空串表示这条命令不占键。</summary>
    /// <value>赋值后立即重画。</value>
    public string Chord
    {
        get => _chord;
        set { _chord = value ?? ""; Render(); }
    }

    /// <summary>没有键位时框里显示的提示文字。</summary>
    /// <value>默认「点击设置」。</value>
    public string EmptyHint
    {
        get => _emptyHint;
        set { _emptyHint = value; Render(); }
    }

    /// <summary>一个空的录入框，等待点击或 Enter 进入录入态。</summary>
    public ChordCaptureBox()
    {
        Focusable = true;
        Cursor = Cursors.Hand;
        CornerRadius = new CornerRadius(6);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(10, 6, 10, 6);
        MinWidth = 40;
        Background = Theme.KeycapBgBrush;
        // 键帽在框里居中：表格里的框等宽对齐，键帽长短不一时靠边会让每行的键位错开
        _host.HorizontalAlignment = HorizontalAlignment.Center;
        Child = _host;
        // 点在框上就是来改键的：这一击吃掉，别冒泡到整行去展开。
        // 必须显式吃掉：BeginCapture 会重画子元素，冒泡到父级时原始命中元素已从树里摘下，
        // 父级按祖先链判断「是不是点在框里」会判成不是。
        MouseLeftButtonUp += (_, e) => { if (_capturing) EndCapture(); else BeginCapture(); e.Handled = true; };
        KeyDown += (_, e) =>
        {
            if (!_capturing && e.Key is Key.Space or Key.Enter) { BeginCapture(); e.Handled = true; }
        };
        PreviewKeyDown += OnPreviewKeyDown;
        LostFocus += (_, _) => CancelIfCapturing();
        Loaded += (_, _) => { Theme.Changed -= Render; Theme.Changed += Render; Render(); };
        Unloaded += (_, _) => { Theme.Changed -= Render; CancelIfCapturing(); };
        MouseEnter += (_, _) => PaintChrome(true); MouseLeave += (_, _) => PaintChrome(true);
        IsKeyboardFocusWithinChanged += (_, _) => PaintChrome(true);
        Render();
    }

    /// <summary>进入录入态：等一个主键，边框转强调色。设置页的加号按钮用它让新键位框一出来就在等键。</summary>
    public void BeginCapture()
    {
        if (_capturing) return;
        _capturing = true;
        _heldMods = Services.HotkeyManager.ReadMods();   // 开始录入前就按住的修饰键也算数
        CaptureBegan?.Invoke();
        // 录入期间把全局钩子的键汇接到自己身上：Win 组合键与 PrtSc 到不了窗口事件，
        // 中文输入法下 Shift+字母还会被标成 ImeProcessed。钩子是唯一能拿到真键的入口。
        Services.HotkeyManager.CaptureSink = OnGlobalKey;
        Focus();
        Render();
    }

    void EndCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        Services.HotkeyManager.CaptureSink = null;
        CaptureEnded?.Invoke();
        Render();
    }

    /// <summary>录入态下丢了焦点：这一笔作废，既不落键也不解绑。</summary>
    void CancelIfCapturing()
    {
        if (!_capturing) return;
        EndCapture();
        Cancelled?.Invoke();
    }

    /// <summary>钩子送来的一个键：修饰键只记账，Esc 解绑，其余连同自记的修饰键状态定成一个键位。
    /// 返回 true 表示这一击归录入框——吞掉之后前台与系统都收不到，Win+Shift+S 不会再弹系统截图工具。
    /// 修饰键自己也要吞：放行的话 Win 松开会弹开始菜单，而吞掉之后系统的输入队列里就没有它了，
    /// 所以状态只能在这里自己记（<see cref="_heldMods"/>），不能再去问 <c>GetAsyncKeyState</c>。</summary>
    bool OnGlobalKey(uint vk, bool isDown, GestureCore.ModifierKeys mods, long time)
    {
        if (!_capturing) return false;
        // 修饰键：按下与抬起都只更新自己的账，不落键位
        var bit = vk switch
        {
            0x11 or 0xA2 or 0xA3 => GestureCore.ModifierKeys.Control,
            0x10 or 0xA0 or 0xA1 => GestureCore.ModifierKeys.Shift,
            0x12 or 0xA4 or 0xA5 => GestureCore.ModifierKeys.Alt,
            0x5B or 0x5C => GestureCore.ModifierKeys.Win,
            _ => GestureCore.ModifierKeys.None,
        };
        if (bit != GestureCore.ModifierKeys.None)
        {
            if (isDown) _heldMods |= bit; else _heldMods &= ~bit;
            return true;   // 修饰键也吞：放行 Win 会在松开时弹出开始菜单
        }
        // 主键的抬起只吞不落账：吞掉按下之后，抬起漏出去会变成「只抬起没按下」的畸形键
        if (!isDown) return true;
        if (vk == 0x1B) { Commit(""); return true; }
        var key = KeyInterop.KeyFromVirtualKey((int)vk);
        if (key == Key.None) return true;
        var wpfMods = ModifierKeys.None;
        if ((_heldMods & GestureCore.ModifierKeys.Control) != 0) wpfMods |= ModifierKeys.Control;
        if ((_heldMods & GestureCore.ModifierKeys.Alt) != 0) wpfMods |= ModifierKeys.Alt;
        if ((_heldMods & GestureCore.ModifierKeys.Shift) != 0) wpfMods |= ModifierKeys.Shift;
        if ((_heldMods & GestureCore.ModifierKeys.Win) != 0) wpfMods |= ModifierKeys.Windows;
        Commit(EditorChord.FromKey(wpfMods, key).ToString());
        return true;
    }

    /// <summary>录成一枚键位。钩子回调里只记账并把收尾投递到 UI 线程：
    /// 宿主拿到键位会落盘、重装全局钩子，那是不能在钩子回调里做的事（在钩子内部卸钩会把它自己打断）。</summary>
    /// <param name="chord">键位串；空串=解绑。</param>
    void Commit(string chord)
    {
        Services.HotkeyManager.CaptureSink = null;
        _chord = chord;
        // 钩子线程 → UI 线程：回调里只做一次 BeginInvoke，绝不在输入链上落盘或重装钩子
        var dispatcher = Dispatcher;
        if (dispatcher != null) dispatcher.BeginInvoke(new Action(() =>
        {
            Picked?.Invoke(chord);
            EndCapture();
        }));
        else { Picked?.Invoke(chord); EndCapture(); }
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturing)
        {
            if (e.Key is Key.Space or Key.Enter) { BeginCapture(); e.Handled = true; }
            return;
        }
        e.Handled = true;   // 录入态吞掉一切：窗口的 Esc 关闭、Tab 换焦点都不该在这时候发生
        // 三条路都要认：System = Alt/带修饰的键，ImeProcessed = 输入法把这一击拿走时标成的形态
        var k = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key,
        };
        // 只按下修饰键不算录成，等主键
        if (k is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.System or Key.LWin or Key.RWin) return;
        if (k == Key.Escape) { _chord = ""; Picked?.Invoke(""); EndCapture(); return; }
        // 先认下这一笔再发事件：框自己显示的就是它报出去的那个键位。
        // 只把新值交给宿主而自己还留着原值，改完框里仍是原键帽——用户以为没生效。
        _chord = EditorChord.FromKey(e.KeyboardDevice.Modifiers, k).ToString();
        Picked?.Invoke(_chord);
        EndCapture();
    }

    void Render()
    {
        _host.Children.Clear();
        if (_capturing)
        {
            _host.Children.Add(new TextBlock
            {
                Text = Loc.T(K.Hotkeys.PressANewKeyEsc),
                FontSize = 12,
                Foreground = new SolidColorBrush(Fluent.AccentText()),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        else if (_chord.Length == 0)
        {
            _host.Children.Add(new TextBlock
            {
                Text = _emptyHint,
                FontSize = 12.5,
                Foreground = Theme.TextDimBrush,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        else
        {
            // 走 token 而不是切串：主键是加号时，串里的加号分不清分隔符与键名
            var toks = EditorChord.Parse(_chord) is { } ec ? ec.Tokens() : new[] { _chord };
            _host.Children.Add(Keycaps.Row(toks, Fluent.KeycapTheme()));
        }
        PaintChrome();
        FitWidth();
    }

    /// <summary>把框宽收到内容头上：一枚键帽就占一枚的宽度，不占住一整格。
    /// 录入态那句提示比任何键帽都宽，宽度差用一次 <see cref="Fluent.Motion.StateMs"/> 过渡，
    /// 直接改会让同一行里别的键位框跟着跳。</summary>
    void FitWidth()
    {
        _host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double target = Math.Max(MinWidth, Math.Ceiling(_host.DesiredSize.Width)
            + Padding.Left + Padding.Right + BorderThickness.Left + BorderThickness.Right);
        double from = ActualWidth;
        BeginAnimation(WidthProperty, null);
        // 还没排版过就没有「当前宽度」可过渡，第一帧直接落位
        if (from < 1 || Math.Abs(from - target) < 0.5) { Width = target; return; }
        Width = from;
        BeginAnimation(WidthProperty, new DoubleAnimation(from, target,
            new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.StateMs)))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }
}
