# 窗级淡入淡出的屏采探针：逐帧算窗口区域的亮度与边缘能量，用来判断斜坡是否真的落到屏幕。
#
# 为什么需要它：透明度动画只有 150–240ms，采样率决定结论可信度。落 PNG 的连拍每帧要编码 ~120ms，
# 实际只跑到 ~7fps，150ms 的斜坡会被量成一次阶跃，据此判定"淡入淡出不可见"是测量误差。
# 本脚本不落盘，CopyFromScreen 后直接在内存里算，实测 ~55fps，一个斜坡能采到 6–8 帧。
#
# 判据：亮度是区域均值，Mica 背板由 DWM 画、不参与窗口透明度，故淡出时亮度几乎不动；
#       边缘能量（相邻像素亮度差的均值）才反映内容结构，淡出应看到它平滑衰减到 0。
#
# 用法：
#   入场  pwsh -File scripts/probe-window-fade.ps1 -Exe <FrameCapture.exe> -ArgList '--settings'
#   退场  加 -CloseAfterMs 300 -Shot <Shot.exe>   （到点向窗口 PostMessage Esc，量淡出段）
# 主题切换：改 %APPDATA%\FrameCapture\settings.json 的 Theme 后重启程序，浅色与暗色各测一遍。
param(
    [string]$Title = 'FrameCapture Settings',
    [string]$Exe = '',
    [string]$ArgList = '--settings',
    [double]$Seconds = 1.0,
    [int]$CloseAfterMs = 0,
    [string]$Shot = '')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Text;using System.Runtime.InteropServices;using System.Collections.Generic;
public static class FadeProbe {
  public delegate bool EB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EB cb, IntPtr l);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
  public static string Find(string t) {
    var hit = new List<int>();
    EnumWindows((h,l) => {
      if (!IsWindowVisible(h)) return true;
      var sb = new StringBuilder(512); GetWindowTextW(h, sb, 512);
      if (sb.ToString() == t) { RECT r; GetWindowRect(h, out r); hit.Add(r.L); hit.Add(r.T); hit.Add(r.R); hit.Add(r.B); }
      return true; }, IntPtr.Zero);
    return hit.Count >= 4 ? string.Join(",", hit.ToArray()) : null; }
}
"@
# 物理像素采样：不按 DPI 感知取到的矩形会整体缩放，区域就套不准窗口
[void][FadeProbe]::SetProcessDpiAwarenessContext([IntPtr](-4))

function Measure-Rect([int]$L, [int]$T, [int]$W, [int]$H) {
    $bmp = New-Object System.Drawing.Bitmap($W, $H, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($L, $T, 0, 0, (New-Object System.Drawing.Size($W, $H)))
    $g.Dispose()
    $rc = New-Object System.Drawing.Rectangle(0, 0, $W, $H)
    $data = $bmp.LockBits($rc, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                          [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $buf = New-Object byte[] ($W * $H * 4)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $buf.Length)
    $bmp.UnlockBits($data)
    $sum = 0.0; $edge = 0.0; $n = 0
    for ($y = 8; $y -lt $H - 8; $y += 6) {
        $row = $y * $W * 4; $prev = -1.0
        for ($x = 8; $x -lt $W - 8; $x += 2) {
            $i = $row + $x * 4
            $l = 0.114 * $buf[$i] + 0.587 * $buf[$i + 1] + 0.299 * $buf[$i + 2]
            if ($prev -ge 0) { $edge += [Math]::Abs($l - $prev) }
            $prev = $l; $sum += $l; $n++ } }
    $bmp.Dispose()
    return , @(($sum / [Math]::Max(1, $n)), ($edge / [Math]::Max(1, $n)))
}

$null = Measure-Rect 400 400 700 460   # 预热：JIT 与首次分配会让头两帧偏慢
$t0 = [Diagnostics.Stopwatch]::GetTimestamp()
$null = Measure-Rect 400 400 700 460
$periodMs = ([Diagnostics.Stopwatch]::GetTimestamp() - $t0) / [Diagnostics.Stopwatch]::Frequency * 1000
"单帧耗时 = $([Math]::Round($periodMs,1)) ms  →  采样率 ≈ $([Math]::Round(1000/$periodMs,0)) fps"
if ($periodMs -gt 40) { throw "采样率不足（$([Math]::Round($periodMs,0)) ms/帧），量不出 150ms 级斜坡，结论无效" }

$rect = $null; $triggered = $false
$t0 = [Diagnostics.Stopwatch]::GetTimestamp()
$frames = New-Object System.Collections.Generic.List[object]
$k = 0
Start-Process -FilePath $Exe -ArgumentList $ArgList
while ($true) {
    $ms = ([Diagnostics.Stopwatch]::GetTimestamp() - $t0) / [Diagnostics.Stopwatch]::Frequency * 1000
    if ($ms -gt $Seconds * 1000) { break }
    if (-not $rect) {
        $rs = [string][FadeProbe]::Find($Title)
        if ($rs.Length -gt 3) {
            $parts = $rs.Split(',')
            $pl = [int]$parts[0]; $pt = [int]$parts[1]; $pr = [int]$parts[2]; $pb = [int]$parts[3]
            $rect = @(($pl + 60), ($pt + 60), [Math]::Min(700, ($pr - $pl) - 120), [Math]::Min(460, ($pb - $pt) - 120))
        }
    }
    if ($CloseAfterMs -gt 0 -and -not $triggered -and $ms -gt $CloseAfterMs) {
        $triggered = $true
        Start-Process -FilePath $Shot -ArgumentList 'keyw', "`"$Title`"", '0x1B' -NoNewWindow }
    $m = if ($rect) { Measure-Rect $rect[0] $rect[1] $rect[2] $rect[3] } else { Measure-Rect 900 500 700 460 }
    $frames.Add([pscustomobject]@{ K = $k; Ms = [Math]::Round($ms, 0)
        Lum = [Math]::Round($m[0], 1); Edge = [Math]::Round($m[1], 2); In = [bool]$rect })
    $k++
}
"采样帧数 = $($frames.Count)   关闭触发点 = $CloseAfterMs ms"
foreach ($f in $frames) {
    $n = 'f{0:D3}' -f $f.K; $tm = '{0,5}' -f $f.Ms; $lm = '{0,6}' -f $f.Lum; $eg = '{0,6}' -f $f.Edge
    "  $n  t=${tm}ms  亮度=$lm  边缘=$eg  窗内=$($f.In)" }

# 斜坡判定：从触发点到窗口消失（边缘能量向上跳变＝露出背后桌面结构）之间，
# 边缘能量出现 ≥3 个递减台阶即淡出在屏幕上逐帧生效。
if ($CloseAfterMs -gt 0) {
    $drops = 0; $prev = -1.0
    foreach ($f in @($frames | Where-Object { $_.In -and $_.Ms -ge $CloseAfterMs })) {
        if ($prev -ge 0 -and $f.Edge -gt $prev + 2) { break }   # 向上跳变＝窗已关，后面是桌面
        if ($prev -ge 0 -and $f.Edge -lt $prev - 0.15) { $drops++ }
        $prev = $f.Edge }
    if ($drops -ge 3) { "PROBE PASS 退场边缘能量递减 $drops 阶 → 淡出逐帧落屏" }
    else { "PROBE FAIL 退场边缘能量只递减 $drops 阶 → 未采到斜坡，检查采样率或动画是否真在跑" ; exit 1 }
}
