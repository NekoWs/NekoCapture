# 在隔离目录验证桌面启动、真实权限、参数与交接时序；-RequestElevation 显式请求 UAC。
param([switch]$RequestElevation, [switch]$ElevatedChild)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path $repo 'test-out/standard-handoff-validation'
$runtime = Join-Path $fixture 'runtime'
New-Item -ItemType Directory -Force -Path $runtime | Out-Null
$saved = @{}
foreach ($name in @('TEMP','TMP','FC_SETTINGS_DIR','FC_TEST_NO_FG','FC_NO_ELEVATE')) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name)
}
try {
    $env:TEMP = $runtime; $env:TMP = $runtime
    $env:FC_SETTINGS_DIR = Join-Path $fixture 'settings'
    $env:FC_TEST_NO_FG = '1'; $env:FC_NO_ELEVATE = '1'
    $log = Join-Path $fixture 'result.txt'
    if ($RequestElevation) {
        $shell = (Get-Command pwsh).Source
        $child = Start-Process -FilePath $shell -Verb RunAs -WindowStyle Hidden -Wait -PassThru `
            -ArgumentList @('-NoProfile','-File',('"' + $PSCommandPath + '"'),'-ElevatedChild')
        try { if ($child.ExitCode -ne 0) { throw "Elevated handoff validation failed: $($child.ExitCode)" } }
        finally { $child.Dispose() }
        Get-Content -LiteralPath $log
        return
    }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try { $elevated = ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) }
    finally { $identity.Dispose() }
    if ($ElevatedChild -and -not $elevated) { throw 'Validation parent is not elevated' }
    "PARENT_ELEVATED=$elevated" | Set-Content -LiteralPath $log -Encoding utf8
    $exe = Join-Path $repo 'tools/SelfTest/bin/x64/Debug/net8.0-windows10.0.19041.0/SelfTest.exe'
    & $exe standard-handoff >> $log 2>&1
    if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $log; exit 1 }
    Get-Content -LiteralPath $log
    if ($ElevatedChild) { exit 0 }
} finally {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
}
