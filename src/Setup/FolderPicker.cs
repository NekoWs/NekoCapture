using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FrameCapture.Setup;

/// <summary>
/// 文件夹选择框。走 shell 的现代选择对话框（<c>IFileOpenDialog</c> + <c>FOS_PICKFOLDERS</c>），
/// 与资源管理器里的「选择文件夹」是同一个控件。WinForms 的 FolderBrowserDialog 在 .NET Framework
/// 上仍是老式树形视图，且会把 System.Windows.Forms 整个拖进引导器。
/// </summary>
internal static class FolderPicker
{
    /// <summary>FileOpenDialog 的 COM 类 id。</summary>
    private static readonly Guid ClsidFileOpenDialog = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");

    /// <summary>IShellItem 的接口 id，也是 SHCreateItemFromParsingName 要索取的接口。</summary>
    private static readonly Guid IidShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    private const uint FosPickFolders = 0x00000020;
    private const uint FosForceFileSystem = 0x00000040;
    private const uint FosPathMustExist = 0x00000800;
    private const uint FosNoTestFileCreate = 0x00010000;
    /// <summary>SIGDN_FILESYSPATH。这个枚举不是从 0x8005 顺排的小数：写成 0x8005 会被 shell
    /// 认成无效值并退回显示名，选中的目录就只剩最后一段文件夹名。</summary>
    private const uint SigdnFilesyspath = 0x80058000;

    /// <summary>用户在对话框里点了取消。</summary>
    private const int ErrorCancelled = unchecked((int)0x800704C7);

    /// <summary>弹出选择框。调用线程必须是 STA（WPF 界面线程天然是）。</summary>
    /// <param name="owner">宿主窗口，决定模态归属与弹出位置。</param>
    /// <param name="initial">初始定位的目录；为空或 shell 认不出时由它自己的默认位置接手。</param>
    /// <param name="title">标题栏文案。</param>
    /// <param name="okLabel">确认按钮文案。</param>
    /// <returns>选中的目录；取消或 shell 报错时为 null。</returns>
    internal static string? Pick(Window owner, string? initial, string title, string okLabel)
    {
        object? raw = null;
        try
        {
            var t = Type.GetTypeFromCLSID(ClsidFileOpenDialog);
            if (t == null) return null;
            raw = Activator.CreateInstance(t);
            if (raw is not IFileOpenDialog dlg) return null;

            dlg.SetOptions(FosPickFolders | FosForceFileSystem | FosPathMustExist | FosNoTestFileCreate);
            dlg.SetTitle(title);
            dlg.SetOkButtonLabel(okLabel);

            // .NET Framework 的参考程序集没给 IsNullOrWhiteSpace 标空值语义，用它判会留下告警；
            // 这两个判断本身等价，写法上让编译器也能跟上。
            if (initial != null && initial.Length > 0 && Directory.Exists(initial))
            {
                // 定位失败不该拦人：shell 认不出这个路径就让它开在默认位置
                var iid = IidShellItem;   // ref 参数要可变局部量，静态只读字段递不进去
                if (SHCreateItemFromParsingName(initial, IntPtr.Zero, ref iid, out var start) == 0 && start != null)
                {
                    dlg.SetFolder(start);
                    Marshal.ReleaseComObject(start);
                }
            }

            var hr = dlg.Show(new WindowInteropHelper(owner).Handle);
            if (hr != 0) return null;   // 取消与一切失败同样处理：什么都不改

            if (dlg.GetResult(out IShellItem item) != 0 || item == null) return null;
            try
            {
                if (item.GetDisplayName(SigdnFilesyspath, out var buffer) != 0 || buffer == IntPtr.Zero) return null;
                try { return Marshal.PtrToStringUni(buffer); }
                finally { Marshal.FreeCoTaskMem(buffer); }
            }
            finally { Marshal.ReleaseComObject(item); }
        }
        catch (COMException ex) when (ex.HResult == ErrorCancelled) { return null; }
        catch { return null; }   // shell 不可用（精简系统、组策略禁用）时，安装按默认位置继续
        finally { if (raw != null) Marshal.ReleaseComObject(raw); }
    }

    /// <summary>把一个磁盘路径包成 IShellItem，供对话框定位初始目录。</summary>
    /// <param name="path">目录路径。</param>
    /// <param name="bindCtx">绑定上下文，传 null 指针即可。</param>
    /// <param name="riid">要索取的接口 id。</param>
    /// <param name="item">取到的对象；失败时为 null。</param>
    /// <returns>HRESULT。</returns>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        string path, IntPtr bindCtx, ref Guid riid, out IShellItem item);

    /// <summary>shell 对象。只声明要用的前四个槽位——vtable 按顺序取，多写少写都会错位。</summary>
    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr bindCtx, ref Guid handlerId, ref Guid riid, out IntPtr result);
        [PreserveSig] int GetParent(out IShellItem parent);
        [PreserveSig] int GetDisplayName(uint displayNameKind, out IntPtr name);
        [PreserveSig] int GetAttributes(uint mask, out uint attributes);
    }

    /// <summary>
    /// 文件/文件夹选择对话框。前 23 个槽位属于 IModalWindow 与 IFileDialog，
    /// 顺序就是 vtable 顺序，挪动或漏写一个都会让后面的调用打到别的方法上。
    /// 全部标 PreserveSig：HRESULT 自己判，取消不当异常抛。
    /// </summary>
    [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig] int Show(IntPtr owner);
        [PreserveSig] int SetFileTypes(uint count, IntPtr filters);
        [PreserveSig] int SetFileTypeIndex(uint index);
        [PreserveSig] int GetFileTypeIndex(out uint index);
        [PreserveSig] int Advise(IntPtr sink, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int SetOptions(uint options);
        [PreserveSig] int GetOptions(out uint options);
        [PreserveSig] int SetDefaultFolder(IShellItem item);
        [PreserveSig] int SetFolder(IShellItem item);
        [PreserveSig] int GetFolder(out IShellItem item);
        [PreserveSig] int GetCurrentSelection(out IShellItem item);
        [PreserveSig] int SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        [PreserveSig] int SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        [PreserveSig] int SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        [PreserveSig] int GetResult(out IShellItem item);
        [PreserveSig] int SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
        [PreserveSig] int Close(int hr);
        [PreserveSig] int SetClientGuid(ref Guid guid);
        [PreserveSig] int ClearClientData();
        [PreserveSig] int SetFilter(IntPtr filter);
        [PreserveSig] int GetResults(out IntPtr items);
        [PreserveSig] int GetSelectedItems(out IntPtr items);
    }
}
