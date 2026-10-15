using System;
using System.Collections.Generic;

namespace FrameCapture.Core;

/// <summary>
/// 英文译名表：键是界面上的中文原文，值是英文。表按界面分区，一个分区一个 partial 文件，
/// 改完一个窗口就加一段。缺译不是错误——<see cref="Loc.T"/> 回退中文；
/// 但译名里不该再有汉字，也不该有括号标注，SelfTest 的 loc 用例逐条盯着这两点。
/// </summary>
public static partial class StringsEn
{
    static readonly List<string> _conflicts = new();
    static Dictionary<string, string>? _table;

    /// <summary>全部译名分区。新分区必须在这里登记，没登记的分区等于整段没翻译。</summary>
    static (string Zh, string En)[][] Batches => new (string Zh, string En)[][] { Shell, Settings, Pages, Capture, Record, Pins, Gestures, Keys };

    /// <summary>
    /// 合成后的整张表：首次取用时构建，同一句中文重复登记时保留先登记的那条。
    /// 构建写在属性里而不是静态字段初始化器里——分区数组分布在本类的多个 partial 文件里，
    /// 字段初始化器跨文件的先后由编译器定，在初始化器里读它们可能读到 null。
    /// </summary>
    /// <returns>中文原文 → 英文译名；只读使用，别往里写。</returns>
    public static Dictionary<string, string> Table => _table ??= Build();

    /// <summary>
    /// 同一句中文在不同分区被译成两个不同英文的条目。构建时收集而不是抛异常——
    /// 静态构造里抛异常会带走整个应用，而这条缺陷的表现只是某句文案用了另一个说法。
    /// </summary>
    /// <returns>冲突条目的中文原文；空表表示没有冲突。</returns>
    public static IReadOnlyList<string> Conflicts => _conflicts;

    static Dictionary<string, string> Build()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var batch in Batches)
            foreach (var (zh, en) in batch)
            {
                if (d.TryGetValue(zh, out var prev))
                {
                    if (prev != en) _conflicts.Add(zh);
                    continue;
                }
                d[zh] = en;
            }
        return d;
    }
}
