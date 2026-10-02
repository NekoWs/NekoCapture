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
    public static string Generate(CapturedSequence seq, int desiredFrames, string path)
    {
        // 均匀抽帧
        int n = seq.Frames.Count;
        int take = Math.Min(Math.Max(1, desiredFrames), n);
        var idxs = new int[take];
        for (int i = 0; i < take; i++)
            idxs[i] = (int)Math.Round(i * (n - 1) / (double)Math.Max(1, take - 1));

        int cols = Math.Min(take, 8);
        int rows = (take + cols - 1) / cols;

        // 单元格尺寸：宽 288，高按帧宽高比
        int srcW = seq.Frames[0].W, srcH = seq.Frames[0].H;
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
                var frame = seq.Frames[idxs[i]];
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

        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
        return path;
    }
}
