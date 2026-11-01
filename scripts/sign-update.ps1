param(
    [Parameter(Mandatory)] [string]$OutDir,
    [string]$Repository = 'NekoWs/NekoCapture',
    [string]$KeyPath
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = [xml](Get-Content -LiteralPath (Join-Path $root 'src/NekoCapture/NekoCapture.csproj') -Raw)
$version = [string]$project.Project.PropertyGroup.Version | Where-Object { $_ }
$name = "NekoCapture-Setup-$version-x64.exe"
$installer = Join-Path $OutDir $name
if (-not (Test-Path -LiteralPath $installer)) { throw "缺少安装包: $installer" }
$pem = if ($KeyPath) { [IO.File]::ReadAllText((Resolve-Path -LiteralPath $KeyPath)) } else { $env:UPDATE_SIGNING_KEY }
if ([string]::IsNullOrWhiteSpace($pem)) { throw '需要 UPDATE_SIGNING_KEY secret 或 -KeyPath 私钥路径' }
$rsa = [Security.Cryptography.RSA]::Create()
$trusted = [Security.Cryptography.RSA]::Create()
try {
    $rsa.ImportFromPem($pem)
    $trusted.ImportFromPem([IO.File]::ReadAllText((Join-Path $root 'assets/update-public.pem')))
    if (-not [Linq.Enumerable]::SequenceEqual[byte]($rsa.ExportSubjectPublicKeyInfo(), $trusted.ExportSubjectPublicKeyInfo())) {
        throw '签名密钥与客户端内置公钥不匹配'
    }
    $manifest = [ordered]@{
        schema = 1
        repository = $Repository
        version = $version
        installer = $name
        length = (Get-Item -LiteralPath $installer).Length
        sha256 = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
        publishedUtc = [DateTime]::UtcNow.ToString('O')
    }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($manifest | ConvertTo-Json -Compress))
    $signature = $rsa.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $envelope = [ordered]@{ manifest = [Convert]::ToBase64String($bytes); signature = [Convert]::ToBase64String($signature) }
    [IO.File]::WriteAllText((Join-Path $OutDir 'update.json'), ($envelope | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
    $artifacts = @($name, "NekoCapture-$version-x64-offline.zip")
    $sums = @(Get-ChildItem -LiteralPath $OutDir -File | Where-Object { $_.Name -in $artifacts } | Sort-Object Name | ForEach-Object {
        "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
    })
    [IO.File]::WriteAllLines((Join-Path $OutDir 'SHA256SUMS.txt'), $sums, [Text.UTF8Encoding]::new($false))
    Write-Host "已生成 $version 的签名更新清单"
} finally { $pem = $null; $rsa.Dispose(); $trusted.Dispose() }
