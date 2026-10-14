# E 回归护栏（UIAUDIT.md E 条目）：一条命令跑完整闸门——
#   构建 0 error → SelfTest 全过（≥基线数）→ 注入可达性探针 → 全套 e2e（捕获链路/热键/点按语义/焦点安全/状态机 fuzz）
# 语义：SKIP（反外挂拦截注入）≠ FAIL；缺 PASS 标记一律按 FAIL（不信任无结果的运行）。
# 用法: pwsh -NoProfile -File scripts/guardrail.ps1 [-SkipE2e] [-MinSelfTestPass 495]
param([switch]$SkipE2e, [int]$MinSelfTestPass = 495)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "lib-hotkey.ps1")
$exe    = Join-Path $root "src/FrameCapture/bin/x64/Debug/net8.0-windows10.0.19041.0/FrameCapture.exe"
$shot   = Join-Path $root "tools/Shot/bin/x64/Debug/net8.0-windows/Shot.exe"
$stExe  = Join-Path $root "tools/SelfTest/bin/x64/Debug/net8.0-windows10.0.19041.0/SelfTest.exe"
$outDir = Join-Path $root "test-out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$transcript = Join-Path $outDir "guardrail-last-run.txt"
$logLines = [System.Collections.Generic.List[string]]::new()
function Say([string]$s) { $logLines.Add($s); Write-Host $s }
$results = [System.Collections.Generic.List[object]]::new()
function Add-Result([string]$name, [string]$status, [string]$note = "") {
    $results.Add([pscustomobject]@{ Name = $name; Status = $status; Note = $note })
    Say ("  [{0,-4}] {1} {2}" -f $status, $name, $note)
}

# 0a) 注释护栏（docs/COMMENT-STYLE.md）：注释不合规直接 FAIL，不进构建
$lintOut = & pwsh -NoProfile -File (Join-Path $PSScriptRoot "lint-comments.ps1") -Quiet 2>&1 | Out-String
$lm = [regex]::Match($lintOut, 'LINT-COMMENTS (\w+)\s+FAIL=(\d+) WARN=(\d+)')
if ($lm.Success) {
    $lf = [int]$lm.Groups[2].Value; $lw = [int]$lm.Groups[3].Value
    Add-Result "lint:comments" $(if ($lf -eq 0) { "PASS" } else { "FAIL" }) "FAIL=$lf WARN=$lw"
    if ($lf -gt 0) { Say (& pwsh -NoProfile -File (Join-Path $PSScriptRoot "lint-comments.ps1") 2>&1 | Out-String) }
} else { Add-Result "lint:comments" "FAIL" "扫描器无输出" }

# 0b) 动效令牌护栏（GOAL.md：动效必须引用 Fluent.Motion 令牌）
$motionOut = & pwsh -NoProfile -File (Join-Path $PSScriptRoot "lint-motion-tokens.ps1") -Quiet 2>&1 | Out-String
$mm = [regex]::Match($motionOut, 'LINT-MOTION (\w+)\s+FAIL=(\d+)')
if ($mm.Success) {
    $mf = [int]$mm.Groups[2].Value
    Add-Result "lint:motion-tokens" $(if ($mf -eq 0) { "PASS" } else { "FAIL" }) "FAIL=$mf"
    if ($mf -gt 0) { Say (& pwsh -NoProfile -File (Join-Path $PSScriptRoot "lint-motion-tokens.ps1") 2>&1 | Out-String) }
} else { Add-Result "lint:motion-tokens" "FAIL" "扫描器无输出" }

# 1) 杀掉运行中的实例（否则 MSB3027 会让构建静默失败）
$hadUserInstance = [bool](Get-Process FrameCapture -EA SilentlyContinue)
Get-Process FrameCapture -EA SilentlyContinue | Stop-Process -Force
Start-Sleep 0.6

# 1) 构建：主程序 + 工具（计数 ': error'）
function Build-Project([string]$name, [string]$proj, [string]$outExe) {
    $o = dotnet build $proj -c Debug -p:Platform=x64 -v q --nologo 2>&1 | Out-String
    $nErr = ([regex]::Matches($o, ': error')).Count
    $exists = Test-Path $outExe
    Add-Result "build:$name" $(if ($nErr -eq 0 -and $exists) { "PASS" } else { "FAIL" }) "errors=$nErr exe=$(if ($exists) {'ok' } else {'missing'})"
    if ($nErr -ne 0) { Say $o }
    return ($nErr -eq 0 -and $exists)
}
$okBuild = Build-Project "app"   (Join-Path $root "src/FrameCapture") $exe
$okBuild = (Build-Project "Shot" (Join-Path $root "tools/Shot") $shot) -and $okBuild
$okBuild = (Build-Project "SelfTest" (Join-Path $root "tools/SelfTest") $stExe) -and $okBuild

