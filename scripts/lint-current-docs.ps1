# 开发文档只保存当前契约、验证状态与待处理事项。
param([switch]$Quiet)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$files = @(Get-ChildItem -LiteralPath $root -File -Filter '*.md') +
    @(Get-ChildItem -LiteralPath (Join-Path $root 'docs') -File -Filter '*.md') +
    @(Get-Item -LiteralPath (Join-Path $root 'README.txt'))
$pattern = '本轮|这轮|这一轮|上一轮|上轮|第\s*\d+\s*轮|改前|改后(?!面)|原先|此前|之前|原来|曾经|退役|弃用|改为|修掉|教训|历史|\b[RT]\d{2,3}\b|\bRound\s+\d+'
$failures = @()
foreach ($file in $files) {
    $fenced = $false
    $number = 0
    foreach ($line in [IO.File]::ReadAllLines($file.FullName)) {
        $number++
        if ($line -match '^\s*```') { $fenced = -not $fenced; continue }
        if ($fenced) { continue }
        $prose = $line -replace '`[^`]*`', ''
        if ($prose -match $pattern) { $failures += "  FAIL $($file.Name):$number $line" }
    }
}
$scripts = @(Get-ChildItem -LiteralPath (Join-Path $root 'scripts') -Recurse -File -Filter '*.ps1')
foreach ($script in $scripts) {
    $tokens = $null
    [Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$null) | Out-Null
    foreach ($token in $tokens | Where-Object Kind -eq Comment) {
        $prose = $token.Text -replace '`[^`]*`', ''
        if ($prose -match $pattern) { $failures += "  FAIL $($script.Name):$($token.Extent.StartLineNumber) $($token.Text)" }
    }
}
if (-not $Quiet) { $failures | ForEach-Object { Write-Host $_ } }
Write-Host ("LINT-DOCS {0} FAIL={1} 文件={2}" -f $(if ($failures.Count) { 'FAIL' } else { 'PASS' }), $failures.Count, ($files.Count + $scripts.Count))
if ($failures.Count) { exit 1 }
