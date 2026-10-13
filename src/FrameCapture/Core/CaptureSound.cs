using System.IO;
using System.Runtime.InteropServices;

namespace FrameCapture.Core;

/// <summary>
/// 截图提示音：捕获完成时放一声柔和短促的「嘀」，给用户「截好了」的听觉确认（QQ 招牌反馈）。
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

    /// <summary>播放一声（异步，不阻塞 UI 线程）。首次合成后缓存字节。绝不抛——无音频设备等情况静默忽略。
    /// 自定义音：wav 直接读字节走 winmm（无编解码依赖，保证可播）；ogg/mp3 等走 WPF MediaPlayer（系统媒体管线）。
    /// 交给播放管线前的失败（文件不存在、非 UI 线程、打开即抛、wav 头畸形）一律回退内置嘀声；
    /// MediaPlayer 打开后的异步解码失败没有回调，那一趟会无声。</summary>
    /// <param name="customPath">用户指定的提示音文件；null、空白或文件不存在都直接用内置嘀声。</param>
    public static void Play(string? customPath = null)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(customPath) && PlayCustom(customPath)) return;
            _cached ??= MakeWav(Samples());
            // SND_MEMORY|SND_ASYNC|SND_NODEFAULT：Windows 自持缓冲，fire-and-forget，无对象生命周期问题
            bool ok = PlaySound(_cached, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
            Trace.Log($"capture sound played={ok} bytes={_cached.Length}");
        }
        catch (Exception e) { Trace.Log($"capture sound failed: {e.GetType().Name}"); }
    }

    static byte[]? _customBytes;
    static string _customKey = "";
    static System.Windows.Media.MediaPlayer? _mp;

    /// <summary>返回 true=已交给播放管线（wav 成功或已交给 MediaPlayer）；false=回退内置嘀声。</summary>
    static bool PlayCustom(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                string key = path + "|" + File.GetLastWriteTimeUtc(path);
                if (_customKey != key) { _customBytes = File.ReadAllBytes(path); _customKey = key; }
                if (_customBytes != null && PlaySound(_customBytes, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT))
                    return true;
                return false;   // wav 播放失败（如畸形头）→ 回退嘀声
            }
            // 非 wav：MediaPlayer 需 UI 线程（与 Dispatcher 绑定）；捕获完成都在 UI 线程，异常场景直接回退
            if (System.Windows.Application.Current?.Dispatcher.CheckAccess() != true) return false;
            _mp?.Stop(); _mp?.Close();
            _mp = new System.Windows.Media.MediaPlayer();
            _mp.Open(new Uri(path));
            _mp.Play();
            return true;
        }
        catch (Exception e) { Trace.Log($"custom sound failed: {e.GetType().Name} → 回退嘀声"); return false; }
    }
}
