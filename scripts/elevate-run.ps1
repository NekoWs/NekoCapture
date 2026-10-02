# 提交已写好的队列脚本并等待结果。用法: elevate-run.ps1 -Name launchapp
param([Parameter(Mandatory=$true)][string]$Name, [int]$TimeoutSec=90)
$root = "D:\Backup\Capture\tools\elev"
$o = "$root\results\$Name.out"
Remove-Item $o -ErrorAction SilentlyContinue
$deadline=(Get-Date).AddSeconds($TimeoutSec)
while ((Get-Date) -lt $deadline) { if (Test-Path $o) { break }; Start-Sleep -Milliseconds 300 }
if (Test-Path $o) { Get-Content $o } else { "TIMEOUT waiting $o"; exit 2 }
