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
    static readonly Guid IidGraphicsCaptureItemInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"); // um/Interop.h

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

    /// <summary>IUnknown:QueryInterface（vtable slot 0）。</summary>
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
    /// <returns>IDirect3DDevice 的 IUnknown 指针，引用计数已加，调用方负责 <c>Marshal.Release</c>。
    /// 硬件适配器建不出来时退 WARP；两者都失败抛 <see cref="COMException"/>，调用方据此回退 GDI。</returns>
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
                Core.Trace.Log($"D3D11CreateDevice dt={dt} hr=0x{hr:X8}");
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
    /// <param name="memoryBufferReference">WinRT IMemoryBufferReference 对象，投影或原生实现均可。</param>
    /// <returns>帧缓冲首字节指针：归 WGC 所有，只读、不得释放，对应的 <c>Close()</c> 一调用就失效。失败抛 <see cref="COMException"/>。</returns>
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
    /// <param name="hMonitor">当前已连接显示器的 HMONITOR 句柄。</param>
    /// <returns>整块显示器的捕获项，<c>Size</c> 为该屏物理像素；引用由调用方持有。任一步失败抛 <see cref="COMException"/>。</returns>
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

    /// <summary>HWND → GraphicsCaptureItem（按窗口捕获：含 DWM 边框/圆角，圆角处带 alpha，GPU 窗也干净）。</summary>
    /// <param name="hwnd">目标窗口句柄。窗口销毁后本捕获项失效，会话停止产出帧。</param>
    /// <returns>窗口本体的捕获项，<c>Size</c> 含 DWM 边框因此比客户区大；引用由调用方持有。</returns>
    public static GraphicsCaptureItem CreateItemForWindow(IntPtr hwnd)
    {
        Guid iid = IidGraphicsCaptureItemInterop;
        RoGetActivationFactory("Windows.Graphics.Capture.GraphicsCaptureItem", ref iid, out IntPtr factoryPtr);
        try
        {
            var f = (IGraphicsCaptureItemInterop)Marshal.GetTypedObjectForIUnknown(factoryPtr, typeof(IGraphicsCaptureItemInterop));
            Guid iidItem = new Guid("00000000-0000-0000-C000-000000000046"); // IUnknown：FromAbi 内部自行 QI
            f.CreateForWindow(hwnd, ref iidItem, out IntPtr itemPtr);
            return GraphicsCaptureItem.FromAbi(itemPtr);
        }
        finally { Marshal.Release(factoryPtr); }
    }

    #region 系统捕获提示边框（Windows 11）

    /// <summary>
    /// Windows.Graphics.Capture.IGraphicsCaptureSession3（Win11 新增）。
    ///
    /// GUID 与槽序取自本机 Windows SDK 10.0.26100.0：
    /// MIDL_INTERFACE("f2cdd966-22ae-5ea1-9596-3a289344c3be")
    /// → Include\10.0.26100.0\winrt\windows.graphics.capture.h
    /// 槽序（含 IInspectable 的 6 个）：
    /// 0-2 QueryInterface/AddRef/Release，3-5 GetIids/GetRuntimeClassName/GetTrustLevel，
    /// 6 get_IsBorderRequired，7 put_IsBorderRequired。
    ///
    /// 因此这里必须**完整声明**前面 6 个方法，才能让 put 落在槽 7；不能只声明本接口自己的两个。
    /// 钉住的 19041 投影程序集没有 IsBorderRequired 成员，所以只能手写 vtable。
    /// </summary>
    [ComImport, Guid("F2CDD966-22AE-5EA1-9596-3A289344C3BE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureSession3
    {
    #endregion

        #region IInspectable（槽 0-5，仅为对齐槽位，实际不调用）
        void GetIids(out uint iidCount, out IntPtr iids);
        void GetRuntimeClassName(out IntPtr className);
        void GetTrustLevel(out int trustLevel);
        #endregion

        #region IGraphicsCaptureSession3 自身（槽 6-7）
        void GetIsBorderRequired(out bool value);
        void PutIsBorderRequired(bool value);
    }

    /// <summary>
    /// 关闭 WGC 的系统提示边框，并**回读**确认真的生效。
    ///
    /// 回读是必须的：按官方文档，用户/系统未授予 Borderless 权限时，
    /// "把属性设为 false 会成功，但值被忽略，后续捕获仍显示边框" —— 也就是静默不生效，
    /// 不报错。所以只信 put 不抛异常会误报成功。
    ///
    /// 找不到该接口（Win10 等）抛 COMException，由调用方降级为"保持系统默认"。
    /// </summary>
    /// <param name="session">WinRT GraphicsCaptureSession 投影对象；只认它持有的原生 IUnknown。</param>
    /// <param name="required">false = 请求无边框。结论以返回值为准，不看这里。</param>
    /// <returns>回读到的 IsBorderRequired 实际值：false 才代表边框真的关掉了。</returns>
    public static bool SetBorderRequired(object session, bool required)
    {
        // 必须拿投影对象持有的**原生** IUnknown：Marshal.GetIUnknownForObject 返回的是托管 CCW，
        // 它只会应答投影程序集里存在的接口，QI IGraphicsCaptureSession3 必然 E_NOINTERFACE。
        IntPtr punk = IntPtr.Zero;
        if (session is WinRT.IWinRTObject wrt && wrt.NativeObject.ThisPtr != IntPtr.Zero)
        {
            punk = wrt.NativeObject.ThisPtr;
            Marshal.AddRef(punk);
        }
        if (punk == IntPtr.Zero) throw new InvalidOperationException("session 原生 IUnknown 不可用");
        try
        {
            Guid iid = typeof(IGraphicsCaptureSession3).GUID;
            int hr = Marshal.QueryInterface(punk, ref iid, out IntPtr p3);
            if (hr != 0) throw new COMException($"QI IGraphicsCaptureSession3 hr=0x{hr:X8}", hr);
            try
            {
                var s3 = (IGraphicsCaptureSession3)Marshal.GetTypedObjectForIUnknown(p3, typeof(IGraphicsCaptureSession3));
                s3.PutIsBorderRequired(required);
                s3.GetIsBorderRequired(out bool actual); // 静默忽略时这里仍是 true
                return actual;
            }
            finally { Marshal.Release(p3); }
        }
        finally { Marshal.Release(punk); }
    }
        #endregion

}
