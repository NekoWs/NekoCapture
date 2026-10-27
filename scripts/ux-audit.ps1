# 双语版面审计：把 SelfTest 的 uxaudit 模式按「界面 × 语言」铺成矩阵，汇总成一份报告。
#
# 查两类肉眼容易漏的缺陷：
#   裁字/省略号/裁行 —— 英文比中文长，按中文排的版面在英文下挤掉字；
#   漏翻 —— 英文界面里还印着中文（动态拼出来的文案静态扫描看不见）。
# 每个界面一个独立进程：一个界面建窗炸了不影响其余，单例窗口也不会串味。
# 这些进程彼此完全独立（各自建窗、各自退出、不碰共享文件），所以并发跑：
# 串行 56 次进程启动要 80 多秒，并发之后同样的组合数只要十几秒。
#
# 用法: pwsh -NoProfile -File scripts/ux-audit.ps1 [-Surfaces settings,help] [-Out test-out/uxaudit/report.txt] [-Throttle 8]
param(
    [string[]]$Surfaces = @(),
    [string]$Out = "test-out/uxaudit/report.txt",
    [switch]$Quiet,
    [int]$Throttle = 8
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "tools/SelfTest/bin/x64/Debug/net8.0-windows10.0.19041.0/SelfTest.exe"
if (-not (Test-Path $exe)) { throw "缺 SelfTest 产物：先构建 tools/SelfTest" }
if (-not $Surfaces.Count) { $Surfaces = @(& $exe uxaudit list | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }

$dir = Split-Path -Parent $Out
New-Item -ItemType Directory -Force -Path $dir | Out-Null

# 每一项跑完把输出写进自己的临时文件：并发时 stdout 会互相穿插，落文件才拼得回来。
$scratchRoot = [IO.Path]::GetFullPath((Join-Path $root 'test-out/uxaudit-runtime'))
$scratch = Join-Path $scratchRoot ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
$pairs = foreach ($s in $Surfaces) { foreach ($l in @("zh", "en")) { [pscustomobject]@{ Surface = $s; Lang = $l } } }

try
{
    $pairs | ForEach-Object -Parallel {
        $exe = $using:exe
        $scratch = $using:scratch
        $name = "$($_.Lang)-$($_.Surface)"
        $o = & $exe uxaudit $_.Lang $_.Surface 2>&1 | Out-String
        Set-Content -LiteralPath (Join-Path $scratch "$name.exit") -Value $LASTEXITCODE
        Set-Content -LiteralPath (Join-Path $scratch "$name.txt") -Value $o -Encoding utf8
    } -ThrottleLimit $Throttle | Out-Null

    $rows = [System.Collections.Generic.List[string]]::new()
    foreach ($p in $pairs)
    {
        $file = Join-Path $scratch ("$($p.Lang)-$($p.Surface).txt")
        $o = if (Test-Path $file) { Get-Content -LiteralPath $file -Raw } else { "" }
        $hit = @($o -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -like "UXA|*" })
        $scan = (@($o -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -like "UXS|*" }) | Select-Object -First 1) -replace '^UXS\|[^|]+\|', ''
        $code = Get-Content -LiteralPath (Join-Path $scratch ("$($p.Lang)-$($p.Surface).exit")) -ErrorAction SilentlyContinue
        if (-not $scan -or ($code -ne '0' -and $hit.Count -eq 0)) {
            $rows.Add("UXA|$($p.Lang)|$($p.Surface)|运行失败|exit=$code; missing scan or result")
        }
        foreach ($h in $hit) { $rows.Add(($h -replace '^UXA\|', "UXA|$($p.Lang)|")) }
        if (-not $Quiet) { "{0,-16} {1}  缺陷 {2,-3} 扫到 {3}" -f $p.Surface, $p.Lang, $hit.Count, $scan }
    }
}
finally {
    $resolved = [IO.Path]::GetFullPath($scratch)
    if (-not $resolved.StartsWith($scratchRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "审计目录越界: $resolved" }
    Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction SilentlyContinue
}

# 空报告也覆盖输出文件。
Set-Content -LiteralPath $Out -Encoding utf8 -Value $(if ($rows.Count) { $rows } else { @("（无缺陷）") })
if (-not $Quiet)
{
    ""
    "合计 {0} 条 → {1}" -f $rows.Count, $Out
    $rows | Group-Object { ($_ -split '\|')[3] } | Sort-Object Count -Descending | ForEach-Object { "  {0,-8} {1}" -f $_.Name, $_.Count }
}
Write-Host ("UX-AUDIT {0}  FAIL={1} 组合={2}" -f `
    $(if ($rows.Count -eq 0) { "PASS" } else { "FAIL" }), $rows.Count, ($pairs.Count))
if ($rows.Count -gt 0) { exit 1 } else { exit 0 }
