using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GE = GestureEditor;

namespace FrameCapture.Core;

/// <summary>
/// 键位录入框：点一下，按下新键即改键，按 Esc 取消这个键位。
/// 与手势配置器的组合键框同一套形态——常态显示键帽，录入态显示提示，边框在录入态转成强调色。
/// 录入期间吞掉所有按键，窗口自己的 Esc 关闭与 Tab 焦点移动都不会被触发。
/// </summary>
public sealed class ChordCaptureBox : Border
{
    readonly Grid _host = new();
    bool _capturing;
    string _chord = "";
    string _emptyHint = Loc.T("点击设置");

    /// <summary>键位改定：参数是新的键位串，空串表示解绑。</summary>
    public event Action<string>? Picked;

    /// <summary>录入中途失焦：既没改键也没解绑，这一笔不算。新加的键位框据此把自己收回去。</summary>
    public event Action? Cancelled;

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
        MinWidth = 116;
        Background = new SolidColorBrush(Theme.CardBg);
        // 键帽在框里居中：表格里的框等宽对齐，键帽长短不一时靠边会让每行的键位错开
        _host.HorizontalAlignment = HorizontalAlignment.Center;
        Child = _host;
        MouseLeftButtonUp += (_, _) => { if (_capturing) EndCapture(); else BeginCapture(); };
        KeyDown += (_, e) =>
        {
            if (!_capturing && e.Key is Key.Space or Key.Enter) { BeginCapture(); e.Handled = true; }
        };
        PreviewKeyDown += OnPreviewKeyDown;
        LostFocus += (_, _) => CancelIfCapturing();
        Render();
    }

    /// <summary>进入录入态：等一个主键，边框转强调色。设置页的加号按钮用它让新键位框一出来就在等键。</summary>
    public void BeginCapture()
    {
        _capturing = true;
        Focus();
        Render();
    }

    void EndCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        Render();
    }

    /// <summary>录入态下丢了焦点：这一笔作废，既不落键也不解绑。</summary>
    void CancelIfCapturing()
    {
        if (!_capturing) return;
        _capturing = false;
        Render();
        Cancelled?.Invoke();
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturing)
        {
            if (e.Key is Key.Space or Key.Enter) { BeginCapture(); e.Handled = true; }
            return;
        }
        e.Handled = true;   // 录入态吞掉一切：窗口的 Esc 关闭、Tab 换焦点都不该在这时候发生
        var k = e.Key == Key.System ? e.SystemKey : e.Key;
        // 只按下修饰键不算录成，等主键
        if (k is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.System or Key.LWin or Key.RWin) return;
        if (k == Key.Escape) { Picked?.Invoke(""); EndCapture(); return; }
        Picked?.Invoke(EditorChord.FromKey(e.KeyboardDevice.Modifiers, k).ToString());
        EndCapture();
    }

    void Render()
    {
        _host.Children.Clear();
        if (_capturing)
        {
            _host.Children.Add(new TextBlock
            {
                Text = Loc.T("按新键 · Esc 解绑"),
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
                Foreground = new SolidColorBrush(Theme.TextDim),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        else
        {
            // 走 token 而不是切串：主键是加号时，串里的加号分不清分隔符与键名
            var toks = EditorChord.Parse(_chord) is { } ec ? ec.Tokens() : new[] { _chord };
            _host.Children.Add(GE.Keycaps.Row(toks, new GE.KeycapTheme
            {
                Face = Fluent.KeycapFace(),
                Text = Theme.Text,
                Stroke = Theme.Stroke,
                Dim = Theme.TextDim,
                Size = 12,
            }));
        }
        BorderBrush = new SolidColorBrush(_capturing ? Theme.Accent : Theme.Stroke);
    }
}
