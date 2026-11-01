# 改色即时生效取证：真进程开着设置页时，从外部往 settings.json 写一枚自定义色，
# 看两件事——窗口句柄没换（没有重建窗口），卡片底色当场变了（共享画刷把颜色送到了在屏元素）。
# 用法：pwsh -NoProfile -File scripts/cap-live.ps1
param(
    [string]$OutDir = 'test-out/live',
    [string]$SettingsDir = 'test-out/live-settings',
    [string]$Token = 'dark.CardBg',
    [string]$Hex = '#3A5570'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$scratch = Join-Path $root $SettingsDir
$out = Join-Path $root $OutDir
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
New-Item -ItemType Directory -Force -Path $out | Out-Null
Remove-Item (Join-Path $out '*.png') -Force -ErrorAction SilentlyContinue

$exe = Join-Path $root 'src\NekoCapture\bin\x64\Debug\net8.0-windows10.0.19041.0\NekoCapture.exe'
if (-not (Test-Path $exe)) { throw "找不到 $exe" }
$cap = Join-Path $root 'tools\UICap\bin\Release\net8.0-windows\UICap.exe'
if (-not (Test-Path $cap)) { $cap = Join-Path $root 'tools\UICap\bin\Debug\net8.0-windows\UICap.exe' }

$user = Join-Path $env:APPDATA 'NekoCapture\settings.json'
$scratchFile = Join-Path $scratch 'settings.json'
if (Test-Path $user) { Copy-Item $user $scratchFile -Force }
$s = Get-Content $scratchFile -Raw | ConvertFrom-Json
$s.Theme = 'dark'
$s | ConvertTo-Json -Depth 40 | Set-Content $scratchFile -Encoding utf8

if (-not ('LiveWin' -as [type])) {
    Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public static class LiveWin {
 [DllImport("user32.dll")] public static extern IntPtr FindWindow(string a,string b);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int cx,int cy,uint f);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 public static IntPtr H(string title) { return FindWindow(null, title); }
 public static void Raise(string title) {
   var h = FindWindow(null, title);
   if (h == IntPtr.Zero) return;
   SetForegroundWindow(h);
   SetWindowPos(h, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002);   // TOPMOST | NOMOVE | NOSIZE
 }
}
'@
}
Add-Type -AssemblyName System.Drawing

# 卡片那一块的平均亮度：卡片底色被自定义色压住时会明显偏离出厂值
function Get-Luma([string]$Path, [int]$x0, [int]$y0, [int]$x1, [int]$y1) {
    $bmp = [System.Drawing.Bitmap]::new($Path)
    try {
        $sum = 0.0; $n = 0
        for ($y = $y0; $y -lt $y1; $y += 3) {
            for ($x = $x0; $x -lt $x1; $x += 3) {
                $c = $bmp.GetPixel($x, $y)
                $sum += 0.2126 * $c.R + 0.7152 * $c.G + 0.0722 * $c.B
                $n++
            }
        }
        return [math]::Round($sum / [Math]::Max(1, $n), 2)
    } finally { $bmp.Dispose() }
}

Get-Process NekoCapture -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 350
$env:NC_SETTINGS_DIR = $scratch
$env:NC_THEME = 'dark'
$env:NC_SETTINGS_PAGE = 'palette'
$p = Start-Process $exe -ArgumentList '--settings' -PassThru
try {
    $found = $false
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 250
        & $cap win 'NekoCapture Settings' (Join-Path $out 'wait.png') 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { $found = $true; break }
    }
    if (-not $found) { throw '没等到设置窗' }
    Start-Sleep -Milliseconds 900
    [void][LiveWin]::Raise('NekoCapture Settings')
    Start-Sleep -Milliseconds 250
    $h0 = [LiveWin]::H('NekoCapture Settings')

    $before = Join-Path $out 'before.png'
    & $cap region 'NekoCapture Settings' $before | Out-Null
    $l0 = Get-Luma $before 400 620 1080 900      # 令牌那一节的行区

    # 外部写一枚自定义色：进程按 watcher 热重载（500ms 防抖），界面应当当场跟上
    $s2 = Get-Content $scratchFile -Raw | ConvertFrom-Json
    if ($null -eq $s2.Colors) { $s2 | Add-Member -NotePropertyName Colors -NotePropertyValue ([pscustomobject]@{}) }
    $s2.Colors | Add-Member -NotePropertyName $Token -NotePropertyValue $Hex -Force
    $s2 | ConvertTo-Json -Depth 40 | Set-Content $scratchFile -Encoding utf8
    Start-Sleep -Milliseconds 1500

    $h1 = [LiveWin]::H('NekoCapture Settings')
    $after = Join-Path $out 'after.png'
    & $cap region 'NekoCapture Settings' $after | Out-Null
    $l1 = Get-Luma $after 400 620 1080 900

    Write-Host ("句柄 {0} → {1}（同 = 没有重建窗口）" -f $h0, $h1)
    Write-Host ("令牌区平均亮度 {0} → {1}（差 {2}）" -f $l0, $l1, [math]::Round([math]::Abs($l1 - $l0), 2))
    if ($h0 -eq $h1 -and [math]::Abs($l1 - $l0) -gt 1.5) { Write-Host 'LIVE APPLY OK' } else { Write-Host 'LIVE APPLY FAIL' }
}
finally {
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 300
}
Get-Process NekoCapture -ErrorAction SilentlyContinue | Stop-Process -Force
