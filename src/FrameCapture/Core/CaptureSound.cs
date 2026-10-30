using System.IO;
using System.Runtime.InteropServices;

namespace FrameCapture.Core;

/// <summary>
/// 截图提示音：捕获完成时放一声柔和短促的「嘀」，给用户「截好了」的听觉确认。
/// 内置音不依赖外部音频文件——运行时合成一段 44.1kHz/16bit 单声道 WAV（纯函数，可单测），
/// Win32 PlaySound 异步播放不阻塞 UI。用户另选自定义音文件时走 <see cref="Play"/> 的 customPath 分支。
/// 音量刻意压低、时长压短、起音 2ms 防爆音：宁可轻不可噪，且设置里带「试听」让用户先听再决定开不开。
/// </summary>
public static class CaptureSound
{
    // 合成参数（保守取值：柔和、短促、不刺耳）
    const int Rate = 44100;
    const double Ms = 42;
    const double Freq = 1000;
    const double Amp = 0.18;

    /// <summary>合成一段柔和快门「嘀」声的 PCM 样本，供单测与 WAV 封装共用。</summary>
    /// <param name="rate">采样率（Hz）。</param>
    /// <param name="ms">总时长（毫秒）；过短也至少产出一个采样。</param>
    /// <param name="freq">正弦频率（Hz），决定音高。</param>
    /// <param name="amp">包络峰值幅度（0–1 的归一化比例）。</param>
    /// <returns>长度 ≈ rate×ms/1000 的样本，取值 −1..1，包络为 2ms 起音 + 指数衰减。</returns>
    public static double[] Samples(int rate = Rate, double ms = Ms, double freq = Freq, double amp = Amp)
    {
        int n = Math.Max(1, (int)(rate * ms / 1000.0));
        var s = new double[n];
        double tau = ms / 1000.0 * 0.28;          // 指数衰减时间常数（短尾=干脆）
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / rate;
            double attack = Math.Min(1.0, t / 0.002);   // 2ms 线性起音，避免起始阶跃爆音
            double env = Math.Exp(-t / tau) * attack;
            s[i] = Math.Sin(2 * Math.PI * freq * t) * env * amp;
        }
        return s;
    }

    /// <summary>把 PCM 样本封成标准 44 字节头 PCM WAV（16bit 单声道）。</summary>
    /// <param name="samples">归一化样本（−1..1），写盘前逐个钳到端点，越界不报错。</param>
    /// <param name="rate">采样率（Hz），同时决定头里的字节率与块对齐。</param>
    /// <returns>完整 WAV 字节，可直接落盘或交给 PlaySound(SND_MEMORY)。</returns>
    public static byte[] MakeWav(double[] samples, int rate = Rate)
    {
        int dataBytes = samples.Length * 2;
        using var ms = new MemoryStream(44 + dataBytes);
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);                 // fmt 块大小
        w.Write((short)1);           // PCM
        w.Write((short)1);           // 单声道
        w.Write(rate);
        w.Write(rate * 2);           // 字节率 = rate × 声道 × 2
        w.Write((short)2);           // 块对齐
        w.Write((short)16);          // 位深
        w.Write("data"u8);
        w.Write(dataBytes);
        foreach (var v in samples)
        {
            double c = Math.Clamp(v, -1.0, 1.0);
            w.Write((short)(c * short.MaxValue));
        }
        w.Flush();
        return ms.ToArray();
    }

    static byte[]? _cached;

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    static extern bool PlaySound(byte[] data, IntPtr hmod, uint flags);

    const uint SND_ASYNC = 0x0001, SND_NODEFAULT = 0x0002, SND_MEMORY = 0x0004;

    private static readonly object SoundGate = new();
    private static int _request;
    private static byte[]? _customBytes;
    private static string _customKey = "";
    private static System.Windows.Media.MediaPlayer? _mp;

    /// <summary>后台读取和播放 WAV；其他格式在界面线程交给 MediaPlayer。失败时使用内置提示音。</summary>
    /// <param name="customPath">自定义音效路径；null 或不可读时使用内置音效。</param>
    public static async void Play(string? customPath = null)
    {
        int request = Interlocked.Increment(ref _request);
        bool allowMedia = System.Windows.Application.Current?.Dispatcher.CheckAccess() == true;
        using var activity = Services.BackgroundWork.Hold();
        try
        {
            string? media = await Services.BackgroundWork.Run(() => PrepareAndPlay(customPath, allowMedia, request));
            if (media == null || request != Volatile.Read(ref _request)) return;
            _mp?.Stop(); _mp?.Close();
            _mp = new System.Windows.Media.MediaPlayer();
            _mp.Open(new Uri(media)); _mp.Play();
        }
        catch (Exception ex)
        {
            Trace.Log("capture sound: " + ex.Message);
            try { await Services.BackgroundWork.Run(() => PrepareAndPlay(null, false, request)); }
            catch (Exception error) { Trace.Log("capture sound fallback: " + error.Message); }
        }
    }

    private static string? PrepareAndPlay(string? path, bool allowMedia, int request)
    {
        lock (SoundGate)
        {
            if (request != Volatile.Read(ref _request)) return null;
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    if (!path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                    {
                        if (allowMedia) return path;
                    }
                    else
                    {
                        string key = path + "|" + File.GetLastWriteTimeUtc(path);
                        if (_customKey != key) { _customBytes = File.ReadAllBytes(path); _customKey = key; }
                        if (request != Volatile.Read(ref _request)) return null;
                        if (_customBytes != null && PlaySound(_customBytes, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT)) return null;
                    }
                }
            }
            catch (Exception ex) { Trace.Log("custom sound: " + ex.Message); }
            if (request != Volatile.Read(ref _request)) return null;
            _cached ??= MakeWav(Samples());
            PlaySound(_cached, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
            return null;
        }
    }
}
