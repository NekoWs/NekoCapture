<#
.SYNOPSIS
    一键产出 FrameCapture 发行安装包。
.DESCRIPTION
    读 csproj 版本号 → 验证当前代码 → 发布两种运行时形态 → 各编一个 per-machine MSI → 报体积与 SHA256。
    版本号唯一来源是 src/FrameCapture/FrameCapture.csproj 的 <Version>，脚本不另存一份。
    工具链钉在 WiX v5（见 dotnet-tools.json，rollForward=false）：v6 起官方二进制挂 OSMF 维护费 EULA。
.EXAMPLE
    pwsh -NoProfile -File scripts/publish-release.ps1
    pwsh -NoProfile -File scripts/publish-release.ps1 -Publisher "你的名字" -Flavor self
    pwsh -NoProfile -File scripts/publish-release.ps1 -SkipVerify   # 只重打包，不重跑验证
#>
param(
    [string]$Publisher = 'FrameCapture',
    [ValidateSet('both', 'self', 'fd')] [string]$Flavor = 'both',
    [string]$OutDir = 'release',
    [switch]$SkipVerify
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Step($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }

# 版本号：MSI 只吃 major.minor.build 且每段 <= 65535，四段版本号在这里就拦下
$csproj = 'src\FrameCapture\FrameCapture.csproj'
$verLine = Select-String -Path $csproj -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
if (-not $verLine) { throw "$csproj 里没有 <Version>" }
$Version = $verLine.Matches[0].Groups[1].Value
if ($Version -notmatch '^\d+\.\d+(\.\d+)?$') { throw "版本号 $Version 不是 MSI 能接受的 major.minor[.build]" }
$parts = $Version.Split('.')
foreach ($p in $parts) { if ([int]$p -gt 65535) { throw "版本号 $Version 的段 $p 超出 65535" } }
if ($parts.Count -eq 2) { $Version = "$Version.0" }
Write-Host "版本 $Version   发布者 $Publisher   形态 $Flavor"

# 构建前必须停掉运行中的实例，否则 exe 被占用
Step '停止运行中的实例'
$wasRunning = @(Get-Process FrameCapture -ErrorAction SilentlyContinue).Count -gt 0
Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force
if ($wasRunning) { Start-Sleep -Milliseconds 800; Write-Host '  已停止' } else { Write-Host '  本来就没在跑' }

if (-not $SkipVerify) {
    Step '验证：编译 + 断言 + 护栏'
    $b = dotnet build FrameCapture.slnx -c Debug -v q --nologo 2>&1 | Out-String
    $err = @($b -split "`n" | ForEach-Object{ $_.Trim() } | Where-Object{ $_ -match ': error' })
    if ($err.Count) { $err | ForEach-Object{ Write-Host "  $_" -ForegroundColor Red }; throw '编译有错误，不出包' }
    $warn = @($b -split "`n" | ForEach-Object{ $_.Trim() } | Where-Object{ $_ -match ': warning ' -and $_ -notmatch 'NU1900' } | Select-Object -Unique)
    if ($warn.Count) { $warn | ForEach-Object{ Write-Host "  $_" -ForegroundColor Yellow } }

    $selfTest = 'tools\SelfTest\bin\x64\Debug\net8.0-windows10.0.19041.0\SelfTest.exe'
    if (-not (Test-Path $selfTest)) { throw "找不到 $selfTest" }
    $t = & $selfTest 2>&1 | Out-String
    $line = ($t -split "`n" | Where-Object{ $_ -match 'RESULT' } | Select-Object -First 1)
    if (-not $line) { throw 'SelfTest 没给出 RESULT' }
    Write-Host "  $($line.Trim())"
    if ($line -notmatch 'FAIL=0') { throw '断言未全绿，不出包' }

    $g = pwsh -NoProfile -File scripts/guardrail.ps1 2>&1 | Out-String
    $gLast = ($g -split "`n" | Where-Object{ $_.Trim() } | Select-Object -Last 1)
    Write-Host "  $($gLast.Trim())"
    if ($gLast -notmatch 'PASS') { throw '护栏未通过，不出包' }
}

# 发布必须从干净状态构建：apphost 由 CreateAppHost 生成后带自己的图标，改 <ApplicationIcon>
# 不会让它重新生成，增量发布出来的 exe 会沿用中间产物里那枚（实测 148 KB 无图标 / 186 KB 有图标）。
# 图标进不进包取决于构建缓存，这种不确定性不能带到发行物里。
Step '清理 Release 中间产物'
$cleaned = 0
foreach ($proj in @(Get-ChildItem $root\src -Directory)) {
    foreach ($sub in 'obj', 'bin') {
        $d = Join-Path $proj.FullName $sub
        if (-not (Test-Path $d)) { continue }
        foreach ($rel in @(Get-ChildItem $d -Directory -Recurse -Filter 'Release')) {
            if (-not (Test-Path $rel.FullName)) { continue }
            Remove-Item $rel.FullName -Recurse -Force
            $cleaned++
        }
    }
}
Write-Host "  已清理 $cleaned 个 Release 目录"

# 工具链：本地工具按 dotnet-tools.json 钉版本还原，UI 扩展缺了才补
Step '工具链'
dotnet tool restore | Out-Null
$wixVer = (Get-Content dotnet-tools.json -Raw | ConvertFrom-Json).tools.wix.version
$extRef = "WixToolset.UI.wixext/$wixVer"
$cached = dotnet wix extension list -g 2>&1 | Out-String
if ($cached -notmatch "WixToolset\.UI\.wixext\s+$([regex]::Escape($wixVer))") {
    Write-Host "  缓存 UI 扩展 $extRef"
    dotnet wix extension add -g $extRef | Out-Null
} else { Write-Host "  UI 扩展已就位" }
Write-Host "  WiX $(dotnet wix --version)"

$icon = Join-Path $root 'assets\FrameCapture.ico'
if (-not (Test-Path $icon)) { throw "缺少图标 $icon，先跑 python scripts/make-icon.py" }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$variants = @()
if ($Flavor -in 'both', 'self') { $variants += @{ Key = 'self'; SelfContained = 'true'; Suffix = 'self-contained' } }
if ($Flavor -in 'both', 'fd')   { $variants += @{ Key = 'fd';   SelfContained = 'false'; Suffix = 'framework-dependent' } }

$results = @()
foreach ($v in $variants) {
    $stage = Join-Path $OutDir "staging\$($v.Key)"
    Step "发布 $($v.Suffix)"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    $p = dotnet publish $csproj -c Release -r win-x64 --self-contained $v.SelfContained `
         -p:Platform=x64 -o $stage -v q --nologo 2>&1 | Out-String
    $perr = @($p -split "`n" | ForEach-Object{ $_.Trim() } | Where-Object{ $_ -match ': error' })
    if ($perr.Count) { $perr | ForEach-Object{ Write-Host "  $_" -ForegroundColor Red }; throw "发布 $($v.Suffix) 失败" }
    $rawSize = (Get-ChildItem $stage -Recurse -File | Measure-Object Length -Sum).Sum
    Write-Host ("  发布目录 {0:N1} MB / {1} 个文件" -f ($rawSize / 1MB), @(Get-ChildItem $stage -Recurse -File).Count)

    $msi = Join-Path $OutDir "FrameCapture-$Version-x64-$($v.Suffix).msi"
    if (Test-Path $msi) { Remove-Item $msi -Force }
    $w = dotnet wix build packaging\FrameCapture.wxs `
         -d "PublishDir=$(Resolve-Path $stage)" `
         -d "Version=$Version" `
         -d "Icon=$icon" `
         -d "Publisher=$Publisher" `
         -ext $extRef `
         -intermediatefolder (Join-Path $OutDir "wixobj\$($v.Key)") `
         -o $msi 2>&1 | Out-String
    $w -split "`n" | ForEach-Object{ $_.Trim() } | Where-Object{ $_ } | ForEach-Object{ Write-Host "  $_" }
    if (-not (Test-Path $msi)) { throw "WiX 未产出 $msi" }

    $results += [pscustomobject]@{
        包     = Split-Path $msi -Leaf
        体积MB = [math]::Round((Get-Item $msi).Length / 1MB, 1)
        SHA256 = (Get-FileHash $msi -Algorithm SHA256).Hash.Substring(0, 16).ToLower()
    }
}

Step '产物'
$results | Format-Table -AutoSize | Out-String -Width 200 | ForEach-Object{ $_ -split "`n" } | Where-Object{ $_.Trim() } | ForEach-Object{ "  $_" }
Write-Host "  完整哈希：Get-FileHash release\*.msi -Algorithm SHA256"
if ($wasRunning) {
    $exe = 'src\FrameCapture\bin\x64\Debug\net8.0-windows10.0.19041.0\FrameCapture.exe'
    if (Test-Path $exe) { Start-Process (Join-Path $root $exe); Write-Host '  已把开发实例重新拉起' }
}
