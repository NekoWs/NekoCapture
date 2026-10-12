# R7 缩放回归：手势按住→录制→编辑器→End(1:1)→PageUp×3(放大)→Home(适配)→Esc。
# R126 B2：进入编辑器走统一测试手势集（按住 Ctrl+Shift+G→edit.lastvideo），并固定大区域。
# 断言 = trace 键路由（editor key=None+End/PageUp/Home）+ ≥5 条 editor zoom= 决策行；
# 截图取证（HUD 应显示 1:1 / 195%）落 test-out/zoom-evidence/。注入被拦截（编辑器未出现）→ SKIP ≠ FAIL。
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "lib-hotkey.ps1")
$exe   = Join-Path $root "src/FrameCapture/bin/x64/Debug/net8.0-windows10.0.19041.0/FrameCapture.exe"
$shot  = Join-Path $root "tools/Shot/bin/x64/Debug/net8.0-windows/Shot.exe"
$uicap = Join-Path $root "tools/UICap/bin/Release/net8.0-windows/UICap.exe"
$out   = Join-Path $root "test-out/zoom-evidence"
New-Item -ItemType Directory -Force -Path $out | Out-Null
$trace = Join-Path $root "src/fc-trace.log"
$mark = (Get-Item $trace -EA SilentlyContinue).Length
$capHk = 'Ctrl+Shift+G'
Get-Process FrameCapture -EA SilentlyContinue | Stop-Process -Force
Start-Sleep 0.5
# 钉住统一测试手势集 + 大区域（适配缩放必 <1.0，End/Home 才各自产生 zoom 决策行；
# 用户小区域会让 End=1:1 变无操作 → 决策行 4<5 假失败，2026-10 踩过）。先杀后写。
$bak = Set-FcTestGestureConfig -RegionX 681 -RegionY 521 -RegionW 1726 -RegionH 980
try {
    $env:FC_TEST_NO_FG = "1"
    $p = Start-Process $exe -PassThru
    Start-Sleep 1.5
    Invoke-FcHotkey $shot $capHk 700 | Out-Null
    Start-Sleep 2.0
    $ed = & $shot rect "FrameCapture Editor" 2>$null
    if ($ed -match "not-found") {
        Write-Host "ZOOM SKIP: 编辑器未出现（注入被拦截）"
        if (!$p.HasExited) { $p.Kill() }
        exit 0
    }
    Wait-FcModifiersUp; & $shot keyw "FrameCapture Editor" 35 2>$null | Out-Null; Start-Sleep 0.6   # End → 1:1
    if (Test-Path $uicap) { & $uicap region "FrameCapture Editor" "$out\zoom-1to1.png" | Out-Null }
    1..3 | ForEach-Object { Wait-FcModifiersUp; & $shot keyw "FrameCapture Editor" 33 2>$null | Out-Null; Start-Sleep 0.3 }  # PageUp×3
    if (Test-Path $uicap) { & $uicap region "FrameCapture Editor" "$out\zoom-in.png" | Out-Null }
    Wait-FcModifiersUp; & $shot keyw "FrameCapture Editor" 36 2>$null | Out-Null; Start-Sleep 0.6   # Home → 适配
    Wait-FcModifiersUp; & $shot keyw "FrameCapture Editor" 27 2>$null | Out-Null; Start-Sleep 1.0   # Esc
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
    Get-Process FrameCapture -EA SilentlyContinue | Stop-Process -Force
    Remove-Item Env:\FC_TEST_NO_FG -EA SilentlyContinue
    if ($bak) { Set-Content (Join-Path $env:APPDATA "FrameCapture\settings.json") -Value $bak -Encoding UTF8 }
}
