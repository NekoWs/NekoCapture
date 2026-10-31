using System.Runtime.InteropServices;
using System.Windows;
using FrameCapture.Native;

namespace FrameCapture.Services;

/// <summary>独立键盘消息线程；界面阻塞不阻塞输入链，按需界面仅在会话期间安装钩子。</summary>
public sealed partial class HotkeyManager
{
    private static volatile bool _scrollActive, _countdownActive, _toastActive, _pickerActive, _paused;
    private static Func<uint, GestureCore.ModifierKeys, bool>? _armedRecordKey;
    private static Func<uint, bool, GestureCore.ModifierKeys, long, bool>? _captureSink;
    private static HotkeyManager? _activeManager;
    private readonly HashSet<uint> _captureKeyUps = new();
    private Thread? _inputThread;
    private uint _inputThreadId;
    private volatile bool _inputStopping;

    /// <summary>创建会话键事件源。</summary>
    public HotkeyManager() => _activeManager = this;

    private bool HasSessions => ScrollActive || CountdownActive || ToastActive || PickerActive
        || CaptureSink != null || ArmedRecordKey != null || _armedRecordKeyUps.Count > 0 || _captureKeyUps.Count > 0
        || _pickerNudgeKeyUps.Count > 0;

    private static void SessionsChanged()
    {
        if (!ResidentBridge.IsWorker || _activeManager is not { } manager || Application.Current?.Dispatcher is not { } dispatcher) return;
        void Refresh()
        {
            if (manager.HasSessions != manager.Installed) manager.Install();
        }
        if (dispatcher.CheckAccess()) Refresh(); else dispatcher.BeginInvoke(Refresh);
    }

    private void StartInputThread()
    {
        var ready = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _inputStopping = false;
        _inputThread = new Thread(() =>
        {
            _inputThreadId = Win32.GetCurrentThreadId();
            PeekInputMessage(out _, IntPtr.Zero, 0, 0, 0);
            try
            {
                _proc = HookProc;
                _hook = Win32.SetWindowsHookExW(Win32.WH_KEYBOARD_LL, _proc, Win32.GetModuleHandleW(null), 0);
                Exception? failure = _hook == IntPtr.Zero ? new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()) : null;
                Core.Trace.Log($"Install hook={_hook}");
                ready.TrySetResult(failure);
                if (_hook == IntPtr.Zero || _inputStopping) return;
                while (!_inputStopping && GetInputMessage(out var message, IntPtr.Zero, 0, 0) > 0)
                { TranslateInputMessage(ref message); DispatchInputMessage(ref message); }
            }
            finally
            {
                if (_hook != IntPtr.Zero) Win32.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero; _proc = null; _inputThreadId = 0;
            }
        }) { IsBackground = true, Name = "Capture input", Priority = ThreadPriority.AboveNormal };
        _inputThread.Start();
        var failure = ready.Task.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        if (failure != null) throw failure;
    }

    private void StopInputThread()
    {
        var thread = _inputThread;
        if (thread == null) return;
        _inputStopping = true;
        if (_inputThreadId != 0) PostInputThreadMessage(_inputThreadId, 0x0012, IntPtr.Zero, IntPtr.Zero);
        if (thread != Thread.CurrentThread && !thread.Join(2000)) throw new TimeoutException("Keyboard thread did not stop");
        _inputThread = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InputMessage
    {
        /// <summary>目标窗口。</summary>
        public IntPtr Window;
        /// <summary>消息编号。</summary>
        public uint Id;
        /// <summary>消息参数。</summary>
        public UIntPtr W;
        /// <summary>指针参数。</summary>
        public IntPtr L;
        /// <summary>消息时间。</summary>
        public uint Time;
        /// <summary>光标位置。</summary>
        public int X, Y;
        /// <summary>系统保留字段。</summary>
        public uint Private;
    }
    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    private static extern bool PeekInputMessage(out InputMessage message, IntPtr window, uint first, uint last, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMessageW")]
    private static extern int GetInputMessage(out InputMessage message, IntPtr window, uint first, uint last);
    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    private static extern bool PostInputThreadMessage(uint thread, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll", EntryPoint = "TranslateMessage")]
    private static extern bool TranslateInputMessage(ref InputMessage message);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static extern IntPtr DispatchInputMessage(ref InputMessage message);
}
