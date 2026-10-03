# 乱按键盘 fuzz：向编辑器窗口 PostMessage 随机按键，验证状态机不崩不卡死
# 用法: pwsh -NoProfile -File scripts/fuzz-keys.ps1 [-Keys 300]
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "lib-hotkey.ps1")
$exe = Join-Path $root "src\FrameCapture\bin\x64\Debug\net8.0-windows\FrameCapture.exe"
$shot = Join-Path $root "tools/Shot/bin/x64/Debug/net8.0-windows/Shot.exe"
$errlog = "$env:APPDATA\FrameCapture\error.log"
$N = 300

$errSize0 = if (Test-Path $errlog) { (Get-Item $errlog).Length } else { 0 }
Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 0.6
Start-Process $exe
Start-Sleep 2.0
Invoke-FcHotkey $shot (Get-FcCaptureHotkey) 900 | Out-Null   # 按当前设置的捕获键触发（~1s 后自动结束）
Start-Sleep 2.5

# 编辑器常用键（VK）：Space X D L O A P C Del ←↑→↓ Home End Tab，刻意排除 Enter/Esc
$vks = @(32,88,68,76,79,65,80,67,46,37,38,39,40,36,35,9)
$rnd = New-Object System.Random(12345)
$alive = $true
for ($i = 0; $i -lt $N; $i++) {
    $vk = $vks[$rnd.Next($vks.Length)]
    & $shot keyw "FrameCapture Editor" $vk 2>$null | Out-Null
    if (-not (Get-Process FrameCapture -ErrorAction SilentlyContinue)) { $alive = $false; break }
    if (($i % 40) -eq 39) { Start-Sleep -Milliseconds 60 }
}
Start-Sleep 0.5
$rect = & $shot rect "FrameCapture Editor"
$proc = Get-Process FrameCapture -ErrorAction SilentlyContinue
$errSize1 = if (Test-Path $errlog) { (Get-Item $errlog).Length } else { 0 }
# 乱按后再走一遍正常流程：下一帧→保存帧按钮不应异常（用 Esc 收尾）
& $shot keyw "FrameCapture Editor" 39 2>$null | Out-Null
& $shot keyw "FrameCapture Editor" 27 2>$null | Out-Null
Start-Sleep 0.4
Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force

$ok = $alive -and ($rect -match "rect") -and ($errSize1 -eq $errSize0)
Write-Host "alive=$alive rect=$rect errlog_delta=$($errSize1 - $errSize0)"
if ($ok) { "FUZZ PASS ($N keys)" } else { "FUZZ FAIL"; exit 1 }
