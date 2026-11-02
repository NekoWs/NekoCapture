using System.Windows.Input;
using K = NekoCapture.Core.K;

namespace NekoCapture.Core;

/// <summary>编辑器里一条可自定义的命令。</summary>
/// <param name="Id">稳定标识，写进 <see cref="AppSettings.EditorKeys"/>；改名会丢用户配置，只增不改。</param>
/// <param name="Name">设置页与帮助窗里显示的动作名文案。</param>
/// <param name="Group">设置页里的分组标题文案。</param>
/// <param name="Chords">出厂默认的那组按法，按分派顺序排。用户在设置页里加或减之后，生效的是
/// <see cref="EditorCommands.BindingsFor"/> 取到的那份。</param>
/// <param name="NeedsSelection">只有在画布上选中了对象时才成立的命令。这类命令在分派时先问一遍，
/// 问不中就交给同一个键上的其他命令——<c>Delete</c> 同时挂着「删除选中对象」和「撤销」就是这个情形。</param>
/// <returns>按给定值构造的不可变记录。</returns>
public sealed record EditorCommand(string Id, Str Name, Str Group, string[] Chords, bool NeedsSelection = false)
{
    /// <summary>写一条命令，最多三个默认按法，后面的槽位省略即无。</summary>
    /// <param name="id">命令标识。</param>
    /// <param name="name">显示名文案。</param>
    /// <param name="group">分组标题文案。</param>
    /// <param name="chord">主键位串。</param>
    /// <param name="alt">第二个按法，可省。</param>
    /// <param name="alt2">第三个按法，可省。</param>
    /// <param name="needsSelection">是否只在有选中对象时成立。</param>
    public EditorCommand(string id, Str name, Str group, string chord, string? alt = null, string? alt2 = null, bool needsSelection = false)
        : this(id, name, group,
            new[] { chord }.Concat(new[] { alt, alt2 }.Where(s => !string.IsNullOrEmpty(s)).Select(s => s!)).ToArray(),
            needsSelection)
    {
    }
}

/// <summary>
/// 编辑器命令表：键位分派、设置页键位表、帮助窗三处共用的唯一数据源。
/// 表内顺序即同一键位上的分派顺序，需要选中对象的条件命令排在最前。
/// </summary>
public static class EditorCommands
{
    /// <summary>分组标题，设置页按这个顺序分段。</summary>
    public static readonly Str[] Groups = { K.Commands.GroupDrawingtools, K.Common.Edit, K.Commands.GroupExport, K.Commands.GroupView, K.Commands.GroupPlayback, K.Commands.GroupTimelinetrim, K.Commands.GroupWindow };

    /// <summary>全部命令。绘制工具一节由 <see cref="ToolCatalog"/> 生成，工具与键位不会两处各写一遍。</summary>
    public static readonly EditorCommand[] All = Build();

    static EditorCommand[] Build()
    {
        var list = new List<EditorCommand>
        {
            new("tool.X", ToolCatalog.ExitName, K.Commands.GroupDrawingtools, "X"),
        };
        list.AddRange(ToolCatalog.Tools.Select(t => new EditorCommand("tool." + t.Key, t.Tip, K.Commands.GroupDrawingtools, t.Key)));
        list.AddRange(new[]
        {
            new EditorCommand("delete-object", K.Commands.DeleteObjectName, K.Common.Edit, new[] { "Delete", "Backspace" }, NeedsSelection: true),
            new EditorCommand("undo", K.Editor.Undo, K.Common.Edit, "Ctrl+Z", "Ctrl+U", "Delete"),
            new EditorCommand("redo", K.Editor.Redo, K.Common.Edit, "Ctrl+Y", "Y"),
            new EditorCommand("color-custom", K.Commands.ColorCustomName, K.Common.Edit, "K"),
            new EditorCommand("width-popup", K.Commands.WidthPopupName, K.Common.Edit, "W"),

            new EditorCommand("save-frame", K.Editor.SaveCurrentFrame, K.Commands.GroupExport, "Ctrl+S"),
            new EditorCommand("copy-frame", K.Editor.CopyCurrentFrame, K.Commands.GroupExport, "Ctrl+C"),
            new EditorCommand("pin-frame", K.Editor.PinToScreen, K.Commands.GroupExport, "Ctrl+P"),
            new EditorCommand("ocr", K.Common.RecognizeText, K.Commands.GroupExport, "Ctrl+O"),
            new EditorCommand("save-recording", K.Editor.ExportRecording, K.Commands.GroupExport, "Ctrl+E"),
            new EditorCommand("save-afterimage", K.Editor.SaveAfterimageComposite, K.Commands.GroupExport, "Ctrl+H"),

            new EditorCommand("zoom-in", K.Commands.ZoomInName, K.Commands.GroupView, "Ctrl++", "PgUp"),
            new EditorCommand("zoom-out", K.Commands.ZoomOutName, K.Commands.GroupView, "Ctrl+-", "PgDn"),
            new EditorCommand("zoom-fit", K.Commands.ZoomFitName, K.Commands.GroupView, "Ctrl+0", "Home"),
            new EditorCommand("zoom-one", K.Commands.ZoomOneName, K.Commands.GroupView, "Ctrl+1", "End"),

            new EditorCommand("play-toggle", K.Commands.PlayToggleName, K.Commands.GroupPlayback, "Space"),
            new EditorCommand("frame-prev", K.Editor.PreviousFrame, K.Commands.GroupPlayback, "Left"),
            new EditorCommand("frame-next", K.Editor.NextFrame, K.Commands.GroupPlayback, "Right"),
            new EditorCommand("sec-prev", K.Commands.SecPrevName, K.Commands.GroupPlayback, "Shift+Left"),
            new EditorCommand("sec-next", K.Commands.SecNextName, K.Commands.GroupPlayback, "Shift+Right"),
            new EditorCommand("speed-up", K.Commands.SpeedUpName, K.Commands.GroupPlayback, "+"),
            new EditorCommand("speed-down", K.Commands.SpeedDownName, K.Commands.GroupPlayback, "-"),
            new EditorCommand("speed-reset", K.Commands.SpeedResetName, K.Commands.GroupPlayback, "0"),

            new EditorCommand("trim-a", K.Commands.TrimAName, K.Commands.GroupTimelinetrim, "["),
            new EditorCommand("trim-b", K.Commands.TrimBName, K.Commands.GroupTimelinetrim, "]"),
            new EditorCommand("trim-clear", K.Commands.TrimClearName, K.Commands.GroupTimelinetrim, "Backspace"),

            new EditorCommand("commit", K.Commands.CommitName, K.Commands.GroupWindow, "Enter"),
            new EditorCommand("close", K.Commands.CloseName, K.Commands.GroupWindow, "Esc"),
            new EditorCommand("help", K.Tray.Help, K.Commands.GroupWindow, "F1"),
        });
        return list.ToArray();
    }

