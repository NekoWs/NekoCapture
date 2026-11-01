using NekoCapture.Drawing;

namespace NekoCapture.Core;

/// <summary>
/// 裁剪 → 标注重映射决策表（纯逻辑）。
/// 不变量：完整位于裁剪框内（含 Eps 容差）的标注保留并做坐标变换（全帧归一化 → 裁剪框归一化）；
/// 跨界或框外标注丢弃——钳制会把自由笔/箭头沿裁剪边抹成贴边残影，丢弃才是可预期行为。
/// 像素级往返保证：Bake(Crop(f, r), Remap(a, r)) ≡ Crop(Bake(f, a), r)（对保留标注成立，SelfTest 覆盖）。
/// </summary>
public static class CropRemap
{
    const double Eps = 1e-4;

    /// <summary>按裁剪框重映射标注坐标。</summary>
    /// <param name="anns">坐标为全帧归一化（0..1）的标注列表。</param>
    /// <param name="cx0">裁剪框左上角横坐标，全帧归一化。</param>
    /// <param name="cy0">裁剪框左上角纵坐标，全帧归一化。</param>
    /// <param name="cw">裁剪框宽，全帧归一化；≤0 视为无有效裁剪框，返回空列表。</param>
    /// <param name="ch">裁剪框高，全帧归一化。</param>
    /// <returns>新列表：只含所有点都在框内（含 1e-4 容差）的标注，坐标已换算成裁剪框归一化；输入不被修改。</returns>
    public static List<Annotation> Remap(IReadOnlyList<Annotation> anns, double cx0, double cy0, double cw, double ch)
    {
        var kept = new List<Annotation>();
        if (cw <= 0 || ch <= 0) return kept;
        double x0 = cx0 - Eps, y0 = cy0 - Eps, x1 = cx0 + cw + Eps, y1 = cy0 + ch + Eps;
        foreach (var a in anns)
        {
            if (a.Points.Count == 0) continue;
            bool inside = true;
            foreach (var p in a.Points)
                if (p.X < x0 || p.X > x1 || p.Y < y0 || p.Y > y1) { inside = false; break; }
            if (!inside) continue;
            var na = new Annotation { Kind = a.Kind, Color = a.Color, Thickness = a.Thickness, Text = a.Text };
            foreach (var p in a.Points)
                na.Points.Add(((float)((p.X - cx0) / cw), (float)((p.Y - cy0) / ch)));
            kept.Add(na);
        }
        return kept;
    }
}
