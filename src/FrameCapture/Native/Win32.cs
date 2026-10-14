using System.Runtime.InteropServices;
using System.Text;

namespace FrameCapture.Native;

/// <summary>
/// Win32 / DWM / GDI 的 P/Invoke 声明集中地：只放声明与极薄封装，不放业务判断。
/// 坐标约定：除注明"窗口相对"外，一律物理像素、虚拟桌面绝对坐标。
/// 句柄约定：不使用 SafeHandle——Get/Create 得到的 DC、位图、钩子由调用方在同一调用栈内配对释放。
/// </summary>
internal static class Win32
{
    #region 内存状态
    /// <summary>
    /// MEMORYSTATUSEX 的托管映射：数值单位为字节，仅 dwMemoryLoad 是 0–100 百分比。
    /// 用 class 而非 struct——API 按指针回填，传引用调用方才读得到结果。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public class MEMORYSTATUSEX
    {
        /// <summary>结构字节数。<b>调用前必须已填</b>，填错 API 直接返回 false；初始化器已按 Marshal.SizeOf 填好。</summary>
        public uint dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        /// <summary>可用物理内存（字节）：内存预算只认这一项，不含页面文件。</summary>
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    /// <summary>把当前内存状态回填进调用方持有的结构实例。</summary>
    /// <param name="lpStatus">已 new 出来的实例，dwLength 必须已填好。</param>
    /// <returns>true = 已回填；false = dwLength 与实际结构尺寸不符。</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GlobalMemoryStatusEx(MEMORYSTATUSEX lpStatus);
    #endregion

    #region 键盘钩子
    /// <summary>钩子类型与按键消息号，取值来自 WinUser.h。</summary>
    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    /// <summary>KBDLLHOOKSTRUCT.flags 的位：置位表示这次是抬起，清零表示按下。</summary>
    public const uint LLKHF_UP = 0x80;

    /// <summary>KBDLLHOOKSTRUCT 的托管映射：字段顺序与 Win32 声明一致，重排即读错内存。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        /// <summary>虚拟键码（VK_* 值），不受修饰键影响。</summary>
        public uint vkCode;
        public uint scanCode;
        /// <summary>位标志，见 <see cref="LLKHF_UP"/>；其余位含义见 WinUser.h 的 LLKHF_* 。</summary>
        public uint flags;
        /// <summary>消息时间戳（毫秒），系统时钟精度低于 Stopwatch，不用于测耗时。</summary>
        public uint time;
        public IntPtr dwExtraInfo;
    }

    /// <summary>低级键盘钩子回调。<b>必须在 LowLevelHooksTimeout 内返回</b>：
    /// 超时会被系统静默摘钩，表现为热键无声失效且无任何报错。</summary>
    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    /// <summary>装全局低级键盘钩子。返回的句柄必须配对 UnhookWindowsHookEx。</summary>
    /// <param name="idHook">钩子类型，本重载对应 WH_KEYBOARD_LL。</param>
    /// <param name="lpfn">回调委托。<b>调用方必须持有强引用</b>：委托被 GC 回收后系统回调即野指针。</param>
    /// <param name="hMod">GetModuleHandleW(null) 得到的本进程模块句柄。</param>
    /// <param name="dwThreadId">0 = 全系统所有线程；非 0 只监听该线程。</param>
    /// <returns>钩子句柄；IntPtr.Zero = 失败，原因取 Marshal.GetLastWin32Error。</returns>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookExW(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    /// <summary>摘掉低级钩子；摘除后回调委托才可安全交给 GC。</summary>
    /// <param name="hhk">SetWindowsHookExW 返回的钩子句柄。</param>
    /// <returns>true = 已摘除；false = 句柄无效或已摘过。</returns>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    /// <summary>把消息交给链上下一个钩子。nCode 小于 0 时必须直接返回它，不得自行判断。</summary>
    /// <param name="hhk">本钩子句柄；Win32 允许忽略，仍按原值转发。</param>
    /// <param name="nCode">钩子代码，原样转发。</param>
    /// <param name="wParam">消息 wParam，原样转发。</param>
    /// <param name="lParam">指向 KBDLLHOOKSTRUCT / MSLLHOOKSTRUCT 的指针，原样转发。</param>
    /// <returns>链下钩子的返回值。吞不吞这次输入取决于回调自己返回什么，非吞时即返回本调用结果。</returns>
    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    /// <summary>取已加载模块的句柄，不增加引用计数。</summary>
    /// <param name="name">模块名；null = 调用进程自身的 exe。</param>
    /// <returns>模块基址句柄；IntPtr.Zero = 模块未加载。</returns>
    [DllImport("kernel32.dll")]
    public static extern IntPtr GetModuleHandleW(string? name);

