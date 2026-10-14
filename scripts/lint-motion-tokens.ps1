# 动效时长护栏（GOAL.md 硬约束：动效必须引用 Fluent.Motion 令牌）
#
# 语义：
#   FAIL —— 动画时长写成裸数字。时长散落各处 = 全站节奏无法一次调准，改一处漏一处，
#           同一个语义还会长出两个值（hover 淡入 110、hover 淡出 160 这种）。
# 射程只认动画上下文：
#   1) 动效助手（AnimateColor/AnimateScale/FadeIn/FadeOut）的末位实参
#   2) new Duration(TimeSpan.FromMilliseconds(N))
#   3) 直接喂给动画构造函数的 TimeSpan.FromMilliseconds(N)
# DispatcherTimer.Interval 之类是逻辑定时器，不是动效，不在射程内。
# 令牌可以是宿主的 Fluent.Motion.*，也可以是自带令牌集（自包含库按同名对齐），行内含 Motion. 即视为合规。
# 豁免：行尾加  // lint-motion:ignore <一句理由>   —— 必须带理由。
#
# 用法: pwsh -NoProfile -File scripts/lint-motion-tokens.ps1 [-Paths a,b] [-Quiet]
param(
    [string[]]$Paths = @(),
    [switch]$Quiet
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ($Paths.Count -eq 0) { $Paths = @((Join-Path $root "src"), (Join-Path $root "tools"), (Join-Path $root "Program.cs")) }

$RxHelper   = '(?:AnimateColor|AnimateScale|FadeIn|FadeOut)\([^;\r\n]*?,\s*\d+\s*\)'
$RxDuration = 'Duration\(\s*(?:System\.Windows\.)?TimeSpan\.FromMilliseconds\(\s*\d+\s*\)'
$RxRawSpan  = 'TimeSpan\.FromMilliseconds\(\s*\d+\s*\)'
$RxTimer    = '\bInterval\b'
$RxToken    = '\bMotion\.'
$RxIgnore   = 'lint-motion:ignore'

$script:viol = [System.Collections.Generic.List[object]]::new()
$files = [System.Collections.Generic.List[string]]::new()

foreach ($p in $Paths) {
    if (-not (Test-Path $p)) { continue }
    $item = Get-Item $p
    $targets = if ($item.PSIsContainer) { @(Get-ChildItem -Path $p -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }) }
               else { @($item) }
    foreach ($f in $targets) {
        $files.Add($f.FullName)
        $rel = $f.FullName.Substring($root.Length + 1)
        $lines = @(Get-Content -LiteralPath $f.FullName -Encoding UTF8)
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $ln = $lines[$i]
            if ($ln -match $RxToken -or $ln -match $RxIgnore) { continue }
            $kind = $null
            if ($ln -match $RxHelper) { $kind = "动效助手时长写成裸数字" }
            elseif ($ln -match $RxDuration) { $kind = "Duration 时长写成裸数字" }
            elseif ($ln -match $RxRawSpan -and $ln -notmatch $RxTimer) { $kind = "动画 TimeSpan 时长写成裸数字" }
            if ($kind) { $script:viol.Add([pscustomobject]@{ Kind = $kind; File = $rel; Line = $i + 1; Text = $ln.Trim() }) }
        }
    }
}

if (-not $Quiet) {
    foreach ($g in ($script:viol | Group-Object Kind | Sort-Object { -$_.Count })) {
        Write-Host ("`n== {0}  ({1} 处) ==" -f $g.Name, $g.Count)
        foreach ($v in ($g.Group | Select-Object -First 20)) {
            Write-Host ("  [FAIL] {0}:{1}  {2}" -f $v.File, $v.Line, ($v.Text -replace '\s+', ' '))
        }
        if ($g.Count -gt 20) { Write-Host ("  ...另 {0} 处" -f ($g.Count - 20)) }
    }
}
Write-Host ("LINT-MOTION {0}  FAIL={1} 文件={2}" -f $(if ($script:viol.Count -eq 0) { "PASS" } else { "FAIL" }), $script:viol.Count, $files.Count)
if ($script:viol.Count -gt 0) { exit 1 } else { exit 0 }
