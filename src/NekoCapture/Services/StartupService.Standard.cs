using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace NekoCapture.Services;

/// <summary>桌面进程启动普通权限实例，经同用户管道确认就绪后，在单实例锁释放时交接。</summary>
public static partial class StartupService
{
    private const string StandardArg = "--standard-handoff", StandardPipePrefix = "NekoCapture.Standard.";
    private static NamedPipeServerStream? _standardHandoff;

    private static bool IsTokenElevated(SafeAccessTokenHandle token)
    {
        if (!GetTokenInformation(token, 20, out int elevated, sizeof(int), out _)) throw new Win32Exception();
        return elevated != 0;
    }

    private static bool IsStandardUser(SafeAccessTokenHandle token, string sid)
    {
        using var identity = new WindowsIdentity(token.DangerousGetHandle());
        return identity.User?.Value == sid && !IsTokenElevated(token);
    }

    private static void ValidateStandardProcess(Process process, string? executable = null)
    {
        using var current = Process.GetCurrentProcess();
        using var identity = WindowsIdentity.GetCurrent();
        if (current.SessionId != process.SessionId) throw new InvalidOperationException("Desktop session mismatch");
        using var handle = OpenProcess(0x1000, false, checked((uint)process.Id));
        if (handle.IsInvalid) throw new Win32Exception();
        if (!OpenProcessToken(handle, 0x0008, out var token)) throw new Win32Exception();
        using (token)
            if (!IsStandardUser(token, identity.User!.Value)) throw new InvalidOperationException("Process is not the same standard user");
        if (executable != null)
        {
            var path = new System.Text.StringBuilder(32768);
            int length = path.Capacity;
            if (!QueryFullProcessImageName(handle, 0, path, ref length)) throw new Win32Exception();
            if (!string.Equals(path.ToString(), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Handoff executable mismatch");
        }
    }

    private static void RestartStandard(string? arguments)
    {
        using var child = BeginStandardHandoff(Environment.ProcessPath!, arguments);
    }

    /// <summary>通过桌面启动同用户普通实例，保留管道直到单实例锁释放；调用方负责释放返回的进程对象。</summary>
    /// <param name="executable">目标可执行文件绝对路径。</param>
    /// <param name="arguments">已引用的应用参数。</param>
    /// <param name="show">ShellExecute 的窗口显示方式。</param>
    /// <returns>已校验用户、会话、权限和路径且等待交接的子进程。</returns>
    internal static Process BeginStandardHandoff(string executable, string? arguments, int show = 1)
    {
        if (_standardHandoff != null) throw new InvalidOperationException("Standard handoff already pending");
        string name = StandardPipePrefix + Guid.NewGuid().ToString("N");
        var pipe = CreateStandardPipe(name);
        Process? child = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var connected = pipe.WaitForConnectionAsync(timeout.Token);
            LaunchFromDesktop(executable, QuoteArguments(new[] { StandardArg, name, "--elevated-attempt" }) + " " + (arguments ?? ""), show);
            connected.GetAwaiter().GetResult();
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            string[] hello = (reader.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult() ?? "").Split('|');
            if (hello.Length != 2 || !int.TryParse(hello[0], out int pid) || !long.TryParse(hello[1], out long ticks)
                || pid <= 0 || pid == Environment.ProcessId) throw new InvalidDataException("Invalid handoff identity");
            child = Process.GetProcessById(pid);
            child.EnableRaisingEvents = true;
            if (child.StartTime.ToUniversalTime().Ticks != ticks) throw new InvalidDataException("Handoff process changed");
            ValidateStandardProcess(child, executable);
            var environment = Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
                .Where(entry => IsHandoffEnvironment((string)entry.Key)).ToDictionary(entry => (string)entry.Key, entry => (string?)entry.Value);
            writer.WriteLine(JsonSerializer.Serialize(environment));
            if (reader.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult() != "ready")
                throw new InvalidDataException("Standard instance is not ready");
            _standardHandoff = pipe;
            return child;
        }
        catch
        {
            pipe.Dispose();
            child?.Dispose();
            throw;
        }
    }

    private static bool IsHandoffEnvironment(string name) => name.StartsWith("NC_", StringComparison.OrdinalIgnoreCase)
        || name.Equals("TEMP", StringComparison.OrdinalIgnoreCase) || name.Equals("TMP", StringComparison.OrdinalIgnoreCase);

    private static NamedPipeServerStream CreateStandardPipe(string name)
    {
        using var identity = WindowsIdentity.GetCurrent();
        string sid = identity.User!.Value;
        // 显式用户所有者和 Medium 标签允许同一用户跨提权等级通信。
        string sddl = $"O:{sid}D:P(A;;GA;;;{sid})S:(ML;;NW;;;ME)";
        if (!ConvertStringSecurityDescriptor(sddl, 1, out var descriptor, out _)) throw new Win32Exception();
        try
        {
            var attributes = new StandardPipeSecurity { Size = Marshal.SizeOf<StandardPipeSecurity>(), Descriptor = descriptor };
            var handle = CreateNamedPipe(@"\\.\pipe\" + name, 0x40080003, 8, 1, 4096, 4096, 0, ref attributes);
            if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(); }
            try { return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle); }
            catch { handle.Dispose(); throw; }
        }
        finally { LocalFree(descriptor); }
    }

