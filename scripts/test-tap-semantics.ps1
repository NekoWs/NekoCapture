# 手势点按/按住语义端到端：统一测试手势集（Ctrl+Shift+G 轻点→选区+截图 / 按住→录制→编辑器）。
# 轻点(短按)→以「选区」开头的序列整体交给控制器→弹选区窗；按住(长按)→record.start→record.stop→edit.lastvideo→编辑器 frames>1。
# 断言走 nc-trace「chain picker open」与「open editor frames=N」，结束一律还原用户设置。
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot
. (Join-Path $PSScriptRoot "lib-hotkey.ps1")
$exe = Join-Path $root "src\NekoCapture\bin\x64\Debug\net8.0-windows10.0.19041.0\NekoCapture.exe"
$shot = Join-Path $root "tools\Shot\bin\x64\Debug\net8.0-windows\Shot.exe"
$trace = Join-Path $root "src\nc-trace.log"
$err = (Get-FcLogPath 'error.log')
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
    Get-Process NekoCapture -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 0.6
    Start-FcTestApp $exe | Out-Null; Start-Sleep 2
}

# 钉成统一测试手势集（返回用户原设置原文，末尾还原）
$bak = Set-FcTestGestureConfig
# 手势时长断言使用固定帧率的 GDI，帧数不依赖 WGC 的画面变化通知。
$fixed = Get-FcSettings
$fixed | Add-Member -Force UseWgc $false
$fixed | Add-Member -Force Fps 30
$fixed | Add-Member -Force FollowMonitorRefresh $false
# 手势时序在固定小区域采样，帧数不受桌面分辨率与整屏读回吞吐支配。
Add-Type -AssemblyName System.Windows.Forms
$timingMonitor = [System.Windows.Forms.Screen]::PrimaryScreen
$fixed | Add-Member -Force RegionMonitorId $timingMonitor.DeviceName
$fixed | Add-Member -Force RegionX ($timingMonitor.Bounds.X + 16)
$fixed | Add-Member -Force RegionY ($timingMonitor.Bounds.Y + 16)
$fixed | Add-Member -Force RegionW ([Math]::Min(320, $timingMonitor.Bounds.Width - 32))
$fixed | Add-Member -Force RegionH ([Math]::Min(240, $timingMonitor.Bounds.Height - 32))
$fixed | ConvertTo-Json -Depth 32 | Set-Content (Get-FcSettingsPath) -Encoding UTF8
$capHk = 'Ctrl+Shift+G'
"capture_key=$capHk (test gesture set)"
$scopedPattern = "loop start region=$($fixed.RegionX),$($fixed.RegionY) $($fixed.RegionW)x$($fixed.RegionH) interval="
$scopedBefore = Count-Trace $scopedPattern

try {
    Restart-App

    # 场景1：轻点 80ms（< 250ms 阈值）→ 选区+截图 → 选区窗
    $pk0 = Count-Trace "chain picker open"
    Invoke-FcSynthetic $exe $capHk 80; Start-Sleep 1.8
    $pk1 = Count-Trace "chain picker open"
    $pkWin = & $shot rect "NekoCapture Region" 2>$null
    "tap80: picker_trace_delta=$($pk1 - $pk0) win=[$pkWin]"
    & $shot keyw "NekoCapture Region" 27 2>$null | Out-Null; Start-Sleep 0.6
    $tapOk = ($pk1 - $pk0) -ge 1

    # 场景2：按住 600ms（> 250ms 阈值）→ 起录→停止→编辑器，frames>1
    # 场景间重启隔离——keyw(PostMessage) 不触发 WPF Esc，上一场景的编辑器/选区窗关不掉，
    # 会让下一场景 edit.lastvideo 复用旧窗、不再产生「open editor frames=」trace（环境抖动，非产品缺陷）。
    Restart-App
    $n2 = Count-Trace "open editor frames="
    Invoke-FcSynthetic $exe $capHk 600; Start-Sleep 2.2
    $f2 = Last-Frames $n2
    "hold600: frames=$f2"
    Invoke-FcEditorKey $exe 27; Start-Sleep 0.6
    $holdOk = $f2 -gt 1

    # 场景3：按住 1300ms → 更长录制，frames 明显多于场景2（阈值语义随按住时长伸缩）
    Restart-App
    $n3 = Count-Trace "open editor frames="
    Invoke-FcSynthetic $exe $capHk 1300; Start-Sleep 2.4
    $f3 = Last-Frames $n3
    "hold1300: frames=$f3"
    Invoke-FcEditorKey $exe 27; Start-Sleep 0.6
    $longOk = ($f3 -gt 1) -and ($f3 -gt $f2)
}
finally {
    Get-Process NekoCapture -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-Item Env:\NC_TEST_NO_FG -EA SilentlyContinue
    if ($bak) { Set-Content (Get-FcSettingsPath) -Value $bak -Encoding UTF8 }
}

$e1 = if (Test-Path $err) { (Get-Item $err).Length } else { 0 }
"err_delta=$($e1 - $e0)"
$scopedRuns = (Count-Trace $scopedPattern) - $scopedBefore
"scoped_loops=$scopedRuns"
$ok = $tapOk -and $holdOk -and $longOk -and ($scopedRuns -ge 2) -and (($e1 - $e0) -eq 0)
if ($ok) { "TAP-SEMANTICS PASS"; exit 0 } else { "TAP-SEMANTICS FAIL"; exit 1 }
