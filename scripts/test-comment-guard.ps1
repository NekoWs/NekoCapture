# 校验位置式 record 与方法文档的识别边界。
param([switch]$Quiet)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dir = Join-Path $root 'test-out/comment-guard'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$cases = @(
    @{ Name = 'record'; Pass = $true; Source = '/// <summary>数据值。</summary>
/// <param name="Value">数值。</param>
public readonly record struct Data(int Value);' },
    @{ Name = 'method'; Pass = $false; Source = '/// <summary>查询数据。</summary>
public static int Read() => 1;' },
    @{ Name = 'narrative'; Pass = $false; Source = '// 本轮完成字段调整' }
)
$failed = 0
foreach ($case in $cases) {
    $path = Join-Path $dir ($case.Name + '.cs')
    [IO.File]::WriteAllText($path, $case.Source)
    $output = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'lint-comments.ps1') -Paths $path -RequireDocumentation -Quiet 2>&1 | Out-String
    $passed = $LASTEXITCODE -eq 0 -and $output -match 'LINT-COMMENTS PASS'
    if ($passed -ne $case.Pass) { $failed++; if (-not $Quiet) { Write-Host $output } }
}
Write-Host ("COMMENT-GUARD {0} FAIL={1} 用例={2}" -f $(if ($failed) { 'FAIL' } else { 'PASS' }), $failed, $cases.Count)
if ($failed) { exit 1 }
