# 换色遮罩取证：真进程里把主题从暗翻到亮，连拍一串帧，逐帧量窗口区域的平均亮度。
# 深浅互换时设置窗盖一层退场侧的窗口底再淡出，亮度序列里就会出现介于两档之间的中间帧；
# 没有遮罩的话，序列是从暗档直接跳到亮档，中间不留过渡值。
# 用法：pwsh -NoProfile -File scripts/cap-mask.ps1
param(
    [string]$OutDir = 'test-out/mask',
    [string]$SettingsDir = 'test-out/mask-settings',
    [int]$Frames = 34,
    [int]$GapMs = 25
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$scratch = Join-Path $root $SettingsDir
$out = Join-Path $root $OutDir
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
New-Item -ItemType Directory -Force -Path $out | Out-Null
Remove-Item (Join-Path $out '*.png') -Force -ErrorAction SilentlyContinue

$exe = Join-Path $root 'src\FrameCapture\bin\x64\Debug\net8.0-windows10.0.19041.0\FrameCapture.exe'
if (-not (Test-Path $exe)) { throw "找不到 $exe" }
$cap = Join-Path $root 'tools\UICap\bin\Release\net8.0-windows\UICap.exe'
if (-not (Test-Path $cap)) { $cap = Join-Path $root 'tools\UICap\bin\Debug\net8.0-windows\UICap.exe' }

# 起点用用户那份设置，整份搬进 scratch，绝不写回用户目录
$user = Join-Path $env:APPDATA 'FrameCapture\settings.json'
$scratchFile = Join-Path $scratch 'settings.json'
if (Test-Path $user) { Copy-Item $user $scratchFile -Force }
$set = Get-Content $scratchFile -Raw | ConvertFrom-Json
$set.Theme = 'dark'
$set | ConvertTo-Json -Depth 40 | Set-Content $scratchFile -Encoding utf8

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
Add-Type -AssemblyName System.Drawing

# 整窗区域的平均亮度：遮罩在场时这块面被旧底色压着，读数落在两档之间
function Get-Luma([string]$Path) {
    $bmp = [System.Drawing.Bitmap]::new($Path)
    try {
        $sum = 0.0; $n = 0
        for ($y = 4; $y -lt $bmp.Height; $y += 6) {
            for ($x = 4; $x -lt $bmp.Width; $x += 6) {
                $c = $bmp.GetPixel($x, $y)
                $sum += 0.2126 * $c.R + 0.7152 * $c.G + 0.0722 * $c.B
                $n++
            }
        }
        return [math]::Round($sum / [Math]::Max(1, $n), 1)
    } finally { $bmp.Dispose() }
}

Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 350
$env:FC_SETTINGS_DIR = $scratch
$env:FC_THEME = 'dark'
$env:FC_SETTINGS_PAGE = 'appearance'
$p = Start-Process $exe -ArgumentList '--settings' -PassThru
try {
    $found = $false
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 250
        & $cap win 'FrameCapture Settings' (Join-Path $out 'wait.png') 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { $found = $true; break }
    }
    if (-not $found) { throw '没等到设置窗' }
    Start-Sleep -Milliseconds 900
    [void][RaiseWin]::Go('FrameCapture Settings')
    Start-Sleep -Milliseconds 250

    $base = Join-Path $out 'mask-000-dark.png'
    & $cap region 'FrameCapture Settings' $base | Out-Null
    $dark = Get-Luma $base

    # 翻到亮档：进程按 settings.json 的外部改动热重载（500ms 防抖），遮罩在应用新调色板那一刻盖上
    $set.Theme = 'light'
    $set | ConvertTo-Json -Depth 40 | Set-Content $scratchFile -Encoding utf8

    $series = @($dark)
    for ($i = 1; $i -le $Frames; $i++) {
        Start-Sleep -Milliseconds $GapMs
        $f = Join-Path $out ("mask-{0:d3}.png" -f $i)
        & $cap region 'FrameCapture Settings' $f 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) { $series += -1; continue }
        $series += Get-Luma $f
    }
    $light = $series[-1]
    Write-Host ("暗档 {0} → 亮档 {1}" -f $dark, $light)
    Write-Host ("序列：" + ($series -join ' '))
    $lo = [Math]::Min($dark, $light) + 8
    $hi = [Math]::Max($dark, $light) - 8
    $mids = @($series | Where-Object { $_ -gt $lo -and $_ -lt $hi })
    Write-Host ("中间帧 {0} 张（两档之间 ±8 之内）" -f $mids.Count)
    if ($mids.Count -ge 2) { Write-Host 'MASK VISIBLE' } else { Write-Host 'NO MASK' }
}
finally {
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 300
}
Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force
