using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameCapture.Capture;
using FrameCapture.Services;

namespace FrameCapture.Encoding;

/// <summary>按帧号与时间码排列的胶片条，每行最多八格。</summary>
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
        if (n <= 0) return new List<int>();
        everyN = Math.Max(1, everyN);
        int count = (n - 1) / everyN + 1;
        int take = desired > 0 ? Math.Min(count, desired) : count;
        var list = new List<int>(take);
        for (int i = 0; i < take; i++)
        {
            int sample = take < count ? (int)Math.Round(i * (count - 1d) / Math.Max(1, take - 1)) : i;
            list.Add(sample * everyN);
        }
        return list;
    }

    /// <summary>
    /// 由帧序列渲染胶片条位图：每行最多 8 格，每格下方标帧号与秒数。
    /// 本方法只在内存里出图，写文件或写剪贴板由调用方决定。
    /// </summary>
    /// <param name="frames">要上格的帧，<b>与 <paramref name="idxs"/> 一一对应</b>：第 i 格画 frames[i]。
    /// 编辑器递进来的是抽样烘焙后的那几帧，不是整段录制——按整段下标去取会越界。
    /// 缩放比按 frames[0] 的宽高定。</param>
    /// <param name="idxs">每格标注的原始帧号（时间轴下标），顺序即格子排列顺序。<b>为空即抛异常</b>：空条没有意义。</param>
    /// <param name="cellTargetW">单格目标宽（像素），高按帧宽高比、封顶 1.25 倍格宽；
    /// 内边距、标号行高与字号都按它缩放。默认 288 是成片尺寸，弹窗预览给个小的即可。</param>
    /// <param name="decimate">true = 每格先按格子大小抽稀再画，供弹窗实时预览用：
    /// 逐格从原始分辨率缩放时几十格要读几百 MB，滑杆跟不上。成片走默认的完整质量。</param>
    /// <returns>已 Freeze 的 Pbgra32 位图，96 DPI，可跨线程使用。</returns>
    /// <exception cref="ArgumentException">idxs 为空，或帧数不够填满格子。</exception>
    public static BitmapSource Render(IReadOnlyList<CapturedFrame> frames, IReadOnlyList<int> idxs,
        int cellTargetW = 288, bool decimate = false)
    {
        if (frames.Count < idxs.Count) throw new ArgumentException("Insufficient strip frames");
        return RenderMapped(i => frames[i], idxs, cellTargetW, decimate);
    }

    /// <summary>逐格读取和渲染，只保留格子尺寸的图像，支持取消。</summary>
    /// <param name="frameAt">按格子下标读取或烘焙一帧。</param>
    /// <param name="idxs">各格标注的原始帧号。</param>
    /// <param name="cellTargetW">格子目标宽度。</param>
    /// <param name="decimate">是否使用快速预览抽稀。</param>
    /// <param name="cancellation">逐格检查的取消信号。</param>
    /// <returns>可跨线程使用的冻结位图。</returns>
    internal static BitmapSource RenderMapped(Func<int, CapturedFrame> frameAt, IReadOnlyList<int> idxs,
        int cellTargetW = 288, bool decimate = false, CancellationToken cancellation = default)
    {
        int take = idxs.Count;
        if (take == 0) throw new ArgumentException("empty strip");
        cancellation.ThrowIfCancellationRequested();
        CapturedFrame? first = frameAt(0);
        cellTargetW = Math.Max(24, cellTargetW);

        int cols = Math.Min(take, 8);
        int rows = (take + cols - 1) / cols;

        // 单元格尺寸：宽 = 目标宽，高按帧宽高比并封顶 1.25 倍格宽
        int srcW = first.W, srcH = first.H;
        int cellW = cellTargetW;
        int cellH = Math.Min((int)Math.Round(srcH * (cellW / (double)srcW)), cellW * 5 / 4);
        int pad = Math.Max(3, cellW / 36), label = Math.Max(11, cellW / 16);
        double font = Math.Max(8, cellW / 24.0);
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
                cancellation.ThrowIfCancellationRequested();
                int r = i / cols, c = i % cols;
                int x = pad + c * (cellW + pad);
                int y = pad + r * ((int)fh + label + pad);
                var frame = i == 0 ? first! : frameAt(i);
                first = null;
                var bmp = decimate ? DecimatedSource(frame, cellW) : CellSource(frame, cellW, Math.Max(1, (int)fh));
                dc.DrawImage(bmp, new Rect(x, y, fw, fh));
                var ft = new FormattedText(
                    $"F{idxs[i]:00}  {frame.TimeMs / 1000.0:0.00}s",
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, font,
                    new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)), 1.0);
                dc.DrawText(ft, new Point(x + 2, y + fh + 1));
            }
        }

        var rtb = new RenderTargetBitmap(imgW, imgH, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    private static BitmapSource CellSource(CapturedFrame frame, int width, int height)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            drawing.DrawImage(BgraImage.ToSource(frame), new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze();
        return bitmap;
    }

    /// <summary>按格子大小整数倍抽稀后出图：每格取 step×step 块的四角均值，输出约等于格子分辨率。
    /// 只用于实时预览——48 格逐格缩放原始帧要读几百 MB，滑杆会卡；抽稀后每格只写几千像素。</summary>
    static BitmapSource DecimatedSource(CapturedFrame f, int targetW)
    {
        int step = Math.Max(1, f.W / Math.Max(1, targetW));
        int w = Math.Max(1, f.W / step), h = Math.Max(1, f.H / step);
        var src = f.Bgra;
        var buf = new byte[w * h * 4];
        int last = step - 1;
        for (int y = 0; y < h; y++)
        {
            int y0 = Math.Min(y * step, f.H - 1) * f.W * 4;
            int y1 = Math.Min(y * step + last, f.H - 1) * f.W * 4;
            int dRow = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                int x0 = Math.Min(x * step, f.W - 1) * 4;
                int x1 = Math.Min(x * step + last, f.W - 1) * 4;
                int p00 = y0 + x0, p01 = y0 + x1, p10 = y1 + x0, p11 = y1 + x1;
                for (int ch = 0; ch < 4; ch++)
                    buf[dRow + x * 4 + ch] =
                        (byte)((src[p00 + ch] + src[p01 + ch] + src[p10 + ch] + src[p11 + ch]) / 4);
            }
        }
        return BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, buf, w * 4);
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
        var img = RenderMapped(i => seq.Frames[idxs[i]], idxs);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(img));
        using var fs = File.Create(path);
        enc.Save(fs);
        return path;
    }
}
