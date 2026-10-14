using System.IO;
using System.Windows;
using FrameCapture.Capture;
using FrameCapture.Services;

namespace FrameCapture.Encoding;

/// <summary>
/// 剪贴板写入：GIF 文件（CF_HDROP）+ 当前帧位图（CF_DIB）+ 自定义格式（原始 GIF 字节）。
/// 必须在 UI(STA) 线程调用。
/// </summary>
public static class ClipboardService
{
    public const string GifFormat = "FrameCapture.Gif";

    /// <summary>
    /// 把一次捕获写成临时 GIF（%TEMP%\FrameCapture\capture-HHmmss.gif）并连同当前帧放上剪贴板。
    /// 剪贴板被别的应用占用时重试 6 次、每次退避 80ms；<b>6 次全失败则静默返回</b>，不向调用方抛异常。
    /// </summary>
    /// <param name="seq">捕获序列，用其帧列表取位图形态。</param>
    /// <param name="gifBytes">已编码好的完整 GIF 字节，直接落盘，不做二次编码。</param>
    /// <param name="frameIndex">作为位图形态的帧下标，越界会被钳到有效范围。</param>
    public static void SetCapture(CapturedSequence seq, byte[] gifBytes, int frameIndex)
    {
        string dir = Path.Combine(Path.GetTempPath(), "FrameCapture");
        Directory.CreateDirectory(dir);
        string gifPath = Path.Combine(dir, $"capture-{DateTime.Now:HHmmss}.gif");
        File.WriteAllBytes(gifPath, gifBytes);

        var data = BuildDataObject(seq, gifBytes, frameIndex, gifPath);

        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, true);
                return;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(80); // 目标应用占用剪贴板，重试
            }
        }
    }

    /// <summary>组装三格式 DataObject（CF_HDROP 文件 + CF_DIB 位图 + 自定义 GIF 字节）。可独立测试。</summary>
    /// <param name="seq">捕获序列，取当前帧做位图形态。</param>
    /// <param name="gifBytes">GIF 原始字节，作为自定义格式与可序列化格式的负载。</param>
    /// <param name="frameIndex">位图形态的帧下标，越界按 0..Count-1 钳制，不抛异常。</param>
    /// <param name="gifPath">已落盘的 GIF 文件绝对路径，作为 CF_HDROP 内容；本方法不负责创建它。</param>
    /// <returns>三形态齐备的 DataObject，尚未写入剪贴板。帧位图转换失败时该形态缺席，
    /// 文件与 GIF 字节两种形态照常给出——粘贴不到图也要能粘贴到文件。</returns>
    public static DataObject BuildDataObject(CapturedSequence seq, byte[] gifBytes, int frameIndex, string gifPath)
    {
        var data = new DataObject();
        // 1) CF_HDROP：GIF 文件
        var files = new System.Collections.Specialized.StringCollection();
        files.Add(gifPath);
        data.SetFileDropList(files);
        // 2) CF_DIB：当前帧（用于直接粘贴到聊天/画图）
        try
        {
            var frame = seq.Frames[Math.Clamp(frameIndex, 0, seq.Frames.Count - 1)];
            data.SetImage(BgraImage.ToSource(frame));
        }
        catch { }
        // 3) 自定义格式：原始 GIF 字节
        data.SetData(GifFormat, gifBytes);
        data.SetData(DataFormats.Serializable, gifBytes);
        return data;
    }

    /// <summary>仅复制单帧位图。includePng=true（窗口截图带 alpha）时额外放 "PNG" 格式保透明，
    /// 并把 CF_DIB 拍平到白底（只认 DIB 的应用不会看到透明区变黑）。
    /// 剪贴板被占用时重试 6 次、每次退避 80ms，全失败静默返回。</summary>
    /// <param name="frame">要复制的帧。</param>
    /// <param name="includePng">帧带有效 alpha 时必须为 true；纯不透明截图传 false 可少编码一份 PNG。</param>
    public static void SetFrame(CapturedFrame frame, bool includePng = false)
    {
        object payload = includePng ? BuildAlphaDataObject(frame) : (object)BgraImage.ToSource(frame);
        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                if (includePng) Clipboard.SetDataObject((DataObject)payload, true);
                else Clipboard.SetImage((System.Windows.Media.Imaging.BitmapSource)payload);
                return;
            }
            catch (System.Runtime.InteropServices.COMException) { Thread.Sleep(80); }
        }
    }

    /// <summary>带 alpha 帧的剪贴板 DataObject：PNG 格式（透明，现代应用）+ 白底拍平位图（CF_DIB 兜底）。</summary>
    static DataObject BuildAlphaDataObject(CapturedFrame frame)
    {
        var data = new DataObject();
        try { data.SetData("PNG", new MemoryStream(BgraImage.PngBytes(frame))); } catch { }
        try { data.SetImage(BgraImage.ToSource(BgraImage.FlattenOver(frame, 255, 255, 255))); } catch { }
        return data;
    }
}
