using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameCapture.Capture;
using FrameCapture.Services;

namespace FrameCapture.Encoding;

/// <summary>
/// 给 Agent 看的逐帧序列单图：横向胶片条（每行最多 8 格），每格带帧号与时间码。
/// 目标：一张 PNG 传达整段动画，省 Token。
/// </summary>
public static class AgentStrip
{
    /// <summary>抽样索引——先按步长 everyN 取（1=逐帧），若超过 desired 上限再均匀稀释到 desired。
    /// 纯函数供编辑器小窗预览计数与 SelfTest 锁语义。</summary>
    /// <param name="n">总帧数；小于等于 0 得到空列表。</param>
    /// <param name="desired">结果帧数上限；0 或负数 = 不设上限，只按步长取。</param>
    /// <param name="everyN">步长，小于 1 按 1 处理（即逐帧）。</param>
    /// <returns>升序且不重复的帧下标，首帧恒在列；发生均匀稀释时末帧同样入选。</returns>
    public static List<int> SampleIndices(int n, int desired, int everyN)
    {
        var list = new List<int>();
        if (n <= 0) return list;
        everyN = Math.Max(1, everyN);
        for (int i = 0; i < n; i += everyN) list.Add(i);
        if (desired > 0 && list.Count > desired)
        {
            var thin = new List<int>(desired);
            for (int k = 0; k < desired; k++)
                thin.Add(list[(int)Math.Round(k * (list.Count - 1) / (double)Math.Max(1, desired - 1))]);
            return thin;
        }
        return list;
    }

    /// <summary>
    /// 由帧序列渲染胶片条位图：每行最多 8 格，每格下方标帧号与秒数。
    /// 本方法只在内存里出图，写文件或写剪贴板由调用方决定。
    /// </summary>
    /// <param name="frames">源帧序列，必须覆盖 idxs 里的每个下标；缩放比按 idxs[0] 那帧的宽高定。</param>
    /// <param name="idxs">要上格的帧下标，数组顺序即格子排列顺序。<b>为空即抛异常</b>：空条没有意义。</param>
    /// <returns>已 Freeze 的 Pbgra32 位图，96 DPI，可跨线程使用。
    /// 单格宽固定 288，高按帧宽高比并封顶 360。</returns>
    /// <exception cref="ArgumentException">idxs 为空。</exception>
    public static BitmapSource Render(IReadOnlyList<CapturedFrame> frames, IReadOnlyList<int> idxs)
    {
        int take = idxs.Count;
        if (take == 0) throw new ArgumentException("empty strip");

        int cols = Math.Min(take, 8);
        int rows = (take + cols - 1) / cols;

        // 单元格尺寸：宽 288，高按帧宽高比
        int srcW = frames[idxs[0]].W, srcH = frames[idxs[0]].H;
        double scale = 288.0 / srcW;
        int cellW = (int)Math.Round(srcW * scale);
        int cellH = (int)Math.Round(srcH * scale);
        cellH = Math.Min(cellH, 360);
        int pad = 8, label = 18;
        double fw = cellW, fh = Math.Min(cellH, (int)(cellW * srcH / (double)srcW));

        int imgW = cols * (cellW + pad) + pad;
        int imgH = rows * ((int)fh + label + pad) + pad;

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)), null,
                new Rect(0, 0, imgW, imgH));

            var typeface = new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            for (int i = 0; i < take; i++)
            {
                int r = i / cols, c = i % cols;
                int x = pad + c * (cellW + pad);
                int y = pad + r * ((int)fh + label + pad);
                var frame = frames[idxs[i]];
                var bmp = BgraImage.ToSource(frame);
                dc.DrawImage(bmp, new Rect(x, y, fw, fh));
                var ft = new FormattedText(
                    $"F{idxs[i]:00}  {frame.TimeMs / 1000.0:0.00}s",
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, 12,
                    new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)), 1.0);
                dc.DrawText(ft, new Point(x + 2, y + fh + 1));
            }
        }

        var rtb = new RenderTargetBitmap(imgW, imgH, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>渲染胶片条并写 PNG 文件，是带落盘的完整入口（<see cref="Render"/> 只出图）。
    /// 见 SelfTest 的胶片条落盘用例。</summary>
    /// <param name="seq">源序列，逐帧参与抽样（步长 1）。</param>
    /// <param name="desiredFrames">上格帧数上限；0 或负数 = 全帧上条。</param>
    /// <param name="path">PNG 目标路径；已存在则覆盖，所在目录必须已存在。</param>
    /// <returns>原样返回 <c>path</c>，便于调用方拿返回值直接判落盘结果。</returns>
    public static string Generate(CapturedSequence seq, int desiredFrames, string path)
    {
        var idxs = SampleIndices(seq.Frames.Count, desiredFrames, 1);
        var img = Render(seq.Frames, idxs);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(img));
        using var fs = File.Create(path);
        enc.Save(fs);
        return path;
    }
}
