using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32.SafeHandles;

namespace FrameCapture.Runtime;

/// <summary>界面输出的数值键位快照，常驻进程不解释行为图。</summary>
internal sealed record ResidentInput
{
    /// <summary>绑定稳定标识。</summary>
    public string Id { get; set; } = "";
    /// <summary>主键和修饰键位。</summary>
    public int Key { get; set; }
    /// <summary>GestureCore 修饰键位。</summary>
    public int Mods { get; set; }
    /// <summary>GestureCore 触发枚举值。</summary>
    public int Trigger { get; set; }
    /// <summary>连击窗口，毫秒。</summary>
    public int DoubleClickMs { get; set; }
    /// <summary>长按阈值，毫秒。</summary>
    public int LongPressMs { get; set; }
    /// <summary>绑定优先级。</summary>
    public int Priority { get; set; }
}

/// <summary>与设置文件时间戳对应的常驻输入快照。</summary>
internal sealed class ResidentManifest
{
    /// <summary>源设置文件最后写入的 UTC ticks。</summary>
    public long SourceTicks { get; set; }
    /// <summary>快捷键是否启用。</summary>
    public bool Enabled { get; set; }
    /// <summary>手势和行为配置摘要，相同配置的界面冷启动不重置按住状态。</summary>
    public string Signature { get; set; } = "";
    /// <summary>可运行绑定的数值输入。</summary>
    public ResidentInput[] Inputs { get; set; } = [];
    /// <summary>正常托盘提示，由界面文案表提供。</summary>
    public string Tooltip { get; set; } = "FrameCapture";
    /// <summary>暂停托盘提示，由界面文案表提供。</summary>
    public string PausedTooltip { get; set; } = "FrameCapture";
    /// <summary>配置目录中的正常托盘图标。</summary>
    public string IconFile { get; set; } = "resident-tray.ico";
    /// <summary>配置目录中的暂停托盘图标。</summary>
    public string PausedIconFile { get; set; } = "resident-tray-paused.ico";
    /// <summary>图标编码与配色的身份，变化时重新加载文件。</summary>
    public string IconRevision { get; set; } = "";
}

/// <summary>常驻配置与命令的静态 JSON 元数据。</summary>
[JsonSerializable(typeof(ResidentManifest))]
[JsonSerializable(typeof(string[]))]
internal partial class ResidentJson : JsonSerializerContext { }

/// <summary>同目录同用户进程之间的有界窗口消息；消息只受理命令，不传递帧像素。</summary>
internal static class ResidentProtocol
{
    /// <summary>消息窗口的进程间数据标记。</summary>
    internal const long DataTag = 0x46435253;
    /// <summary>界面进程的常驻协作参数。</summary>
    internal const string WorkerFlag = "--resident-worker";
    /// <summary>当前配置目录。</summary>
    internal static string DirectoryPath => Environment.GetEnvironmentVariable("FC_SETTINGS_DIR") is { Length: > 0 } path
        ? Path.GetFullPath(path) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FrameCapture");
    /// <summary>常驻输入快照路径。</summary>
    internal static string ManifestPath => Path.Combine(DirectoryPath, "resident-inputs.json");
    /// <summary>独立配置实例的窗口与互斥范围。</summary>
    internal static string Suffix => Environment.GetEnvironmentVariable("FC_RESIDENT_INSTANCE") is { Length: > 0 } value ? "." + value : "";
    /// <summary>常驻消息窗口名称。</summary>
    internal static string WindowName => "FrameCapture.Resident" + Suffix;

    /// <summary>寻找本配置实例的常驻窗口。</summary>
    /// <returns>消息窗口；未启动时为零。</returns>
    internal static IntPtr FindHost() => FindWindowEx(IntPtr.Zero, IntPtr.Zero, null, WindowName);

