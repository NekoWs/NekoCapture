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

    /// <summary>由帧序列渲染胶片条位图（每格带帧号与时间码）。不再落盘，编辑器直接复制剪贴板。</summary>
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

    /// <summary>兼容旧入口：均匀抽 desiredFrames 帧渲染并写 PNG 文件。</summary>
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
