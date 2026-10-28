# 内存回归探针：编辑器/选区反复开合，判的是「涨到平台」还是「线性涨」。
#
# 为什么不能只看一两个点的数字：第一轮开合会把 4K 快照、WPF 合成、编码器缓冲一次性建起来，
# 私有字节从 ~86MB 跳到 ~234MB 就再也不动；.NET 也不主动还内存。单点采样看见「涨了 148MB」
# 会误报泄漏，看见「没涨」又可能只是没跑到泄漏那一步。只有反复开合看曲线才分得清：
# 平台 = 缓存到上限或 GC 没跑；每轮都涨 = 真泄漏。
#
# 全程零 OS 级注入：合成键经消息专用窗喂进引擎 KeySink（与 test-editor-keys 同一条路），
# 所以能进护栏，用户在用电脑时也能跑。
# 用法: pwsh -NoProfile -File scripts/probe-memory.ps1 [-Cycles 5]
param([int]$Cycles = 5)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot
. (Join-Path $PSScriptRoot "lib-hotkey.ps1")
$exe = Join-Path $root "src/FrameCapture/bin/x64/Debug/net8.0-windows10.0.19041.0/FrameCapture.exe"
$shot = Join-Path $root "tools/Shot/bin/x64/Debug/net8.0-windows/Shot.exe"
$capHk = 'Ctrl+Shift+G'
$scopeHk = 'Ctrl+Shift+R'

function Get-Mem {
    $p = Get-Process FrameCapture -ErrorAction SilentlyContinue
    if (-not $p) { return $null }
    $p.Refresh()
    [pscustomobject]@{ Mb = $p.PrivateMemorySize64 / 1MB; Handles = $p.HandleCount; Threads = $p.Threads.Count }
}
function Snap([string]$tag) {
    $m = Get-Mem
    if (-not $m) { Write-Host ("{0,-18} 进程没了" -f $tag); return $null }
    Write-Host ("{0,-18} Private={1,7:N1} MB  handles={2,5}  threads={3}" -f $tag, $m.Mb, $m.Handles, $m.Threads)
    return $m
}

# 阈值按实测噪声给：预热后连续九轮开合，私有字节抖动 0.1 MB、句柄 +1。
# 一轮 4K 帧约 33 MB——每轮漏一帧会在「最后两轮之间」留下 ~33 MB，被 25 MB 这条线抓住。
$MaxMbGrowth = 25
$MaxHandleGrowth = 120

$bak = Set-FcTestGestureConfig
try {
    Get-Process FrameCapture -EA SilentlyContinue | Stop-Process -Force; Start-Sleep 0.8
    $env:FC_TEST_NO_FG = "1"
    Start-Process $exe | Out-Null   # 子进程继承 FC_TEST_NO_FG 与 FC_SETTINGS_DIR
    Start-Sleep 2.2
    Snap "1-idle" | Out-Null

    # 选区开合一轮：4K 快照是最大的一块，先让它进缓存。
    # 收场只能靠重启进程：选区窗是非激活窗，Esc/Enter/C/F/Tab 全部由 HotkeyManager 全局钩子代收，
    # PostMessage 的键与 --synthetic 合成键都到不了它，而它也没有右键取消——留着它，后面的录制手势会被整屏遮罩吃掉。
    Invoke-FcSynthetic $exe $scopeHk 120; Start-Sleep -Milliseconds 1500
    $pick = Snap "2-picker-open"
    if (-not $pick) { Write-Host "MEM-PROBE FAIL 选区窗期间进程不见了"; exit 1 }
    Get-Process FrameCapture -EA SilentlyContinue | Stop-Process -Force; Start-Sleep 0.8
    Start-Process $exe | Out-Null; Start-Sleep 2.2
    Snap "2b-restarted" | Out-Null

    # 编辑器开合若干轮，逐轮记录
    $marks = @()
    for ($i = 1; $i -le $Cycles; $i++) {
        Invoke-FcSynthetic $exe $capHk 700
        # 等编辑器出来：护栏并行跑其它关时这一步会慢上几秒，给一段重试而不是卡死一个固定等待
        $ed = "not-found"
        for ($w = 0; $w -lt 16; $w++) {
            Start-Sleep -Milliseconds 500
            $ed = & $shot rect "FrameCapture Editor" 2>$null
            if ($ed -notmatch "not-found") { break }
        }
        if ($ed -match "not-found") { Write-Host "MEM-PROBE FAIL 第 $i 轮编辑器没出现"; exit 1 }
        $closed = $false
        for ($w = 0; $w -lt 24; $w++) {
            if ((& $shot rect "FrameCapture Editor" 2>$null) -match 'not-found') { $closed = $true; break }
            if ($w % 6 -eq 0) { Invoke-FcEditorKey $exe 27 }
            Start-Sleep -Milliseconds 100
        }
        if (-not $closed) { Write-Host "MEM-PROBE FAIL 第 $i 轮编辑器未关闭"; exit 1 }
        Start-Sleep -Milliseconds 1200
        $m = Snap ("3-cycle-{0}" -f $i)
        if (-not $m) { Write-Host "MEM-PROBE FAIL 第 $i 轮之后进程不见了"; exit 1 }
        $marks += $m
    }

    # 判平台：取最后两轮之间的增量。轮数太少时退回第一轮与最后一轮。
    $a = if ($marks.Count -ge 2) { $marks[$marks.Count - 2] } else { $marks[0] }
    $b = $marks[-1]
    $dMb = $b.Mb - $a.Mb
    $dH = $b.Handles - $a.Handles
    $ok = ($dMb -lt $MaxMbGrowth) -and ($dH -lt $MaxHandleGrowth)
    Write-Host ("最后两轮增量：Private {0:N1} MB（线 {1}）handles {2:+0;-0;0}（线 {3}）" -f $dMb, $MaxMbGrowth, $dH, $MaxHandleGrowth)
    if ($ok) { Write-Host "MEM-PROBE PASS（$Cycles 轮开合后落在平台上）"; exit 0 }
    Write-Host "MEM-PROBE FAIL（每轮还在涨，像泄漏不像缓存）"; exit 1
}
finally {
    Get-Process FrameCapture -EA SilentlyContinue | Stop-Process -Force
    Remove-Item Env:\FC_TEST_NO_FG -EA SilentlyContinue
    if ($bak) { Set-Content (Get-FcSettingsPath) -Value $bak -Encoding UTF8 }
}
