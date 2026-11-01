# 检查轮廓生命周期与 CaptureGuards 决策。
# 证明两件事（全在自动化边界内：合成键经消息专用窗喂进引擎 + 自家窗 keyw + 桌面 BitBlt，无 OS 级输入）：
#  1) 常显模式下一次"录制→编辑器"之后，范围轮廓必须回到屏上
#     （旧版 FinishCapture 无条件 FadeAway，注释谎称"由后续分支重新点亮"）。
#  2) 编辑器开着再按捕获热键（RefocusEditor 路径），轮廓不得被隐藏
#     （旧版 BeginCapture 第一行无条件 HideOutline，守卫在其后）。
# 取证 = 桌面截图在"矩形边框带"上的差分（scripts/lib-band-diff.py，忽略矩形内部）+ trace 增量断言。
# 机制开关：NC_TEST_OUTLINE_IN_CAPTURE=1 让轮廓窗可被截到（用户环境永不置位）；
#           NC_TEST_NO_FG=1 不抢前台。
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "lib-hotkey.ps1")
$exe   = Join-Path $root "src/NekoCapture/bin/x64/Debug/net8.0-windows10.0.19041.0/NekoCapture.exe"
$shot  = Join-Path $root "tools/Shot/bin/x64/Debug/net8.0-windows/Shot.exe"
$uicap = Join-Path $root "tools/UICap/bin/Release/net8.0-windows/UICap.exe"
$bandPy = Join-Path $PSScriptRoot "lib-band-diff.py"
$trace = Join-Path $root "src/nc-trace.log"
$outDir = Join-Path $root "test-out/outline-lifecycle"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# 测试区域：主屏左上角（编辑器居中，绝不重叠；避开任务栏/托盘时钟）
$RX, $RY, $RW, $RH = 120, 120, 260, 180

function BandDiff([string]$a, [string]$b) {
    [int](python $bandPy $a $b $RX $RY $RW $RH 3 24)
}

function TraceTail([long]$from) {
    $fs = [System.IO.File]::Open($trace, 'Open', 'Read', 'ReadWrite')
    try {
        if ($fs.Length -le $from) { return "" }
        $fs.Seek($from, 'Begin') | Out-Null
        (New-Object System.IO.StreamReader($fs)).ReadToEnd()
    } finally { $fs.Dispose() }
}
function TraceLen { if (Test-Path $trace) { (Get-Item $trace).Length } else { 0 } }

