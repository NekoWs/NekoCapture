# E 回归护栏（UIAUDIT.md E 条目）：一条命令跑完整闸门——
#   静态扫描 → 构建 0 error → SelfTest 全过（≥基线数）→ 产物起得来 → 控制器响应探针 → 全套 e2e
# 默认路径零 OS 级输入：合成键经主实例的消息专用窗喂进手势引擎，用户正用电脑时也能跑完，
# 键盘鼠标一下都不动。
# 焦点安全是唯一例外——它测的就是「全局热键会不会漏字符进前台应用」，不经真实注入测不到，
# 因此默认 SKIP；要跑请显式加 -AllowInjection，加了之后仍有安静窗口闸兜底。
# 语义：SKIP（本轮没跑的项）≠ FAIL；缺 PASS 标记一律按 FAIL（不信任无结果的运行）。
# 静态扫描不依赖构建，与构建并行起跑，SelfTest 期间收结果；三个构建互不写同一个 obj，也并行。
# 用法: pwsh -NoProfile -File scripts/guardrail.ps1 [-SkipE2e] [-AllowInjection] [-MinSelfTestPass 560]
param([switch]$SkipE2e, [switch]$AllowInjection, [int]$MinSelfTestPass = 674)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "lib-hotkey.ps1")
$exe    = Join-Path $root "src/FrameCapture/bin/x64/Debug/net8.0-windows10.0.19041.0/FrameCapture.exe"
$shot   = Join-Path $root "tools/Shot/bin/x64/Debug/net8.0-windows/Shot.exe"
$stExe  = Join-Path $root "tools/SelfTest/bin/x64/Debug/net8.0-windows10.0.19041.0/SelfTest.exe"
$outDir = Join-Path $root "test-out"
$tmp    = Join-Path $outDir "guardrail-tmp"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
$transcript = Join-Path $outDir "guardrail-last-run.txt"
$logLines = [System.Collections.Generic.List[string]]::new()
function Say([string]$s) { $logLines.Add($s); Write-Host $s }
$results = [System.Collections.Generic.List[object]]::new()
function Add-Result([string]$name, [string]$status, [string]$note = "", [double]$sec = -1) {
    $results.Add([pscustomobject]@{ Name = $name; Status = $status; Note = $note })
    $tail = if ($sec -ge 0) { "  [{0:N1}s]" -f $sec } else { "" }
    Say ("  [{0,-4}] {1,-44} {2}{3}" -f $status, $name, $note, $tail)
}

