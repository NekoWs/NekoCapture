namespace FrameCapture.Core;

/// <summary>
/// 贴图方向键微调的纯决策：把「按了哪个方向键、有没有按住 Shift」翻成一次位移。
/// 贴图常被用来和屏幕上的既有界面对齐，鼠标拖到单像素对不齐，方向键才有那个精度。
/// </summary>
public static class PinNudge
{
    /// <summary>一格 1 DIP：像素级对齐用的细格。</summary>
    public const double Step = 1;

    /// <summary>按住 Shift 的一格 10 DIP：跨半个屏幕用的粗格。</summary>
    public const double Coarse = 10;

    /// <summary>算出这次按键应给的位移量。</summary>
    /// <param name="key">按下的键。</param>
    /// <param name="shift">是否按住 Shift。</param>
    /// <returns>水平与垂直位移；<paramref name="key"/> 不是方向键时两项都是 0。</returns>
    public static (double Dx, double Dy) Move(System.Windows.Input.Key key, bool shift)
    {
        double s = shift ? Coarse : Step;
        return key switch
        {
            System.Windows.Input.Key.Left => (-s, 0),
            System.Windows.Input.Key.Right => (s, 0),
            System.Windows.Input.Key.Up => (0, -s),
            System.Windows.Input.Key.Down => (0, s),
            _ => (0, 0),
        };
    }
}
