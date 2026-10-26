using System.Windows.Media;

namespace FrameCapture.Core;

/// <summary>一套配色预设：一批 <c>dark.CardBg</c> → HEX 形式的覆盖值，外加这一套配的强调色，
/// 整体套用即换一整套调色板。</summary>
public sealed class ColorPreset
{
    /// <param name="id">稳定标识，写进设置的 <c>ColorPreset</c> 字段。</param>
    /// <param name="accent">这套配色的强调色 HEX；空串表示沿用出厂强调色。</param>
    /// <param name="colors">覆盖表；空表就是出厂调色板。只写想动的那几枚，其余跟随出厂。</param>
    public ColorPreset(string id, string accent, IReadOnlyDictionary<string, string> colors)
    {
        Id = id;
        Accent = accent;
        Colors = colors;
    }

    /// <summary>稳定标识。</summary>
    public string Id { get; }

    /// <summary>这套配色的强调色 HEX；空串表示沿用出厂强调色。</summary>
    public string Accent { get; }

    /// <summary>这套预设要写进覆盖表的条目。</summary>
    public IReadOnlyDictionary<string, string> Colors { get; }

    /// <summary>这套配色的强调色色值，出厂预设给 <see cref="Theme.DefaultAccent"/>。</summary>
    public Color AccentColor => HexColor.Parse(Accent) ?? Theme.DefaultAccent;

    /// <summary>芯片上那条预览色带：窗口底、面板底、卡片底、行底，四段表面足够认出这套配色的性格。
    /// 摆的都是表面色，明暗两侧放在一起才分得出深浅层次，正文色那种极端值只会把整条带子拉成一个色。</summary>
    /// <param name="dark">按哪一侧主题预览。</param>
    /// <returns>四枚不透明颜色，按从左到右排列。</returns>
    public Color[] Swatches(bool dark)
    {
        var keys = new[] { "WindowBg", "PanelBg", "CardBg", "RowBg" };
        var got = new Color[keys.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            var c = Colors.TryGetValue((dark ? "dark." : "light.") + keys[i], out var hex) ? HexColor.Parse(hex) : null;
            got[i] = Theme.Opaque(c ?? Theme.DefaultColor(keys[i], dark));
        }
        return got;
    }
}

/// <summary>内置配色预设。每套都是成对写的明暗两值，切主题不会掉回出厂色；
/// 每套另配一枚强调色，套用时一起落，整屏的颜色才算一个整体。</summary>
public static class ColorPresets
{
    static Dictionary<string, string> Map(params (string Key, string Hex)[] items)
    {
        var d = new Dictionary<string, string>(items.Length, StringComparer.Ordinal);
        foreach (var (k, h) in items) d[k] = h;
        return d;
    }

    /// <summary>出厂调色板：覆盖表为空，一切跟随 <see cref="Theme"/> 的出厂值。</summary>
    public static readonly ColorPreset Default = new("default", "", Map());

    /// <summary>石墨：不带色相的中性灰阶，屏幕内容与界面之间不互相染色；
    /// 强调色退到偏冷的钢蓝，与灰阶同族而认得出。</summary>
    public static readonly ColorPreset Graphite = new("graphite", "#8FA8C8", Map(
        ("dark.WindowBg", "#1A1B1D"), ("dark.PanelBg", "#232427"), ("dark.ChromeBg", "#25262A"),
        ("dark.CardBg", "#202124"), ("dark.RowBg", "#2A2C30"), ("dark.NavBg", "#1E1F22"),
        ("dark.RowHover", "#35373C"), ("dark.TrackBg", "#2DFFFFFF"),
        ("dark.Text", "#F5F6F8"), ("dark.TextDim", "#C6C9D0"),
        ("dark.Stroke", "#3DFFFFFF"), ("dark.StrokeSoft", "#24FFFFFF"),
        ("dark.Hover", "#1FFFFFFF"), ("dark.Press", "#33FFFFFF"),
        ("light.WindowBg", "#F4F5F6"), ("light.PanelBg", "#FFFFFF"), ("light.ChromeBg", "#ECEDEF"),
        ("light.CardBg", "#FFFFFF"), ("light.RowBg", "#F0F1F3"), ("light.NavBg", "#EAEBEE"),
        ("light.RowHover", "#E5E7EA"), ("light.TrackBg", "#1F000000"),
        ("light.Text", "#16171A"), ("light.TextDim", "#585D66"),
        ("light.Stroke", "#33000000"), ("light.StrokeSoft", "#1F000000"),
        ("light.Hover", "#14000000"), ("light.Press", "#24000000")));

