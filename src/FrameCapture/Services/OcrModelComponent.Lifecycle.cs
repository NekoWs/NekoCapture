using System.IO;
using FrameCapture.Core;

namespace FrameCapture.Services;

/// <summary>模型识别期间的文件租约与后台卸载。</summary>
internal sealed partial class OcrModelComponent
{
    int _inUse;

    /// <summary>是否有识别任务正在使用模型。</summary>
    internal bool InUse => Volatile.Read(ref _inUse) != 0;

    /// <summary>在识别期间保留模型，避免安装或卸载改动文件。</summary>
    /// <returns>识别结束时释放的租约；模型不可用时为空。</returns>
    internal async Task<IDisposable?> AcquireRecognitionAsync()
    {
        await _installGate.WaitAsync().ConfigureAwait(false);
        if (!Available) { _installGate.Release(); return null; }
        Volatile.Write(ref _inUse, 1); Changed?.Invoke();
        return new RecognitionLease(this);
    }

    /// <summary>后台删除本模型目录；识别或安装期间拒绝卸载。</summary>
    /// <returns>卸载成功或模型已不存在时为真，忙碌或失败时为假。</returns>
    internal async Task<bool> UninstallAsync()
    {
        if (!await _installGate.WaitAsync(0)) return false;
        using var activity = BackgroundWork.Hold();
        Busy = true; CurrentProgress = new(OcrComponentPhase.Uninstalling, 0, 0); Changed?.Invoke();
        try
        {
            string directory = InstallDirectory;
            await Task.Run(() => { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); });
            CurrentProgress = new(OcrComponentPhase.Missing, 0, 0);
            return true;
        }
        catch (Exception error)
        {
            CurrentProgress = new(OcrComponentPhase.UninstallFailed, 0, 0);
            Trace.Log("OCR model uninstall: " + error);
            return false;
        }
        finally { Busy = false; _installGate.Release(); Changed?.Invoke(); }
    }

    sealed class RecognitionLease(OcrModelComponent owner) : IDisposable
    {
        OcrModelComponent? _owner = owner;

        /// <inheritdoc/>
        public void Dispose()
        {
            var model = Interlocked.Exchange(ref _owner, null);
            if (model == null) return;
            Volatile.Write(ref model._inUse, 0); model._installGate.Release(); model.Changed?.Invoke();
        }
    }
}
