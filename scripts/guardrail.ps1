# 构建、静态扫描、SelfTest、双语审计、启动探针与端到端检查。
# 默认不注入 OS 级输入；-Full 包含应用内消息驱动的端到端检查。
# -AllowInjection 显式启用焦点安全检查；SKIP 不表示验证通过。
# -Quick 仅构建与静态扫描；-SkipE2e 跳过端到端检查。
param([switch]$Full, [switch]$Quick, [switch]$SkipE2e, [switch]$AllowInjection, [int]$MinSelfTestPass = 1087)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "lib-hotkey.ps1")
$exe    = Join-Path $root "src/FrameCapture/bin/x64/Debug/net8.0-windows10.0.19041.0/FrameCapture.exe"
$shot   = Join-Path $root "tools/Shot/bin/x64/Debug/net8.0-windows/Shot.exe"
$stExe  = Join-Path $root "tools/SelfTest/bin/x64/Debug/net8.0-windows10.0.19041.0/SelfTest.exe"
$outDir = Join-Path $root "test-out"
$tmp    = Join-Path $outDir "guardrail-tmp"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
function Remove-TestDirectory([string]$path) {
    $full = [IO.Path]::GetFullPath($path)
    $allowed = [IO.Path]::GetFullPath($outDir).TrimEnd('\') + '\'
    if (-not $full.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw "测试目录越界: $full" }
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
}
Remove-TestDirectory $tmp
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
    $p = Start-Process -FilePath $filePath -ArgumentList $quoted -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput $o -RedirectStandardError $e -WorkingDirectory $root
    return [pscustomobject]@{ Proc = $p; Out = $o; Err = $e }
}
function Wait-Async($h) {
    $h.Proc.WaitForExit()
    $s = ""
    foreach ($f in @($h.Out, $h.Err)) {
        if (-not (Test-Path $f)) { continue }
        $bytes = [IO.File]::ReadAllBytes($f)
        try { $s += [Text.UTF8Encoding]::new($false, $true).GetString($bytes) }
        catch {
            [Text.Encoding]::RegisterProvider([Text.CodePagesEncodingProvider]::Instance)
            $s += [Text.Encoding]::GetEncoding(936).GetString($bytes)
        }
    }
    # 报进程自己的存活时间：从发起到收卷的墙钟把「排队等 SelfTest」也算进去了
    $sec = ($h.Proc.ExitTime - $h.Proc.StartTime).TotalSeconds
    return [pscustomobject]@{ Text = $s; Code = $h.Proc.ExitCode; Sec = $sec }
}

$savedEnvironment = @{}
foreach ($name in @('TEMP', 'TMP', 'FC_SETTINGS_DIR', 'FC_TEST_NO_FG')) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}
$runtimeTemp = Join-Path $outDir 'guardrail-runtime'
New-Item -ItemType Directory -Force -Path $runtimeTemp | Out-Null
$lockingInstances = @()
try {
    $env:TEMP = $runtimeTemp
    $env:TMP = $runtimeTemp
    $env:FC_TEST_NO_FG = '1'
# 0) 静态扫描：每个扫描器自己打一行 `LINT-XXX PASS|FAIL …`，这里按同一套规则收
$lints = @(
    @{ n = "lint:comments";      f = "lint-comments.ps1";      mark = "LINT-COMMENTS" },
    @{ n = "lint:current-docs";  f = "lint-current-docs.ps1";  mark = "LINT-DOCS" },
    @{ n = "lint:comment-parser"; f = "test-comment-guard.ps1"; mark = "COMMENT-GUARD" },
    @{ n = "lint:motion-tokens"; f = "lint-motion-tokens.ps1"; mark = "LINT-MOTION" },
    @{ n = "lint:copy-parens";   f = "lint-copy-parens.ps1";   mark = "LINT-COPY" },
    @{ n = "lint:i18n-coverage"; f = "lint-i18n-coverage.ps1"; mark = "LINT-I18N" },
    @{ n = "lint:setup-paths";   f = "lint-setup-paths.ps1";   mark = "LINT-SETUP-PATHS" }
)
$lintJobs = [ordered]@{}
foreach ($t in $lints) {
    $lintJobs[$t.n] = Start-Async $t.n "pwsh" @("-NoProfile", "-File", (Join-Path $PSScriptRoot $t.f), "-Quiet")
}

# 1) 只杀掉从构建输出目录跑起来的实例：它们锁住 bin 下的文件，MSB3027 会让构建静默失败。
#    用户自己那份跑在 release\ 或安装目录里，与构建产物不是同一个文件，不必动它。
$srcRoot = Join-Path $root 'src'
$lockingInstances = @(Get-Process FrameCapture -EA SilentlyContinue | Where-Object {
    try { $_.Path -and $_.Path.StartsWith($srcRoot, [StringComparison]::OrdinalIgnoreCase) } catch { $false }
})
$hadUserInstance = (@(Get-Process FrameCapture -EA SilentlyContinue).Count - $lockingInstances.Count) -gt 0
foreach ($p in $lockingInstances) { Stop-Process -Id $p.Id -Force -EA SilentlyContinue }
if ($lockingInstances.Count) { Start-Sleep 0.6 }

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
    $ok = ($r.Code -eq 0 -and $nErr -eq 0 -and $missing.Count -eq 0)
    Add-Result $k $(if ($ok) { "PASS" } else { "FAIL" }) ("errors={0}{1}" -f $nErr,
        $(if ($missing.Count) { " 缺产物: " + (($missing | ForEach-Object { Split-Path $_ -Leaf }) -join ',') } else { "" })) $r.Sec
    if (-not $ok) { Say $r.Text; $okBuild = $false }
}

