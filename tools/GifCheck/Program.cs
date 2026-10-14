// GifCheck <in.gif> [dumpOutPrefix]
using System.IO;
using System.Windows.Media.Imaging;

string path = args[0];
var dec = new System.Windows.Media.Imaging.GifBitmapDecoder(new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);
var frames = dec.Frames;
Console.WriteLine($"frames={frames.Count} size={frames[0].PixelWidth}x{frames[0].PixelHeight}");
try { Console.WriteLine("meta-loop=" + (dec.Metadata?.GetQuery("/app/{DC6D5EF9-0C18-4624-A3D2-4CE43E1EC589}/LoopCount"))); } catch { }

int[] dumpIdx2 = args.Length > 1 ? new[] { 0, frames.Count / 2, frames.Count - 1 } : Array.Empty<int>();
foreach (int i in dumpIdx2)
{
    var f = frames[i];
    var enc = new PngBitmapEncoder();
    enc.Frames.Add(BitmapFrame.Create(f));
    string p = $"{args[1]}-{i}.png";
    using var fs = File.Create(p);
    enc.Save(fs);
    // 简单求像素校验和判断帧间是否有运动
    var px = new byte[(int)f.PixelWidth * (int)f.PixelHeight * 4];
    f.CopyPixels(px, (int)f.PixelWidth * 4, 0);
    long sum = 0; for (int k = 0; k < px.Length; k += 997) sum += px[k];
    Console.WriteLine($"frame{i} sum={sum} -> {p}");
}
// 每帧 GCE 延迟（通过 SizeInformation? WPF 不暴露，跳过——用解码帧数验证结构合法）

foreach (var f2 in frames)
{
    try
    {
        // 帧元数据的静态类型是 ImageMetadata，取查询串要落到 BitmapMetadata 上
        var d = (f2.Metadata as BitmapMetadata)?.GetQuery("/delay");
        Console.Write($"d={d} ");
    }
    catch (Exception ex) { Console.Write("dx " + ex.GetType().Name + " "); }
}
Console.WriteLine();
