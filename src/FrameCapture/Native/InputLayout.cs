using System.Runtime.InteropServices;

namespace FrameCapture.Native;

/// <summary>
/// SendInput 输入结构在 x64 下的布局读数。P/Invoke 声明收在内部类型里，布局数值留一个公开读数口，
/// 供注入前的自检与外部校验引用——不必为此放开整个程序集的 internals 可见性。
/// </summary>
public static class InputLayout
{
    /// <value><c>INPUT</c> 的字节数，x64 原生为 40。</value>
    public static int SizeOfInput => Marshal.SizeOf<Win32.INPUT>();

    /// <value><c>MOUSEINPUT</c> 的字节数，x64 原生为 32。</value>
    public static int SizeOfMouseInput => Marshal.SizeOf<Win32.MOUSEINPUT>();

    /// <value><c>INPUT.mi</c> 的字节偏移，x64 原生为 8（<c>type</c> 之后按 8 字节对齐补空）。</value>
    public static int MouseOffset => Marshal.OffsetOf<Win32.INPUT>("mi").ToInt32();

    /// <value>三项读数与 x64 原生布局全部吻合时为 true；不符时滚轮注入拒发，避免把畸形结构交给系统。</value>
    public static bool Ok => SizeOfInput == Win32.SIZEOF_INPUT && MouseOffset == 8 && SizeOfMouseInput == 32;
}
