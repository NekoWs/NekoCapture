# 全站配色的双主题取证：编辑器与贴图窗各拍一张明暗对照。
# 这两扇是「压在图片上的界面」——工具条、画布四周、贴图描边都要在明暗两套下各自读得清。
# 就绪卡与各类提示条不在射程：它们几秒就走，探针等不到自己那扇窗，拍到的是屏幕上别的东西。
# 用法：pwsh -NoProfile -File scripts/cap-surfaces.ps1
param(
    [string]$OutDir = 'test-out/surfaces',
    [string]$SettingsDir = 'test-out/surface-settings'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$scratch = Join-Path $root $SettingsDir
$out = Join-Path $root $OutDir
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
New-Item -ItemType Directory -Force -Path $out | Out-Null

$exe = Join-Path $root 'src\NekoCapture\bin\x64\Debug\net8.0-windows10.0.19041.0\NekoCapture.exe'
if (-not (Test-Path $exe)) { throw "找不到 $exe" }
$cap = Join-Path $root 'tools\UICap\bin\Release\net8.0-windows\UICap.exe'
if (-not (Test-Path $cap)) { $cap = Join-Path $root 'tools\UICap\bin\Debug\net8.0-windows\UICap.exe' }

$user = Join-Path $env:APPDATA 'NekoCapture\settings.json'
if (Test-Path $user) { Copy-Item $user (Join-Path $scratch 'settings.json') -Force }

Add-Type -AssemblyName System.Drawing
# 一张有明暗两半的样图：压在图上的界面在两侧各被量一次
$sample = Join-Path $out 'sample.png'
$bmp = [System.Drawing.Bitmap]::new(520, 320)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.FillRectangle([System.Drawing.Brushes]::White, 0, 0, 260, 320)
$g.FillRectangle([System.Drawing.Brushes]::Black, 260, 0, 260, 320)
$g.FillRectangle([System.Drawing.Brushes]::Tomato, 40, 60, 180, 90)
$g.FillRectangle([System.Drawing.Brushes]::SteelBlue, 300, 170, 180, 90)
$g.DrawString('NekoCapture', ([System.Drawing.Font]::new('Segoe UI', 16)), ([System.Drawing.Brushes]::Gray), 30, 200)
$g.Dispose()
$bmp.Save($sample, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

if (-not ('RaiseWin' -as [type])) {
    Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public static class RaiseWin {
 [DllImport("user32.dll")] public static extern IntPtr FindWindow(string a,string b);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int cx,int cy,uint f);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 public static void Go(string title) {
   var h = FindWindow(null, title);
   if (h == IntPtr.Zero) return;
   SetForegroundWindow(h);
   SetWindowPos(h, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002);   // TOPMOST | NOMOVE | NOSIZE
 }
}
'@
}

foreach ($th in @('dark', 'light')) {
    Get-Process NekoCapture -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 400
    $env:NC_SETTINGS_DIR = $scratch
    $env:NC_THEME = $th
    foreach ($surf in @(@('editor', '--editor'), @('pin', '--pin'))) {
        $name = $surf[0]; $flag = $surf[1]
        $p = Start-Process $exe -ArgumentList @($flag, $sample) -PassThru
        try {
            $file = Join-Path $out "$name-$th.png"
            $found = $false
            for ($i = 0; $i -lt 40; $i++) {
                Start-Sleep -Milliseconds 250
                & $cap win "NekoCapture $($name.Substring(0,1).ToUpper())$($name.Substring(1))" (Join-Path $out 'wait.png') 2>$null | Out-Null
                if ($LASTEXITCODE -eq 0) { $found = $true; break }
            }
            if (-not $found) { Write-Host "SKIP $name-$th（没等到窗口）"; continue }
            Start-Sleep -Milliseconds 1200
            [void][RaiseWin]::Go("NekoCapture $($name.Substring(0,1).ToUpper())$($name.Substring(1))")
            Start-Sleep -Milliseconds 250
            & $cap region "NekoCapture $($name.Substring(0,1).ToUpper())$($name.Substring(1))" $file | Out-Null
            if ($LASTEXITCODE -ne 0) { Write-Host "FAIL $name-$th"; continue }
            Write-Host "OK $file"
        }
        finally {
            Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 300
        }
    }
}
Get-Process NekoCapture -ErrorAction SilentlyContinue | Stop-Process -Force