    /// <summary>午夜蓝：暗侧整套沉进深海蓝，描边与轨道带一点蓝白；亮侧同色相的浅蓝灰。
    /// 层级靠明度拉开，四层表面之间各差一档，卡片不靠描边也分得开。</summary>
    public static readonly ColorPreset Midnight = new("midnight", "#5AB0FF", Map(
        ("dark.WindowBg", "#0F1626"), ("dark.PanelBg", "#172033"), ("dark.ChromeBg", "#1A2338"),
        ("dark.CardBg", "#1B2437"), ("dark.RowBg", "#222E46"), ("dark.NavBg", "#141C2C"),
        ("dark.RowHover", "#2A3A58"), ("dark.TrackBg", "#33A8C6FF"),
        ("dark.Text", "#F2F6FF"), ("dark.TextDim", "#C2CEE4"),
        ("dark.Stroke", "#40A8C6FF"), ("dark.StrokeSoft", "#26A8C6FF"),
        ("dark.Hover", "#1EFFFFFF"), ("dark.Press", "#2EFFFFFF"),
        ("light.WindowBg", "#F2F5FB"), ("light.PanelBg", "#FFFFFF"), ("light.ChromeBg", "#E6ECF7"),
        ("light.CardBg", "#FFFFFF"), ("light.RowBg", "#EDF1F9"), ("light.NavBg", "#E7EDF8"),
        ("light.RowHover", "#E3E9F5"), ("light.TrackBg", "#1F2A3A5C"),
        ("light.Text", "#101827"), ("light.TextDim", "#4F5C74"),
        ("light.Stroke", "#33233247"), ("light.StrokeSoft", "#1F233247"),
        ("light.Hover", "#14233247"), ("light.Press", "#24233247")));

    /// <summary>暖沙：整套往暖褐走，亮侧像纸、暗侧像灯下的木桌。强调色是那盏灯本身。</summary>
    public static readonly ColorPreset Sand = new("sand", "#E0A35C", Map(
        ("dark.WindowBg", "#1E1A15"), ("dark.PanelBg", "#29241D"), ("dark.ChromeBg", "#2B2620"),
        ("dark.CardBg", "#262119"), ("dark.RowBg", "#332C23"), ("dark.NavBg", "#221D17"),
        ("dark.RowHover", "#40382C"), ("dark.TrackBg", "#2DE8D9BF"),
        ("dark.Text", "#FBF5EA"), ("dark.TextDim", "#D3C7B4"),
        ("dark.Stroke", "#3BE8D9BF"), ("dark.StrokeSoft", "#24E8D9BF"),
        ("dark.Hover", "#1EF5E8D2"), ("dark.Press", "#2EF5E8D2"),
        ("light.WindowBg", "#FAF6EE"), ("light.PanelBg", "#FFFDF8"), ("light.ChromeBg", "#F1E9DC"),
        ("light.CardBg", "#FFFDF8"), ("light.RowBg", "#F5EEE2"), ("light.NavBg", "#F1E9DB"),
        ("light.RowHover", "#ECE3D3"), ("light.TrackBg", "#1F5A4A32"),
        ("light.Text", "#21190F"), ("light.TextDim", "#6A5C48"),
        ("light.Stroke", "#335A4A32"), ("light.StrokeSoft", "#1F5A4A32"),
        ("light.Hover", "#145A4A32"), ("light.Press", "#245A4A32")));

