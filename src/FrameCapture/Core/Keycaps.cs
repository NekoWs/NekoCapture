using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FrameCapture.Core;

/// <summary>键帽配色：由调用方随主题注入，画键帽的这一件不认识深浅。</summary>
public sealed class KeycapTheme
{
    /// <summary>键帽底色，取卡面色叠一层 hover（<see cref="Keycaps.Lift"/>）。</summary>
    public Color Face;

    /// <summary>键帽字色。</summary>
    public Color Text;

    /// <summary>键帽描边，取分隔线色。</summary>
    public Color Stroke;

    /// <summary>次要文字色：未设键时的占位文案用它。</summary>
    public Color Dim;

    /// <summary>键帽字号，默认 12。</summary>
    public double Size = 12;
}

/// <summary>
/// 组合键的键帽渲染：每个键一枚圆角小片，片与片之间留一点缝，底边描边加粗一档做出按键的厚度感。
/// 键与键之间不画「+」——键帽本身已经说明「同时按下」，中间再夹符号只会把一排键帽切碎。
/// 设置窗的键位行与键位录入框共用这一件，同一串键位在任何地方都是同一个形状。
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
                sp.Children.Add(new TextBlock
                {
                    Text = emptyHint,
                    FontSize = t.Size,
                    Foreground = new SolidColorBrush(t.Dim),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            return sp;
        }
        for (int i = 0; i < tokens.Count; i++)
            sp.Children.Add(Chip(tokens[i], t, i > 0 ? 5 : 0));
        return sp;
    }

    /// <summary>这串文字是不是键位。判它是不是占位文案靠**身份**（就是调用方给的那句），
    /// 不靠「里面有没有中文」——英文的 <c>No key</c> 没有中文，按语言猜会把它切成一枚「No」键帽加一句 key。
    /// 剩下的部分再看有没有中日韩文字：「F2 按下/松开」的组合键部分是 <c>F2</c>、后缀才是中文，照样是键位。</summary>
    /// <param name="keys">键位串。</param>
    /// <param name="separators">分隔多条按法的串，与 <see cref="ChordRow"/> 用同一份。</param>
    /// <param name="placeholder">本界面用的占位文案（如「未设键」/ <c>No key</c>）；与它相等就不是键位。</param>
    /// <returns>能渲染成键帽返回 true；是占位文案或空串返回 false。</returns>
    public static bool LooksLikeKeys(string? keys, IReadOnlyList<string> separators, string? placeholder = null)
    {
        if (string.IsNullOrWhiteSpace(keys)) return false;
        if (placeholder != null && string.Equals(keys, placeholder, StringComparison.Ordinal)) return false;
        var parts = keys.Split(separators.ToArray(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0 && parts.All(p =>
        {
            int cut = p.IndexOf(' ');
            var chord = cut < 0 ? p : p[..cut];
            return chord.Length > 0 && !chord.Any(c => c >= 0x4E00 && c <= 0x9FFF);
        });
    }

    /// <summary>把「多条按法拼成的键位串」渲染成一排键帽：按法之间一条斜杠，
    /// 每条按法后面的触发后缀（如「按下」「松手」）用暗色小字跟在键帽后面。</summary>
    /// <param name="keys">键位串，如 <c>F6，Ctrl+Alt+R</c> 或 <c>F2 按下/松开</c>；没有按法时走 <paramref name="emptyHint"/>。</param>
    /// <param name="t">配色与字号。</param>
    /// <param name="separators">分隔多条按法的串。只该给「本地化逗号」与「带空格的斜杠」：
    /// 触发后缀里的斜杠两边不带空格（<c>按下/松开</c>），带空格才是在分隔两条按法（<c>Enter / Esc</c>）。</param>
    /// <param name="emptyHint">一条按法都没有时的占位文案；传 null 则返回空面板。</param>
    /// <returns>横向排列的键帽面板，可直接塞进 Grid 或 StackPanel。</returns>
    public static FrameworkElement ChordRow(string? keys, KeycapTheme t, IReadOnlyList<string> separators, string? emptyHint = null)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var parts = string.IsNullOrWhiteSpace(keys)
            ? Array.Empty<string>()
            : keys.Split(separators.ToArray(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            if (emptyHint != null) sp.Children.Add(Note(emptyHint, t));
            return sp;
        }
        // 占位文案不是键位：照键位去切，会给它画上一枚键帽，看起来就像一个名叫它自己的键。
        // 认它靠身份（就是 emptyHint 那句），语言无关。
        if (emptyHint != null && string.Equals(keys, emptyHint, StringComparison.Ordinal) ||
            !LooksLikeKeys(keys, separators, emptyHint))
        {
            sp.Children.Add(Note(keys, t));
            return sp;
        }
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) sp.Children.Add(Note("/", t, 5));
            // 组合键与触发后缀以第一个空格分界：Ctrl+Alt+R 没有后缀，F2 按下/松开的后缀是「按下/松开」。
            int cut = parts[i].IndexOf(' ');
            var chord = cut < 0 ? parts[i] : parts[i][..cut];
            var suffix = cut < 0 ? "" : parts[i][(cut + 1)..];
            foreach (var tok in chord.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                sp.Children.Add(Chip(tok, t, sp.Children.Count > 0 ? 5 : 0));
            if (suffix.Length > 0) sp.Children.Add(Note(suffix, t, 6));
        }
        return sp;
    }

    /// <summary>键帽旁边的一句小字：占位文案或触发后缀，暗色、比键帽小一点。</summary>
    /// <param name="text">要显示的文字。</param>
    /// <param name="t">配色与字号。</param>
    /// <param name="gapLeft">与前面那块之间的缝隙。</param>
    /// <returns>一句小字。</returns>
    static TextBlock Note(string text, KeycapTheme t, double gapLeft = 0) => new()
    {
        Text = text,
        FontSize = t.Size,
        Foreground = new SolidColorBrush(t.Dim),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(gapLeft, 0, 0, 0),
    };

    /// <summary>一枚键帽：圆角、描边、底边加粗，内容居中且不小于一个字母键的宽度。</summary>
    /// <param name="token">键帽里显示的文字。</param>
    /// <param name="t">配色与字号。</param>
    /// <param name="gapLeft">与前一枚键帽之间的缝隙。</param>
    /// <returns>键帽控件。</returns>
    static Border Chip(string token, KeycapTheme t, double gapLeft)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(6),
            MinWidth = 24,
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(gapLeft, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(1, 1, 1, 2),
            Background = new SolidColorBrush(t.Face),
            BorderBrush = new SolidColorBrush(t.Stroke),
            Child = new TextBlock
            {
                Text = token,
                TextAlignment = TextAlignment.Center,
                FontFamily = new FontFamily(MonoFont),
                FontSize = t.Size,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(t.Text),
            },
        };
    }
}
