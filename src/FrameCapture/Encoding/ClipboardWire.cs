using System.IO;
using FrameCapture.Capture;

namespace FrameCapture.Encoding;

/// <summary>剪贴板后台进程的二进制请求格式，像素与 GIF 直接写入管道。</summary>
internal static class ClipboardWire
{
    /// <summary>独立进程入口参数。</summary>
    internal const string Argument = "--clipboard-worker";
    /// <summary>协议标识及版本。</summary>
    internal const int Magic = 0x46434301;
    /// <summary>文本写入操作。</summary>
    internal const byte Text = 1;
    /// <summary>静图写入操作。</summary>
    internal const byte Image = 2;
    /// <summary>录制写入操作。</summary>
    internal const byte Capture = 3;
    /// <summary>图片读取操作。</summary>
    internal const byte ReadImage = 4;

    /// <summary>写出单帧元数据与紧凑 BGRA。</summary>
    /// <param name="writer">请求或响应管道。</param>
    /// <param name="frame">只读像素帧。</param>
    internal static void WriteFrame(BinaryWriter writer, CapturedFrame frame)
    {
        writer.Write(frame.W); writer.Write(frame.H); writer.Write(frame.TimeMs);
        writer.Write(frame.Bgra.Length); writer.Write(frame.Bgra);
    }

    /// <summary>读取并校验一帧紧凑 BGRA。</summary>
    /// <param name="reader">请求或响应管道。</param>
    /// <returns>由接收方持有的独立帧。</returns>
    internal static CapturedFrame ReadFrame(BinaryReader reader)
    {
        int width = reader.ReadInt32(), height = reader.ReadInt32();
        double time = reader.ReadDouble();
        int length = reader.ReadInt32();
        if (width <= 0 || height <= 0 || (long)width * height * 4 != length)
            throw new InvalidDataException("Invalid clipboard frame");
        var pixels = GC.AllocateUninitializedArray<byte>(length);
        reader.BaseStream.ReadExactly(pixels);
        return new CapturedFrame { W = width, H = height, TimeMs = time, Bgra = pixels };
    }
}
