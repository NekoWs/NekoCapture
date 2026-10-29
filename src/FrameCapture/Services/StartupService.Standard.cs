using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FrameCapture.Services;

/// <summary>使用同一用户的桌面令牌启动普通权限实例，并在运行前校验其权限。</summary>
public static partial class StartupService
{
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

    /// <summary>获取当前会话同一用户的普通桌面主令牌，调用方负责释放。</summary>
    /// <returns>可用于创建普通权限进程的主令牌；桌面不可用或权限不符时抛出异常。</returns>
    internal static SafeAccessTokenHandle OpenStandardShellToken()
    {
        IntPtr shell = Native.Win32.GetShellWindow();
        if (shell == IntPtr.Zero) throw new InvalidOperationException("Desktop shell unavailable");
        Native.Win32.GetWindowThreadProcessId(shell, out uint pid);
        using var current = Process.GetCurrentProcess();
        using var desktop = Process.GetProcessById(checked((int)pid));
        if (current.SessionId != desktop.SessionId) throw new InvalidOperationException("Desktop session mismatch");
        using var process = OpenProcess(0x1000, false, pid);
        if (process.IsInvalid) throw new Win32Exception();
        if (!OpenProcessToken(process, 0x000A, out var token)) throw new Win32Exception();
        using (token)
        {
            using var identity = WindowsIdentity.GetCurrent();
            string sid = identity.User?.Value ?? throw new InvalidOperationException("User SID unavailable");
            if (!IsStandardUser(token, sid)) throw new InvalidOperationException("Desktop token is not the same standard user");
            if (!DuplicateTokenEx(token, 0x000B, IntPtr.Zero, 2, 1, out var primary)) throw new Win32Exception();
            return primary;
        }
    }

    private static void RestartStandard(string? arguments)
    {
        using var token = OpenStandardShellToken();
        using var current = Process.GetCurrentProcess();
        using var identity = WindowsIdentity.GetCurrent();
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable");
        string flags = QuoteArguments(new[] { exe, ParentArg, current.Id.ToString(),
            current.StartTime.ToUniversalTime().Ticks.ToString(), "--elevated-attempt" });
        var command = new StringBuilder(flags + " " + (arguments ?? ""));
        var startup = new StandardStartupInfo { Size = Marshal.SizeOf<StandardStartupInfo>(), Desktop = @"winsta0\default" };
        // CREATE_SUSPENDED 保证令牌校验完成前子进程不能执行应用代码。
        if (!CreateProcessWithTokenW(token, 1, exe, command, 4, IntPtr.Zero, Path.GetDirectoryName(exe), ref startup, out var info))
            throw new Win32Exception();
        using var process = new SafeProcessHandle(info.Process, true);
        using var thread = new SafeWaitHandle(info.Thread, true);
        bool resumed = false;
        try
        {
            if (!OpenProcessToken(process, 0x0008, out var childToken)) throw new Win32Exception();
            using (childToken)
                if (!IsStandardUser(childToken, identity.User!.Value)) throw new InvalidOperationException("Child token is not the same standard user");
            if (ResumeThread(thread) == uint.MaxValue) throw new Win32Exception();
            resumed = true;
        }
        finally { if (!resumed) TerminateProcess(process, 1); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StandardStartupInfo
    {
        /// <summary>结构字节数。</summary>
        public int Size;
        /// <summary>保留字段、桌面名称与窗口标题。</summary>
        public string? Reserved, Desktop, Title;
        /// <summary>位置、大小、字符网格与控制标记。</summary>
        public int X, Y, Width, Height, Columns, Rows, Fill, Flags;
        /// <summary>窗口显示标记与保留数据字节数。</summary>
        public short Show, ReservedSize;
        /// <summary>保留数据与标准输入输出句柄。</summary>
        public IntPtr ReservedData, Input, Output, Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StandardProcessInfo
    {
        /// <summary>进程与主线程句柄，由调用方关闭。</summary>
        public IntPtr Process, Thread;
        /// <summary>进程与线程标识。</summary>
        public uint ProcessId, ThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass, out int information, int size, out int returned);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(SafeAccessTokenHandle token, uint access, IntPtr attributes, int level, int type, out SafeAccessTokenHandle duplicate);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessWithTokenW(SafeAccessTokenHandle token, uint logonFlags, string application,
        StringBuilder command, uint flags, IntPtr environment, string? directory, ref StandardStartupInfo startup, out StandardProcessInfo process);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(SafeWaitHandle thread);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);
}
