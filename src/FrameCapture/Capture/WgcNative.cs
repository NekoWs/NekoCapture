using System.Runtime.InteropServices;
using Windows.Graphics.Capture;

namespace FrameCapture.Capture;

/// <summary>
/// WGC 最小 interop：D3D11 设备 → WinRT IDirect3DDevice；HMONITOR → GraphicsCaptureItem。
/// GUID 取自 Windows SDK（windows.graphics.capture.interop.h / windows.graphics.direct3d11.deviceinterop.h）。
/// 任一步失败抛 COMException → 调用方回退 GDI。
/// </summary>
internal static class WgcNative
{
    static readonly Guid IidGraphicsCaptureItemInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"); // um/Windows.Graphics.Capture.Interop.h
    static readonly Guid IidDirect3DDeviceInterop = new("C89BAAEE-0437-4851-9F95-6AD3542709D5");
    static readonly Guid IidIdxgIDevice = new("54EC757C-C669-4834-A617-1EDFC802E7CB");

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        void CreateForWindow(IntPtr hwnd, ref Guid riid, out IntPtr result); // vtable slot 0（SDK 头文件顺序）
        void CreateForMonitor(IntPtr hMonitor, ref Guid riid, out IntPtr result); // slot 1
    }

    [ComImport, Guid("C89BAAEE-0437-4851-9F95-6AD3542709D5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDeviceInterop
    {
        void Create(IntPtr dxgiDevice, out IntPtr device);
        void Get(IntPtr device, out IntPtr dxgiDevice);
    }

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string source, uint length, out IntPtr hString);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern void WindowsDeleteString(IntPtr hString);

    [DllImport("combase.dll", PreserveSig = true)]
    private static extern int RoGetActivationFactory(IntPtr classId, ref Guid iid, out IntPtr factory);

    private static void RoGetActivationFactory(string classId, ref Guid iid, out IntPtr factory)
    {
        int hc = WindowsCreateString(classId, (uint)classId.Length, out IntPtr hs);
        if (hc != 0) throw new COMException($"WindowsCreateString hr=0x{hc:X8}", hc);
        try
        {
            int hr = RoGetActivationFactory(hs, ref iid, out factory);
            if (hr != 0) throw new COMException($"RoGetActivationFactory({classId}) hr=0x{hr:X8}", hr);
        }
        finally { WindowsDeleteString(hs); }
    }

    [DllImport("d3d11.dll", PreserveSig = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(
        IntPtr adapter, int driverType, IntPtr software, uint flags,
        IntPtr pFeatureLevels, uint featureLevelCount, uint sdkVersion,
        out IntPtr device, out uint featureLevel, out IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int QueryIfaceDelegate(IntPtr self, ref Guid riid, out IntPtr ppv);

    /// <summary>IUnknown::QueryInterface（vtable slot 0）。</summary>
    private static IntPtr ComQuery(IntPtr punk, Guid iid)
    {
        IntPtr vt = Marshal.ReadIntPtr(punk);
        IntPtr fn = Marshal.ReadIntPtr(vt);
        var qi = Marshal.GetDelegateForFunctionPointer<QueryIfaceDelegate>(fn);
        Guid g = iid;
        int hr = qi(punk, ref g, out IntPtr p);
        if (hr != 0) throw new COMException($"QI {iid} hr=0x{hr:X8}", hr);
        return p;
    }

    /// <summary>创建硬件 D3D11 设备并包装为 WinRT IDirect3DDevice（调用方持有返回的 IUnknown 引用）。</summary>
    public static IntPtr CreateDirect3DDevice()
    {
        const uint SDK_VERSION = 7; // D3D11
        uint[] levels = { 0xC100 /*FL_11_1*/, 0xB000 /*FL_11_0*/ };
        GCHandle levelsHandle = GCHandle.Alloc(levels, GCHandleType.Pinned);
        IntPtr d3d, ctx;
        int hr;
        try
        {
            int[] tryTypes = { 0 /*HARDWARE*/, 5 /*WARP*/ };
            hr = unchecked((int)0x80070057); d3d = IntPtr.Zero; ctx = IntPtr.Zero;
            foreach (int dt in tryTypes)
            {
                hr = D3D11CreateDevice(IntPtr.Zero, dt, IntPtr.Zero, 0,
                                       levelsHandle.IsAllocated ? levelsHandle.AddrOfPinnedObject() : IntPtr.Zero,
                                       (uint)levels.Length, SDK_VERSION, out IntPtr d3d0, out _, out IntPtr ctx0);
                if (hr == 0) { d3d = d3d0; ctx = ctx0; break; }
                FrameCapture.Core.Trace.Log($"D3D11CreateDevice dt={dt} hr=0x{hr:X8}");
            }
        }
        finally { levelsHandle.Free(); }
        if (hr != 0) throw new COMException($"D3D11CreateDevice hr=0x{hr:X8}", hr);
        IntPtr dxgi = IntPtr.Zero, dev = IntPtr.Zero;
        _ = ctx;
        try
        {
            // d3d11.dll 官方 free function：ID3D11Device(IUnknown*) → Windows.Graphics.Direct3D11.IDirect3DDevice
            int hr2 = CreateDirect3D11DeviceFromDXGIDevice(d3d, out dev);
            if (hr2 != 0) throw new COMException($"CreateDirect3D11DeviceFromDXGIDevice hr=0x{hr2:X8}", hr2);
            return dev;
        }
        catch
        {
            if (dev != IntPtr.Zero) Marshal.Release(dev);
            throw;
        }
        finally
        {
            Marshal.Release(d3d);
        }
    }

    /// <summary>IMemoryBufferReference → 原始字节指针（IBufferByteAccess，vtable slot 0）。</summary>
    public static IntPtr ReferenceBytes(object memoryBufferReference)
    {
        // 优先取投影对象持有的原生 IUnknown（CCW 不暴露 IMemoryBufferByteAccess）
        IntPtr punk = IntPtr.Zero;
        if (memoryBufferReference is WinRT.IWinRTObject wrtObj)
        {
            IntPtr dbg = wrtObj.NativeObject.ThisPtr;
            if (dbg != IntPtr.Zero) { Marshal.AddRef(dbg); punk = dbg; }
        }
        if (punk == IntPtr.Zero) punk = Marshal.GetIUnknownForObject(memoryBufferReference);
        try
        {
            Guid iid = new Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D"); // Windows.Foundation.IMemoryBufferByteAccess
            IntPtr p = ComQuery(punk, iid);
            try
            {
                IntPtr vt = Marshal.ReadIntPtr(p);
                // vtable 前 3 槽为 IUnknown；GetBuffer 是接口第 0 个方法 = 槽 3
                IntPtr fn = Marshal.ReadIntPtr(vt, 3 * IntPtr.Size);
                var buf = Marshal.GetDelegateForFunctionPointer<BufferSlotDelegate>(fn);
                int hr = buf(p, out IntPtr data, out _);
                if (hr != 0) throw new COMException($"IMemoryBufferByteAccess hr=0x{hr:X8}", hr);
                return data;
            }
            finally { Marshal.Release(p); }
        }
        finally { Marshal.Release(punk); }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int BufferSlotDelegate(IntPtr self, out IntPtr value, out uint capacity);

    /// <summary>HMONITOR → GraphicsCaptureItem。</summary>
    public static GraphicsCaptureItem CreateItemForMonitor(IntPtr hMonitor)
    {
        Guid iid = IidGraphicsCaptureItemInterop;
        RoGetActivationFactory("Windows.Graphics.Capture.GraphicsCaptureItem", ref iid, out IntPtr factoryPtr);
        try
        {
            var f = (IGraphicsCaptureItemInterop)Marshal.GetTypedObjectForIUnknown(factoryPtr, typeof(IGraphicsCaptureItemInterop));
            Guid iidItem = new Guid("00000000-0000-0000-C000-000000000046"); // IUnknown：FromAbi 内部自行 QI
            f.CreateForMonitor(hMonitor, ref iidItem, out IntPtr itemPtr);
            return GraphicsCaptureItem.FromAbi(itemPtr);
        }
        finally { Marshal.Release(factoryPtr); }
    }
}
