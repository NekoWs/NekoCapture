# 点按/长按语义端到端：TapThresholdMs 默认/0/大阈值 三场景，用 fc-trace「open editor frames=N」断言
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot
$exe = Join-Path $root "src\FrameCapture\bin\x64\Debug\net8.0-windows\FrameCapture.exe"
$shot = Join-Path $root "tools\Shot\bin\x64\Debug\net8.0-windows\Shot.exe"
$cfg = Join-Path $env:APPDATA "FrameCapture\settings.json"
$trace = Join-Path $root "src\fc-trace.log"
$err = Join-Path $env:APPDATA "FrameCapture\error.log"
$e0 = if (Test-Path $err) { (Get-Item $err).Length } else { 0 }
$bak = if (Test-Path $cfg) { Get-Content $cfg -Raw } else { $null }

function Set-Th([int]$tap) {
    $s = if ($bak) { $bak | ConvertFrom-Json } else { [pscustomobject]@{} }
    $s | Add-Member -Force TapThresholdMs $tap
    $s | ConvertTo-Json | Set-Content $cfg -Encoding UTF8
}
function Restart-App {
    Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 0.6
    Start-Process $exe; Start-Sleep 2
}
function Last-Frames([int]$before) {
    $lines = @(Get-Content $trace -ErrorAction SilentlyContinue | Select-String "open editor frames=")
    if ($lines.Count -le $before) { return -1 }
    [int]($lines[-1] -replace ".*frames=", "")
}

New-Item -ItemType Directory -Force -Path (Split-Path $cfg) | Out-Null

# 场景1：阈值 400ms（注入链路有 ~150ms 抖动，避开 250 边界），点按 100ms → 单帧
Set-Th 400; Restart-App
$n0 = @(Get-Content $trace -ErrorAction SilentlyContinue | Select-String "open editor frames=").Count
& $shot hk 71 100 | Out-Null; Start-Sleep 1.8
$f1 = Last-Frames $n0
"tap400 hold100: frames=$f1"
& $shot keyw "FrameCapture Editor" 27 2>$null | Out-Null; Start-Sleep 0.5


Set-Th 0; Restart-App
$n1 = @(Get-Content $trace -ErrorAction SilentlyContinue | Select-String "open editor frames=").Count
# 400ms：若是点按语义会截成 1 帧；长按语义应保留多帧（fps≈20）
& $shot hk 71 400 | Out-Null; Start-Sleep 1.8
$f2 = Last-Frames $n1
"tap0 hold400: frames=$f2"
& $shot keyw "FrameCapture Editor" 27 2>$null | Out-Null; Start-Sleep 0.5

# 场景3：阈值 1000ms，长按 500ms → 仍按点按处理成单帧（延迟可配置语义）
Set-Th 1000; Restart-App
$n2 = @(Get-Content $trace -ErrorAction SilentlyContinue | Select-String "open editor frames=").Count
& $shot hk 71 500 | Out-Null; Start-Sleep 1.8
$f3 = Last-Frames $n2
"tap1000 hold500: frames=$f3"
& $shot keyw "FrameCapture Editor" 27 2>$null | Out-Null; Start-Sleep 0.5

# 场景4：阈值 1000ms，长按 1300ms → 多帧（超过阈值恢复正常长按）
$n3 = @(Get-Content $trace -ErrorAction SilentlyContinue | Select-String "open editor frames=").Count
& $shot hk 71 1300 | Out-Null; Start-Sleep 2
$f4 = Last-Frames $n3
"tap1000 hold1300: frames=$f4"
& $shot keyw "FrameCapture Editor" 27 2>$null | Out-Null

Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force
if ($bak) { Set-Content $cfg -Value $bak -Encoding UTF8 } else { Remove-Item $cfg -ErrorAction SilentlyContinue }
$e1 = if (Test-Path $err) { (Get-Item $err).Length } else { 0 }
"err_delta=$($e1 - $e0)"
$ok = ($f1 -eq 1) -and ($f2 -gt 1) -and ($f3 -eq 1) -and ($f4 -gt 1) -and (($e1 - $e0) -eq 0)
if ($ok) { "TAP-SEMANTICS PASS" } else { "TAP-SEMANTICS FAIL"; exit 1 }
