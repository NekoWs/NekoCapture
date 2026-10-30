using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using FrameCapture.Core;

namespace FrameCapture.Services;

/// <summary>可选 small 模型的下载、校验与资源提取；不加载第三方程序集。</summary>
internal static partial class OcrSmallComponent
{
    const string Revision = "ppocr-v6-small-1.0.0";
    static readonly string[] ModelFiles = ["det.onnx", "rec.onnx", "cls.onnx", "dict.txt"];
    static CancellationTokenSource? _cancellation;
    static readonly SemaphoreSlim InstallGate = new(1, 1);

    /// <summary>模型包总下载字节数。</summary>
    internal const long DownloadBytes = 27_076_458;
    /// <summary>识别工作进程的位置；测试可指定隔离的工作进程。</summary>
    internal static string WorkerPath => Environment.GetEnvironmentVariable("FC_OCR_WORKER") is { Length: > 0 } path
        ? path : Path.Combine(AppContext.BaseDirectory, "OcrWorker", "FrameCapture.OcrWorker.exe");
    /// <summary>当前是否在安装组件。</summary>
    internal static bool Busy { get; private set; }
    /// <summary>下载完成比例，取值为 0 至 1。</summary>
    internal static double Progress => Math.Clamp(CurrentProgress.Bytes / (double)DownloadBytes, 0, 1);
    /// <summary>可见安装阶段与下载统计的不可变快照。</summary>
    internal static OcrComponentProgress CurrentProgress { get; private set; } = new(OcrComponentPhase.Missing, 0, 0);
    /// <summary>最近一次安装是否失败。</summary>
    internal static bool Failed => CurrentProgress.Phase == OcrComponentPhase.Failed;
    /// <summary>下载状态改变；订阅方负责回到界面线程。</summary>
    internal static event Action? Changed;
    /// <summary>完整模型所在目录；不存在时为空。</summary>
    internal static string? ModelDirectory
    {
        get
        {
            string directory = Path.Combine(SettingsService.Dir, "components", Revision);
            return Complete(directory) ? directory : null;
        }
    }
    /// <summary>模型和工作进程是否均可用。</summary>
    internal static bool Available => File.Exists(WorkerPath) && ModelDirectory != null;

    static bool Complete(string directory)
    {
        try { return File.ReadAllText(Path.Combine(directory, "ready")) == Revision
            && ModelFiles.All(file => new FileInfo(Path.Combine(directory, file)) is { Exists: true, Length: > 0 }); }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>取消当前安装。</summary>
    internal static void Cancel() => _cancellation?.Cancel();

    /// <summary>下载并安装固定版本的模型，重复请求共享安装状态。</summary>
    /// <returns>安装结束的任务；错误通过状态与日志反馈。</returns>
    internal static async Task InstallAsync()
    {
        if (!await InstallGate.WaitAsync(0)) return;
        if (Available) { InstallGate.Release(); return; }
        using var activity = BackgroundWork.Hold();
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation; Busy = true;
        CurrentProgress = new(OcrComponentPhase.Connecting, 0, 0); Changed?.Invoke();
        string parent = Path.Combine(SettingsService.Dir, "components");
        string staging = Path.Combine(parent, "ocr-download-" + Guid.NewGuid().ToString("N"));
        try
        {
            await Task.Run(async () =>
            {
                Directory.CreateDirectory(staging);
                await Download("sdcb.simdpaddleocr.models.chinesev6small", "E77523D9DCC682DD802D9271400DBBB2A6B3ED22BB7A9724E05BF5A75E585539",
                    26_114_884, 0, Path.Combine(staging, "small.zip"), cancellation.Token);
                await Download("sdcb.simdpaddleocr.models.textlineorientation", "D925F70D542C4A2E829E423BE24B3BA17508BDE3215C7BDC0799D98E741303D8",
                    961_574, 26_114_884, Path.Combine(staging, "cls.zip"), cancellation.Token);
                CurrentProgress = new(OcrComponentPhase.Installing, DownloadBytes, 0); Changed?.Invoke();
                ExtractModels(Path.Combine(staging, "small.zip"), "Sdcb.SimdPaddleOCR.Models.ChineseV6Small", staging,
                    new Dictionary<string, string> { ["det.onnx"] = "det.onnx", ["rec.onnx"] = "rec.onnx", ["dict.txt"] = "dict.txt" });
                ExtractModels(Path.Combine(staging, "cls.zip"), "Sdcb.SimdPaddleOCR.Models.TextLineOrientation", staging,
                    new Dictionary<string, string> { ["cls.onnx"] = "cls.onnx" });
                cancellation.Token.ThrowIfCancellationRequested();
                string target = Path.Combine(parent, Revision); Directory.CreateDirectory(target);
                foreach (string file in ModelFiles) File.Move(Path.Combine(staging, file), Path.Combine(target, file), true);
                File.WriteAllText(Path.Combine(target, "NOTICE.txt"),
                    "PP-OCRv6 small and TextLineOrientation models: PaddleOCR / Sdcb.SimdPaddleOCR, Apache-2.0.\n" +
                    "https://github.com/PaddlePaddle/PaddleOCR\nhttps://github.com/sdcb/SimdPaddleOCR\n");
                string license = Path.Combine(AppContext.BaseDirectory, "third-party", "ocr-small", "Apache-2.0.txt");
                if (File.Exists(license)) File.Copy(license, Path.Combine(target, "Apache-2.0.txt"), true);
                File.WriteAllText(Path.Combine(target, "ready"), Revision);
            });
            CurrentProgress = new(OcrComponentPhase.Available, DownloadBytes, 0);
        }
        catch (OperationCanceledException) { CurrentProgress = CurrentProgress with { Phase = OcrComponentPhase.Cancelled, Speed = 0 }; }
        catch (Exception error)
        {
            CurrentProgress = CurrentProgress with { Phase = OcrComponentPhase.Failed, Speed = 0 };
            Trace.Log("OCR component: " + error);
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Trace.Log("OCR staging cleanup: " + error.Message); }
            _cancellation = null; Busy = false; InstallGate.Release(); Changed?.Invoke();
        }
    }

    static void ExtractModels(string package, string assembly, string directory, Dictionary<string, string> resources)
    {
        using var archive = ZipFile.OpenRead(package);
        var entry = archive.GetEntry($"lib/netstandard2.0/{assembly}.dll") ?? throw new InvalidDataException("Missing OCR model resources.");
        string dll = Path.Combine(directory, assembly + ".dll"); entry.ExtractToFile(dll);
        using var stream = File.OpenRead(dll); using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var data = pe.GetSectionData(pe.PEHeaders.CorHeader!.ResourcesDirectory.RelativeVirtualAddress).GetContent();
        int extracted = 0;
        foreach (var handle in metadata.ManifestResources)
        {
            var resource = metadata.GetManifestResource(handle);
            string name = metadata.GetString(resource.Name);
            if (!resource.Implementation.IsNil || !name.StartsWith(assembly + ".", StringComparison.Ordinal)) continue;
            if (!resources.TryGetValue(name[(assembly.Length + 1)..], out string? output)) continue;
            int at = checked((int)resource.Offset);
            int count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at, 4));
            using var file = File.Create(Path.Combine(directory, output)); file.Write(data.AsSpan(at + 4, count)); extracted++;
        }
        if (extracted != resources.Count) throw new InvalidDataException("Incomplete OCR model resources.");
    }
}
