<#
.SYNOPSIS
    一键产出 NekoCapture 发行安装包。
.DESCRIPTION
    读 csproj 版本号 → 发布两种运行时形态 → 产出引导器 exe 与离线 zip → 报体积与 SHA256。
    版本号唯一来源是 src/NekoCapture/NekoCapture.csproj 的 <Version>，脚本不另存一份。
    引导器是 .NET Framework 4.8 工程，产物是免前置的单 exe；主程序需要 .NET 8 的原因是 WGC 捕获与 OCR。
.EXAMPLE
    pwsh -NoProfile -File scripts/publish-release.ps1
    pwsh -NoProfile -File scripts/publish-release.ps1 -Publisher "你的名字" -Flavor self
#>
param(
    [string]$Publisher = 'NekoCapture',
    [ValidateSet('both', 'self', 'fd')] [string]$Flavor = 'both',
    [string]$OutDir = 'release',
    [switch]$NoRestart
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Remove-ReleaseDirectory([string]$path) {
    $full = [IO.Path]::GetFullPath($path)
    $allowed = [IO.Path]::GetFullPath($root).TrimEnd('\') + '\'
    if (-not $full.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw "发布目录越界: $full" }
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force }
}
$resolvedOut = [IO.Path]::GetFullPath($OutDir)
if (-not $resolvedOut.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "输出目录必须位于仓库内: $resolvedOut" }

function Step($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }

# 版本号：程序集版本每段必须 <= 65535 且最多四段，不合规矩在这里就拦下
$csproj = 'src\NekoCapture\NekoCapture.csproj'
$verLine = Select-String -Path $csproj -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
if (-not $verLine) { throw "$csproj 里没有 <Version>" }
$Version = $verLine.Matches[0].Groups[1].Value
if ($Version -notmatch '^\d+\.\d+(\.\d+)?$') { throw "版本号 $Version 不是可用的 major.minor[.build]" }
$parts = $Version.Split('.')
foreach ($p in $parts) { if ([int]$p -gt 65535) { throw "版本号 $Version 的段 $p 超出 65535" } }
if ($parts.Count -eq 2) { $Version = "$Version.0" }
Write-Host "版本 $Version   发布者 $Publisher   形态 $Flavor"

# 构建前必须停掉运行中的实例，否则 exe 被占用
Step '停止运行中的实例'
$wasRunning = @(Get-Process NekoCapture -ErrorAction SilentlyContinue).Count -gt 0
Get-Process NekoCapture.Resident -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process NekoCapture -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
if (@(Get-Process NekoCapture,NekoCapture.Resident -ErrorAction SilentlyContinue).Count) {
    Write-Host '  当前权限无法关闭的实例保留，文件占用由构建和发布报告'
} elseif ($wasRunning) { Start-Sleep -Milliseconds 800; Write-Host '  已停止' } else { Write-Host '  本来就没在跑' }


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
            Remove-ReleaseDirectory $rel.FullName
            $cleaned++
        }
    }
}
Write-Host "  已清理 $cleaned 个 Release 目录"

# 引导器是 .NET Framework 4.8 工程，不需要任何外部打包工具链：
# 载荷以 zip 形式内嵌进 exe，运行时自己解到 Program Files。
$icon = Join-Path $root 'assets\NekoCapture.ico'
if (-not (Test-Path $icon)) { throw "缺少图标 $icon，先跑 python scripts/make-icon.py" }
$setupProj = Join-Path $root 'src\Setup\Setup.csproj'
if (-not (Test-Path $setupProj)) { throw "缺少引导器工程 $setupProj" }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$variants = @()
if ($Flavor -in 'both', 'self') { $variants += @{ Key = 'self'; SelfContained = 'true'; Suffix = 'self-contained' } }
if ($Flavor -in 'both', 'fd')   { $variants += @{ Key = 'fd';   SelfContained = 'false'; Suffix = 'framework-dependent' } }

