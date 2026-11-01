# 编辑器乱按 fuzz：向编辑器窗口 PostMessage 300 个随机常用键，验证状态机不崩、不卡死、不写崩溃日志。
# 进编辑器走合成键：--synthetic 经消息专用窗把键喂进引擎 KeySink，全程零 OS 级注入——
# 用户正用电脑时也能跑，也没有「全局热键被别的软件抢了」这类外因可怪。
# 用法: pwsh -NoProfile -File scripts/fuzz-keys.ps1 [-Keys 300]
param([int]$Keys = 300)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "lib-hotkey.ps1")
$exe  = Join-Path $root "src/NekoCapture/bin/x64/Debug/net8.0-windows10.0.19041.0/NekoCapture.exe"
$shot = Join-Path $root "tools/Shot/bin/x64/Debug/net8.0-windows/Shot.exe"
$errlog = Get-FcLogPath 'error.log'
$capHk = 'Ctrl+Shift+G'

$errSize0 = if (Test-Path $errlog) { (Get-Item $errlog).Length } else { 0 }
Get-Process NekoCapture -EA SilentlyContinue | Stop-Process -Force
Start-Sleep 0.6
$bak = Set-FcTestGestureConfig   # 先杀后写，避免运行实例回写覆盖
try {
    $env:NC_TEST_NO_FG = "1"
    Start-Process $exe | Out-Null   # 子进程继承 NC_TEST_NO_FG 与 NC_SETTINGS_DIR
    Start-Sleep 1.8
    Invoke-FcSynthetic $exe $capHk 700
    Start-Sleep 2.2
    $ed0 = & $shot rect "NekoCapture Editor" 2>$null
    if ($ed0 -match "not-found") {
        Write-Host "FAIL: 编辑器未出现——合成键未经消息通道抵达引擎（主实例需带 NC_TEST_NO_FG=1 启动）"
        exit 1
    }
    # 编辑器常用键（VK）：Space X D L O A P C Del ←↑→↓ Home End Tab，刻意排除 Enter/Esc
    $vks = @(32,88,68,76,79,65,80,67,46,37,38,39,40,36,35,9)
    $rnd = New-Object System.Random(12345)
    $alive = $true
    for ($i = 0; $i -lt $Keys; $i++) {
        $vk = $vks[$rnd.Next($vks.Length)]
        & $shot keyw "NekoCapture Editor" $vk 2>$null | Out-Null
        if (-not (Get-Process NekoCapture -EA SilentlyContinue)) { $alive = $false; break }
        if (($i % 40) -eq 39) { Start-Sleep -Milliseconds 60 }
    }
    Start-Sleep 0.5
    # 乱按一通之后还得能走正常流程：下一帧，然后 Esc 收尾
    & $shot keyw "NekoCapture Editor" 39 2>$null | Out-Null
    Start-Sleep -Milliseconds 250
    $rect = & $shot rect "NekoCapture Editor"
    & $shot keyw "NekoCapture Editor" 27 2>$null | Out-Null
    Start-Sleep 0.4
    $errSize1 = if (Test-Path $errlog) { (Get-Item $errlog).Length } else { 0 }
    $ok = $alive -and ($rect -match "rect") -and ($errSize1 -eq $errSize0)
    Write-Host "alive=$alive rect=$rect errlog_delta=$($errSize1 - $errSize0)"
    if ($ok) { Write-Host "FUZZ PASS ($Keys keys)"; exit 0 }
    Write-Host "FUZZ FAIL"; exit 1
}
finally {
    Get-Process NekoCapture -EA SilentlyContinue | Stop-Process -Force
    Remove-Item Env:\NC_TEST_NO_FG -EA SilentlyContinue
    if ($bak) { Set-Content (Get-FcSettingsPath) -Value $bak -Encoding UTF8 }
}
