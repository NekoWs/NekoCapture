using System.Diagnostics;
using System.IO;
using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Services;
using Trace = FrameCapture.Core.Trace;

namespace FrameCapture.Encoding;

/// <summary>串行调度短生命周期的 STA 剪贴板进程，超时终止进程以阻止迟到写入。</summary>
internal static class ClipboardBroker
{
    static readonly SemaphoreSlim Queue = new(1, 1);
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(4);
    static readonly CancellationTokenSource Shutdown = new();

    static ClipboardBroker()
    {
        if (System.Windows.Application.Current is { } application)
        {
            void Subscribe() => application.Exit += (_, _) => Shutdown.Cancel();
            if (application.Dispatcher.CheckAccess()) Subscribe();
            else application.Dispatcher.BeginInvoke(new Action(Subscribe));
        }
    }

    /// <summary>异步发送一次写入请求并等待真实结果。</summary>
    /// <param name="operation">协议操作。</param>
    /// <param name="write">在后台线程序列化负载。</param>
    /// <param name="cancellation">取消等待或正在执行的操作。</param>
    /// <returns>数据是否完整写入并持久化到系统剪贴板。</returns>
    internal static async Task<bool> WriteAsync(byte operation, Action<BinaryWriter> write, CancellationToken cancellation)
    {
        var result = await ExchangeAsync(operation, write, cancellation).ConfigureAwait(false);
        return result.Success;
    }

    /// <summary>异步读取剪贴板图片，不在主体进程触发延迟渲染。</summary>
    /// <param name="cancellation">取消等待或读取。</param>
    /// <returns>读取成功的图片；超时、繁忙或没有图片时为空。</returns>
    internal static async Task<CapturedFrame?> ReadImageAsync(CancellationToken cancellation)
    {
        var result = await ExchangeAsync(ClipboardWire.ReadImage, _ => { }, cancellation).ConfigureAwait(false);
        return result.Success ? result.Frame : null;
    }

    static async Task<(bool Success, CapturedFrame? Frame)> ExchangeAsync(byte operation,
        Action<BinaryWriter> write, CancellationToken cancellation)
    {
        using var activity = BackgroundWork.Hold();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation, Shutdown.Token);
        var token = lifetime.Token;
        try
        {
            if (!await Queue.WaitAsync(Timeout, token).ConfigureAwait(false)) return (false, null);
            try
            {
                return await Task.Run(() => RunWorkerAsync(operation, write, token), token).ConfigureAwait(false);
            }
            finally { Queue.Release(); }
        }
        catch (OperationCanceledException) { return (false, null); }
        catch (Exception error) { Trace.Log($"clipboard broker: {error}"); return (false, null); }
    }

    static async Task<(bool Success, CapturedFrame? Frame)> RunWorkerAsync(byte operation,
        Action<BinaryWriter> write, CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(Timeout);
        string executable = Path.Combine(AppContext.BaseDirectory, "FrameCapture.exe");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true,
        };
        start.ArgumentList.Add(ClipboardWire.Argument);
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Clipboard worker did not start");
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
        Task? exchange = null;
        try
        {
            var send = Task.Run(() =>
            {
                using var writer = new BinaryWriter(process.StandardInput.BaseStream, System.Text.Encoding.UTF8, leaveOpen: true);
                writer.Write(ClipboardWire.Magic); writer.Write(operation);
                write(writer);
                writer.Flush();
            });
            var receive = Task.Run(() =>
            {
                using var reader = new BinaryReader(process.StandardOutput.BaseStream, System.Text.Encoding.UTF8, leaveOpen: true);
                bool success = reader.ReadBoolean();
                return (Success: success, Frame: success && operation == ClipboardWire.ReadImage ? ClipboardWire.ReadFrame(reader) : null);
            });
            exchange = Task.WhenAll(send, receive, process.WaitForExitAsync());
            await exchange.WaitAsync(deadline.Token).ConfigureAwait(false);
            return process.ExitCode == 0 ? await receive.ConfigureAwait(false) : (false, null);
        }
        catch (OperationCanceledException)
        {
            Trace.Log($"clipboard {(cancellation.IsCancellationRequested ? "cancelled" : "timed out")} operation={operation}");
            return (false, null);
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                }
            }
            catch (Exception error) { Trace.Log($"clipboard worker cleanup: {error.Message}"); }
            try { process.StandardInput.Close(); } catch { }
            try { process.StandardOutput.Close(); } catch { }
            if (exchange != null)
                _ = exchange.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
