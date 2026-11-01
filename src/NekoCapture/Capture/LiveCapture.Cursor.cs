using System.Runtime.InteropServices;
using NekoCapture.Native;

namespace NekoCapture.Capture;

/// <summary>GDI 帧的鼠标指针合成，沿用屏幕物理坐标与指针热点。</summary>
public sealed partial class LiveCapture
{
    private static IntPtr _cursorHandle;
    private static int _cursorHotspotX, _cursorHotspotY;

    private static void DrawCursor(IntPtr dc, RectI region)
    {
        var cursor = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        if (!GetCursorInfo(ref cursor) || (cursor.Flags & 1) == 0) return;
        PaintCursor(dc, region, cursor.Handle, cursor.Position.X, cursor.Position.Y);
    }

    // 调用方持有 StageLock；热点按句柄缓存，复制出的位图立即释放。
    private static bool PaintCursor(IntPtr dc, RectI region, IntPtr handle, int x, int y)
    {
        if (handle == IntPtr.Zero) return false;
        if (handle != _cursorHandle)
        {
            if (!GetIconInfo(handle, out var icon)) return false;
            try
            {
                _cursorHotspotX = (int)icon.HotspotX;
                _cursorHotspotY = (int)icon.HotspotY;
                _cursorHandle = handle;
            }
            finally
            {
                if (icon.Mask != IntPtr.Zero) Win32.DeleteObject(icon.Mask);
                if (icon.Color != IntPtr.Zero) Win32.DeleteObject(icon.Color);
            }
        }
        return DrawIconEx(dc, x - region.X - _cursorHotspotX, y - region.Y - _cursorHotspotY,
            handle, 0, 0, 0, IntPtr.Zero, 3);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        /// <summary>结构体大小，单位为字节。</summary>
        public int Size;
        /// <summary>指针显示状态，低位表示可见。</summary>
        public int Flags;
        /// <summary>系统拥有的指针句柄。</summary>
        public IntPtr Handle;
        /// <summary>指针热点的屏幕物理坐标。</summary>
        public Win32.POINT Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        /// <summary>非零表示图标，零表示指针。</summary>
        public int IsIcon;
        /// <summary>热点横坐标，单位为物理像素。</summary>
        public uint HotspotX;
        /// <summary>热点纵坐标，单位为物理像素。</summary>
        public uint HotspotY;
        /// <summary>调用方负责释放的掩码位图。</summary>
        public IntPtr Mask;
        /// <summary>调用方负责释放的颜色位图。</summary>
        public IntPtr Color;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(ref CursorInfo cursor);
    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr handle, out IconInfo icon);
    [DllImport("user32.dll")]
    private static extern bool DrawIconEx(IntPtr dc, int x, int y, IntPtr icon, int width, int height,
        uint step, IntPtr brush, uint flags);
}
