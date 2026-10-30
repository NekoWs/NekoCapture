using System.IO;
using System.Diagnostics;
using Trace = FrameCapture.Core.Trace;
using System.Runtime.InteropServices;
using System.Windows;
using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Services;

namespace FrameCapture.Encoding;

/// <summary>独立 STA 进程执行系统剪贴板调用，不创建窗口、托盘或快捷键服务。</summary>
internal static class ClipboardWorker
{
    /// <summary>在 STA 启动线程读取单次请求并写回结果。</summary>
    /// <param name="parentId">启动本进程的主体进程标识。</param>
    /// <returns>协议完成时为零，负载或系统调用异常时为一。</returns>
    internal static int Run(int parentId)
    {
        using var input = new BinaryReader(Console.OpenStandardInput());
        using var output = new BinaryWriter(Console.OpenStandardOutput());
        try
        {
            using var parent = Process.GetProcessById(parentId);
            parent.Exited += (_, _) => Environment.Exit(1);
            parent.EnableRaisingEvents = true;
            if (parent.HasExited) return 1;
            if (input.ReadInt32() != ClipboardWire.Magic) throw new InvalidDataException("Invalid clipboard request");
            byte operation = input.ReadByte();
            if (operation == ClipboardWire.ReadImage)
            {
                CapturedFrame? frame = null;
                bool found = Retry(() => { frame = BgraImage.FromSource(Clipboard.GetImage()); });
                output.Write(found && frame != null);
                if (found && frame != null) ClipboardWire.WriteFrame(output, frame);
            }
            else output.Write(Write(operation, input));
            output.Flush();
            return 0;
        }
        catch (Exception error)
        {
            Trace.Log($"clipboard worker: {error}");
            return 1;
        }
    }

    static bool Write(byte operation, BinaryReader input)
    {
        if (operation == ClipboardWire.Text)
        {
            string text = input.ReadString();
            return Retry(() => Clipboard.SetText(text));
        }
        if (operation == ClipboardWire.Image)
        {
            bool alpha = input.ReadBoolean();
            var frame = ClipboardWire.ReadFrame(input);
            var data = ClipboardService.BuildFrameDataObject(frame, alpha);
            return Retry(() => Clipboard.SetDataObject(data, true));
        }
        if (operation == ClipboardWire.Capture)
        {
            string path = input.ReadString();
            var frame = ClipboardWire.ReadFrame(input);
            int length = input.ReadInt32();
            if (length < 0) throw new InvalidDataException("Invalid clipboard GIF");
            var bytes = GC.AllocateUninitializedArray<byte>(length);
            input.BaseStream.ReadExactly(bytes);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            var bounds = new RectI(0, 0, frame.W, frame.H);
            var sequence = new CapturedSequence { Frames = new[] { frame }, Region = bounds, MonitorBounds = bounds };
            var data = ClipboardService.BuildDataObject(sequence, bytes, 0, path);
            return Retry(() => Clipboard.SetDataObject(data, true));
        }
        throw new InvalidDataException("Unknown clipboard operation");
    }

    static bool Retry(Action action)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            try { action(); return true; }
            catch (COMException) { if (attempt < 5) Thread.Sleep(80); }
        }
        return false;
    }
}
