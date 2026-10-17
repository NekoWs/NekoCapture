# 双语版面审计：把 SelfTest 的 uxaudit 模式按「界面 × 语言」铺成矩阵，汇总成一份报告。
#
# 查两类肉眼容易漏的缺陷：
#   裁字/省略号/裁行 —— 英文比中文长，按中文排的版面在英文下挤掉字；
#   漏翻 —— 英文界面里还印着中文（动态拼出来的文案静态扫描看不见）。
# 每个界面独立进程：一个界面建窗炸了不影响其余，单例窗口也不会串味。
#
# 用法: pwsh -NoProfile -File scripts/ux-audit.ps1 [-Surfaces settings,help] [-Out test-out/uxaudit/report.txt]
param(
    [string[]]$Surfaces = @(),
    [string]$Out = "test-out/uxaudit/report.txt",
    [switch]$Quiet
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "tools/SelfTest/bin/x64/Debug/net8.0-windows10.0.19041.0/SelfTest.exe"
if (-not (Test-Path $exe)) { throw "缺 SelfTest 产物：先构建 tools/SelfTest" }
if (-not $Surfaces.Count) { $Surfaces = @(& $exe uxaudit list | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }

$dir = Split-Path -Parent $Out
New-Item -ItemType Directory -Force -Path $dir | Out-Null

$rows = [System.Collections.Generic.List[string]]::new()
foreach ($s in $Surfaces) {
    foreach ($l in @("zh", "en")) {
        $o = & $exe uxaudit $l $s 2>&1 | Out-String
        $hit = @($o -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -like "UXA|*" })
        $scan = (@($o -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -like "UXS|*" }) | Select-Object -First 1) -replace '^UXS\|[^|]+\|', ''
        foreach ($h in $hit) { $rows.Add(($h -replace '^UXA\|', "UXA|$l|")) }
        if (-not $Quiet) { "{0,-16} {1}  缺陷 {2,-3} 扫到 {3}" -f $s, $l, $hit.Count, $scan }
    }
}

# 空管道不会截断文件：全绿那一轮若直接管道给 Set-Content，上一轮的缺陷清单会留在原地冒充本轮结果。
Set-Content -LiteralPath $Out -Encoding utf8 -Value $(if ($rows.Count) { $rows } else { @("（本轮无缺陷）") })
if (-not $Quiet)
{
    ""
    "合计 {0} 条 → {1}" -f $rows.Count, $Out
    $rows | Group-Object { ($_ -split '\|')[3] } | Sort-Object Count -Descending | ForEach-Object { "  {0,-8} {1}" -f $_.Name, $_.Count }
}
Write-Host ("UX-AUDIT {0}  FAIL={1} 组合={2}" -f `
    $(if ($rows.Count -eq 0) { "PASS" } else { "FAIL" }), $rows.Count, ($Surfaces.Count * 2))
if ($rows.Count -gt 0) { exit 1 } else { exit 0 }
