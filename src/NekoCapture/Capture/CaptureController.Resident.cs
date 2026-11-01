using System.IO;
using System.Text.Json;
using NekoCapture.Capture;
using NekoCapture.Core;

namespace NekoCapture.Services;

/// <summary>空闲时保存可复用产物，释放像素后以元数据恢复。</summary>
public sealed partial class CaptureController
{
    private const string SessionFile = "resident-session.json";
    private static readonly JsonSerializerOptions SessionJson = new() { IncludeFields = true };

    /// <summary>当前无捕获、编辑或待完成的行为，允许常驻宿主收起界面进程。</summary>
    internal bool CanSuspend => !_disposed && State == Phase.Idle && !_scrollStarting && !_scrollFinishing
        && _scrollBar == null && _delayWindow == null && !_delayFiring && (_chainTask == null || _chainTask.IsCompleted)
        && (_nextCapture?.CanSuspend ?? true);

    /// <summary>已保存快照后释放捕获像素，重新接入惰性产物，保留可立即响应的界面服务。</summary>
    internal void ReleaseResidentBuffers()
    {
        _lastStill.Clear();
        (_lastSequence?.Frames as IDisposable)?.Dispose(); _lastSequence = null;
        ReleaseLocalCaptureBuffers();
        RestoreResidentSession();
    }

    /// <summary>最近产物仍引用的临时文件，过期清理必须保留。</summary>
    /// <returns>录制与滚动截图缓存路径。</returns>
    internal static IEnumerable<string> ResidentFiles()
    {
        var session = ReadSession();
        if (session.Video?.Path is { Length: > 0 } video) yield return video;
        if (session.Scroll?.Path is { Length: > 0 } scroll) yield return scroll;
    }

    /// <summary>从本配置目录恢复上次产物；像素保持惰性读取，损坏的缓存被忽略。</summary>
    internal void RestoreResidentSession()
    {
        try
        {
            var session = ReadSession();
            if (session.Still is { } still && File.Exists(still.Path))
                _lastStill.Restore(() => LoadStill(still.Path), still.Region, still.Device, still.WindowCapture);
            if (session.Video is { } video && File.Exists(video.Path))
            {
                long size = new FileInfo(video.Path).Length;
                if (video.Index.Any(entry => entry.W <= 0 || entry.H <= 0 || (long)entry.W * entry.H * 4 != entry.Len
                    || entry.Offset < 20 || entry.Offset > size - entry.Len)) throw new InvalidDataException("Invalid resident video index");
                _lastSequence = new CapturedSequence
                {
                    Frames = new FrameSpoolList(video.Path, video.Index, ownsFile: false), Region = video.Region,
                    MonitorBounds = video.MonitorBounds, MonitorDevice = video.Device, Fps = video.Fps, DroppedFrames = video.Dropped
                };
            }
            if (session.Scroll is { } scroll && File.Exists(scroll.Path)) _scrollResume = (scroll.Path, scroll.Region, scroll.Device);
        }
        catch (Exception error) { Trace.Log("resident session restore: " + error); }
    }