$results = @()
Step '发布原生常驻组件'
$residentStage = Join-Path $OutDir 'staging/resident'
Remove-ReleaseDirectory $residentStage
$residentPublish = dotnet publish src/Resident/Resident.csproj -c Release -r win-x64 -o $residentStage -v q --nologo 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) { throw "原生常驻组件发布失败: $residentPublish" }
$residentExe = Join-Path $residentStage 'NekoCapture.Resident.exe'
if (-not (Test-Path -LiteralPath $residentExe)) { throw '缺少原生常驻组件' }
Step '发布原生 OCR 工作进程'
$ocrStage = Join-Path $OutDir 'staging/ocr-worker'
Remove-ReleaseDirectory $ocrStage
$ocrPublish = dotnet publish src/OcrWorker/OcrWorker.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -o $ocrStage -v q --nologo 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) { throw "OCR 工作进程发布失败: $ocrPublish" }
$ocrExe = Join-Path $ocrStage 'NekoCapture.OcrWorker.exe'
if (-not (Test-Path -LiteralPath $ocrExe)) { throw '缺少 OCR 工作进程' }
$residentAssets = Get-Content -LiteralPath 'src/Resident/obj/project.assets.json' -Raw | ConvertFrom-Json -AsHashtable
$runtimeDependency = @($residentAssets.project.frameworks.Values | ForEach-Object { $_.downloadDependencies } |
    Where-Object name -eq 'Microsoft.NETCore.App.Runtime.NativeAOT.win-x64' | Select-Object -First 1)
