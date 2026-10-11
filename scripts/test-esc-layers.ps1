# R10 Esc 分层回归：捕获→编辑器→L(进绘制态)→Esc(应只退工具，编辑器仍在 + trace esc→browse)→Esc(关闭)。
# R12 起 D=点工具已删除，改用 L=直线（语义不变：任意绘制工具进入绘制态）。
# 语义与其余 e2e 一致：注入被拦截 → SKIP ≠ FAIL。
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "lib-hotkey.ps1")
$exe   = Join-Path $root "src/FrameCapture/bin/x64/Debug/net8.0-windows10.0.19041.0/FrameCapture.exe"
$shot  = Join-Path $root "tools/Shot/bin/x64/Debug/net8.0-windows/Shot.exe"
$trace = Join-Path $root "src/fc-trace.log"
$mark = (Get-Item $trace -EA SilentlyContinue).Length
$capHk = Get-FcCaptureHotkey
Get-Process FrameCapture -EA SilentlyContinue | Stop-Process -Force
Start-Sleep 0.5
$env:FC_TEST_NO_FG = "1"
$p = Start-Process $exe -PassThru
Start-Sleep 1.5
Invoke-FcHotkey $shot $capHk 700 | Out-Null
Start-Sleep 2.0
$ed = & $shot rect "FrameCapture Editor" 2>$null
if ($ed -match "not-found") {
    Write-Host "ESC-LAYERS SKIP: 编辑器未出现（注入被拦截）"
    if (!$p.HasExited) { $p.Kill() }
    Remove-Item Env:\FC_TEST_NO_FG -EA SilentlyContinue
    exit 0
}
& $shot keyw "FrameCapture Editor" 76 2>$null | Out-Null; Start-Sleep 0.4   # L → 直线工具（绘制态）
& $shot keyw "FrameCapture Editor" 27 2>$null | Out-Null; Start-Sleep 0.6   # Esc → 应只退工具
$ed2 = & $shot rect "FrameCapture Editor" 2>$null
$stillOpen = "$ed2" -notmatch "not-found"
& $shot keyw "FrameCapture Editor" 27 2>$null | Out-Null; Start-Sleep 1.2   # Esc → 应关闭
$ed3 = & $shot rect "FrameCapture Editor" 2>$null
$closed = "$ed3" -match "not-found"
if (!$p.HasExited) { $p.Kill() }
Remove-Item Env:\FC_TEST_NO_FG -EA SilentlyContinue
$log = ""
if ((Get-Item $trace).Length -gt $mark) {
    $fs = [System.IO.File]::Open($trace, 'Open', 'Read', 'ReadWrite')
    $fs.Seek($mark, 'Begin') | Out-Null
    $sr = New-Object System.IO.StreamReader($fs)
    $log = $sr.ReadToEnd(); $sr.Close()
}
$hasBrowse = $log -match "editor esc→browse"
Write-Host "stillOpen=$stillOpen browseTrace=$hasBrowse closed=$closed"
if ($stillOpen -and $hasBrowse -and $closed) { Write-Host "ESC-LAYERS PASS"; exit 0 }
Write-Host "ESC-LAYERS FAIL"
($log -split "`n" | Select-String "editor key|esc") | Select-Object -Last 12
exit 1
