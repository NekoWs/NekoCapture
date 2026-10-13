using Microsoft.Win32;

namespace FrameCapture.Services;

/// <summary>开机自启：HKCU\...\Run 注册表项。</summary>
public static class StartupService
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "FrameCapture";

    /// <summary>
    /// 写 / 删 HKCU 的 <c>Software\Microsoft\Windows\CurrentVersion\Run</c> 自启值。
    /// 整段吞异常：注册表被策略禁用或不可写时，后果只是「自启没生效」，
    /// 不能让设置页或退出流程因此失败。
    /// </summary>
    /// <param name="enable">
    /// true = 写入当前进程 exe 路径（加引号，路径含空格时不会被 Run 项拆成参数）；
    /// 取不到 <c>Environment.ProcessPath</c> 时不写任何值。
    /// false = 删除该值，值本就不存在视为成功。
    /// </param>
    public static void Apply(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (enable)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe)) key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch { }
    }
}