# SelfTest 与安装器文件探针并行；批量建窗审计在动画采样结束后运行。
$auditJob = $null
$spJob = $null
if (-not $Quick) {
    $spJob = Start-Async "setup-probe" "pwsh" @("-NoProfile", "-Command",
        "dotnet build '$(Join-Path $root "tools/SetupProbe")' -c Debug -v q --nologo; & '$(Join-Path $root "tools/SetupProbe/bin/Debug/net48/SetupProbe.exe")'")
}
$stJob = if ($okBuild -and -not $Quick) { Start-Async "selftest" $stExe @() } else { $null }

# 3.1) SelfTest：全过且不少于基线数。用例名还要两两不同——重名的两条只看得见一条
if ($stJob) {
    $r = Wait-Async $stJob
    $so = $r.Text
    Set-Content -LiteralPath (Join-Path $outDir 'selftest-last-run.txt') -Value $so -Encoding utf8
    $stSec = $r.Sec
    $m = [regex]::Match($so, 'RESULT PASS=(\d+) FAIL=(\d+)')
    if ($m.Success) {
        $p = [int]$m.Groups[1].Value; $f = [int]$m.Groups[2].Value
        $ok = ($r.Code -eq 0) -and ($f -eq 0) -and ($p -ge $MinSelfTestPass)
        Add-Result "selftest" $(if ($ok) { "PASS" } else { "FAIL" }) "PASS=$p FAIL=$f (floor $MinSelfTestPass)" $stSec
        if (-not $ok) { Say (($so -split "`n" | Select-String '^FAIL' | Select-Object -First 12) -join "`n") }
        $names = ([regex]::Matches($so, '(?m)^(?:PASS|FAIL) (\S+)') | ForEach-Object { $_.Groups[1].Value })
        $dup = @($names | Group-Object | Where-Object Count -gt 1)
        Add-Result "selftest:names-unique" $(if ($dup.Count -eq 0 -and $names.Count -eq $p + $f) { "PASS" } else { "FAIL" }) `
            ("{0} 条用例，重名 {1}" -f $names.Count, $dup.Count) 0
        if ($dup.Count) { Say (($dup | ForEach-Object { "  重名 {0} ×{1}" -f $_.Name, $_.Count }) -join "`n") }
    } else { Add-Result "selftest" "FAIL" "无 RESULT 行" $stSec; Say (($so -split "`n" | Select-Object -Last 12) -join "`n") }
} elseif ($Quick) { Add-Result "selftest" "SKIP" "-Quick（只扫静态与构建）"; Add-Result "selftest:names-unique" "SKIP" "-Quick" } else { Add-Result "selftest" "SKIP" "构建失败"; Add-Result "selftest:names-unique" "SKIP" "构建失败" }

