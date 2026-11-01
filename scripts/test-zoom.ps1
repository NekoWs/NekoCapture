# 缩放回归：合成键按住→录制→编辑器→End(1:1)→PageUp×3(放大)→Home(适配)→Esc。
# 进入编辑器走统一测试手势集（按住 Ctrl+Shift+G→edit.lastvideo），并固定大区域；零 OS 级输入。
# 断言 = trace 键路由（editor key=None+End/PageUp/Home）+ ≥5 条 editor zoom= 决策行；
# 截图取证（HUD 应显示 1:1 / 195%）落 test-out/zoom-evidence/。
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "lib-hotkey.ps1")
$exe   = Join-Path $root "src/NekoCapture/bin/x64/Debug/net8.0-windows10.0.19041.0/NekoCapture.exe"
$shot  = Join-Path $root "tools/Shot/bin/x64/Debug/net8.0-windows/Shot.exe"
$uicap = Join-Path $root "tools/UICap/bin/Release/net8.0-windows/UICap.exe"
$out   = Join-Path $root "test-out/zoom-evidence"
New-Item -ItemType Directory -Force -Path $out | Out-Null
$trace = Join-Path $root "src/nc-trace.log"
$mark = (Get-Item $trace -EA SilentlyContinue).Length
$capHk = 'Ctrl+Shift+G'
Get-Process NekoCapture -EA SilentlyContinue | Stop-Process -Force
Start-Sleep 0.5
# 钉住统一测试手势集 + 大区域（适配缩放必 <1.0，End/Home 才各自产生 zoom 决策行；
# 用户小区域会让 End=1:1 变无操作 → 决策行 4<5 假失败，2026-10 踩过）。先杀后写。
$bak = Set-FcTestGestureConfig -RegionX 681 -RegionY 521 -RegionW 1726 -RegionH 980
try {
    $env:NC_TEST_NO_FG = "1"
    $p = Start-Process $exe -PassThru
    Start-Sleep 1.5
    Invoke-FcSynthetic $exe $capHk 700
    Start-Sleep 2.2
    $ed = & $shot rect "NekoCapture Editor" 2>$null
    if ($ed -match "not-found") {
        Write-Host "ZOOM FAIL: 编辑器未出现——合成键未经消息通道抵达引擎"
        if (!$p.HasExited) { $p.Kill() }
        exit 1
    }
    Invoke-FcEditorKey $exe 35; Start-Sleep 0.6   # End → 1:1
    if (Test-Path $uicap) { & $uicap region "NekoCapture Editor" "$out\zoom-1to1.png" | Out-Null }
    1..3 | ForEach-Object { Invoke-FcEditorKey $exe 33; Start-Sleep 0.3 }  # PageUp×3
    if (Test-Path $uicap) { & $uicap region "NekoCapture Editor" "$out\zoom-in.png" | Out-Null }
    Invoke-FcEditorKey $exe 36; Start-Sleep 0.6   # Home → 适配
    Invoke-FcEditorKey $exe 27; Start-Sleep 1.0   # Esc
    if (!$p.HasExited) { $p.Kill() }
    $log = ""
    if ((Get-Item $trace).Length -gt $mark) {
        $fs = [System.IO.File]::Open($trace, 'Open', 'Read', 'ReadWrite')
        $fs.Seek($mark, 'Begin') | Out-Null
        $sr = New-Object System.IO.StreamReader($fs)
        $log = $sr.ReadToEnd(); $sr.Close()
    }
    $hasEnd  = $log -match "editor key=None\+End"
    $hasPgUp = $log -match "editor key=None\+PageUp"
    $hasHome = $log -match "editor key=None\+Home"
    $zoomLines = @([regex]::Matches($log, "editor zoom=")).Count
    Write-Host "end=$hasEnd pgup=$hasPgUp home=$hasHome zoomLines=$zoomLines"
    if ($hasEnd -and $hasPgUp -and $hasHome -and $zoomLines -ge 5) { Write-Host "ZOOM PASS"; exit 0 }
    Write-Host "ZOOM FAIL"
    ($log -split "`n" | Select-String "editor key|zoom") | Select-Object -Last 15
    exit 1
}
finally {
    Get-Process NekoCapture -EA SilentlyContinue | Stop-Process -Force
    Remove-Item Env:\NC_TEST_NO_FG -EA SilentlyContinue
    if ($bak) { Set-Content (Get-FcSettingsPath) -Value $bak -Encoding UTF8 }
}
