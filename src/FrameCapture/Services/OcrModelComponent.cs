using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using FrameCapture.Core;

namespace FrameCapture.Services;

/// <summary>可选模型各自的下载、校验和安装会话；不加载第三方程序集。</summary>
internal sealed partial class OcrModelComponent
{
    const long OrientationBytes = 961_574;
    readonly string _revision, _package, _assembly, _hash;
    readonly long _packageBytes;
    static readonly string[] ModelFiles = ["det.onnx", "rec.onnx", "cls.onnx", "dict.txt"];
    CancellationTokenSource? _cancellation;
    readonly SemaphoreSlim _installGate = new(1, 1);

    /// <summary>Small 模型的共享安装会话。</summary>
    internal static OcrModelComponent Small { get; } = new(OcrService.SmallEngine, "Small", 26_114_884,
        "E77523D9DCC682DD802D9271400DBBB2A6B3ED22BB7A9724E05BF5A75E585539");
    /// <summary>Medium 模型的共享安装会话。</summary>
    internal static OcrModelComponent Medium { get; } = new(OcrService.MediumEngine, "Medium", 100_363_760,
        "1DA9AA4002233D456F4EE259B8030493E14582811F49938CBEFB771C1489314C");
    /// <summary>设置页可选择的全部可选模型。</summary>
    internal static IReadOnlyList<OcrModelComponent> All { get; } = Array.AsReadOnly(new[] { Small, Medium });

    private OcrModelComponent(string engine, string suffix, long packageBytes, string hash)
    {
        Engine = engine; _revision = "ppocr-v6-" + engine + "-1.0.0";
        _package = "sdcb.simdpaddleocr.models.chinesev6" + engine;
        _assembly = "Sdcb.SimdPaddleOCR.Models.ChineseV6" + suffix;
        _packageBytes = packageBytes; _hash = hash;
    }

    /// <summary>模型对应的引擎设置值。</summary>
    internal string Engine { get; }
    /// <summary>模型的界面名称。</summary>
    internal Str Label => Engine == OcrService.SmallEngine ? K.Ocr.SmallEngine : K.Ocr.MediumEngine;
    /// <summary>按引擎设置值查找模型，系统引擎不对应可选模型。</summary>
    /// <param name="engine">引擎设置值。</param>
    /// <returns>对应模型会话，或空值。</returns>
    internal static OcrModelComponent? For(string engine) => All.FirstOrDefault(model => model.Engine == engine);

    /// <summary>模型包总下载字节数。</summary>
    internal long DownloadBytes => _packageBytes + OrientationBytes;
    /// <summary>识别工作进程的位置；测试可指定隔离的工作进程。</summary>
    internal static string WorkerPath => Environment.GetEnvironmentVariable("FC_OCR_WORKER") is { Length: > 0 } path
        ? path : Path.Combine(AppContext.BaseDirectory, "OcrWorker", "FrameCapture.OcrWorker.exe");
    /// <summary>当前是否在安装或卸载组件。</summary>
    internal bool Busy { get; private set; }
    /// <summary>下载完成比例，取值为 0 至 1。</summary>
    internal double Progress => Math.Clamp(CurrentProgress.Bytes / (double)DownloadBytes, 0, 1);
    /// <summary>可见安装阶段与下载统计的不可变快照。</summary>
    internal OcrComponentProgress CurrentProgress { get; private set; } = new(OcrComponentPhase.Missing, 0, 0);
    /// <summary>最近一次安装是否失败。</summary>
    internal bool Failed => CurrentProgress.Phase == OcrComponentPhase.Failed;
    /// <summary>下载状态改变；订阅方负责回到界面线程。</summary>
    internal event Action? Changed;
    /// <summary>完整模型所在目录；不存在时为空。</summary>
    internal string? ModelDirectory
    {
        get
        {
            string directory = InstallDirectory;
            return Complete(directory) ? directory : null;
        }
    }
    /// <summary>模型和工作进程是否均可用。</summary>
    internal bool Available => File.Exists(WorkerPath) && ModelDirectory != null;

    string InstallDirectory => Path.Combine(SettingsService.Dir, "components", _revision);
    /// <summary>是否存在需要卸载的模型目录。</summary>
    internal bool HasFiles => Directory.Exists(InstallDirectory);

    bool Complete(string directory)
    {
        try { return File.ReadAllText(Path.Combine(directory, "ready")) == _revision
            && ModelFiles.All(file => new FileInfo(Path.Combine(directory, file)) is { Exists: true, Length: > 0 }); }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>取消当前安装。</summary>
    internal void Cancel() => _cancellation?.Cancel();

    /// <summary>下载并安装固定版本的模型，重复请求共享安装状态。</summary>
    /// <returns>安装结束的任务；错误通过状态与日志反馈。</returns>
    internal async Task InstallAsync()
    {
        if (!await _installGate.WaitAsync(0)) return;
        if (Available) { _installGate.Release(); return; }
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
                await Download(_package, _hash, _packageBytes, 0, Path.Combine(staging, "model.zip"), cancellation.Token);
                await Download("sdcb.simdpaddleocr.models.textlineorientation", "D925F70D542C4A2E829E423BE24B3BA17508BDE3215C7BDC0799D98E741303D8",
                    OrientationBytes, _packageBytes, Path.Combine(staging, "cls.zip"), cancellation.Token);
                CurrentProgress = new(OcrComponentPhase.Installing, DownloadBytes, 0); Changed?.Invoke();
                ExtractModels(Path.Combine(staging, "model.zip"), _assembly, staging,
                    new Dictionary<string, string> { ["det.onnx"] = "det.onnx", ["rec.onnx"] = "rec.onnx", ["dict.txt"] = "dict.txt" });
                ExtractModels(Path.Combine(staging, "cls.zip"), "Sdcb.SimdPaddleOCR.Models.TextLineOrientation", staging,
                    new Dictionary<string, string> { ["cls.onnx"] = "cls.onnx" });
                cancellation.Token.ThrowIfCancellationRequested();
                string target = Path.Combine(parent, _revision); Directory.CreateDirectory(target);
                foreach (string file in ModelFiles) File.Move(Path.Combine(staging, file), Path.Combine(target, file), true);
                File.WriteAllText(Path.Combine(target, "NOTICE.txt"),
                    $"PP-OCRv6 {Engine} and TextLineOrientation models: PaddleOCR / Sdcb.SimdPaddleOCR, Apache-2.0.\n" +
                    "https://github.com/PaddlePaddle/PaddleOCR\nhttps://github.com/sdcb/SimdPaddleOCR\n");
                string license = Path.Combine(AppContext.BaseDirectory, "third-party", "ocr", "Apache-2.0.txt");
                if (File.Exists(license)) File.Copy(license, Path.Combine(target, "Apache-2.0.txt"), true);
                File.WriteAllText(Path.Combine(target, "ready"), _revision);
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
            _cancellation = null; Busy = false; _installGate.Release(); Changed?.Invoke();
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