# 2) SelfTest
if ($okBuild) {
    $so = & $stExe 2>&1 | Out-String
    $m = [regex]::Match($so, 'RESULT PASS=(\d+) FAIL=(\d+)')
    if ($m.Success) {
        $p = [int]$m.Groups[1].Value; $f = [int]$m.Groups[2].Value
        $ok = ($f -eq 0) -and ($p -ge $MinSelfTestPass)
        Add-Result "selftest" $(if ($ok) { "PASS" } else { "FAIL" }) "PASS=$p FAIL=$f (floor $MinSelfTestPass)"
    } else { Add-Result "selftest" "FAIL" "无 RESULT 行" }
} else { Add-Result "selftest" "SKIP" "构建失败" }

# 3) 注入可达性探针 + 4) e2e
if (-not $SkipE2e -and $okBuild) {
    $capHk = Get-FcCaptureHotkey
    $blocked = $false
    $probe = Start-Process $exe -PassThru; Start-Sleep 1.5
    Invoke-FcHotkey $shot $capHk 200 | Out-Null; Start-Sleep 1.2
    # 注入可达性：捕获键命中 FC 即会弹窗——轻点走选区窗、长按走录制→编辑器。
    # 任一 FC 窗出现即证明注入可达；两者都没有才判为被反外挂拦截。
    $edProbe = & $shot rect "FrameCapture Editor" 2>$null
    $pkProbe = & $shot rect "FrameCapture Region" 2>$null
    if ($edProbe -match "not-found" -and $pkProbe -match "not-found") { $blocked = $true }
    else {
        if ($edProbe -notmatch "not-found") { & $shot keyw "FrameCapture Editor" 27 2>$null | Out-Null }
        if ($pkProbe -notmatch "not-found") { & $shot keyw "FrameCapture Region" 27 2>$null | Out-Null }
        Start-Sleep 0.4
    }
    Get-Process FrameCapture -EA SilentlyContinue | Stop-Process -Force
    if ($blocked) {
        Say "  !! 注入探针未命中编辑器——前台疑似反外挂游戏，e2e 全部 SKIP（≠FAIL）"
    }
    $e2e = @(
        @{ n = "e2e:editor-keys(捕获→编辑器→Enter→剪贴板三格式)"; f = "test-editor-keys.ps1";    mark = "EDITOR-KEYS" },
        @{ n = "e2e:zoom(1:1/放大/适配 键路由+zoom决策)";             f = "test-zoom.ps1";           mark = "ZOOM" },
        @{ n = "e2e:esc-layers(绘制态Esc不丢捕获)";                   f = "test-esc-layers.ps1";     mark = "ESC-LAYERS" },
        @{ n = "e2e:tap-semantics(手势轻点截图/长按录制)";             f = "test-tap-semantics.ps1";  mark = "TAP-SEMANTICS" },
        @{ n = "e2e:focus-safety(吞键不漏字符)";                     f = "test-focus-safety.ps1";   mark = "FOCUS-SAFETY" },
        @{ n = "e2e:fuzz-statemachine(乱键存活)";                    f = "fuzz-statemachine.ps1";   mark = "FUZZ" }
    )
    foreach ($t in $e2e) {
        if ($blocked) { Add-Result $t.n "SKIP" "注入被拦截"; continue }
        $o = & pwsh -NoProfile -File (Join-Path $PSScriptRoot $t.f) 2>&1 | Out-String
        $code = $LASTEXITCODE
        $status = if ($o -match "$($t.mark).*PASS") { "PASS" }
                  elseif ($o -match "SKIP") { "SKIP" }
                  elseif ($o -match "$($t.mark).*FAIL" -or $code -ne 0) { "FAIL" }
                  else { "FAIL" }
        $note = if ($status -eq "FAIL") { "exit=$code; " + (($o -split "`n" | Select-Object -Last 3) -join " / ") } else { "" }
        Add-Result $t.n $status $note
        if ($o -match "capture_hotkey|restored_capture") { Say ($o -split "`n" | Select-String "hotkey=" | Out-String).Trim() }
    }
} elseif ($SkipE2e) { foreach ($t in @("editor-keys","hotkey","tap-semantics","focus-safety","fuzz")) { Add-Result "e2e:$t" "SKIP" "-SkipE2e" } }

# 5) 恢复稳态：护栏开始前用户实例在跑，就跑一个新的回来
if ($hadUserInstance) { Start-Process $exe; Say "  (已恢复用户的 FrameCapture 实例)" }

# 汇总
$failN = @($results | Where-Object Status -eq "FAIL").Count
$skipN = @($results | Where-Object Status -eq "SKIP").Count
Say ""
Say ("GUARDRAIL {0}  (FAIL={1} SKIP={2} 共 {3} 项)" -f $(if ($failN -eq 0) { "PASS" } else { "FAIL" }), $failN, $skipN, $results.Count)
$logLines | Set-Content $transcript -Encoding UTF8
exit $(if ($failN -eq 0) { 0 } else { 1 })
