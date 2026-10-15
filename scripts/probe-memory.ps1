# 内存取证：空闲 → 选区 → 取消 → 录制 → 编辑器 → 关闭，逐点取工作集与私有字节。
# 只读进程指标，不改产品行为；结束还原用户设置。
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot
. (Join-Path $PSScriptRoot "lib-hotkey.ps1")
$exe = Join-Path $root "src\FrameCapture\bin\x64\Debug\net8.0-windows10.0.19041.0\FrameCapture.exe"
$shot = Join-Path $root "tools\Shot\bin\x64\Debug\net8.0-windows\Shot.exe"

function Snap([string]$tag) {
    $p = Get-Process FrameCapture -ErrorAction SilentlyContinue
    if (-not $p) { "{0,-20} not running" -f $tag; return }
    $p.Refresh()
    "{0,-20} WS={1,7:N1} MB  Private={2,7:N1} MB  handles={3,5}  threads={4}" -f $tag,
        ($p.WorkingSet64 / 1MB), ($p.PrivateMemorySize64 / 1MB), $p.HandleCount, $p.Threads.Count
}

$bak = Set-FcTestGestureConfig
try {
    Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 0.8
    Start-Process $exe; Start-Sleep 6
    Snap "1-idle"

    & $shot hk 71 80 | Out-Null; Start-Sleep 1.8      # Ctrl+Shift+G 轻点 → 选区窗
    Snap "2-picker-open"
    & $shot commit "FrameCapture Region" 27 | Out-Null; Start-Sleep 3
    Snap "3-after-cancel"

    & $shot hk 71 1300 | Out-Null; Start-Sleep 2.2    # 按住 → 起录 → 停 → 编辑器
    Snap "4-editor-open"
    & $shot commit "FrameCapture Editor" 27 | Out-Null; Start-Sleep 4
    Snap "5-after-editor"
    & $shot hk 71 1300 | Out-Null; Start-Sleep 2.2
    Snap "6-second-editor"
    & $shot commit "FrameCapture Editor" 27 | Out-Null; Start-Sleep 4
    Snap "7-idle-again"
}
finally {
    Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force
    if ($bak) { Set-Content (Join-Path $env:APPDATA "FrameCapture\settings.json") -Value $bak -Encoding UTF8 }
}