if (-not $runtimeDependency.Count) { throw '缺少 NativeAOT 运行库版本' }
$runtimeVersion = [regex]::Match($runtimeDependency[0].version, '\d+\.\d+\.\d+(?:-[\w.]+)?').Value
$runtimePackage = @($residentAssets.packageFolders.Keys | ForEach-Object {
    Join-Path $_ "microsoft.netcore.app.runtime.nativeaot.win-x64/$runtimeVersion"
} | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'LICENSE.TXT') } | Select-Object -First 1)
if (-not $runtimePackage.Count) { throw '缺少 NativeAOT 运行库许可文件' }
foreach ($v in $variants) {
    $stage = Join-Path $OutDir "staging\$($v.Key)"
    Step "发布 $($v.Suffix)"
    Remove-ReleaseDirectory $stage
    $p = dotnet publish $csproj -c Release -r win-x64 --self-contained $v.SelfContained `
         -p:Platform=x64 -o $stage -v q --nologo 2>&1 | Out-String
    $publishCode = $LASTEXITCODE
    $perr = @($p -split "`n" | ForEach-Object{ $_.Trim() } | Where-Object{ $_ -match ': error' })
    if ($publishCode -ne 0 -or $perr.Count) { $perr | ForEach-Object{ Write-Host "  $_" -ForegroundColor Red }; throw "发布 $($v.Suffix) 失败" }
    Copy-Item -LiteralPath $residentExe -Destination $stage
    $ocrDirectory = Join-Path $stage 'OcrWorker'
    New-Item -ItemType Directory -Force -Path $ocrDirectory | Out-Null
    Copy-Item -LiteralPath $ocrExe -Destination $ocrDirectory
    if (Get-ChildItem -LiteralPath $stage -Recurse -File | Where-Object { $_.Extension -eq '.onnx' -or $_.Name -like 'Sdcb.SimdPaddleOCR.Models.*' }) {
        throw 'OCR 模型只能按需下载，不得包含在主安装包中'
    }
    $runtimeNotices = Join-Path $stage 'licenses/dotnet'
    New-Item -ItemType Directory -Force -Path $runtimeNotices | Out-Null
    foreach ($name in @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
        Copy-Item -LiteralPath (Join-Path $runtimePackage[0] $name) -Destination $runtimeNotices
    }
    $rawSize = (Get-ChildItem $stage -Recurse -File | Measure-Object Length -Sum).Sum
    Write-Host ("  发布目录 {0:N1} MB / {1} 个文件" -f ($rawSize / 1MB), @(Get-ChildItem $stage -Recurse -File).Count)

    Step '发布配置升级程序'
    $upgrade = dotnet publish src/ConfigUpgrade/ConfigUpgrade.csproj -c Release -r win-x64 --self-contained $v.SelfContained -p:Platform=x64 -o $stage -v q --nologo 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "配置升级程序发布失败: $upgrade" }

    if ($v.Key -eq 'fd') {
        # 框架依赖形态是发行主体：载荷内嵌进引导器，用户只下一个 exe
        Step '打包引导器载荷'
        $zip = Join-Path $OutDir 'payload.zip'
        if (Test-Path $zip) { Remove-Item $zip -Force }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, 'Optimal', $false)
        Write-Host ("  载荷 {0:N1} MB / {1} 个文件 → {2:N1} MB" -f `
            ($rawSize / 1MB), @(Get-ChildItem $stage -Recurse -File).Count, ((Get-Item $zip).Length / 1MB))

        Step '构建引导器'
        # 必须传绝对路径：EmbeddedResource 的 Include 按工程目录解析，相对路径会被拼到 src\Setup 下
        $sb = dotnet build $setupProj -c Release -p:PayloadZip="$(Resolve-Path $zip)" -p:Company=$Publisher `
              -v q --nologo 2>&1 | Out-String
        $setupCode = $LASTEXITCODE
        $serr = @($sb -split "`n" | ForEach-Object{ $_.Trim() } | Where-Object{ $_ -match ': error' })
        if ($setupCode -ne 0 -or $serr.Count) { $serr | ForEach-Object{ Write-Host "  $_" -ForegroundColor Red }; throw '引导器构建失败' }
        $built = Join-Path $root 'src\Setup\bin\Release\net48\NekoCapture-Setup.exe'
        if (-not (Test-Path $built)) { throw "引导器未产出 $built" }

        $setupOut = Join-Path $OutDir "NekoCapture-Setup-$Version-x64.exe"
        if (Test-Path $setupOut) { Remove-Item $setupOut -Force }
        Copy-Item $built $setupOut
        Remove-Item $zip -Force
        $artifact = $setupOut
    } else {
        # 离线包：给不能联网装运行时的机器，体积大但开箱即用
        Step '打包离线包'
        $offline = Join-Path $OutDir "NekoCapture-$Version-x64-offline.zip"
        if (Test-Path $offline) { Remove-Item $offline -Force }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::CreateFromDirectory($stage, $offline, 'Optimal', $false)
        Write-Host ("  离线包 {0:N1} MB / {1} 个文件 → {2:N1} MB" -f `
            ($rawSize / 1MB), @(Get-ChildItem $stage -Recurse -File).Count, ((Get-Item $offline).Length / 1MB))
        $artifact = $offline
    }

    $results += [pscustomobject]@{
        包     = Split-Path $artifact -Leaf
        体积MB = [math]::Round((Get-Item $artifact).Length / 1MB, 1)
        SHA256 = (Get-FileHash $artifact -Algorithm SHA256).Hash.Substring(0, 16).ToLower()
    }
}

Step '产物'
$results | Format-Table -AutoSize | Out-String -Width 200 | ForEach-Object{ $_ -split "`n" } | Where-Object{ $_.Trim() } | ForEach-Object{ "  $_" }
Write-Host "  完整哈希：Get-FileHash release\NekoCapture-*.exe, release\NekoCapture-*.zip -Algorithm SHA256"
if ($wasRunning -and -not $NoRestart) {
    $exe = 'src\NekoCapture\bin\x64\Debug\net8.0-windows10.0.19041.0\NekoCapture.exe'
    if (Test-Path $exe) { Start-Process (Join-Path $root $exe) -WindowStyle Hidden; Write-Host '  已把开发实例重新拉起' }
}