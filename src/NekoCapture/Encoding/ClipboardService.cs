using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using NekoCapture.Capture;
using NekoCapture.Services;

namespace NekoCapture.Encoding;

/// <summary>异步剪贴板入口，系统调用由独立 STA 进程执行，调用方须等待真实结果。</summary>
public static class ClipboardService
{
    /// <summary>剪贴板内保存原始 GIF 字节的格式名。</summary>
    public const string GifFormat = "NekoCapture.Gif";

    /// <summary>异步复制完整录制及当前帧，保留独立帧租约直到请求结束。</summary>
    /// <param name="seq">只读序列。</param>
    /// <param name="gifBytes">完整 GIF 字节。</param>
    /// <param name="frameIndex">当前帧下标，自动钳制。</param>
    /// <param name="cancellation">取消排队或写入。</param>
    /// <returns>完整写入为真，繁忙、超时或取消为假。</returns>
    public static async Task<bool> SetCaptureAsync(CapturedSequence seq, byte[] gifBytes, int frameIndex,
        CancellationToken cancellation = default)
    {
        var frames = BakedFrameList.RetainRange(seq.Frames, 0, seq.Frames.Count);
        using var lease = frames as IDisposable;
        string path = Path.Combine(Path.GetTempPath(), "NekoCapture", $"capture-{Guid.NewGuid():N}.gif");
        bool success = await ClipboardBroker.WriteAsync(ClipboardWire.Capture, writer =>
        {
            writer.Write(path);
            ClipboardWire.WriteFrame(writer, BakedFrameList.Read(frames, Math.Clamp(frameIndex, 0, frames.Count - 1), cancellation));
            writer.Write(gifBytes.Length); writer.Write(gifBytes);
        }, cancellation).ConfigureAwait(false);
        if (!success) try { File.Delete(path); } catch { }
        return success;
    }

    /// <summary>异步复制静图，带 alpha 时同时提供透明 PNG 与白底位图。</summary>
    /// <param name="frame">调用期间只读的像素帧。</param>
    /// <param name="includePng">是否提供透明 PNG。</param>
    /// <param name="cancellation">取消排队或写入。</param>
    /// <returns>完整写入为真，繁忙、超时或取消为假。</returns>
    public static Task<bool> SetFrameAsync(CapturedFrame frame, bool includePng = false, CancellationToken cancellation = default) =>
        ClipboardBroker.WriteAsync(ClipboardWire.Image, writer =>
        {
            writer.Write(includePng); ClipboardWire.WriteFrame(writer, frame);
        }, cancellation);

    /// <summary>异步复制文本。</summary>
    /// <param name="text">要复制的文本。</param>
    /// <param name="cancellation">取消排队或写入。</param>
    /// <returns>完整写入为真，繁忙、超时或取消为假。</returns>
    public static Task<bool> SetTextAsync(string text, CancellationToken cancellation = default) =>
        ClipboardBroker.WriteAsync(ClipboardWire.Text, writer => writer.Write(text), cancellation);

    /// <summary>将冻结位图转换为像素后异步复制。</summary>
    /// <param name="image">可冻结的位图，须在所属线程调用。</param>
    /// <param name="cancellation">取消转换或写入。</param>
    /// <returns>完整写入为真，转换失败、繁忙或超时为假。</returns>
    public static async Task<bool> SetImageAsync(BitmapSource image, CancellationToken cancellation = default)
    {
        try
        {
            if (!image.IsFrozen) image.Freeze();
            var frame = await BackgroundWork.Run(() => BgraImage.FromSource(image), cancellation).ConfigureAwait(false);
            return frame != null && await SetFrameAsync(frame, includePng: true, cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception error) { Core.Trace.Log($"clipboard image conversion: {error}"); return false; }
    }

    /// <summary>异步读取剪贴板图片，延迟渲染不占用主体线程。</summary>
    /// <param name="cancellation">取消排队或读取。</param>
    /// <returns>读取成功的图片；无图片、繁忙或超时为空。</returns>
    public static Task<CapturedFrame?> ReadImageAsync(CancellationToken cancellation = default) =>
        ClipboardBroker.ReadImageAsync(cancellation);

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

    /// <summary>在当前 STA 线程组装静图数据，供后台进程持久化。</summary>
    /// <param name="frame">只读像素帧。</param>
    /// <param name="includePng">是否提供透明 PNG 与白底兜底图。</param>
    /// <returns>尚未写入系统的完整数据对象。</returns>
    internal static DataObject BuildFrameDataObject(CapturedFrame frame, bool includePng)
    {
        var data = new DataObject();
        if (includePng)
        {
            data.SetData("PNG", new MemoryStream(BgraImage.PngBytes(frame)));
            data.SetImage(BgraImage.ToSource(BgraImage.FlattenOver(frame, 255, 255, 255)));
        }
        else data.SetImage(BgraImage.ToSource(frame));
        return data;
    }
}
