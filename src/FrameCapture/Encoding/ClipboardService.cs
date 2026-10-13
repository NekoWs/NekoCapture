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
                System.Threading.Thread.Sleep(80); // 目标应用占用剪贴板，重试
            }
        }
    }

    /// <summary>组装三格式 DataObject（CF_HDROP 文件 + CF_DIB 位图 + 自定义 GIF 字节）。可独立测试。</summary>
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
    /// 并把 CF_DIB 拍平到白底（只认 DIB 的应用不会看到透明区变黑）。</summary>
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
            catch (System.Runtime.InteropServices.COMException) { System.Threading.Thread.Sleep(80); }
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