    /// <summary>按 Id 取命令。</summary>
    /// <param name="id">命令标识。</param>
    /// <returns>找到的命令；Id 不在表里返回 null。</returns>
    public static EditorCommand? Find(string id) => All.FirstOrDefault(c => c.Id == id);

    /// <summary>这条命令当前生效的按法列表。</summary>
    /// <param name="s">用户设置。</param>
    /// <param name="c">命令。</param>
    /// <returns>用户动过取用户那份，一条也没有就是空表；没动过取默认按法。</returns>
    public static List<string> BindingsFor(AppSettings s, EditorCommand c)
    {
        if (!s.EditorKeys.TryGetValue(c.Id, out var v)) return c.Chords.ToList();
        return v.Length == 0
            ? new List<string>()
            : v.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    /// <summary>把一条命令的按法列表写回设置。要在 <see cref="SettingsService.Mutate"/> 里调用。</summary>
    /// <param name="s">要写入的设置。</param>
    /// <param name="c">命令。</param>
    /// <param name="chords">按法串，顺序即分派顺序；空串与重复的按法会被丢掉。</param>
    public static void WriteBindings(AppSettings s, EditorCommand c, IEnumerable<string> chords)
    {
        var kept = new List<string>();
        foreach (var ch in chords)
            if (ch.Length > 0 && !kept.Contains(ch)) kept.Add(ch);
        // 与默认一致就不留配置项：字典里只留真正的偏离，恢复默认键位时不会被自己写过的值绊住
        if (kept.SequenceEqual(c.Chords)) s.EditorKeys.Remove(c.Id);
        else s.EditorKeys[c.Id] = string.Join("|", kept);
    }

    /// <summary>这条命令是否被用户动过——设置页据此决定「恢复」按钮是否出现。</summary>
    /// <param name="s">用户设置。</param>
    /// <param name="c">命令。</param>
    /// <returns>当前按法列表与默认按法不完全一致则为真。</returns>
    public static bool IsCustomized(AppSettings s, EditorCommand c) =>
        !BindingsFor(s, c).SequenceEqual(c.Chords);

    /// <summary>把全部命令的生效键位摊成一张分派表。</summary>
    /// <param name="s">用户设置。</param>
    /// <returns>按键位归一后的分组表：同一个键位上挂着多条命令时，需要选中对象的条件命令排在前面，
    /// 其余按 <see cref="All"/> 的表内顺序。解绑与认不出的键位不进表。</returns>
    public static Dictionary<(ModifierKeys Mods, Key Key), List<string>> BuildMap(AppSettings s)
    {
        var map = new Dictionary<(ModifierKeys, Key), List<string>>();
        foreach (var c in All.Where(c => c.NeedsSelection).Concat(All.Where(c => !c.NeedsSelection)))
            foreach (var raw in BindingsFor(s, c))
            {
                var ch = EditorChord.Parse(raw);
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
            foreach (var raw in BindingsFor(s, c))
            {
                var ch = EditorChord.Parse(raw);
                if (ch == null) continue;
                var key = ch.Value.ToString();
                if (!byChord.TryGetValue(key, out var ids)) byChord[key] = ids = new List<string>();
                if (!ids.Contains(c.Id)) ids.Add(c.Id);
            }
        return byChord.Where(kv => kv.Value.Count > 1).Select(kv => (kv.Key, kv.Value)).ToList();
    }
}