    /// <summary>普通实例在服务创建前接收隔离环境，并等待源实例释放单实例锁。</summary>
    /// <param name="arguments">包含随机管道名的启动参数。</param>
    /// <returns>无交接参数或已收到继续启动信号时为 true；验证失败、断线或超时为 false。</returns>
    internal static bool WaitForStandardHandoff(string[] arguments)
    {
        int index = Array.IndexOf(arguments, StandardArg);
        if (index < 0) return true;
        if (IsElevated || index + 1 >= arguments.Length || !arguments[index + 1].StartsWith(StandardPipePrefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(arguments[index + 1][StandardPipePrefix.Length..], "N", out _)) return false;
        try
        {
            using var pipe = new NamedPipeClientStream(".", arguments[index + 1], PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            pipe.ConnectAsync(timeout.Token).GetAwaiter().GetResult();
            using var reader = new StreamReader(pipe);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            using var process = Process.GetCurrentProcess();
            writer.WriteLine($"{process.Id}|{process.StartTime.ToUniversalTime().Ticks}");
            string payload = reader.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult() ?? throw new EndOfStreamException();
            var environment = JsonSerializer.Deserialize<Dictionary<string, string?>>(payload) ?? throw new InvalidDataException("Missing handoff environment");
            foreach (string name in Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(IsHandoffEnvironment).ToArray())
                Environment.SetEnvironmentVariable(name, null);
            foreach (var entry in environment)
                if (IsHandoffEnvironment(entry.Key)) Environment.SetEnvironmentVariable(entry.Key, entry.Value);
            writer.WriteLine("ready");
            return reader.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult() == "continue";
        }
        catch (Exception ex) { Core.Trace.Log("standard handoff failed: " + ex); return false; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StandardPipeSecurity
    {
        /// <summary>结构字节数。</summary>
        public int Size;
        /// <summary>本用户访问控制和完整性标签的自相对描述符。</summary>
        public IntPtr Descriptor;
        /// <summary>句柄是否可被子进程继承。</summary>
        public int Inherit;
    }

    /// <summary>在单实例锁释放后允许普通实例继续启动，关闭同用户交接管道。</summary>
    internal static void ReleaseStandardHandoff()
    {
        var pipe = Interlocked.Exchange(ref _standardHandoff, null);
        if (pipe == null) return;
        using (pipe)
        {
            try { using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true }; writer.WriteLine("continue"); }
            catch (Exception ex) { ReportStartupFailure("standard handoff release", ex); }
        }
    }

    private static void LaunchFromDesktop(string executable, string arguments, int show)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            object? windows = null, desktop = null, document = null, application = null;
            try
            {
                IntPtr shell = Native.Win32.GetShellWindow();
                if (shell == IntPtr.Zero) throw new InvalidOperationException("Desktop shell unavailable");
                Native.Win32.GetWindowThreadProcessId(shell, out uint pid);
                using var process = Process.GetProcessById(checked((int)pid));
                ValidateStandardProcess(process);
                windows = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"))!);
                object location = 0, root = 0; int hwnd = 0;
                desktop = ((dynamic)windows!).FindWindowSW(ref location, ref root, 8, out hwnd, 1);
                if (new IntPtr(hwnd) != shell) throw new InvalidOperationException("Desktop automation window mismatch");
                document = ((dynamic)desktop!).Document;
                application = ((dynamic)document!).Application;
                ((dynamic)application!).ShellExecute(executable, arguments, Path.GetDirectoryName(executable), "open", show);
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
            finally
            {
                foreach (object? item in new[] { application, document, desktop, windows })
                    try { if (item != null && Marshal.IsComObject(item)) Marshal.FinalReleaseComObject(item); } catch { }
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        completion.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass, out int information, int size, out int returned);
    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, System.Text.StringBuilder name, ref int length);
    [DllImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ConvertStringSecurityDescriptor(string descriptor, uint revision, out IntPtr security, out uint size);
    [DllImport("kernel32.dll", EntryPoint = "CreateNamedPipeW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafePipeHandle CreateNamedPipe(string name, uint mode, uint type, uint instances, uint outputSize,
        uint inputSize, uint timeout, ref StandardPipeSecurity security);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
