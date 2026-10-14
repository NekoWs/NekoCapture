using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using FrameCapture.Core;

namespace FrameCapture.Setup;

/// <summary>
/// 引导器入口。命令行形态：
/// 无参数 = 界面；<c>--install</c> = 提权后执行安装；
/// <c>--uninstall</c> = 提权后卸载（内部再派生 <c>--from-temp</c> 以删掉自己那份）。
/// </summary>
internal static class App
{
    /// <summary>进程入口。</summary>
    /// <param name="args">命令行参数。</param>
    [STAThread]
    private static void Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        Fluent.ApplyGlobalStyles();
        Theme.Apply(ThemeModeFromRegistry(), "#4CC2FF");

        if (args.Length >= 2 && args[0] == "--preview")
        {
            // 出图核对界面：建窗但不显示，取内容离屏渲染
            Preview.Render(args[1], SetupWindow.Mode.Idle);
            return;
        }
        if (Array.IndexOf(args, "--install") >= 0) { app.Run(new SetupWindow(SetupWindow.Mode.Install)); return; }
        if (Array.IndexOf(args, "--uninstall") >= 0) { app.Run(new SetupWindow(SetupWindow.Mode.Uninstall)); return; }
        app.Run(new SetupWindow(SetupWindow.Mode.Idle));
    }

    /// <summary>跟随系统深浅偏好，与主程序读同一个注册表值。</summary>
    /// <returns>"dark" 或 "light"。</returns>
    private static string ThemeModeFromRegistry()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int i && i == 0 ? "dark" : "light";
        }
        catch { return "dark"; }
    }

    /// <summary>当前是否已提权。</summary>
    internal static bool Elevated
    {
        get
        {
            try
            {
                using var id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }

    /// <summary>用 runas 动词重新拉起自己，触发一次 UAC。</summary>
    /// <param name="arguments">传给新实例的参数。</param>
    /// <returns>true = 新实例已启动，当前实例应退出。</returns>
    internal static bool RelaunchElevated(string arguments)
    {
        var exe = Process.GetCurrentProcess().MainModule?.FileName
                  ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
        try
        {
            var psi = new ProcessStartInfo(exe, arguments) { UseShellExecute = true, Verb = "runas" };
            using var p = Process.Start(psi);
            return p != null;
        }
        catch
        {
            return false; // 用户在 UAC 里点了「否」
        }
    }

    /// <summary>把自身复制到临时目录，用于卸载时删掉安装目录里那份自己。</summary>
    /// <returns>临时副本路径；复制失败时 null。</returns>
    internal static string? CopySelfToTemp()
    {
        try
        {
            var exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return null;
            var dest = Path.Combine(Path.GetTempPath(), "FrameCapture-uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(exe, dest, true);
            return dest;
        }
        catch { return null; }
    }
}