# 动画采样与批量建窗审计分开运行，避免渲染负载改变采样时刻。
if (-not $Quick) { $auditJob = Start-Async "ux-audit" "pwsh" @("-NoProfile", "-File", (Join-Path $PSScriptRoot "ux-audit.ps1"), "-Quiet") }

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
#      只在仓库内与临时目录写文件，不碰注册表，也不动已装程序。结果在 SelfTest 跑的时候就收好了。
$spR = if ($spJob) { Wait-Async $spJob } else { $null }
$spOut = $spR.Text
if (-not $spR) { Add-Result "probe:setup-paths" "SKIP" "-Quick" }
elseif ($spOut -match 'SETUP-PROBE') {
    $okSp = $spOut -match 'SETUP-PROBE PASS'
    Add-Result "probe:setup-paths" $(if ($okSp) { "PASS" } else { "FAIL" }) `
        ("{0} 项搬运断言" -f ([regex]::Matches($spOut, '(?m)^\s*(?:PASS|FAIL) ').Count)) $spR.Sec
    if (-not $okSp) { Say $spOut }
} else {
    Add-Result "probe:setup-paths" "FAIL" "探针构建失败或无产物" $spR.Sec
    Say $spOut
}

# 4.7) 双语版面审计：英文比中文长，按中文排的版面在英文下会把字挤掉或压成省略号；
#      英文界面里残留的中文（动态拼出来那种、静态扫描看不见的）也在这里抓。全程离屏建窗，不动屏幕。
$uxR = if ($auditJob) { Wait-Async $auditJob } else { $null }
$ux = if ($uxR) { $uxR.Text } else { "" }
$uxLine = ($ux -split "`r?`n" | Where-Object { $_ -match '^UX-AUDIT (PASS|FAIL)' } | Select-Object -Last 1)
if (-not $uxR) { Add-Result "lint:ux-bilingual" "SKIP" "-Quick" } elseif ($uxLine) {
    $stUx = if ($uxLine -match '^UX-AUDIT PASS') { "PASS" } else { "FAIL" }
    Add-Result "lint:ux-bilingual" $stUx (($uxLine -replace '^UX-AUDIT (PASS|FAIL)\s*', "").Trim()) $uxR.Sec
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
Remove-TestDirectory $fcDir
New-Item -ItemType Directory -Force -Path $fcDir | Out-Null
if (Test-Path $fcUser) { Copy-Item $fcUser (Join-Path $fcDir 'settings.json') -Force }
$env:FC_SETTINGS_DIR = $fcDir

# 冒烟要弹欢迎浮窗。默认档要静默：先在测试设置里关掉它。-Full 时照原样弹，
# 因为那一次本来就要看「首次运行会提示」这条路径。
if ($hadUserInstance) {
    Add-Result "smoke:startup" "SKIP" "用户实例在跑（单实例互斥，不替它再起一个）"
} elseif ($okBuild) {
    if (-not $Full) {
        $smokeSet = Join-Path $fcDir "settings.json"
        if (Test-Path $smokeSet) {
            $js = Get-Content $smokeSet -Raw | ConvertFrom-Json
            $js.StartupToast = $false
            ($js | ConvertTo-Json -Depth 12) | Set-Content $smokeSet -Encoding UTF8
        }
    }
    $t0 = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process $exe -WindowStyle Hidden -PassThru
    Start-Sleep 2.5
    $alive = Get-Process -Id $p.Id -EA SilentlyContinue
    $note = if ($alive) { "进程存活，托盘就绪" } else { "退出码 $($p.ExitCode)" }
    if ($alive) { Stop-Process -Id $p.Id -Force -EA SilentlyContinue }
    Add-Result "smoke:startup" $(if ($alive) { "PASS" } else { "FAIL" }) $note $t0.Elapsed.TotalSeconds
} else { Add-Result "smoke:startup" "SKIP" "构建失败" }

# 6) 控制器响应探针：--picker 让主实例走 BeginScopeSelection（与热键命中同一个入口），
#    它会在屏幕上开一扇全屏选区窗——默认档要静默，只在 -Full 跑。
if (-not $Full) {
    Add-Result "probe:controller-responds" "SKIP" "默认档不跑（会开全屏选区窗，发布前用 -Full）"
} elseif ($okBuild) {
    $t0 = [Diagnostics.Stopwatch]::StartNew()
    $env:FC_TEST_NO_FG = "1"
    Start-Process $exe -WindowStyle Hidden -ArgumentList '--picker' -PassThru | Out-Null
    # 窗口可能比两秒慢：轮询到 8 秒（判据不变：选区窗必须出现）
    $pkProbe = "not-found"
    $deadline = (Get-Date).AddSeconds(8)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 400
        $pkProbe = & $shot rect "FrameCapture Region" 2>$null
        if ("$pkProbe" -notmatch "not-found") { break }
    }
    Add-Result "probe:controller-responds" $(if ($pkProbe -notmatch "not-found") { "PASS" } else { "FAIL" }) $pkProbe $t0.Elapsed.TotalSeconds
    & $shot keyw "FrameCapture Region" 27 2>$null | Out-Null
    Start-Sleep 0.4
    Remove-Item Env:\FC_TEST_NO_FG -EA SilentlyContinue
    Get-Process FrameCapture -EA SilentlyContinue | Stop-Process -Force
} else { Add-Result "probe:controller-responds" "SKIP" "构建失败" }

# 7) e2e：每一项都要重启一次主程序（单实例是硬约束，串行跑不掉），合计约两分半。
#    平时改代码走默认档跳过它，发布前用 -Full 跑全套。
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
if (-not $Full) {
    foreach ($t in $e2e) { Add-Result $t.n "SKIP" "默认档不跑 e2e（发布前用 -Full）" }
} elseif ($SkipE2e) {
    # SKIP 名单就是 e2e 名单本身：报一份和实际不相符的清单，等于谎报没跑什么
    foreach ($t in $e2e) { Add-Result $t.n "SKIP" "-SkipE2e" }
} elseif ($okBuild) {
    # e2e 会改设置（主题、热键、手势配置都写在 settings.json）——那些写全落在上面起的临时目录里，
    # 用户那份碰不到。这里只保证一件事：中途抛异常也要记一条 FAIL，别让汇总看起来像全跑过。
    #
    # 先清场：任何还在跑的实例都会让脚本新起的那个变成「第二实例」——新进程把参数转发给旧进程就退出，
    # 旧进程又没带测试开关，于是每一项都表现为「编辑器没出现」。杀不掉的实例（多半是提权起的）
    # 会让整段 e2e 全部报错，而错因不在被测代码上：那种情况如实记 SKIP 并写明原因。
    Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
    $leftover = @(Get-Process FrameCapture -ErrorAction SilentlyContinue)
    if ($leftover.Count -gt 0) {
        $why = "有 " + $leftover.Count + " 个 FrameCapture 实例杀不掉（多半是提权起的），先退出它再跑 -Full"
        foreach ($t in $e2e) { Add-Result $t.n "SKIP" $why }
        $e2e = @()
    }
    try {
        foreach ($t in $e2e) {
            # 焦点安全是全套里唯一必须真注入 OS 级按键的一项：注入期间 Ctrl 真的会被按住约 900ms，
            # 用户此刻滚一下滚轮，浏览器就跟着缩放。安静窗口只保证「过去 3 秒没输入」，
            # 预测不了下一秒——所以默认不跑，显式 -AllowInjection 才跑，跑了也还要再过一道闸。
            if ($t.gated) {
                if (-not $AllowInjection) { Add-Result $t.n "SKIP" "需 -AllowInjection（会注入 OS 级按键）"; continue }
                # 闸跳过时必须带上当场量到的键位：只写「用户持续有输入」是把结论当证据，
                # 机器空着而某个键状态卡住时，这句话会冤枉人。
                if (-not (Wait-FcQuiet -TimeoutSec 25 -QuietMs 3000)) { Add-Result $t.n "SKIP" ("等不到安静窗口：" + $script:FcQuietDiag); continue }
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
            # SKIP 也要把子项自己说的那句话带上来：只剩一个「SKIP」，看结果的人只能猜，
            # 而猜出来的原因通常按到别人头上。
            $note = if ($status -eq "FAIL") { "exit={0}; {1}" -f $r.Code, (($lines | Select-Object -Last 3) -join " / ") }
                    elseif ($status -eq "SKIP") { (($lines | Select-String 'SKIP' | Select-Object -First 1 | ForEach-Object { $_.Line.Trim() }) -join " / ") }
                    else { "" }
            # 显式要求跑注入项（-AllowInjection）却没跑成，按 FAIL 处理：
            # 显式要求的验证必须实际执行。
            if ($t.gated -and $AllowInjection -and $status -eq "SKIP") {
                $note = "显式要求注入却仍跳过：" + $note
                $status = "FAIL"
            }
            Add-Result $t.n $status $note $r.Sec
            if ($o -match "capture_hotkey|restored_capture") { Say (($lines | Select-String "hotkey=" | Out-String).Trim()) }
        }
    } catch {
        # 一条脚本抛到这儿，整套 e2e 就此中断：如实记一条 FAIL，别让汇总看起来像全跑过
        Add-Result "e2e:phase" "FAIL" ("e2e 阶段抛出：" + $_.Exception.Message) 0
    }
} else { foreach ($t in $e2e) { Add-Result $t.n "SKIP" "构建失败" } }

# 汇总
$failN = @($results | Where-Object Status -eq "FAIL").Count
$skipN = @($results | Where-Object Status -eq "SKIP").Count
Say ""
Say ("GUARDRAIL {0}  (FAIL={1} SKIP={2} 共 {3} 项)" -f $(if ($failN -eq 0) { "PASS" } else { "FAIL" }), $failN, $skipN, $results.Count)
# 使用显式值写入，空结果也覆盖输出文件。
Set-Content $transcript -Encoding UTF8 -Value $(if ($logLines.Count) { $logLines } else { @("（无日志行）") })
Remove-TestDirectory $tmp
exit $(if ($failN -eq 0) { 0 } else { 1 })

} finally {
    foreach ($name in $savedEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name])
    }
    if ($lockingInstances.Count) { Start-Process $exe -WindowStyle Hidden }
}
