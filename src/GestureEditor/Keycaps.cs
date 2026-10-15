using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GestureEditor;

/// <summary>键帽配色：由宿主随主题注入，库内不假设深浅。</summary>
public sealed class KeycapTheme
{
    /// <summary>键帽底色，取卡面色。</summary>
    public Color Face;

    /// <summary>键帽字色。</summary>
    public Color Text;

    /// <summary>键帽描边，取分隔线色。</summary>
    public Color Stroke;

    /// <summary>次要文字色：未设键时的占位文案用它。</summary>
    public Color Dim;

    /// <summary>键帽字号，默认 12。</summary>
    public double Size = 12;

    /// <summary>幽灵键帽：无底、描边与字色跟随所在行的文字色。用在会被选中态整片换底的列表行里，
    /// 键帽跟着那一行的字色一起变，不会在 accent 底上留一块卡色补丁。</summary>
    public bool Ghost;
}

/// <summary>
/// 组合键的键帽渲染：每个键一枚圆角小片，片与片之间留一点缝，底边描边加粗一档做出按键的厚度感。
/// 键与键之间不画「+」——键帽本身已经说明「同时按下」，中间再夹符号只会把一排键帽切碎。
/// 设置窗口与手势配置器共用这一份，键位显示在两处保持同一形状。
/// </summary>
public static class Keycaps
{
    /// <summary>等宽字体链：键帽里的字母与数字宽度一致，一排键帽看着才齐。</summary>
    public const string MonoFont = "Consolas, Cascadia Mono, Segoe UI";

    /// <summary>把半透明叠色合成到不透明底色上：键帽底 = 所在表面叠一层 hover，比卡片亮一档才立得住。</summary>
    /// <param name="src">不透明底色，通常是卡片背景。</param>
    /// <param name="over">半透明叠色，通常是主题的 hover。</param>
    /// <returns>合成后的不透明色，alpha 取 <paramref name="src"/>。</returns>
    public static Color Lift(Color src, Color over)
    {
        double a = over.A / 255.0;
        return Color.FromArgb(src.A,
            (byte)(src.R * (1 - a) + over.R * a),
            (byte)(src.G * (1 - a) + over.G * a),
            (byte)(src.B * (1 - a) + over.B * a));
    }

    /// <summary>把组合键串渲染成一排键帽。</summary>
    /// <param name="chord">组合键串，形如 <c>Ctrl+Alt+A</c>；空串走 <paramref name="emptyHint"/>。
    /// 主键本身就是加号的键位请用 <see cref="Row(IReadOnlyList{string}, KeycapTheme, string)"/> 直接给 token，
    /// 串里分不出「分隔用的加号」和「当键用的加号」。</param>
    /// <param name="t">配色与字号。</param>
    /// <param name="emptyHint">未设键时的占位文案；传 null 则返回空面板。</param>
    /// <returns>横向排列的键帽面板，可直接塞进 Grid 或 StackPanel。</returns>
    public static FrameworkElement Row(string? chord, KeycapTheme t, string? emptyHint = null) =>
        Row(string.IsNullOrWhiteSpace(chord)
            ? Array.Empty<string>()
            : chord.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            t, emptyHint);

    /// <summary>把已经拆好的键名渲染成一排键帽。</summary>
    /// <param name="tokens">键名序列，按显示顺序；空序列走 <paramref name="emptyHint"/>。</param>
    /// <param name="t">配色与字号。</param>
    /// <param name="emptyHint">没有键名时的占位文案；传 null 则返回空面板。</param>
    /// <returns>横向排列的键帽面板。</returns>
    public static FrameworkElement Row(IReadOnlyList<string> tokens, KeycapTheme t, string? emptyHint = null)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (tokens.Count == 0)
        {
            if (emptyHint != null)
            {
                var hint = new TextBlock
                {
                    Text = emptyHint,
                    FontSize = t.Size,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                // 幽灵形态不设色：显式赋 null 是「用空画刷画」= 什么都不画，只有不赋值才走继承
                if (!t.Ghost) hint.Foreground = new SolidColorBrush(t.Dim);
                sp.Children.Add(hint);
            }
            return sp;
        }
        for (int i = 0; i < tokens.Count; i++)
            sp.Children.Add(Chip(tokens[i], t, i > 0 ? 5 : 0));
        return sp;
    }

    /// <summary>一枚键帽：圆角、描边、底边加粗，内容居中且不小于一个字母键的宽度。
    /// 幽灵形态不设自己的颜色，字色由所在容器继承，描边绑到同一个字色上并按四成不透明度压淡。</summary>
    /// <param name="token">键帽里显示的文字。</param>
    /// <param name="t">配色与字号。</param>
    /// <param name="gapLeft">与前一枚键帽之间的缝隙。</param>
    /// <returns>键帽控件。</returns>
    static Border Chip(string token, KeycapTheme t, double gapLeft)
    {
        var label = new TextBlock
        {
            Text = token,
            TextAlignment = TextAlignment.Center,
            FontFamily = new FontFamily(MonoFont),
            FontSize = t.Size,
            FontWeight = FontWeights.SemiBold,
        };
        if (!t.Ghost) label.Foreground = new SolidColorBrush(t.Text);
        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            MinWidth = 24,
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(gapLeft, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(1, 1, 1, 2),
            Child = label,
        };
        if (t.Ghost)
        {
            // 字色不设、由容器继承；描边取同一字色的四成透明度，选中行换 accent 底时 outline 自然退到背景里
            Color c = t.Text;
            border.BorderBrush = new SolidColorBrush(Color.FromArgb(0x73, c.R, c.G, c.B));
        }
        else
        {
            border.Background = new SolidColorBrush(t.Face);
            border.BorderBrush = new SolidColorBrush(t.Stroke);
        }
        return border;
    }
}
