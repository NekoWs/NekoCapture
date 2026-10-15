using System.Windows.Input;

namespace FrameCapture.Core;

/// <summary>编辑器里一条可自定义的命令。</summary>
/// <param name="Id">稳定标识，写进 <see cref="AppSettings.EditorKeys"/>；改名会丢用户配置，只增不改。</param>
/// <param name="Name">设置页与帮助窗里显示的动作名。</param>
/// <param name="Group">设置页里的分组标题。</param>
/// <param name="Chords">默认键位串，按分派顺序排；空串表示这一槽不占键。一条命令可以有多个按法，
/// 每个按法在设置页里各自独立可改。</param>
/// <param name="NeedsSelection">只有在画布上选中了对象时才成立的命令。这类命令在分派时先问一遍，
/// 问不中就交给同一个键上的其他命令——<c>Delete</c> 同时挂着「删除选中对象」和「撤销」就是这个情形。</param>
/// <returns>按给定值构造的不可变记录。</returns>
public sealed record EditorCommand(string Id, string Name, string Group, string[] Chords, bool NeedsSelection = false)
{
    /// <summary>写一条命令，最多三个默认按法，后面的槽位省略即无。</summary>
    /// <param name="id">命令标识。</param>
    /// <param name="name">显示名。</param>
    /// <param name="group">分组标题。</param>
    /// <param name="chord">主键位串。</param>
    /// <param name="alt">第二个按法，可省。</param>
    /// <param name="alt2">第三个按法，可省。</param>
    /// <param name="needsSelection">是否只在有选中对象时成立。</param>
    public EditorCommand(string id, string name, string group, string chord, string? alt = null, string? alt2 = null, bool needsSelection = false)
        : this(id, name, group,
            new[] { chord }.Concat(new[] { alt, alt2 }.Where(s => !string.IsNullOrEmpty(s))).ToArray(), needsSelection)
    {
    }

    /// <summary>第 <paramref name="slot"/> 个按法在设置里存于哪个键。</summary>
    /// <param name="slot">槽位，0 是主键位。</param>
    /// <returns>配置字典里的键：主键位用命令 Id，其余槽位用 <c>Id.1</c>、<c>Id.2</c>。</returns>
    public string SettingKey(int slot) => slot == 0 ? Id : Id + "." + slot;

    /// <summary>这条命令有几个按法槽位。</summary>
    /// <value>默认键位的个数，至少一个。</value>
    public int Slots => Chords.Length;
}

/// <summary>
/// 编辑器命令表：键位分派、设置页键位表、帮助窗三处共用的唯一数据源。
/// 表内顺序即同一键位上的分派顺序，需要选中对象的条件命令排在最前。
/// </summary>
public static class EditorCommands
{
    /// <summary>分组标题，设置页按这个顺序分段。</summary>
    public static readonly string[] Groups = { "绘制工具", "编辑", "导出", "视图", "播放", "时间轴裁剪", "窗口" };

    /// <summary>全部命令。绘制工具一节由 <see cref="ToolCatalog"/> 生成，工具与键位不会两处各写一遍。</summary>
    public static readonly EditorCommand[] All = Build();

    static EditorCommand[] Build()
    {
        var list = new List<EditorCommand>
        {
            new("tool.X", ToolCatalog.ExitName, "绘制工具", "X"),
        };
        list.AddRange(ToolCatalog.Tools.Select(t => new EditorCommand("tool." + t.Key, t.Tip, "绘制工具", t.Key)));
        list.AddRange(new[]
        {
            new EditorCommand("delete-object", "删除选中对象", "编辑", new[] { "Delete", "Backspace" }, NeedsSelection: true),
            new EditorCommand("undo", "撤销", "编辑", "Ctrl+Z", "Ctrl+U", "Delete"),
            new EditorCommand("redo", "重做", "编辑", "Ctrl+Y", "Y"),
            new EditorCommand("color-custom", "取色与自定义颜色", "编辑", "K"),
            new EditorCommand("width-popup", "线宽选择", "编辑", "W"),

            new EditorCommand("save-frame", "保存当前帧", "导出", "Ctrl+S"),
            new EditorCommand("copy-frame", "复制当前帧", "导出", "Ctrl+C"),
            new EditorCommand("pin-frame", "钉到屏幕", "导出", "Ctrl+P"),
            new EditorCommand("ocr", "识别文字", "导出", "Ctrl+O"),
            new EditorCommand("save-recording", "导出录制", "导出", "Ctrl+E"),
            new EditorCommand("save-afterimage", "保存视觉暂留合成图", "导出", "Ctrl+H"),

            new EditorCommand("zoom-in", "放大", "视图", "Ctrl++", "PgUp"),
            new EditorCommand("zoom-out", "缩小", "视图", "Ctrl+-", "PgDn"),
            new EditorCommand("zoom-fit", "适配窗口", "视图", "Ctrl+0", "Home"),
            new EditorCommand("zoom-one", "逐像素 1:1", "视图", "Ctrl+1", "End"),

            new EditorCommand("play-toggle", "播放 / 暂停", "播放", "Space"),
            new EditorCommand("frame-prev", "上一帧", "播放", "Left"),
            new EditorCommand("frame-next", "下一帧", "播放", "Right"),
            new EditorCommand("sec-prev", "往前一秒", "播放", "Shift+Left"),
            new EditorCommand("sec-next", "往后一秒", "播放", "Shift+Right"),
            new EditorCommand("speed-up", "播放加速", "播放", "+"),
            new EditorCommand("speed-down", "播放减速", "播放", "-"),
            new EditorCommand("speed-reset", "播放常速", "播放", "0"),

            new EditorCommand("trim-a", "裁剪起点设为当前帧", "时间轴裁剪", "["),
            new EditorCommand("trim-b", "裁剪终点设为当前帧", "时间轴裁剪", "]"),
            new EditorCommand("trim-clear", "清除裁剪区间", "时间轴裁剪", "Backspace"),

            new EditorCommand("commit", "确定并复制", "窗口", "Enter"),
            new EditorCommand("close", "取消并关闭", "窗口", "Esc"),
            new EditorCommand("help", "帮助", "窗口", "F1"),
        });
        return list.ToArray();
    }

