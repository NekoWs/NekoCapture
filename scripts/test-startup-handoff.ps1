# 仅在仓库内启动两个隔离实例，验证提权交接的锁时序；不登记系统入口、不请求 UAC。
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$root = Join-Path $repo 'test-out/startup-handoff'
$runtime = Join-Path $root 'runtime'
$exe = Join-Path $repo 'src/FrameCapture/bin/x64/Debug/net8.0-windows10.0.19041.0/FrameCapture.exe'
if (@(Get-Process FrameCapture -ErrorAction SilentlyContinue).Count) { throw 'A user instance is running; test refused' }
$saved = @{}
foreach ($name in @('FC_SETTINGS_DIR','FC_NO_ELEVATE','FC_TEST_NO_FG','TEMP','TMP')) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name)
}
$first = $null; $second = $null
try {
    New-Item -ItemType Directory -Path $runtime -Force | Out-Null
    @{ StartupToast=$false; FirstRunHintShown=$true; RunAtStartup=$true; RunAsAdmin=$true; Gestures=@{Enabled=$false} } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $root 'settings.json') -Encoding utf8
    $env:FC_SETTINGS_DIR=$root; $env:FC_NO_ELEVATE='1'; $env:FC_TEST_NO_FG='1'; $env:TEMP=$runtime; $env:TMP=$runtime
    if (-not ('StartupWindowProbe' -as [type])) {
        Add-Type @'
using System;
using System.Runtime.InteropServices;
/// <summary>读取本程序消息窗口的所属进程。</summary>
public static class StartupWindowProbe {
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr p,IntPtr a,string c,string n);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint p);
 /// <summary>查询当前主实例。</summary>
 /// <returns>主实例进程 ID；没有实例时为零。</returns>
 public static uint Owner(){uint p=0;var h=FindWindowEx(new IntPtr(-3),IntPtr.Zero,null,"FrameCapture.MessageSink");if(h!=IntPtr.Zero)GetWindowThreadProcessId(h,out p);return p;}
}
'@
    }
    $first=Start-Process -FilePath $exe -ArgumentList '--startup' -WindowStyle Hidden -PassThru
    $limit=[Diagnostics.Stopwatch]::StartNew()
    while ([StartupWindowProbe]::Owner() -ne $first.Id -and $limit.ElapsedMilliseconds -lt 6000) { Start-Sleep -Milliseconds 50 }
    if ([StartupWindowProbe]::Owner() -ne $first.Id) { throw 'Initial instance did not become ready' }
    $ticks=$first.StartTime.ToUniversalTime().Ticks
    $second=Start-Process -FilePath $exe -ArgumentList @('--wait-parent',$first.Id,$ticks,'--startup') -WindowStyle Hidden -PassThru
    Start-Sleep -Milliseconds 500
    if ($second.HasExited -or [StartupWindowProbe]::Owner() -ne $first.Id) { throw 'Handoff child did not wait for the owner' }
    Stop-Process -Id $first.Id -Force
    $first.WaitForExit()
    $limit.Restart()
    while ([StartupWindowProbe]::Owner() -ne $second.Id -and $limit.ElapsedMilliseconds -lt 6000) { Start-Sleep -Milliseconds 50 }
    if ($second.HasExited -or [StartupWindowProbe]::Owner() -ne $second.Id) { throw 'Handoff child did not acquire ownership' }
    'STARTUP-HANDOFF PASS: child waits before mutex and becomes the sole primary'
} catch {
    Write-Output ('STARTUP-HANDOFF FAIL: ' + $_.Exception.Message)
    throw
} finally {
    foreach ($instance in @($first,$second)) {
        if ($null -ne $instance -and -not $instance.HasExited) { Stop-Process -Id $instance.Id -Force }
    }
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name,$saved[$name]) }
}