    /// <summary>某一刻的按键状态，与消息队列无关。</summary>
    /// <param name="vKey">虚拟键码（VK_* 值）。</param>
    /// <returns>最高位表示当前是否按下；最低位表示自上次调用以来是否被按过。</returns>
    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    public const int VK_CONTROL = 0x11, VK_MENU = 0x12, VK_SHIFT = 0x10, VK_LWIN = 0x5B, VK_RWIN = 0x5C;

    /// <summary>该虚拟键此刻是否处于按下状态（读高位，不消费"按过"标志，可安全轮询）。</summary>
    /// <param name="vk">虚拟键码，取 VK_* 常量。</param>
    /// <returns>true = 当前按下。两次轮询之间发生的按下加抬起会漏读。</returns>
    public static bool IsKeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
    #endregion

    #region 鼠标钩子（手势引擎的鼠标触发源）
    /// <summary>钩子类型与鼠标键消息号，取值来自 WinUser.h。</summary>
    public const int WH_MOUSE_LL = 14;
    public const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;
    public const int WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208;

    /// <summary>MSLLHOOKSTRUCT 的托管映射：字段顺序与 Win32 声明一致，重排即读错内存。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        /// <summary>事件发生时的光标位置，物理像素、虚拟桌面绝对坐标。</summary>
        public POINT pt;
        /// <summary>滚轮增量等有符号扩展值；普通按键消息为 0。</summary>
        public uint mouseData;
        public uint flags;
        /// <summary>消息时间戳（毫秒），系统时钟，不用于测耗时。</summary>
        public uint time;
        public IntPtr dwExtraInfo;
    }

    /// <summary>低级鼠标钩子回调。<b>必须在 LowLevelHooksTimeout 内返回</b>：
    /// 超时会被系统静默摘钩，表现为手势触发无声失效且无任何报错。</summary>
    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    /// <summary>装全局低级鼠标钩子。返回的句柄必须配对 UnhookWindowsHookEx。</summary>
    /// <param name="idHook">钩子类型，本重载对应 WH_MOUSE_LL。</param>
    /// <param name="lpfn">回调委托。<b>调用方必须持有强引用</b>：委托被 GC 回收后系统回调即野指针。</param>
    /// <param name="hMod">GetModuleHandleW(null) 得到的本进程模块句柄。</param>
    /// <param name="dwThreadId">0 = 全系统所有线程；非 0 只监听该线程。</param>
    /// <returns>钩子句柄；IntPtr.Zero = 失败，原因取 Marshal.GetLastWin32Error。</returns>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookExW(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
    #endregion

    #region 光标 / 显示器
    /// <summary>Win32 POINT：两个 32 位有符号整数，顺序不可换。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    /// <summary>Win32 RECT：左上任右下，右下为不含边（宽 = Right-Left）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    /// <summary>取当前光标位置。</summary>
    /// <param name="pt">输出：物理像素、虚拟桌面绝对坐标。</param>
    /// <returns>true = 已取到；false = 光标不在本桌面（罕见，锁屏切换瞬间）。</returns>
    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT pt);

    /// <summary>取包含该点的显示器句柄。</summary>
    /// <param name="pt">物理像素、虚拟桌面绝对坐标。</param>
    /// <param name="flags">MONITOR_DEFAULTTONULL / TOPRIMARY / TONEAREST 之一，决定无命中时的行为。</param>
    /// <returns>HMONITOR；IntPtr.Zero 仅在 MONITOR_DEFAULTTONULL 且该点落在屏外时出现。</returns>
    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    /// <summary>MonitorFrom* 的 flags 取值：无命中时返回 0 / 主屏 / 最近屏。</summary>
    public const uint MONITOR_DEFAULTTONULL = 0, MONITOR_DEFAULTTOPRIMARY = 1, MONITOR_DEFAULTTONEAREST = 2;

    /// <summary>把光标移到指定位置。</summary>
    /// <param name="x">目标 X，物理像素、虚拟桌面绝对坐标。</param>
    /// <param name="y">目标 Y，同上。</param>
    /// <returns>true = 已移动；false = 目标落在虚拟桌面外，或此刻有程序锁定光标。</returns>
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);
    #endregion

    #region 滚轮注入
    /// <summary>SendInput 的 INPUT.type 取值：0 = 鼠标事件。</summary>
    public const uint INPUT_MOUSE = 0;
    /// <summary>MOUSEINPUT.dwFlags 位：本次是滚轮事件，增量取 mouseData。</summary>
    public const uint MOUSEEVENTF_WHEEL = 0x0800;
    /// <summary>一格滚轮的固定增量（WinUser.h 的 WHEEL_DELTA）。</summary>
    public const int WHEEL_DELTA = 120;
    /// <summary>x64 下原生 INPUT 的字节数：type(4) + 对齐填充(4) + MOUSEINPUT(32)。</summary>
    public const int SIZEOF_INPUT = 40;

    /// <value>当前运行时算出的 INPUT 尺寸是否与原生一致。false 表示结构体布局对不上，注入必须整个停用。</value>
    public static bool InputLayoutOk { get; } = Marshal.SizeOf<INPUT>() == SIZEOF_INPUT;

    /// <summary>
    /// MOUSEINPUT 的托管映射，字段顺序与 WinUser.h 一致：x64 下 32 字节，dwExtraInfo 是 IntPtr，前面补 4 字节对齐填充。
    /// 它是 INPUT 那个联合里最大的成员，只声明它就够，不需要 Explicit 联合。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        /// <summary>横向坐标或增量（物理像素）；滚轮事件不用，填 0。</summary>
        public int dx;
        /// <summary>纵向坐标或增量（物理像素）；滚轮事件不用，填 0。</summary>
        public int dy;
        /// <summary>滚轮增量：一格 = <see cref="WHEEL_DELTA"/>，负值向下滚。其余鼠标事件按位含义见 WinUser.h。</summary>
        public uint mouseData;
        /// <summary>MOUSEEVENTF_* 位组合。</summary>
        public uint dwFlags;
        /// <summary>事件时间戳（毫秒）；填 0 = 由系统填当前时间。</summary>
        public uint time;
        /// <summary>随事件透传的附加信息，低级鼠标钩子可读；本仓填 IntPtr.Zero。</summary>
        public IntPtr dwExtraInfo;
    }

    /// <summary>SendInput 的一条输入：x64 下 type 之后 mi 落在偏移 8，合计 40 字节，与原生 INPUT 一致。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        /// <summary>输入类型，取 INPUT_* 常量；0 = 鼠标。</summary>
        public uint type;
        /// <summary>鼠标输入，联合里最大的成员。</summary>
        public MOUSEINPUT mi;
    }

    /// <summary>把输入插进调用线程的输入流，与真实硬件输入走同一分发通道。</summary>
    /// <param name="nInputs">pInputs 指向的数组长度。</param>
    /// <param name="pInputs">首条输入。</param>
    /// <param name="cbSize">单条输入的字节数，<b>必须等于原生 sizeof(INPUT)</b>：填小了系统按这个宽度读，会读越界内存。</param>
    /// <returns>实际插入的条数；0 = 被拒（cbSize 不符、被 UIPI 拦住高完整性窗口、锁屏或安全桌面）。</returns>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, ref INPUT pInputs, int cbSize);

    /// <summary>向光标所在窗口注入 notches 格滚轮。结构体尺寸对不上时直接拒绝，宁可不滚也不发畸形输入。</summary>
    /// <param name="notches">滚轮格数：正数向上、负数向下，一格 = <see cref="WHEEL_DELTA"/> 单位。</param>
    /// <returns>true = 系统已收下这条输入；false = 尺寸自检没过，或 SendInput 一条都没收下。</returns>
    public static bool Wheel(int notches)
    {
        if (!InputLayoutOk)
        {
            FrameCapture.Core.Trace.Log($"wheel refused: sizeof(INPUT)={Marshal.SizeOf<INPUT>()} expect {SIZEOF_INPUT}");
            return false;
        }
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT
            {
                dx = 0,
                dy = 0,
                mouseData = unchecked((uint)(-WHEEL_DELTA * notches)),
                dwFlags = MOUSEEVENTF_WHEEL,
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            },
        };
        return SendInput(1, ref input, Marshal.SizeOf<INPUT>()) == 1;
    }
    #endregion

    #region 窗口枚举（范围吸附）
    /// <summary>EnumWindows 回调。返回 false 会中止整轮枚举，逐窗过滤时必须返回 true 跳过。</summary>
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    /// <summary>按 Z 序自顶向下枚举全部顶层窗口，含不可见与 cloaked 窗口。</summary>
    /// <param name="lpEnumFunc">每个窗口调一次；返回 false 立即停止枚举。</param>
    /// <param name="lParam">透传给回调的上下文，本仓用 IntPtr.Zero（状态走闭包捕获）。</param>
    /// <returns>true = 枚举完整走完；false = 回调提前中止或枚举出错。</returns>
    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    /// <summary>窗口 WS_VISIBLE 位是否置起。cloaked 窗口与最小化窗口同样算可见。</summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <returns>true = 样式上可见；不代表屏幕上真看得见，需配合 DWMWA_CLOAKED 判断。</returns>
    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    /// <summary>取窗口标题（UTF-16）。</summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="lpString">调用方预分配且容量足够的 StringBuilder，容量不足会截断。</param>
    /// <param name="nMaxCount">最多写入字符数，含终止符。</param>
    /// <returns>实际写入字符数（不含终止符）；0 = 无标题或窗口已销毁。</returns>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    /// <summary>设置窗口标题。</summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="lpString">新标题，UTF-16；跨进程设置受 UIPI 限制，低完整性进程改不动高完整性窗口。</param>
    /// <returns>true = 已发出设置请求；false = 句柄无效。</returns>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool SetWindowTextW(IntPtr hWnd, string lpString);

    /// <summary>读窗口 32 位属性。<b>只用于 GWL_STYLE / GWL_EXSTYLE 这类 32 位索引</b>：
    /// 本导出按 DWORD 返回，取 GWLP_HWNDPARENT 这类指针型索引会在 x64 下被截断。</summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="nIndex">索引，取 GWL_* 常量（可为负值）。</param>
    /// <returns>该索引当前的 32 位值；窗口不存在时为 0 且 GetLastError 不区分。</returns>
    [DllImport("user32.dll")]
    public static extern uint GetWindowLongW(IntPtr hWnd, int nIndex);

    /// <summary>写窗口 32 位属性，索引限制同 <see cref="GetWindowLongW"/>。</summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="nIndex">索引，取 GWL_* 常量。</param>
    /// <param name="dwNewLong">新的完整 32 位值——不是要置位的掩码，调用方自己算或运算。</param>
    /// <returns>写入前该索引的原值；0 可能既是原值也是失败，需先 SetLastError 再判。</returns>
    [DllImport("user32.dll")]
    public static extern uint SetWindowLongW(IntPtr hWnd, int nIndex, uint dwNewLong);

    /// <summary>Win32 分层窗口 alpha（0=全透明）。WPF 的 Window.Opacity 要等首帧渲染才应用到 HWND，
    /// 未应用的那几帧 DWM 显示不透明黑面（入场黑闪）；在 SourceInitialized 直接置 alpha 可消除。</summary>
    /// <param name="hWnd">已带 WS_EX_LAYERED 扩展样式的窗口句柄。</param>
    /// <param name="crKey">颜色键，按 IntPtr 传以保持 32/64 位下的参数宽度；只用 alpha 时传 0。</param>
    /// <param name="bAlpha">整窗不透明度 0–255，与 WPF 的 0–1 Opacity 不同标度。</param>
    /// <param name="dwFlags">生效位，本仓只用 LWA_ALPHA。</param>
    /// <returns>true = 已生效；false = 窗口未带 WS_EX_LAYERED。</returns>
    [DllImport("user32.dll")]
    public static extern bool SetLayeredWindowAttributes(IntPtr hWnd, IntPtr crKey, byte bAlpha, uint dwFlags);
    /// <summary>SetLayeredWindowAttributes 的 dwFlags 位：bAlpha 生效。</summary>
    public const uint LWA_ALPHA = 0x2;

    /// <summary>设置 DWM 窗口属性（int 型值）。</summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="attr">属性号，取 DWMWA_* 常量。</param>
    /// <param name="pvAttribute">按引用传值的存储，DWM 直接读其地址。</param>
    /// <param name="cbAttribute">该存储的字节数，填错即读越界。</param>
    /// <returns>S_OK(0) = 已生效；非 0 = 属性号或尺寸不被当前系统支持，调用方静默忽略即可。</returns>
    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, ref int pvAttribute, int cbAttribute);
    /// <summary>圆角偏好属性号与取值（1=不圆角 2=上两角 3=四角）。</summary>
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWCP_ROUND = 2;

    /// <summary>改位置、尺寸、Z 序与显示状态，按 uFlags 逐项生效。</summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="hWndInsertAfter">Z 序插入点，取 HWND_TOPMOST 或 IntPtr.Zero。</param>
    /// <param name="X">新左上角 X，物理像素、虚拟桌面绝对坐标；带 SWP_NOMOVE 时忽略。</param>
    /// <param name="Y">新左上角 Y，同上；带 SWP_NOMOVE 时忽略。</param>
    /// <param name="cx">新宽度（物理像素）；带 SWP_NOSIZE 时忽略。</param>
    /// <param name="cy">新高度（物理像素）；带 SWP_NOSIZE 时忽略。</param>
    /// <param name="uFlags">SWP_* 掩码组合。置顶窗不带 SWP_NOACTIVATE 会抢前台。</param>
    /// <returns>true = 已应用；false = 句柄无效，扩展错误取 GetLastError。</returns>
    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    /// <summary>Z 序插入点：置于最顶且不改变其他窗口的相对顺序。</summary>
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    /// <summary>SetWindowPos 的行为位：不改尺寸 / 不改位置 / 不激活 / 顺带显示 / 不级联拥有窗。</summary>
    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010,
                       SWP_SHOWWINDOW = 0x0040, SWP_NOOWNERCASCADE = 0x0080;

    /// <summary>取桌面壳层窗口句柄，用于在枚举里排除桌面本身。</summary>
    /// <returns>壳层窗口句柄；IntPtr.Zero = 壳层未运行。</returns>
    [DllImport("user32.dll")]
    public static extern IntPtr GetShellWindow();

    /// <summary>取客户区矩形。</summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="lpRect">输出：<b>窗口相对坐标</b>，左上角通常为 (0,0)，尺寸不含边框与标题栏。</param>
    /// <returns>true = 已回填；false = 句柄无效。</returns>
    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>取窗口外框矩形。</summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="lpRect">输出：物理像素、虚拟桌面绝对坐标。含 DWM 的不可见调整边框，
    /// 因此比肉眼看到的窗框大一圈——要可见边界用 DWMWA_EXTENDED_FRAME_BOUNDS。</param>
    /// <returns>true = 已回填；false = 句柄无效。</returns>
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>窗口有效 DPI（96 制，Win10 1607+）。失败返回 0，调用方自兜底。</summary>
    /// <param name="hWnd">目标窗口句柄；未创建 HWND 时返回 0。</param>
    /// <returns>有效 DPI，96 = 100%；0 = 取不到，须自行回退到 96 或显示器 DPI。</returns>
    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);

    /// <summary>系统度量（DPI 感知进程下为物理像素）。虚拟桌面外接矩形用 SM_XVIRTUALSCREEN 等。</summary>
    /// <param name="nIndex">度量号，取 SM_* 常量。</param>
    /// <returns>度量值，单位物理像素；0 = 该度量取不到或参数无效。</returns>
    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);
    public const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    /// <summary>WDA_EXCLUDEFROMCAPTURE=0x11：窗口不出现在任何屏幕捕获（BitBlt/WGC）中。</summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="dwAffinity">取 WDA_* 常量。只能作用于本进程拥有的顶层窗口。</param>
    /// <returns>true = 已生效；false = 权限不足或句柄无效。</returns>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    /// <summary>窗口显示亲和性：0=无限制，1=只在受保护显示通路呈现、否则替换成纯色，0x11=对一切捕获隐身。</summary>
    public const uint WDA_NONE = 0x0, WDA_MONITOR = 0x1, WDA_EXCLUDEFROMCAPTURE = 0x11;

    /// <summary>显示器有效 DPI（96 制）：浮窗 Show 前换算物理→DIP，避免首帧位置闪烁。</summary>
    /// <param name="hmonitor">HMONITOR。</param>
    /// <param name="dpiType">取 DPI_* 常量；本仓只用 DPI_EFFECTIVE。</param>
    /// <param name="dpiX">输出水平 DPI，96 = 100%。</param>
    /// <param name="dpiY">输出垂直 DPI，通常与 dpiX 相同。</param>
    /// <returns>S_OK(0) = 已回填；非 0 = 句柄无效，调用方回退 96。</returns>
    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    /// <summary>GetDpiForMonitor 的 dpiType：有效 DPI，已含缩放设置。</summary>
    public const int DPI_EFFECTIVE = 0;

    /// <summary>把客户区点换算到屏幕坐标。</summary>
    /// <param name="hWnd">坐标所属窗口。</param>
    /// <param name="lpPoint">入参为窗口相对坐标，成功时就地改写为物理像素、虚拟桌面绝对坐标。</param>
    /// <returns>true = 已换算；false = 句柄无效，此时 lpPoint 保持原值。</returns>
    [DllImport("user32.dll")]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);


    /// <summary>取窗口所属线程与进程 id。</summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="processId">输出进程 id。声明为 out uint，不关心进程时也要给变量（调用方写 out _）。</param>
    /// <returns>线程 id；0 = 句柄无效。AttachThreadInput 需要它。</returns>
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    /// <summary>扩展样式索引与常用扩展样式位。</summary>
    public const int GWL_EXSTYLE = -20;
    public const uint WS_EX_TOOLWINDOW = 0x00000080, WS_EX_TRANSPARENT = 0x00000020,
                       WS_EX_LAYERED = 0x00080000, WS_EX_NOACTIVATE = 0x08000000,
                       WS_EX_APPWINDOW = 0x00040000;

    /// <summary>读 DWM 窗口属性（RECT 型值）。</summary>
    /// <param name="hwnd">目标窗口句柄。</param>
    /// <param name="attr">属性号，取 DWMWA_* 常量。</param>
    /// <param name="pvAttribute">输出值。</param>
    /// <param name="cbAttribute">输出值的字节数，须等于 sizeof(RECT)。</param>
    /// <returns>S_OK(0) = 已回填；非 0 = 不支持，调用方须自行兜底（如退回 GetWindowRect）。</returns>
    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT pvAttribute, int cbAttribute);

    /// <summary>读 DWM 窗口属性（uint 型值）。</summary>
    /// <param name="hwnd">目标窗口句柄。</param>
    /// <param name="attr">属性号，取 DWMWA_* 常量。</param>
    /// <param name="pvAttribute">输出值。</param>
    /// <param name="cbAttribute">输出值的字节数，须为 4。</param>
    /// <returns>S_OK(0) = 已回填；非 0 = 不支持或句柄无效。</returns>
    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out uint pvAttribute, int cbAttribute);

    /// <summary>可见外框（不含 DWM 不可见调整边框）；14 = cloaked 标志，非 0 表示窗口此刻不呈现于当前桌面。</summary>
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const int DWMWA_CLOAKED = 14;

    /// <summary>设置 DWM 窗口属性（uint 型值）。</summary>
    /// <param name="hwnd">目标窗口句柄。</param>
    /// <param name="attr">属性号，取 DWMWA_* 常量。</param>
    /// <param name="pvAttribute">按引用传值的存储。</param>
    /// <param name="cbAttribute">该存储的字节数，须为 4。</param>
    /// <returns>S_OK(0) = 已生效；非 0 = 系统不支持该属性。</returns>
    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint pvAttribute, int cbAttribute);

    /// <summary>请求 DWM 圆角。低版本系统无此属性，失败静默——圆角是装饰，不值得为此崩。</summary>
    /// <param name="hwnd">目标窗口句柄。</param>
    /// <param name="pref">圆角偏好，取 DWMWCP_* 常量，默认四角圆。</param>
    public static void SetRoundedCorners(IntPtr hwnd, uint pref = 2)
    {
        uint v = pref;
        try { DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref v, 4); } catch { }
    }
    #endregion

    #region 前台窗口与线程输入
    /// <summary>把窗口带到前台。</summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <returns>true = 已置前台；false = 前台权限校验没过（只有持前台权的进程可改），
    /// 需走 AttachThreadInput 链或 SwitchToThisWindow 兜底。</returns>
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>取当前前台窗口。</summary>
    /// <returns>前台窗口句柄；IntPtr.Zero = 无前台（切换瞬间）。</returns>
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    /// <summary>把两个线程的输入队列接起来，让附着的线程获得前台权限。</summary>
    /// <param name="idAttach">本线程 id。</param>
    /// <param name="idAttachTo">前台线程 id。</param>
    /// <param name="fAttach">true = 连接，false = 断开。<b>必须配对断开</b>，否则两线程的焦点与键盘状态互相牵连。</param>
    /// <returns>true = 本次连接/断开成功；断开失败时队列由系统自行解绑。</returns>
    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    /// <summary>把窗口提到所属线程的 Z 序顶部。</summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <returns>true = 已调用；不保证窗口真到前台，也不保证激活。</returns>
    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    /// <summary>切到该窗口，走的是系统自己的切换通道，绕过部分前台权限校验。</summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <param name="fAltTab">true = 按 Alt+Tab 的语义切换（最小化窗会被还原）。</param>
    [DllImport("user32.dll")]
    public static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);

    /// <summary>本线程 id，AttachThreadInput 的一侧参数。</summary>
    /// <returns>调用线程的 id，恒非 0。</returns>
    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    /// <summary>句柄当前是否仍是一个有效窗口。</summary>
    /// <param name="hWnd">待校验句柄，可以是任意整数值。</param>
    /// <returns>true = 窗口存在。句柄可能在判断之后立刻失效，不能当同步保证。</returns>
    [DllImport("user32.dll")]
    public static extern bool IsWindow(IntPtr hWnd);

    /// <summary>窗口是否处于最小化状态。</summary>
    /// <param name="hWnd">目标窗口句柄。</param>
    /// <returns>true = 已最小化，此时窗口矩形不可作对齐参考。</returns>
    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);
    #endregion

    #region GDI 捕获
    /// <summary>取窗口（或屏幕）的设备上下文。</summary>
    /// <param name="hWnd">目标窗口；IntPtr.Zero = 整个虚拟桌面。</param>
    /// <returns>DC 句柄；IntPtr.Zero = 失败。<b>必须配对 ReleaseDC</b>，泄漏会耗尽 GDI 句柄。</returns>
    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    /// <summary>归还 GetDC 拿到的 DC。</summary>
    /// <param name="hWnd">与 GetDC 完全相同的窗口句柄，IntPtr.Zero 也要照传。</param>
    /// <param name="hDC">待归还的 DC。</param>
    /// <returns>1 = 已归还；0 = 句柄或窗口不匹配，DC 仍被占用。</returns>
    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    /// <summary>建与目标 DC 兼容的内存 DC。</summary>
    /// <param name="hdc">参照 DC；IntPtr.Zero = 屏幕。</param>
    /// <returns>内存 DC 句柄。<b>必须配对 DeleteDC</b>，且删除前须先选回原对象。</returns>
    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    /// <summary>把对象选入 DC，返回被换出的对象。</summary>
    /// <param name="hdc">目标内存 DC。</param>
    /// <param name="h">待选入的位图或画笔句柄。</param>
    /// <returns>原先选中的对象句柄。<b>用完必须把它选回去再 DeleteDC</b>，否则 DC 删不掉、GDI 泄漏。</returns>
    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    /// <summary>删除内存 DC。</summary>
    /// <param name="hdc">CreateCompatibleDC 得到的 DC。</param>
    /// <returns>true = 已删除；false = DC 内仍选着非初始对象。不可用于 ReleaseDC 拿到的 DC。</returns>
    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern bool DeleteDC(IntPtr hdc);

    /// <summary>删除 GDI 对象（位图、画笔、区域等）。</summary>
    /// <param name="h">待删对象句柄。</param>
    /// <returns>true = 已删除；false = 该对象仍被选进某个 DC，须先选回原对象。</returns>
    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern bool DeleteObject(IntPtr h);

    /// <summary>位块传输。</summary>
    /// <param name="hdc">目标 DC。</param>
    /// <param name="x">目标左上角 X（目标 DC 坐标）。</param>
    /// <param name="y">目标左上角 Y。</param>
    /// <param name="w">宽（像素）。</param>
    /// <param name="h">高（像素）。</param>
    /// <param name="hdcSrc">源 DC，取屏幕 DC 即抓屏。</param>
    /// <param name="xSrc">源左上角 X，虚拟桌面绝对坐标。</param>
    /// <param name="ySrc">源左上角 Y。</param>
    /// <param name="rop">光栅运算，本仓用 SRCCOPY | CAPTUREBLT。</param>
    /// <returns>true = 已拷贝；false = 参数越界或 DC 失效。异步 GDI 下须 GdiFlush 后才可读位。</returns>
    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern bool BitBlt(IntPtr hdc, int x, int y, int w, int h,
        IntPtr hdcSrc, int xSrc, int ySrc, uint rop);

    /// <summary>把位图像素按 DIB 格式取出（写入调用方给的指针）。</summary>
    /// <param name="hdc">位图所属 DC。</param>
    /// <param name="hbmp">源位图，不能正被选进该 DC 之外的 DC。</param>
    /// <param name="firstLine">起始扫描行，按 biHeight 符号方向计。</param>
    /// <param name="lines">取多少行；超过剩余行数会被截到实际值。</param>
    /// <param name="bmi">DIB 描述，biSize 必须已填；函数返回时可能被改写实际尺寸。</param>
    /// <param name="bits">目标内存，容量须够 lines 行。</param>
    /// <param name="usage">DIB_RGB_COLORS：调色板按 RGB 解释。</param>
    /// <returns>实际拷贝的扫描行数；0 = 失败。行数少于请求值不算失败。</returns>
    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint firstLine, uint lines,
        ref BITMAPINFO bmi, IntPtr bits, uint usage);

    /// <summary>把位图像素按 DIB 格式取出（写入托管数组）。</summary>
    /// <param name="hdc">位图所属 DC。</param>
    /// <param name="hbmp">源位图句柄。</param>
    /// <param name="firstLine">起始扫描行。</param>
    /// <param name="lines">取多少行。</param>
    /// <param name="bmi">DIB 描述，biSize 必须已填。</param>
    /// <param name="bits">目标数组，长度须够 lines 行；封送器会固定其地址。</param>
    /// <param name="usage">DIB_RGB_COLORS。</param>
    /// <returns>实际拷贝的扫描行数；0 = 失败。</returns>
    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint firstLine, uint lines,
        ref BITMAPINFO bmi, byte[] bits, uint usage);


    /// <summary>建一段可直接读写的 DIB，像素内存由 GDI 持有。</summary>
    /// <param name="hdc">参照 DC；IntPtr.Zero = 屏幕。</param>
    /// <param name="bmi">DIB 描述。biHeight 取<b>负值</b>才是 top-down 行序，与帧缓冲约定一致。</param>
    /// <param name="usage">DIB_RGB_COLORS。</param>
    /// <param name="ppvBits">输出像素首地址。<b>不得单独释放</b>——随位图句柄 DeleteObject 一起回收。</param>
    /// <param name="hSection">页文件映射段，本仓传 IntPtr.Zero 让系统自己分配。</param>
    /// <param name="offset">段内偏移，配合 hSection 用；本仓传 0。</param>
    /// <returns>位图句柄；IntPtr.Zero = 失败，此时 ppvBits 为 IntPtr.Zero。</returns>
    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, uint usage,
        out IntPtr ppvBits, IntPtr hSection, uint offset);

    /// <summary>冲刷 GDI 的延迟批处理队列。</summary>
    /// <returns>true = 队列已清空。BitBlt 之后不刷就可能读到未落地的像素。</returns>
    [DllImport("gdi32.dll")]
    public static extern bool GdiFlush();

    /// <summary>DIB 压缩类型：无压缩。</summary>
    public const uint BI_RGB = 0;
    /// <summary>光栅运算：直接覆盖；CAPTUREBLT 额外把分层窗口画进结果。</summary>
    public const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;

    /// <summary>
    /// BITMAPINFOHEADER 的托管映射（不含调色板数组，32 位 DIB 用不到）。
    /// 字段顺序与 Win32 声明一致；biSize 必须在传入前填成本结构字节数。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO
    {
        public uint biSize;
        /// <summary>宽（像素）；高为<b>负值</b>表示 top-down 行序，正值是 GDI 默认的 bottom-up。</summary>
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    /// <summary>BITMAPINFO 的字节数，直接填给 biSize。</summary>
    public static readonly uint SIZEOF_BITMAPINFOHEADER = (uint)Marshal.SizeOf<BITMAPINFO>();

    // 工作集回收：录制/编辑大位图关闭后，.NET 只标垃圾不把页交还 OS，工作集虚高；
    // 用户手动 MemReduct 就是调 SetProcessWorkingSetSize(-1,-1) 把空闲页踢回系统。我们在大操作收尾主动做同样的事。
    /// <summary>本进程句柄，伪句柄，无需也不得 CloseHandle。</summary>
    /// <returns>当前进程的伪句柄。</returns>
    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();

    /// <summary>设置进程工作集上下限。</summary>
    /// <param name="hProcess">进程句柄，取 GetCurrentProcess。</param>
    /// <param name="dwMinimumWorkingSetSize">下限；new IntPtr(-1) = 立即把可换出的页踢给系统并按需重设。</param>
    /// <param name="dwMaximumWorkingSetSize">上限，同上约定。</param>
    /// <returns>true = 已设置；false = 句柄无效或权限不足。</returns>
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

    /// <summary>把当前进程工作集裁到最小（空闲页交还 OS）。-1/-1 = 系统按需重设上下限。</summary>
    public static void TrimWorkingSet()
    {
        try { SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1)); } catch { }
    }
    #endregion

    #region WinForms 模态框 Owner
    /// <summary>把 WPF 窗口句柄包成 WinForms IWin32Window，供 FolderBrowserDialog 等模态框挂 owner。</summary>
    internal sealed class WndOwner : System.Windows.Forms.IWin32Window
    {
        /// <summary>包一个已存在的 HWND。</summary>
        /// <param name="handle">WPF 窗口的 HWND，生命周期由 WPF 管，本类不负责销毁。</param>
        public WndOwner(IntPtr handle) => Handle = handle;
        public IntPtr Handle { get; }
    }
    #endregion

}