    /// <summary>读取原子替换的输入快照，读取期间允许发布下一份文件。</summary>
    /// <returns>当前快照。</returns>
    internal static ResidentManifest? ReadManifest()
    {
        using var file = new FileStream(ManifestPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonSerializer.Deserialize(file, ResidentJson.Default.ResidentManifest);
    }

    /// <summary>向消息窗口投递同目录命令，等待最多 150 毫秒。</summary>
    /// <param name="window">接收窗口。</param>
    /// <param name="arguments">独立的命令参数。</param>
    /// <returns>目标已受理时为 true；无窗口、无权限或超时时为 false。</returns>
    internal static bool Send(IntPtr window, string[] arguments)
    {
        if (window == IntPtr.Zero) return false;
        if (Environment.GetEnvironmentVariable("FC_TEST_NO_FG") != "1")
        {
            GetWindowThreadProcessId(window, out uint receiver);
            if (receiver != 0) AllowSetForegroundWindow(receiver);
        }
        string json = JsonSerializer.Serialize(arguments, ResidentJson.Default.StringArray);
        if (json.Length > 16000) return false;
        IntPtr sender = CreateWindowEx(0, "STATIC", "", 0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        IntPtr data = Marshal.StringToCoTaskMemUni(json);
        try
        {
            var message = new CopyData { Tag = new IntPtr(DataTag), Bytes = (json.Length + 1) * 2, Data = data };
            return SendMessageTimeout(window, 0x004A, sender, ref message, 2, 150, out var result) != IntPtr.Zero && result != IntPtr.Zero;
        }
        finally { Marshal.FreeCoTaskMem(data); if (sender != IntPtr.Zero) DestroyWindow(sender); }
    }

    /// <summary>验证发送方属于同一用户、同一安装目录，并读取有界 JSON 参数。</summary>
    /// <param name="sender">发送方消息窗口。</param>
    /// <param name="payload">WM_COPYDATA 数据结构。</param>
    /// <param name="executable">允许的发送方可执行文件名。</param>
    /// <returns>合法命令参数；验证失败时为 null。</returns>
    internal static string[]? Receive(IntPtr sender, IntPtr payload, string executable)
    {
        try
        {
            var data = Marshal.PtrToStructure<CopyData>(payload);
            if (data.Tag.ToInt64() != DataTag || data.Bytes is < 2 or > 32002 || data.Bytes % 2 != 0 || sender == IntPtr.Zero) return null;
            GetWindowThreadProcessId(sender, out uint pid);
            using var process = OpenProcess(0x1000, false, pid);
            if (process.IsInvalid) return null;
            var path = new StringBuilder(512); int length = path.Capacity;
            if (!QueryFullProcessImageName(process, 0, path, ref length))
            {
                if (Marshal.GetLastWin32Error() != 122) return null;
                path = new StringBuilder(32768); length = path.Capacity;
                if (!QueryFullProcessImageName(process, 0, path, ref length)) return null;
            }
            if (!string.Equals(path.ToString(), Path.Combine(AppContext.BaseDirectory, executable), StringComparison.OrdinalIgnoreCase)) return null;
            if (!OpenProcessToken(process, 8, out var token)) return null;
            using (token)
            using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            using (var current = WindowsIdentity.GetCurrent())
                if (identity.User != current.User) return null;
            return JsonSerializer.Deserialize(Marshal.PtrToStringUni(data.Data, data.Bytes / 2 - 1)!, ResidentJson.Default.StringArray);
        }
        catch { return null; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyData
    {
        /// <summary>固定协议标记。</summary>
        public IntPtr Tag;
        /// <summary>含结尾空字符的字节数。</summary>
        public int Bytes;
        /// <summary>UTF-16 JSON 地址。</summary>
        public IntPtr Data;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowExW")]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? name, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")]
    private static extern IntPtr CreateWindowEx(uint style, string name, string title, uint flags, int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr w, ref CopyData data, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint process);
    [DllImport("kernel32.dll")]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref int length);
    [DllImport("advapi32.dll")]
    private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
}
