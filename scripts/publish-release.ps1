<#
.SYNOPSIS
    一键产出 FrameCapture 发行安装包。
.DESCRIPTION
    读 csproj 版本号 → 验证当前代码 → 发布两种运行时形态 → 产出引导器 exe 与离线 zip → 报体积与 SHA256。
    版本号唯一来源是 src/FrameCapture/FrameCapture.csproj 的 <Version>，脚本不另存一份。
    引导器是 .NET Framework 4.8 工程，产物是免前置的单 exe；主程序需要 .NET 8 的原因是 WGC 捕获与 OCR。
.EXAMPLE
    pwsh -NoProfile -File scripts/publish-release.ps1
    pwsh -NoProfile -File scripts/publish-release.ps1 -Publisher "你的名字" -Flavor self
    pwsh -NoProfile -File scripts/publish-release.ps1 -SkipVerify   # 只重打包，不重跑验证
    pwsh -NoProfile -File scripts/publish-release.ps1 -AllowInjection   # 人不在电脑旁时，连焦点安全一起跑
#>
param(
    [string]$Publisher = 'FrameCapture',
    [ValidateSet('both', 'self', 'fd')] [string]$Flavor = 'both',
    [string]$OutDir = 'release',
    [switch]$SkipVerify,
    # 焦点安全是唯一会注入 OS 级按键的回归项，默认跳过；确认无人使用电脑时再显式打开。
    [switch]$AllowInjection
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
$csproj = 'src\FrameCapture\FrameCapture.csproj'
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
$wasRunning = @(Get-Process FrameCapture -ErrorAction SilentlyContinue).Count -gt 0
Get-Process FrameCapture.Resident -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
if (@(Get-Process FrameCapture,FrameCapture.Resident -ErrorAction SilentlyContinue).Count) {
    Write-Host '  当前权限无法关闭的实例保留，文件占用由构建和发布报告'
} elseif ($wasRunning) { Start-Sleep -Milliseconds 800; Write-Host '  已停止' } else { Write-Host '  本来就没在跑' }

if (-not $SkipVerify) {
    Step '验证：编译 + 断言 + 护栏'
    $b = dotnet build FrameCapture.slnx -c Debug -v q --nologo 2>&1 | Out-String
    $buildCode = $LASTEXITCODE
    $err = @($b -split "`n" | ForEach-Object{ $_.Trim() } | Where-Object{ $_ -match ': error' })
    if ($buildCode -ne 0 -or $err.Count) { $err | ForEach-Object{ Write-Host "  $_" -ForegroundColor Red }; throw '编译有错误，不出包' }
    $warn = @($b -split "`n" | ForEach-Object{ $_.Trim() } | Where-Object{ $_ -match ': warning ' -and $_ -notmatch 'NU1900' } | Select-Object -Unique)
    if ($warn.Count) { $warn | ForEach-Object{ Write-Host "  $_" -ForegroundColor Yellow } }

    $selfTest = 'tools\SelfTest\bin\x64\Debug\net8.0-windows10.0.19041.0\SelfTest.exe'
    if (-not (Test-Path $selfTest)) { throw "找不到 $selfTest" }
    $t = & $selfTest 2>&1 | Out-String
    $testCode = $LASTEXITCODE
    $line = ($t -split "`n" | Where-Object{ $_ -match '^RESULT\s+PASS=\d+\s+FAIL=\d+\s*$' } | Select-Object -Last 1)
    if (-not $line) { throw 'SelfTest 没给出 RESULT' }
    Write-Host "  $($line.Trim())"
    if ($testCode -ne 0 -or $line.Trim() -notmatch '\bFAIL=0$') { throw '断言未全绿，不出包' }

    # 护栏默认路径零 OS 级输入，用户在场也能跑完；焦点安全那一项要注入，只有 -AllowInjection 才带上。
    $gArgs = @('-NoProfile', '-File', 'scripts/guardrail.ps1', '-Full')
    if ($AllowInjection) { $gArgs += '-AllowInjection' }
    $g = & pwsh @gArgs 2>&1 | Out-String
    $guardCode = $LASTEXITCODE
    $gLast = ($g -split "`n" | Where-Object{ $_.Trim() } | Select-Object -Last 1)
    Write-Host "  $($gLast.Trim())"
    if ($guardCode -ne 0 -or $gLast.Trim() -notmatch '^GUARDRAIL PASS\b') { throw '护栏未通过，不出包' }
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
            Remove-ReleaseDirectory $rel.FullName
            $cleaned++
        }
    }
}
Write-Host "  已清理 $cleaned 个 Release 目录"

