# 配色审查取证：把设置窗开到指定分页，按明/暗主题各拍一张（桌面 BitBlt，Mica 背板可见）。
# 用法：pwsh -NoProfile -File scripts/cap-palette.ps1 -Page appearance -Themes dark,light
param(
    [string]$Page = 'appearance',
    [string[]]$Themes = @('dark', 'light'),
    [string]$OutDir = 'test-out/palette',
    [string]$SettingsDir = 'test-out/palette-settings',
    [string]$Lang = '',
    [switch]$FromRelease
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$scratch = Join-Path $root $SettingsDir
$out = Join-Path $root $OutDir
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
New-Item -ItemType Directory -Force -Path $out | Out-Null

$exeFlavor = if ($FromRelease) { 'x64\Release\net8.0-windows10.0.19041.0\win-x64' } else { 'x64\Debug\net8.0-windows10.0.19041.0' }
$exe = Join-Path $root "src\NekoCapture\bin\$exeFlavor\NekoCapture.exe"
if (-not (Test-Path $exe)) { throw "找不到 $exe" }
$cap = Join-Path $root 'tools\UICap\bin\Release\net8.0-windows\UICap.exe'
if (-not (Test-Path $cap)) { $cap = Join-Path $root 'tools\UICap\bin\Debug\net8.0-windows\UICap.exe' }

# 起点用用户那份设置（键位/强调色真实），但整份搬进 scratch，绝不写回用户目录
$user = Join-Path $env:APPDATA 'NekoCapture\settings.json'
if (Test-Path $user) { Copy-Item $user (Join-Path $scratch 'settings.json') -Force }
if ($Lang) {
    # 英文版面取证：只改 scratch 那份的语言，用户设置不动
    $f = Join-Path $scratch 'settings.json'
    $s = Get-Content $f -Raw | ConvertFrom-Json
    $s.UiLanguage = $Lang
    $s | ConvertTo-Json -Depth 40 | Set-Content $f -Encoding utf8
}

# 拍摄时将设置窗置于最前：桌面 BitBlt 拍的是屏幕合成，别的窗压在它上面就会拍进别人。
if (-not ('RaiseWin' -as [type])) {
    Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public static class RaiseWin {
 [DllImport("user32.dll")] public static extern IntPtr FindWindow(string a,string b);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int cx,int cy,uint f);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 public static bool Go(string title) {
   var h = FindWindow(null, title);
   if (h == IntPtr.Zero) return false;
   SetForegroundWindow(h);
   return SetWindowPos(h, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002);   // TOPMOST | NOMOVE | NOSIZE
 }
}
'@
}

foreach ($th in $Themes) {
    Get-Process NekoCapture -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 350
    $env:NC_SETTINGS_DIR = $scratch
    $env:NC_THEME = $th
    $env:NC_SETTINGS_PAGE = $Page
    $p = Start-Process $exe -ArgumentList '--settings' -PassThru
    try {
        $found = $false
        for ($i = 0; $i -lt 60; $i++) {
            Start-Sleep -Milliseconds 250
            $w = & $cap win 'NekoCapture Settings' (Join-Path $out "probe-$th.png") 2>$null
            if ($LASTEXITCODE -eq 0) { $found = $true; break }
        }
        if (-not $found) { throw "没等到设置窗（主题 $th）" }
        Start-Sleep -Milliseconds 900   # 等入场淡入与逐项错位落位
        [void][RaiseWin]::Go('NekoCapture Settings')
        Start-Sleep -Milliseconds 250
        $file = Join-Path $out "settings-$Page-$th.png"
        & $cap region 'NekoCapture Settings' $file
        if ($LASTEXITCODE -ne 0) { throw "取证失败（主题 $th）" }
        Write-Host "OK $file"
    }
    finally {
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 300
    }
}
Get-Process NekoCapture -ErrorAction SilentlyContinue | Stop-Process -Force