# ---- 桌面静态性预检：环带若本身在变（动图壁纸等），像素结论不可信 → SKIP（≠FAIL） ----
$cfg = Get-FcSettingsPath
$cfgBackup = Join-Path $outDir "settings-backup.json"
$capHk = Get-FcCaptureHotkey
$fail = @()
try {
    Get-Process NekoCapture -EA SilentlyContinue | Stop-Process -Force
    Start-Sleep 0.5
    & $uicap desk (Join-Path $outDir "static-a.png") | Out-Null
    Start-Sleep 0.5
    & $uicap desk (Join-Path $outDir "static-b.png") | Out-Null
    if ((BandDiff (Join-Path $outDir "static-a.png") (Join-Path $outDir "static-b.png")) -gt 40) {
        Write-Host "SKIP: 测试区域桌面不静态（动图壁纸/小部件），像素差分不可信"; exit 0
    }

    # ---- 钉设置：常显轮廓 + 关录制边框（更难的路径：录制期间轮廓根本没上屏）+ 预置范围 ----
    Add-Type -AssemblyName System.Windows.Forms
    $dev = ([System.Windows.Forms.Screen]::PrimaryScreen).DeviceName
    $s = if (Test-Path $cfg) { Copy-Item $cfg $cfgBackup -Force; Get-Content $cfg -Raw | ConvertFrom-Json } else { [pscustomobject]@{} }
    function SetProp($o, $n, $v) { if ($o.PSObject.Properties[$n]) { $o.$n = $v } else { $o | Add-Member -NotePropertyName $n -NotePropertyValue $v }; $o }
    $s = SetProp $s 'AlwaysShowRegionOutline' $true
    $s = SetProp $s 'ShowRecordingBorder' $false
    $s = SetProp $s 'TapThresholdMs' 0
    $s = SetProp $s 'TapAlwaysShowsRegion' $false
    $s = SetProp $s 'RegionMonitorId' $dev
    $s = SetProp $s 'RegionX' $RX; $s = SetProp $s 'RegionY' $RY
    $s = SetProp $s 'RegionW' $RW; $s = SetProp $s 'RegionH' $RH
    $s | ConvertTo-Json -Depth 12 | Set-Content $cfg -Encoding UTF8

    $env:NC_TEST_NO_FG = "1"; $env:NC_TEST_OUTLINE_IN_CAPTURE = "1"
    $p = Start-Process $exe -PassThru
    Start-Sleep 2.0
    $markRun = TraceLen

    # S0：启动后常显轮廓应在屏上（Initialize→ShowOutlineIfStored）
    & $uicap desk (Join-Path $outDir "s0-startup.png") | Out-Null

    # ---- 录制中（轮廓必须不在屏上：ShowRecordingBorder=false）----
    $hk = Get-FcHotkeyParts $capHk
    $shotJob = Start-Process $shot -ArgumentList @("hkm", $hk.vk, $hk.ctrl, $hk.shift, $hk.alt, $hk.win, 1400) -PassThru -NoNewWindow
    Start-Sleep 0.7
    & $uicap desk (Join-Path $outDir "s1-recording.png") | Out-Null
    $shotJob.WaitForExit()
    Start-Sleep 2.2   # 编辑器开

    $ed = & $shot rect "NekoCapture Editor" 2>$null
    if ($ed -match "not-found") {
        Write-Host "SKIP: 编辑器未出现——游戏/反外挂可能屏蔽注入式按键，关闭游戏后重跑"; exit 0
    }
    # S2：录制结束后轮廓必须回来（修复点 1）
    & $uicap desk (Join-Path $outDir "s2-after-capture.png") | Out-Null

    # ---- 编辑器开着再按捕获热键：RefocusEditor，不得隐藏轮廓（修复点 2）----
    $mark = TraceLen
    Invoke-FcSynthetic $exe $capHk 120
    Start-Sleep 0.8
    & $uicap desk (Join-Path $outDir "s3-refocus.png") | Out-Null
    $inc = TraceTail $mark

    # ---- 断言 ----
    $dRestore = BandDiff (Join-Path $outDir "s1-recording.png") (Join-Path $outDir "s2-after-capture.png")
    if ($dRestore -lt 200) { $fail += "轮廓未在录制结束后回到屏上 (band diff=$dRestore，期望≥200)" }
    $dKeep = BandDiff (Join-Path $outDir "s2-after-capture.png") (Join-Path $outDir "s3-refocus.png")
    if ($dKeep -gt 120) { $fail += "再按捕获键把轮廓弄没了 (band diff=$dKeep，期望≈0)" }
    if ($inc -match "outline: hide") { $fail += "RefocusEditor 路径仍触发了 HideOutline（trace 增量含 'outline: hide'）" }
    if ($inc -notmatch "BeginCapture state=Editing") { $fail += "trace 增量未见 'BeginCapture state=Editing'（第二键未打到编辑器态）" }
    if ((TraceTail $markRun) -match 'hook slow') { $fail += 'slow hook callback (trace has hook slow)' }
    $ed2 = & $shot rect "NekoCapture Editor" 2>$null
    if ($ed2 -match "not-found") { $fail += "RefocusEditor 后编辑器不应消失" }

    # 收尾：Esc 关编辑器
    Invoke-FcEditorKey $exe 27
    Start-Sleep 0.6
} finally {
    Get-Process NekoCapture -EA SilentlyContinue | Stop-Process -Force
    Remove-Item Env:\NC_TEST_NO_FG, Env:\NC_TEST_OUTLINE_IN_CAPTURE -EA SilentlyContinue
    if (Test-Path $cfgBackup) { Copy-Item $cfgBackup $cfg -Force }
}

if ($fail.Count -eq 0) { Write-Host "OUTLINE-LIFECYCLE PASS"; exit 0 }
Write-Host "OUTLINE-LIFECYCLE FAIL"
$fail | ForEach-Object { Write-Host "  - $_" }
exit 1

