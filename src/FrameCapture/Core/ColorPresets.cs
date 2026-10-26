using System.Windows.Media;

namespace FrameCapture.Core;

/// <summary>一套配色预设：一批 <c>dark.CardBg</c> → HEX 形式的覆盖值，整体套用即换一整套调色板。</summary>
public sealed class ColorPreset
{
    /// <param name="id">稳定标识，写进设置的 <c>ColorPreset</c> 字段。</param>
    /// <param name="colors">覆盖表；空表就是出厂调色板。只写想动的那几枚，其余跟随出厂。</param>
    public ColorPreset(string id, IReadOnlyDictionary<string, string> colors)
    {
        Id = id;
        Colors = colors;
    }

    /// <summary>稳定标识。</summary>
    public string Id { get; }

    /// <summary>这套预设要写进覆盖表的条目。</summary>
    public IReadOnlyDictionary<string, string> Colors { get; }

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

/// <summary>内置配色预设。每套都是成对写的明暗两值，切主题不会掉回出厂色。</summary>
public static class ColorPresets
{
    static Dictionary<string, string> Map(params (string Key, string Hex)[] items)
    {
        var d = new Dictionary<string, string>(items.Length, StringComparer.Ordinal);
        foreach (var (k, h) in items) d[k] = h;
        return d;
    }

    /// <summary>出厂调色板：覆盖表为空，一切跟随 <see cref="Theme"/> 的出厂值。</summary>
    public static readonly ColorPreset Default = new("default", Map());

    /// <summary>高对比：表面全部不透明、描边与叠加层加浓、次要文字提到接近正文。</summary>
    public static readonly ColorPreset Contrast = new("contrast", Map(
        ("dark.WindowBg", "#000000"), ("dark.PanelBg", "#0A0A0C"), ("dark.ChromeBg", "#000000"),
        ("dark.CardBg", "#101014"), ("dark.RowBg", "#1A1D22"), ("dark.NavBg", "#0D0F12"),
        ("dark.TextDim", "#E8EAF0"), ("dark.Stroke", "#88FFFFFF"), ("dark.StrokeSoft", "#55FFFFFF"),
        ("dark.Hover", "#33FFFFFF"), ("dark.Press", "#4DFFFFFF"), ("dark.RowHover", "#262A31"),
        ("dark.TrackBg", "#4DFFFFFF"),
        ("light.WindowBg", "#FFFFFF"), ("light.PanelBg", "#FFFFFF"), ("light.ChromeBg", "#FFFFFF"),
        ("light.CardBg", "#FFFFFF"), ("light.RowBg", "#FAFBFD"), ("light.NavBg", "#F5F6F9"),
        ("light.Text", "#000000"), ("light.TextDim", "#3A4048"), ("light.Stroke", "#66000000"),
        ("light.StrokeSoft", "#3D000000"), ("light.Hover", "#26000000"), ("light.Press", "#3A000000"),
        ("light.RowHover", "#E4E7EE"), ("light.TrackBg", "#3D000000")));

    /// <summary>石墨：去掉冷调的中性灰阶，屏幕内容与界面之间不互相染色。</summary>
    public static readonly ColorPreset Graphite = new("graphite", Map(
        ("dark.WindowBg", "#1A1A1A"), ("dark.PanelBg", "#262626"), ("dark.ChromeBg", "#272727"),
        ("dark.CardBg", "#242424"), ("dark.RowBg", "#2E2E2E"), ("dark.NavBg", "#202020"),
        ("dark.RowHover", "#383838"), ("dark.TextDim", "#CFCFCF"),
        ("light.WindowBg", "#F5F5F5"), ("light.PanelBg", "#FFFFFF"), ("light.ChromeBg", "#EAEAEA"),
        ("light.CardBg", "#FFFFFF"), ("light.RowBg", "#F1F1F1"), ("light.NavBg", "#ECECEC"),
        ("light.RowHover", "#E8E8E8"), ("light.TextDim", "#5C5C5C")));

    /// <summary>午夜蓝：暗侧整套染进深蓝，描边与轨道跟着染成蓝白；亮侧同色相的浅蓝灰。</summary>
    public static readonly ColorPreset Midnight = new("midnight", Map(
        ("dark.WindowBg", "#131A29"), ("dark.PanelBg", "#1E2739"), ("dark.ChromeBg", "#1B2334"),
        ("dark.CardBg", "#1B2436"), ("dark.RowBg", "#232E44"), ("dark.NavBg", "#172032"),
        ("dark.RowHover", "#2C3852"), ("dark.TextDim", "#BFC9DE"), ("dark.Stroke", "#40A8C6FF"),
        ("dark.StrokeSoft", "#26A8C6FF"), ("dark.TrackBg", "#33A8C6FF"),
        ("light.WindowBg", "#F1F5FC"), ("light.ChromeBg", "#E3EBF8"), ("light.RowBg", "#EAF0FA"),
        ("light.NavBg", "#E5ECF8"), ("light.RowHover", "#DEE7F6"), ("light.TextDim", "#51607A")));

    /// <summary>暖沙：整套往暖褐走，亮侧像纸，暗侧像灯下的木桌。</summary>
    public static readonly ColorPreset Sand = new("sand", Map(
        ("dark.WindowBg", "#201C16"), ("dark.PanelBg", "#2C2721"), ("dark.ChromeBg", "#2E2820"),
        ("dark.CardBg", "#29241D"), ("dark.RowBg", "#332D24"), ("dark.NavBg", "#262019"),
        ("dark.RowHover", "#3D362B"), ("dark.TextDim", "#D6CCBC"), ("dark.Stroke", "#3BE8D9BF"),
        ("dark.StrokeSoft", "#24E8D9BF"), ("dark.TrackBg", "#2DE8D9BF"),
        ("light.WindowBg", "#FAF6EF"), ("light.PanelBg", "#FFFDF8"), ("light.ChromeBg", "#F2EADF"),
        ("light.CardBg", "#FFFDF8"), ("light.RowBg", "#F4EDE1"), ("light.NavBg", "#F0E8DA"),
        ("light.RowHover", "#ECE2D2"), ("light.TextDim", "#6B5F4E"), ("light.Stroke", "#335A4A32"),
        ("light.StrokeSoft", "#1F5A4A32"), ("light.Hover", "#145A4A32"), ("light.Press", "#245A4A32"),
        ("light.TrackBg", "#1F5A4A32")));

    /// <summary>设置页「配色」里按顺序摆出的全部预设。</summary>
    public static readonly IReadOnlyList<ColorPreset> All = new[] { Default, Contrast, Graphite, Midnight, Sand };

    /// <summary>按标识找预设。</summary>
    /// <param name="id">设置里存的标识；null、空或认不出都回 <see cref="Default"/>。</param>
    /// <returns>命中的预设，或出厂预设。</returns>
    public static ColorPreset Find(string? id)
        => All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal)) ?? Default;
}
