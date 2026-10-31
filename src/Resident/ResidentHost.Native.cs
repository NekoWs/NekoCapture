using System.Runtime.InteropServices;

namespace FrameCapture.Resident;

/// <summary>常驻窗口、托盘和输入的 Win32 边界。</summary>
internal sealed partial class ResidentHost
{
    private delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr w, IntPtr l);
    private delegate IntPtr HookProcedure(int code, IntPtr w, IntPtr l);

    private NotifyIconData IconData() => new()
    {
        Size = Marshal.SizeOf<NotifyIconData>(), Window = _window, Id = 1, Flags = 7, Callback = 0x8001,
        Icon = _paused && _pausedIcon != IntPtr.Zero ? _pausedIcon : _icon, Tip = (_paused ? _manifest.PausedTooltip : _manifest.Tooltip) is { Length: > 127 } text ? text[..127]
            : _paused ? _manifest.PausedTooltip : _manifest.Tooltip
    };

    private bool _iconAdded;
    private IntPtr _pausedIcon;
    private void ReloadIcons()
    {
        void Load(string name, ref IntPtr target)
        {
            string path = Path.Combine(FrameCapture.Runtime.ResidentProtocol.DirectoryPath, Path.GetFileName(name));
            if (!File.Exists(path)) return;
            IntPtr icon = FrameCapture.Runtime.TrayIconSource.LoadFile(path);
            if (icon == IntPtr.Zero) return;
            if (target != IntPtr.Zero) DestroyIcon(target);
            target = icon;
        }
        Load(_manifest.IconFile, ref _icon); Load(_manifest.PausedIconFile, ref _pausedIcon);
    }
    private void UpdateIcon()
    {
        var data = IconData();
        if (!ShellNotifyIcon(_iconAdded ? 1u : 0u, ref data)) { _iconAdded = false; ShellNotifyIcon(0, ref data); }
        _iconAdded = true;
    }
    private void RemoveIcon() { var data = IconData(); ShellNotifyIcon(2, ref data); }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        /// <summary>窗口类样式。</summary>
        public uint Style;
        /// <summary>消息处理入口。</summary>
        public IntPtr Procedure;
        /// <summary>额外存储尺寸。</summary>
        public int ClassExtra, WindowExtra;
        /// <summary>模块与类资源。</summary>
        public IntPtr Instance, Icon, Cursor, Background;
        /// <summary>类菜单与类名。</summary>
        public string? Menu, Name;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        /// <summary>目标窗口。</summary>
        public IntPtr Window;
        /// <summary>消息编号。</summary>
        public uint Id;
        /// <summary>整数参数。</summary>
        public UIntPtr W;
        /// <summary>指针参数。</summary>
        public IntPtr L;
        /// <summary>消息时间。</summary>
        public uint Time;
        /// <summary>光标坐标。</summary>
        public int X, Y;
        /// <summary>系统保留字段。</summary>
        public uint Private;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardData
    {
        /// <summary>键码、扫描码、标志和时间。</summary>
        public uint Key, Scan, Flags, Time;
        /// <summary>附加数据。</summary>
        public UIntPtr Extra;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseData
    {
        /// <summary>物理光标位置。</summary>
        public int X, Y;
        /// <summary>鼠标数据、标志和时间。</summary>
        public uint Data, Flags, Time;
        /// <summary>附加数据。</summary>
        public UIntPtr Extra;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        /// <summary>结构尺寸。</summary>
        public int Size;
        /// <summary>回调窗口。</summary>
        public IntPtr Window;
        /// <summary>图标编号、字段标志与回调消息。</summary>
        public uint Id, Flags, Callback;
        /// <summary>图标句柄。</summary>
        public IntPtr Icon;
        /// <summary>托盘文字。</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? Tip;
        /// <summary>图标状态与状态掩码。</summary>
        public uint State, StateMask;
        /// <summary>气泡正文。</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string? Info;
        /// <summary>版本或气泡时限。</summary>
        public uint Version;
        /// <summary>气泡标题。</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string? InfoTitle;
        /// <summary>气泡标志。</summary>
        public uint InfoFlags;
        /// <summary>图标稳定标识。</summary>
        public Guid Guid;
        /// <summary>气泡图标。</summary>
        public IntPtr BalloonIcon;
    }

    [DllImport("user32.dll", EntryPoint = "RegisterClassW", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClass(ref WindowClass windowClass);
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(uint extra, string? name, string title, uint style, int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    private static extern bool PeekMessage(out Message message, IntPtr window, uint first, uint last, uint flags);
    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll")]
    private static extern uint MsgWaitForMultipleObjectsEx(uint count, ref IntPtr handles, uint timeout, uint wake, uint flags);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? name, string title);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr window, uint message, uint action, IntPtr filter);
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    private static extern bool ShellNotifyIcon(uint operation, ref NotifyIconData data);
    [DllImport("shell32.dll", EntryPoint = "ExtractIconExW", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string path, int index, out IntPtr large, out IntPtr small, uint count);
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW")]
    private static extern IntPtr SetWindowsHookEx(int type, HookProcedure procedure, IntPtr module, uint thread);
    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]
    private static extern UIntPtr SetTimer(IntPtr window, uint id, uint interval, IntPtr procedure);
    [DllImport("user32.dll")]
    private static extern bool KillTimer(IntPtr window, uint id);
    [DllImport("kernel32.dll", EntryPoint = "FindFirstChangeNotificationW", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindFirstChangeNotification(string path, bool subtree, uint changes);
    [DllImport("kernel32.dll")]
    private static extern bool FindNextChangeNotification(IntPtr change);
    [DllImport("kernel32.dll")]
    private static extern bool FindCloseChangeNotification(IntPtr change);
}