    /// <summary>森林：灰绿表面配青绿强调色，暗侧像雨后的林荫，亮侧是带一点绿的纸。</summary>
    public static readonly ColorPreset Forest = new("forest", "#54C98D", Map(
        ("dark.WindowBg", "#121A15"), ("dark.PanelBg", "#1A241D"), ("dark.ChromeBg", "#1C2720"),
        ("dark.CardBg", "#18221C"), ("dark.RowBg", "#223026"), ("dark.NavBg", "#151F19"),
        ("dark.RowHover", "#2A3B30"), ("dark.TrackBg", "#2DBDD9C0"),
        ("dark.Text", "#F1F7F2"), ("dark.TextDim", "#BCCFC2"),
        ("dark.Stroke", "#3ACBE3D3"), ("dark.StrokeSoft", "#24CBE3D3"),
        ("dark.Hover", "#1EFFFFFF"), ("dark.Press", "#2EFFFFFF"),
        ("light.WindowBg", "#F3F7F2"), ("light.PanelBg", "#FFFFFF"), ("light.ChromeBg", "#E9F0E7"),
        ("light.CardBg", "#FFFFFF"), ("light.RowBg", "#F0F5EF"), ("light.NavBg", "#EAF1E8"),
        ("light.RowHover", "#E6EDE4"), ("light.TrackBg", "#1F36503F"),
        ("light.Text", "#121A14"), ("light.TextDim", "#4E5F52"),
        ("light.Stroke", "#33374F3F"), ("light.StrokeSoft", "#1F374F3F"),
        ("light.Hover", "#14374F3F"), ("light.Press", "#24374F3F")));

    /// <summary>高对比：表面收到接近纯黑与纯白，描边与叠加层加浓、次要文字提到接近正文。
    /// 给看不清低对比界面的人一个能用的档，明暗两侧都留出最大的明度差。</summary>
    public static readonly ColorPreset Contrast = new("contrast", "#2668D9", Map(
        ("dark.WindowBg", "#000000"), ("dark.PanelBg", "#0B0B0D"), ("dark.ChromeBg", "#0A0A0C"),
        ("dark.CardBg", "#121216"), ("dark.RowBg", "#1C1F24"), ("dark.NavBg", "#0D0F12"),
        ("dark.RowHover", "#262A31"), ("dark.TrackBg", "#4DFFFFFF"),
        ("dark.Text", "#FFFFFF"), ("dark.TextDim", "#E9ECF1"),
        ("dark.Stroke", "#99FFFFFF"), ("dark.StrokeSoft", "#66FFFFFF"),
        ("dark.Hover", "#33FFFFFF"), ("dark.Press", "#4DFFFFFF"),
        ("light.WindowBg", "#FFFFFF"), ("light.PanelBg", "#FFFFFF"), ("light.ChromeBg", "#FFFFFF"),
        ("light.CardBg", "#FFFFFF"), ("light.RowBg", "#F7F8FB"), ("light.NavBg", "#F2F4F8"),
        ("light.RowHover", "#E8EBF1"), ("light.TrackBg", "#3D000000"),
        ("light.Text", "#000000"), ("light.TextDim", "#33383F"),
        ("light.Stroke", "#77000000"), ("light.StrokeSoft", "#44000000"),
        ("light.Hover", "#26000000"), ("light.Press", "#3A000000")));

    /// <summary>设置页「配色」里按顺序摆出的全部预设。</summary>
    public static readonly IReadOnlyList<ColorPreset> All =
        new[] { Default, Graphite, Midnight, Sand, Forest, Contrast };

    /// <summary>按标识找预设。</summary>
    /// <param name="id">设置里存的标识；null、空或认不出都回 <see cref="Default"/>。</param>
    /// <returns>命中的预设，或出厂预设。</returns>
    public static ColorPreset Find(string? id)
        => All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal)) ?? Default;
}