# 起一个进程，stdout/stderr 落到文件：并行跑扫描与构建，谁先完谁先交卷
function Start-Async([string]$tag, [string]$filePath, [string[]]$argList) {
    $o = Join-Path $tmp "$tag.out"; $e = Join-Path $tmp "$tag.err"
    $quoted = foreach ($a in $argList) { if ($a -match '\s') { '"' + $a + '"' } else { $a } }
    $p = Start-Process -FilePath $filePath -ArgumentList $quoted -NoNewWindow -PassThru `
        -RedirectStandardOutput $o -RedirectStandardError $e -WorkingDirectory $root
    return [pscustomobject]@{ Proc = $p; Out = $o; Err = $e }
}
function Wait-Async($h) {
    $h.Proc.WaitForExit()
    $s = ""
    foreach ($f in @($h.Out, $h.Err)) { if (Test-Path $f) { $s += [IO.File]::ReadAllText($f) } }
    # 报进程自己的存活时间：从发起到收卷的墙钟把「排队等 SelfTest」也算进去了
    $sec = ($h.Proc.ExitTime - $h.Proc.StartTime).TotalSeconds
    return [pscustomobject]@{ Text = $s; Code = $h.Proc.ExitCode; Sec = $sec }
}

# 0) 静态扫描：每个扫描器自己打一行 `LINT-XXX PASS|FAIL …`，这里按同一套规则收
$lints = @(
    @{ n = "lint:comments";      f = "lint-comments.ps1";      mark = "LINT-COMMENTS" },
    @{ n = "lint:motion-tokens"; f = "lint-motion-tokens.ps1"; mark = "LINT-MOTION" },
    @{ n = "lint:copy-parens";   f = "lint-copy-parens.ps1";   mark = "LINT-COPY" },
    @{ n = "lint:i18n-coverage"; f = "lint-i18n-coverage.ps1"; mark = "LINT-I18N" },
    @{ n = "lint:setup-paths";   f = "lint-setup-paths.ps1";   mark = "LINT-SETUP-PATHS" }
)
$lintJobs = [ordered]@{}
foreach ($t in $lints) {
    $lintJobs[$t.n] = Start-Async $t.n "pwsh" @("-NoProfile", "-File", (Join-Path $PSScriptRoot $t.f), "-Quiet")
}

# 1) 杀掉运行中的实例（否则 MSB3027 会让构建静默失败）
$hadUserInstance = [bool](Get-Process FrameCapture -EA SilentlyContinue)
Get-Process FrameCapture -EA SilentlyContinue | Stop-Process -Force
Start-Sleep 0.6

# 2) 构建：SelfTest 带着主程序与 GestureCore 一起建，Shot 与 Setup 各自独立，三者并行。
#    Setup 是 net48 且只链主项目的 Theme/Fluent 共享源文件：主项目往那几个文件里加了对
#    其他工程的依赖，引导器就会编译不过——只有发布脚本会撞见，这里提前拦住。
$buildJobs = [ordered]@{
    "build:app+selftest" = Start-Async "b-selftest" "dotnet" @("build", (Join-Path $root "tools/SelfTest"), "-c", "Debug", "-p:Platform=x64", "-v", "q", "--nologo")
    "build:Shot"         = Start-Async "b-shot"     "dotnet" @("build", (Join-Path $root "tools/Shot"), "-c", "Debug", "-p:Platform=x64", "-v", "q", "--nologo")
    "build:Setup"        = Start-Async "b-setup"    "dotnet" @("build", (Join-Path $root "src/Setup"), "-c", "Debug", "-v", "q", "--nologo")
}
# 每个构建项要求存在的产物：SelfTest 那条同时产出主程序
$buildExe = @{ "build:app+selftest" = @($exe, $stExe); "build:Shot" = @($shot); "build:Setup" = @() }
$okBuild = $true
foreach ($k in $buildJobs.Keys) {
    $r = Wait-Async $buildJobs[$k]
    $nErr = ([regex]::Matches($r.Text, ': error')).Count
    $missing = @($buildExe[$k] | Where-Object { -not (Test-Path $_) })
    $ok = ($nErr -eq 0 -and $missing.Count -eq 0)
    Add-Result $k $(if ($ok) { "PASS" } else { "FAIL" }) ("errors={0}{1}" -f $nErr,
        $(if ($missing.Count) { " 缺产物: " + (($missing | ForEach-Object { Split-Path $_ -Leaf }) -join ',') } else { "" })) $r.Sec
    if (-not $ok) { Say $r.Text; $okBuild = $false }
}

# 3) SelfTest：全过且不少于基线数。用例名还要两两不同——重名的两条只看得见一条
if ($okBuild) {
    $t0 = [Diagnostics.Stopwatch]::StartNew()
    $so = & $stExe 2>&1 | Out-String
    $stSec = $t0.Elapsed.TotalSeconds
    $m = [regex]::Match($so, 'RESULT PASS=(\d+) FAIL=(\d+)')
    if ($m.Success) {
        $p = [int]$m.Groups[1].Value; $f = [int]$m.Groups[2].Value
        $ok = ($f -eq 0) -and ($p -ge $MinSelfTestPass)
        Add-Result "selftest" $(if ($ok) { "PASS" } else { "FAIL" }) "PASS=$p FAIL=$f (floor $MinSelfTestPass)" $stSec
        if (-not $ok) { Say (($so -split "`n" | Select-String '^FAIL' | Select-Object -First 12) -join "`n") }
        $names = ([regex]::Matches($so, '(?m)^(?:PASS|FAIL) (\S+)') | ForEach-Object { $_.Groups[1].Value })
        $dup = @($names | Group-Object | Where-Object Count -gt 1)
        Add-Result "selftest:names-unique" $(if ($dup.Count -eq 0) { "PASS" } else { "FAIL" }) `
            ("{0} 条用例，重名 {1}" -f $names.Count, $dup.Count) 0
        if ($dup.Count) { Say (($dup | ForEach-Object { "  重名 {0} ×{1}" -f $_.Name, $_.Count }) -join "`n") }
    } else { Add-Result "selftest" "FAIL" "无 RESULT 行" $stSec; Say (($so -split "`n" | Select-Object -Last 12) -join "`n") }
} else { Add-Result "selftest" "SKIP" "构建失败"; Add-Result "selftest:names-unique" "SKIP" "构建失败" }

