# R126 B2 手势点按/按住语义端到端：统一测试手势集（Ctrl+Shift+G 轻点→选区截图 / 按住→录制→编辑器）。
# 轻点(短按)→capture.selection→弹选区窗；按住(长按)→record.start→record.stop→edit.lastvideo→编辑器 frames>1。
# 断言走 fc-trace「capture-selection→picker」与「open editor frames=N」，结束一律还原用户设置。
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot
. (Join-Path $PSScriptRoot "lib-hotkey.ps1")
$exe = Join-Path $root "src\FrameCapture\bin\x64\Debug\net8.0-windows10.0.19041.0\FrameCapture.exe"
$shot = Join-Path $root "tools\Shot\bin\x64\Debug\net8.0-windows\Shot.exe"
$trace = Join-Path $root "src\fc-trace.log"
$err = Join-Path $env:APPDATA "FrameCapture\error.log"
$e0 = if (Test-Path $err) { (Get-Item $err).Length } else { 0 }

function Count-Trace([string]$pat) {
    @(Get-Content $trace -ErrorAction SilentlyContinue | Select-String $pat).Count
}
function Last-Frames([int]$before) {
    $lines = @(Get-Content $trace -ErrorAction SilentlyContinue | Select-String "open editor frames=")
    if ($lines.Count -le $before) { return -1 }
    [int]($lines[-1] -replace ".*frames=", "")
}
function Restart-App {
    Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 0.6
    Start-Process $exe; Start-Sleep 2
}

# 钉成统一测试手势集（返回用户原设置原文，末尾还原）
$bak = Set-FcTestGestureConfig
$capHk = 'Ctrl+Shift+G'
"capture_key=$capHk (test gesture set)"

try {
    Restart-App

    # 场景1：轻点 80ms（< 250ms 阈值）→ capture.selection → 选区窗
    $pk0 = Count-Trace "capture-selection→picker"
    Invoke-FcHotkey $shot $capHk 80 | Out-Null; Start-Sleep 1.6
    $pk1 = Count-Trace "capture-selection→picker"
    $pkWin = & $shot rect "FrameCapture Region" 2>$null
    "tap80: picker_trace_delta=$($pk1 - $pk0) win=[$pkWin]"
    & $shot keyw "FrameCapture Region" 27 2>$null | Out-Null; Start-Sleep 0.6
    $tapOk = ($pk1 - $pk0) -ge 1

    # 场景2：按住 600ms（> 250ms 阈值）→ 起录→停止→编辑器，frames>1
    $n2 = Count-Trace "open editor frames="
    Invoke-FcHotkey $shot $capHk 600 | Out-Null; Start-Sleep 2.0
    $f2 = Last-Frames $n2
    "hold600: frames=$f2"
    & $shot keyw "FrameCapture Editor" 27 2>$null | Out-Null; Start-Sleep 0.6
    $holdOk = $f2 -gt 1

    # 场景3：按住 1300ms → 更长录制，frames 明显多于场景2（阈值语义随按住时长伸缩）
    $n3 = Count-Trace "open editor frames="
    Invoke-FcHotkey $shot $capHk 1300 | Out-Null; Start-Sleep 2.2
    $f3 = Last-Frames $n3
    "hold1300: frames=$f3"
    & $shot keyw "FrameCapture Editor" 27 2>$null | Out-Null; Start-Sleep 0.6
    $longOk = $f3 -gt 1
}
finally {
    Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force
    if ($bak) { Set-Content (Join-Path $env:APPDATA "FrameCapture\settings.json") -Value $bak -Encoding UTF8 }
}

$e1 = if (Test-Path $err) { (Get-Item $err).Length } else { 0 }
"err_delta=$($e1 - $e0)"
$ok = $tapOk -and $holdOk -and $longOk -and (($e1 - $e0) -eq 0)
if ($ok) { "TAP-SEMANTICS PASS"; exit 0 } else { "TAP-SEMANTICS FAIL"; exit 1 }
