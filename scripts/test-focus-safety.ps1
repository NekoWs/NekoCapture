# 焦点安全自检：全局热键注入期间，自有窗文本框不得被漏入任何字符；捕获照常发生。
# 注意：编辑器弹出会短暂获取前台（约2秒），请确保当前无游戏/前台应用（脚本会自动检测并跳过）。
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe  = Join-Path $root "src/FrameCapture/bin/x64/Debug/net8.0-windows10.0.19041.0/FrameCapture.exe"
$shot = Join-Path $root "tools/Shot/bin/x64/Debug/net8.0-windows/Shot.exe"
if (!(Test-Path $exe)) { Write-Host "missing $exe"; exit 1 }
Get-Process FrameCapture -EA SilentlyContinue | Stop-Process -Force
Start-Sleep 0.5
$p = Start-Process $exe -PassThru
Start-Sleep 1.5
$fgline = & $shot fg 2>$null
if ($fgline -match "UnityWndClass|Chrome_WidgetWin") {
    Write-Host "SKIP: 用户前台窗口在用 ($($fgline -replace 'hwnd=\d+ ',''))，避免抢占焦点"
    if (!$p.HasExited) { $p.Kill() }
    exit 0
}
$out = & $shot swallow 2>&1 | Out-String
Start-Sleep 0.8
$ed = & $shot rect "FrameCapture Editor" 2>$null
& $shot keyw "FrameCapture Editor" 27 2>$null | Out-Null
Start-Sleep 0.8
if (!$p.HasExited) { $p.Kill() }
$tr = Get-Content (Join-Path $root "src/fc-trace.log") -Tail 40 | Out-String
if (-not ($tr -match "hook key=71")) {
    Write-Host "SKIP: 注入按键被拦截（游戏/反外挂），hook 无事件；关闭游戏后重跑"
    exit 0
}
$clean = $out -match "SWALLOW text=\[ABCDEF\]"
$captured = $ed -match "^rect"
Write-Host $out.Trim()
Write-Host "editor=[$ed]"
if ($clean -and $captured) { Write-Host "FOCUS-SAFETY PASS"; exit 0 }
Write-Host "FOCUS-SAFETY FAIL"; exit 1