# 4) 收静态扫描结果：SelfTest 跑的时候它们一直在旁边跑
foreach ($t in $lints) {
    $r = Wait-Async $lintJobs[$t.n]
    $line = ($r.Text -split "`r?`n") | Where-Object { $_ -match "^$($t.mark) (PASS|FAIL)" } | Select-Object -Last 1
    if ($line) {
        $st = if ($line -match "^$($t.mark) PASS") { "PASS" } else { "FAIL" }
        Add-Result $t.n $st (($line -replace "^$($t.mark) (PASS|FAIL)\s*", "").Trim()) $r.Sec
        if ($st -eq "FAIL") { Say $r.Text }
    } else { Add-Result $t.n "FAIL" "扫描器无输出" $r.Sec; Say $r.Text }
}

# 4.5) 安装器搬运探针：装到非系统盘那条路径本机走不到，只有把搬运逻辑拎出来实机跑一遍才算测过。
#      只在仓库内与临时目录写文件，不碰注册表，也不动已装程序。
$spProj = Join-Path $root "tools/SetupProbe"
$spExe  = Join-Path $spProj "bin/Debug/net48/SetupProbe.exe"
$spBuild = & dotnet build $spProj -c Debug -v q --nologo 2>&1 | Out-String
if (([regex]::Matches($spBuild, ': error')).Count -eq 0 -and (Test-Path $spExe)) {
    $spOut = & $spExe 2>&1 | Out-String
    $okSp = $spOut -match 'SETUP-PROBE PASS'
    Add-Result "probe:setup-paths" $(if ($okSp) { "PASS" } else { "FAIL" }) `
        ("{0} 项搬运断言" -f ([regex]::Matches($spOut, '(?m)^\s*(?:PASS|FAIL) ').Count)) 0
    if (-not $okSp) { Say $spOut }
} else {
    Add-Result "probe:setup-paths" "FAIL" "探针构建失败或无产物"
    Say $spBuild
}

# 4.7) 双语版面审计：英文比中文长，按中文排的版面在英文下会把字挤掉或压成省略号；
#      英文界面里残留的中文（动态拼出来那种、静态扫描看不见的）也在这里抓。全程离屏建窗，不动屏幕。
$ux = & pwsh -NoProfile -File (Join-Path $PSScriptRoot "ux-audit.ps1") -Quiet 2>&1 | Out-String
$uxLine = ($ux -split "`r?`n" | Where-Object { $_ -match '^UX-AUDIT (PASS|FAIL)' } | Select-Object -Last 1)
if ($uxLine) {
    $stUx = if ($uxLine -match '^UX-AUDIT PASS') { "PASS" } else { "FAIL" }
    Add-Result "lint:ux-bilingual" $stUx (($uxLine -replace '^UX-AUDIT (PASS|FAIL)\s*', "").Trim()) 0
    if ($stUx -eq "FAIL") { Say (Get-Content "test-out/uxaudit/report.txt" -Raw -EA SilentlyContinue) }
} else { Add-Result "lint:ux-bilingual" "FAIL" "审计无输出（SelfTest 产物缺失？）"; Say $ux }

# 5) 产物起得来：-SkipE2e 时其余动态项全不跑，主程序能不能起来这件事不能没人管。
#    托盘应用，不弹窗、不注入任何输入。
#
# 验证链一律在自己的设置目录里跑：FC_SETTINGS_DIR 把 settings.json 与崩溃用的 error.log 整套搬进
# test-out/guardrail-settings，起点是用户那份的副本。用户 %APPDATA% 里那份只读不写——
# 中途崩了、被人掐了，都不会在他机器上留下测试态（主题、热键、强调色停在测试钉的那一套）。
$fcDir = Join-Path $PSScriptRoot '..\test-out\guardrail-settings'
$fcUser = Join-Path $env:APPDATA 'FrameCapture\settings.json'
if (Test-Path $fcDir) { Remove-Item $fcDir -Recurse -Force -EA SilentlyContinue }
New-Item -ItemType Directory -Force -Path $fcDir | Out-Null
if (Test-Path $fcUser) { Copy-Item $fcUser (Join-Path $fcDir 'settings.json') -Force }
$env:FC_SETTINGS_DIR = $fcDir

if ($okBuild) {
    $t0 = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process $exe -PassThru
    Start-Sleep 2.5
    $alive = Get-Process -Id $p.Id -EA SilentlyContinue
    $note = if ($alive) { "进程存活，托盘就绪" } else { "退出码 $($p.ExitCode)" }
    if ($alive) { Stop-Process -Id $p.Id -Force -EA SilentlyContinue }
    Add-Result "smoke:startup" $(if ($alive) { "PASS" } else { "FAIL" }) $note $t0.Elapsed.TotalSeconds
} else { Add-Result "smoke:startup" "SKIP" "构建失败" }

# 6) 注入可达性探针 + 7) e2e
$e2e = @(
    @{ n = "e2e:editor-keys(捕获→编辑器→Enter→剪贴板三格式)"; f = "test-editor-keys.ps1";    mark = "EDITOR-KEYS" },
    @{ n = "e2e:zoom(1:1/放大/适配 键路由+zoom决策)";             f = "test-zoom.ps1";           mark = "ZOOM" },
    @{ n = "e2e:esc-layers(绘制态Esc不丢捕获)";                   f = "test-esc-layers.ps1";     mark = "ESC-LAYERS" },
    @{ n = "e2e:tap-semantics(手势轻点截图/长按录制)";             f = "test-tap-semantics.ps1";  mark = "TAP-SEMANTICS" },
    @{ n = "e2e:focus-safety(吞键不漏字符)";                     f = "test-focus-safety.ps1";   mark = "FOCUS-SAFETY"; gated = $true },
    @{ n = "e2e:fuzz-statemachine(乱键存活)";                    f = "fuzz-statemachine.ps1";   mark = "FUZZ" },
    @{ n = "e2e:fuzz-keys(编辑器乱按 300 键存活)";               f = "fuzz-keys.ps1";           mark = "FUZZ" },
    @{ n = "e2e:probe-memory(开合落在平台不漏)";                f = "probe-memory.ps1";        mark = "MEM-PROBE" }
)
if ($SkipE2e) {
    # SKIP 名单就是 e2e 名单本身：报一份和实际不相符的清单，等于谎报没跑什么
    foreach ($t in $e2e) { Add-Result $t.n "SKIP" "-SkipE2e" }
} elseif ($okBuild) {
    # e2e 会改设置（主题、热键、手势配置都写在 settings.json）——那些写全落在上面起的临时目录里，
    # 用户那份碰不到。这里只保证一件事：中途抛异常也要记一条 FAIL，别让汇总看起来像全跑过。
    try {
        # 控制器响应探针：--picker 让主实例走 BeginScopeSelection（与热键命中同一个入口），
        # 选区窗出现即证明产物起得来、控制器在听。零 OS 级输入，也不抢前台。
        $t0 = [Diagnostics.Stopwatch]::StartNew()
        $env:FC_TEST_NO_FG = "1"
        Start-Process $exe -ArgumentList '--picker' -PassThru | Out-Null
        Start-Sleep 2.0
        $pkProbe = & $shot rect "FrameCapture Region" 2>$null
        Add-Result "probe:controller-responds" $(if ($pkProbe -notmatch "not-found") { "PASS" } else { "FAIL" }) $pkProbe $t0.Elapsed.TotalSeconds
        & $shot keyw "FrameCapture Region" 27 2>$null | Out-Null
        Start-Sleep 0.4
        Remove-Item Env:\FC_TEST_NO_FG -EA SilentlyContinue
        Get-Process FrameCapture -EA SilentlyContinue | Stop-Process -Force

        foreach ($t in $e2e) {
            # 焦点安全是全套里唯一必须真注入 OS 级按键的一项：注入期间 Ctrl 真的会被按住约 900ms，
            # 用户此刻滚一下滚轮，浏览器就跟着缩放。安静窗口只保证「过去 3 秒没输入」，
            # 预测不了下一秒——所以默认不跑，显式 -AllowInjection 才跑，跑了也还要再过一道闸。
            if ($t.gated) {
                if (-not $AllowInjection) { Add-Result $t.n "SKIP" "需 -AllowInjection（会注入 OS 级按键）"; continue }
                if (-not (Wait-FcQuiet -TimeoutSec 25 -QuietMs 3000)) { Add-Result $t.n "SKIP" "用户持续有输入，跳过注入项"; continue }
            }
            $t0 = [Diagnostics.Stopwatch]::StartNew()
            $r = Wait-Async (Start-Async ("e2e-" + $t.mark) "pwsh" @("-NoProfile", "-File", (Join-Path $PSScriptRoot $t.f)))
            $o = $r.Text
            # 判定按行来：标记与 PASS 不在同一行不算通过，缺标记一律 FAIL
            $lines = @($o -split "`r?`n")
            $status = if (@($lines | Where-Object { $_ -match "$($t.mark).*PASS" }).Count) { "PASS" }
                      elseif (@($lines | Where-Object { $_ -match "$($t.mark).*FAIL" }).Count -or $r.Code -ne 0) { "FAIL" }
                      elseif (@($lines | Where-Object { $_ -match 'SKIP' }).Count) { "SKIP" }
                      else { "FAIL" }
            $note = if ($status -eq "FAIL") { "exit={0}; {1}" -f $r.Code, (($lines | Select-Object -Last 3) -join " / ") } else { "" }
            Add-Result $t.n $status $note $r.Sec
            if ($o -match "capture_hotkey|restored_capture") { Say (($lines | Select-String "hotkey=" | Out-String).Trim()) }
        }
    } catch {
        # 一条脚本抛到这儿，整套 e2e 就此中断：如实记一条 FAIL，别让汇总看起来像全跑过
        Add-Result "e2e:phase" "FAIL" ("e2e 阶段抛出：" + $_.Exception.Message) 0
    }
} else { foreach ($t in $e2e) { Add-Result $t.n "SKIP" "构建失败" } }

# 8) 恢复稳态：护栏开始前用户实例在跑，就跑一个新的回来
if ($hadUserInstance) { Start-Process $exe; Say "  (已恢复用户的 FrameCapture 实例)" }

# 汇总
$failN = @($results | Where-Object Status -eq "FAIL").Count
$skipN = @($results | Where-Object Status -eq "SKIP").Count
Say ""
Say ("GUARDRAIL {0}  (FAIL={1} SKIP={2} 共 {3} 项)" -f $(if ($failN -eq 0) { "PASS" } else { "FAIL" }), $failN, $skipN, $results.Count)
# 空管道不截断文件：本轮若无日志行，上一轮的记录会留在原地冒充本轮。
Set-Content $transcript -Encoding UTF8 -Value $(if ($logLines.Count) { $logLines } else { @("（本轮无日志行）") })
Remove-Item $tmp -Recurse -Force -EA SilentlyContinue
exit $(if ($failN -eq 0) { 0 } else { 1 })
