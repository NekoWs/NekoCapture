# Esc 分层回归：合成键按住→录制→编辑器→L(进绘制态)→Esc(应只退工具，编辑器仍在 + trace esc→browse)→Esc(关闭)。
# L=直线代表任意绘制工具进入绘制态；进入编辑器走统一测试手势集（按住 Ctrl+Shift+G→edit.lastvideo）。
# 零 OS 级输入：合成键经消息专用窗喂进引擎，编辑器窗全程不被置前台，编辑器键走 --editorkey 直投。
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "lib-hotkey.ps1")
$exe   = Join-Path $root "src/FrameCapture/bin/x64/Debug/net8.0-windows10.0.19041.0/FrameCapture.exe"
$shot  = Join-Path $root "tools/Shot/bin/x64/Debug/net8.0-windows/Shot.exe"
$trace = Join-Path $root "src/fc-trace.log"
$mark = (Get-Item $trace -EA SilentlyContinue).Length
$capHk = 'Ctrl+Shift+G'
Get-Process FrameCapture -EA SilentlyContinue | Stop-Process -Force
Start-Sleep 0.5
$bak = Set-FcTestGestureConfig   # 先杀后写
try {
    $env:FC_TEST_NO_FG = "1"
    $p = Start-Process $exe -PassThru
    Start-Sleep 1.5
    Invoke-FcSynthetic $exe $capHk 700
    # 编辑器出现要等捕获落定，机器忙的时候要多等几秒。轮询到 8 秒封顶：
    # 「编辑器必须出现」是硬条件，只有等待时长随机器走。
    $ed = "not-found"
    $deadline = (Get-Date).AddSeconds(8)
    while ((Get-Date) -lt $deadline) {
        $ed = & $shot rect "FrameCapture Editor" 2>$null
        if ("$ed" -notmatch "not-found") { break }
        Start-Sleep -Milliseconds 250
    }
    if ($ed -match "not-found") {
        Write-Host "ESC-LAYERS FAIL: 编辑器未出现——合成键未经消息通道抵达引擎"
        if (!$p.HasExited) { $p.Kill() }
        exit 1
    }
    Invoke-FcEditorKey $exe 76; Start-Sleep 0.4   # L → 直线工具（绘制态）
    Invoke-FcEditorKey $exe 27; Start-Sleep 0.6   # Esc → 应只退工具
    $ed2 = & $shot rect "FrameCapture Editor" 2>$null
    $stillOpen = "$ed2" -notmatch "not-found"
    # 第二记 Esc 关窗同样轮询：关得慢不算失败，压根没关才算
    Invoke-FcEditorKey $exe 27
    $ed3 = "found"
    $deadline = (Get-Date).AddSeconds(6)
    while ((Get-Date) -lt $deadline) {
        $ed3 = & $shot rect "FrameCapture Editor" 2>$null
        if ("$ed3" -match "not-found") { break }
        Start-Sleep -Milliseconds 250
    }
    $closed = "$ed3" -match "not-found"
    if (!$p.HasExited) { $p.Kill() }
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
}
finally {
    Get-Process FrameCapture -EA SilentlyContinue | Stop-Process -Force
    Remove-Item Env:\FC_TEST_NO_FG -EA SilentlyContinue
    if ($bak) { Set-Content (Join-Path $env:APPDATA "FrameCapture\settings.json") -Value $bak -Encoding UTF8 }
}