    /// <summary>按 Id 取命令。</summary>
    /// <param name="id">命令标识。</param>
    /// <returns>找到的命令；Id 不在表里返回 null。</returns>
    public static EditorCommand? Find(string id) => All.FirstOrDefault(c => c.Id == id);

    /// <summary>这条命令第 slot 个按法当前生效的键位串。</summary>
    /// <param name="s">用户设置。</param>
    /// <param name="c">命令。</param>
    /// <param name="slot">按法槽位，0 是主键位。</param>
    /// <returns>用户改过取用户值，空串表示解绑；没改过取默认值，超出默认按法个数时为空串。</returns>
    public static string ChordFor(AppSettings s, EditorCommand c, int slot = 0) =>
        s.EditorKeys.TryGetValue(c.SettingKey(slot), out var v) ? v
            : slot < c.Chords.Length ? c.Chords[slot] : "";

    /// <summary>这条命令是否被用户动过——设置页据此决定「恢复」按钮是否出现。</summary>
    /// <param name="s">用户设置。</param>
    /// <param name="c">命令。</param>
    /// <returns>任一按法槽位与默认值不同则为真。</returns>
    public static bool IsCustomized(AppSettings s, EditorCommand c)
    {
        for (int i = 0; i < c.Slots; i++)
            if (ChordFor(s, c, i) != c.Chords[i]) return true;
        return false;
    }

    /// <summary>把全部命令的生效键位摊成一张分派表。</summary>
    /// <param name="s">用户设置。</param>
    /// <returns>按键位归一后的分组表：同一个键位上挂着多条命令时，需要选中对象的条件命令排在前面，
    /// 其余按 <see cref="All"/> 的表内顺序。解绑与认不出的键位不进表。</returns>
    public static Dictionary<(ModifierKeys Mods, Key Key), List<string>> BuildMap(AppSettings s)
    {
        var map = new Dictionary<(ModifierKeys, Key), List<string>>();
        foreach (var c in All.Where(c => c.NeedsSelection).Concat(All.Where(c => !c.NeedsSelection)))
            for (int slot = 0; slot < c.Slots; slot++)
            {
                var ch = EditorChord.Parse(ChordFor(s, c, slot));
                if (ch == null) continue;
                var key = (ch.Value.Mods, ch.Value.Key);
                if (!map.TryGetValue(key, out var ids)) map[key] = ids = new List<string>();
                if (!ids.Contains(c.Id)) ids.Add(c.Id);
            }
        return map;
    }

    /// <summary>找出撞键的命令：同一个键位上挂着多条不互斥的命令。</summary>
    /// <param name="s">用户设置。</param>
    /// <returns>键位串 → 占用它的命令 Id 列表，只含撞了两条以上的键位。
    /// 条件命令与无条件命令同键是设计内的分工，不算撞键。</returns>
    public static List<(string Chord, List<string> Ids)> Conflicts(AppSettings s)
    {
        var byChord = new Dictionary<string, List<string>>();
        foreach (var c in All.Where(c => !c.NeedsSelection))
            for (int slot = 0; slot < c.Slots; slot++)
            {
                var ch = EditorChord.Parse(ChordFor(s, c, slot));
                if (ch == null) continue;
                var key = ch.Value.ToString();
                if (!byChord.TryGetValue(key, out var ids)) byChord[key] = ids = new List<string>();
                if (!ids.Contains(c.Id)) ids.Add(c.Id);
            }
        return byChord.Where(kv => kv.Value.Count > 1).Select(kv => (kv.Key, kv.Value)).ToList();
    }
}