    /// <summary>保存当前可复用产物；调用期间宿主必须暂停新的行为请求，失败时保留进程和产物。</summary>
    /// <returns>完成原子元数据写入的异步任务。</returns>
    internal Task SaveResidentSessionAsync()
    {
        var frame = _lastStill.LoadedFrame;
        bool empty = _lastStill.Empty;
        _lastStill.TryGetMetadata(out var stillRegion, out string stillDevice);
        bool transparent = _lastStill.WindowCapture;
        var video = _lastSequence;
        var scroll = _scrollResume;
        return Task.Run(() =>
        {
            Directory.CreateDirectory(SettingsService.Dir);
            var previous = ReadSession();
            var session = new ResidentSession();
            string? newStill = null;
            FrameSpoolList? retained = video?.Frames as FrameSpoolList;
            try
            {
                if (frame != null)
                {
                    newStill = Path.Combine(SettingsService.Dir, "resident-still-" + Guid.NewGuid().ToString("N") + ".fcf");
                    using var file = new FileStream(newStill, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                    using var writer = new BinaryWriter(file);
                    writer.Write(frame.W); writer.Write(frame.H); writer.Write(frame.TimeMs); writer.Write(frame.Bgra.Length); writer.Write(frame.Bgra);
                    session.Still = new StillSession { Path = newStill, Region = stillRegion, Device = stillDevice, WindowCapture = transparent };
                }
                else if (!empty) session.Still = previous.Still;
                if (video != null && video.Frames.Count > 0)
                {
                    if (retained == null)
                    {
                        using var spool = new FrameSpool();
                        foreach (var item in video.Frames) spool.Add(item);
                        retained = spool.OpenReader();
                    }
                    session.Video = new VideoSession { Path = retained.Path, Index = retained.ExportIndex(), Region = video.Region,
                        MonitorBounds = video.MonitorBounds, Device = video.MonitorDevice, Fps = video.Fps, Dropped = video.DroppedFrames };
                }
                if (scroll is { } value) session.Scroll = new StillSession { Path = value.Png, Region = value.Region, Device = value.Device };
                string target = Path.Combine(SettingsService.Dir, SessionFile), temporary = target + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(session, SessionJson));
                File.Move(temporary, target, overwrite: true);
                retained?.RetainFile();
                if (retained != null && !ReferenceEquals(retained, video?.Frames)) retained.Dispose();
                DeleteReplaced(previous.Still?.Path, session.Still?.Path, SettingsService.Dir, "resident-still-");
                DeleteReplaced(previous.Video?.Path, session.Video?.Path, Path.Combine(Path.GetTempPath(), "NekoCapture"), "spool-");
            }
            catch
            {
                if (newStill != null) try { File.Delete(newStill); } catch { }
                if (retained != null && !ReferenceEquals(retained, video?.Frames)) retained.Dispose();
                throw;
            }
        });
    }

    private static ResidentSession ReadSession()
    {
        string path = Path.Combine(SettingsService.Dir, SessionFile);
        if (!File.Exists(path)) return new ResidentSession();
        try { return JsonSerializer.Deserialize<ResidentSession>(File.ReadAllText(path), SessionJson) ?? new ResidentSession(); }
        catch { return new ResidentSession(); }
    }

    private static CapturedFrame? LoadStill(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            using var reader = new BinaryReader(file);
            int w = reader.ReadInt32(), h = reader.ReadInt32(); double time = reader.ReadDouble(); int length = reader.ReadInt32();
            if (w <= 0 || h <= 0 || (long)w * h * 4 != length || file.Length != length + 20L) return null;
            byte[] pixels = GC.AllocateUninitializedArray<byte>(length); file.ReadExactly(pixels);
            return new CapturedFrame { W = w, H = h, Bgra = pixels, TimeMs = time };
        }
        catch { return null; }
    }

    private static void DeleteReplaced(string? previous, string? current, string directory, string prefix)
    {
        if (previous == null || string.Equals(previous, current, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            string path = Path.GetFullPath(previous), root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(path).StartsWith(prefix, StringComparison.Ordinal)) File.Delete(path);
        }
        catch { }
    }

    private sealed class ResidentSession
    {
        /// <summary>创建空的缓存描述。</summary>
        public ResidentSession() { }
        /// <summary>可惰性恢复的静图。</summary>
        public StillSession? Still { get; set; }
        /// <summary>磁盘录制索引。</summary>
        public VideoSession? Video { get; set; }
        /// <summary>可继续的滚动截图。</summary>
        public StillSession? Scroll { get; set; }
    }
    private sealed class StillSession
    {
        /// <summary>创建静图缓存描述。</summary>
        public StillSession() { }
        /// <summary>缓存文件绝对路径。</summary>
        public string Path { get; set; } = "";
        /// <summary>捕获范围。</summary>
        public RectI Region { get; set; }
        /// <summary>显示器设备名。</summary>
        public string Device { get; set; } = "";
        /// <summary>窗口透明通道标记。</summary>
        public bool WindowCapture { get; set; }
    }
    private sealed class VideoSession
    {
        /// <summary>创建录制缓存描述。</summary>
        public VideoSession() { }
        /// <summary>磁盘帧文件。</summary>
        public string Path { get; set; } = "";
        /// <summary>帧元数据。</summary>
        public FrameSpoolList.Entry[] Index { get; set; } = [];
        /// <summary>捕获范围。</summary>
        public RectI Region { get; set; }
        /// <summary>屏幕物理范围。</summary>
        public RectI MonitorBounds { get; set; }
        /// <summary>显示器设备名。</summary>
        public string Device { get; set; } = "";
        /// <summary>录制帧率。</summary>
        public int Fps { get; set; }
        /// <summary>丢帧数。</summary>
        public int Dropped { get; set; }
    }
}