# 引导器是 .NET Framework 4.8 工程，不需要任何外部打包工具链：
# 载荷以 zip 形式内嵌进 exe，运行时自己解到 Program Files。
$icon = Join-Path $root 'assets\FrameCapture.ico'
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
$residentExe = Join-Path $residentStage 'FrameCapture.Resident.exe'
if (-not (Test-Path -LiteralPath $residentExe)) { throw '缺少原生常驻组件' }
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
    $runtimeNotices = Join-Path $stage 'licenses/dotnet'
    New-Item -ItemType Directory -Force -Path $runtimeNotices | Out-Null
    foreach ($name in @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
        Copy-Item -LiteralPath (Join-Path $runtimePackage[0] $name) -Destination $runtimeNotices
    }
    $rawSize = (Get-ChildItem $stage -Recurse -File | Measure-Object Length -Sum).Sum
    Write-Host ("  发布目录 {0:N1} MB / {1} 个文件" -f ($rawSize / 1MB), @(Get-ChildItem $stage -Recurse -File).Count)

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
        $built = Join-Path $root 'src\Setup\bin\Release\net48\FrameCapture-Setup.exe'
        if (-not (Test-Path $built)) { throw "引导器未产出 $built" }

        $setupOut = Join-Path $OutDir "FrameCapture-Setup-$Version-x64.exe"
        if (Test-Path $setupOut) { Remove-Item $setupOut -Force }
        Copy-Item $built $setupOut
        Remove-Item $zip -Force
        $artifact = $setupOut
    } else {
        # 离线包：给不能联网装运行时的机器，体积大但开箱即用
        Step '打包离线包'
        $offline = Join-Path $OutDir "FrameCapture-$Version-x64-offline.zip"
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

# 离线包抽验：解出来真跑一次。字节对、体积对，不等于装上去能用。
# 单实例互斥会把新进程直接并给已在跑的托盘并让它秒退——所以这一步只在没有任何实例时做，
# 有实例时明确报「跳过」，避免把互斥当成包坏了。
Step '抽验离线包'
$offlineRow = @($results | Where-Object { $_.'包' -like '*offline.zip' } | Select-Object -First 1)
$live = @(Get-Process FrameCapture -ErrorAction SilentlyContinue).Count
if ($offlineRow.Count -eq 0) {
    Write-Host '  本轮没有离线包（只发了引导器），跳过'
} elseif ($live -gt 0) {
    Write-Host "  跳过：检测到 $live 个 FrameCapture 实例，单实例互斥会让抽验进程秒退" -ForegroundColor Yellow
} else {
    $smoke = Join-Path $root 'test-out\release-smoke'
    $smokeCfg = Join-Path $root 'test-out\release-smoke-settings'
    foreach ($d in @($smoke, $smokeCfg)) { Remove-ReleaseDirectory $d }
    New-Item -ItemType Directory -Path $smoke | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $root (Join-Path $OutDir $offlineRow[0].'包')), $smoke)
    $smokeExe = Join-Path $smoke 'FrameCapture.exe'
    if (-not (Test-Path $smokeExe)) { throw "离线包根目录里没有 FrameCapture.exe" }
    $env:FC_SETTINGS_DIR = $smokeCfg
    $env:FC_TEST_NO_FG = '1'
    try {
        $proc = Start-Process $smokeExe -WindowStyle Hidden -PassThru
        Start-Sleep -Seconds 6
        $up = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
        if (-not $up) { throw '离线包解出来起不来：6 秒内进程已退出' }
        $errLog = Join-Path $smokeCfg 'error.log'
        if (Test-Path $errLog) { throw "离线包启动即写 error.log：$errLog" }
        Write-Host ("  离线包抽验 PASS：{0} 个文件、进程存活、内存 {1:N0} MB、error.log 为空" -f `
            @(Get-ChildItem $smoke -Recurse -File).Count, ($up.WorkingSet64 / 1MB))
    } finally {
        Remove-Item Env:FC_SETTINGS_DIR, Env:FC_TEST_NO_FG -ErrorAction SilentlyContinue
        foreach ($instance in @(Get-Process FrameCapture -ErrorAction SilentlyContinue)) {
            try {
                $instance.Kill()
                if (-not $instance.WaitForExit(5000)) { throw '抽验进程未退出，保留目录供检查' }
            } finally { $instance.Dispose() }
        }
        foreach ($d in @($smoke, $smokeCfg)) { Remove-ReleaseDirectory $d }
    }
}

Step '构建在线视频组件安装器'
$codecBuild = dotnet build (Join-Path $root 'src/CodecSetup/CodecSetup.csproj') -c Release -p:Company=$Publisher -v q --nologo 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) { throw "视频组件安装器构建失败: $codecBuild" }
$codecOut = Join-Path $OutDir "FrameCapture-Codecs-Setup-$Version-x64.exe"
Copy-Item -LiteralPath (Join-Path $root 'src/CodecSetup/bin/Release/net48/FrameCapture-Codecs-Setup.exe') -Destination $codecOut -Force
$results += [pscustomobject]@{
    包 = Split-Path $codecOut -Leaf
    体积MB = [math]::Round((Get-Item $codecOut).Length / 1MB, 2)
    SHA256 = (Get-FileHash $codecOut -Algorithm SHA256).Hash.Substring(0, 16).ToLower()
}

Step '产物'
$results | Format-Table -AutoSize | Out-String -Width 200 | ForEach-Object{ $_ -split "`n" } | Where-Object{ $_.Trim() } | ForEach-Object{ "  $_" }
Write-Host "  完整哈希：Get-FileHash release\FrameCapture-*.exe, release\FrameCapture-*.zip -Algorithm SHA256"
if ($wasRunning) {
    $exe = 'src\FrameCapture\bin\x64\Debug\net8.0-windows10.0.19041.0\FrameCapture.exe'
    if (Test-Path $exe) { Start-Process (Join-Path $root $exe) -WindowStyle Hidden; Write-Host '  已把开发实例重新拉起' }
}
