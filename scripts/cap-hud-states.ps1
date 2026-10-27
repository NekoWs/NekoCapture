# D9 取证：HUD 边界态双主题像素档案（UIAUDIT.md D9：导出进度 / 取消恢复 / 强制失败 / 丢帧警告）
# 机制（全部在自动化边界内：热键合成 hkm + 自家窗 PostMessage keyw + 桌面 BitBlt，绝不用 OS 级鼠标自动化）：
#   · 前提事实：静态桌面上 WGC 实采约 11fps（trace 实证 5s≈50 帧），60 帧编码仅 ~0.4s——短按既打不爆
#     帧预算（2.5GB）也来不及落在编码窗口里。故：脚本先把 settings.MaxDurationMs 临时抬到 26000
#     （备份→改→跑→还原，外部改文件由 AppHost 的 FileSystemWatcher 热加载，且实例读取配置后启动，无竞态），
#     并以测试专用开关 FC_TEST_RING_BUDGET_MB=2500 钉住帧预算（FrameRing，与 FC_TEST_NO_FG 同约定）；
#     长按 25s ≈ 270 帧 ×12.6MB ≈ 3.4GB > 预算 → 真丢帧；烘焙+编码 ≈ 3s → 进度/取消窗口稳定可拍。
#   · 丢帧警告「⚠内存预算已丢 N 帧」：trace 出现 "ring dropped"。
#   · 取消恢复：Enter 后 0.35s 投 Esc（编码中 Esc=取消编码而非关窗）→ trace "encode cancelled"，
#     断言编辑器仍存活 + HUD 恢复（截图为证）。
#   · 导出进度「导出 GIF… d/n」：取消后再按 Enter 重新起编码（同一素材），立刻 burstregion 连拍播放条，
#     像素差分（逐帧 vs 首帧）挑出 HUD 变化最大的代表帧。
#   · 导出失败：正常路径不可达（编码器对合法输入几乎不抛错）→ FC_TEST_ENCODE_FAIL=1 令编码任务必然抛错
#     （EditorWindow.StartEncode，同 FC_* 约定），trace "encode failed:"。
# trace 断言一律按"本阶段起始字节偏移"读增量，避免同一次运行内跨实例串档。
# 用法: pwsh -NoProfile -File scripts/cap-hud-states.ps1 -Theme dark|light
# 前置：杀掉运行中的 FrameCapture，按 FC_THEME=<Theme> FC_TEST_NO_FG=1 重启测试实例；结束时实例回收、设置还原。
param(
    [Parameter(Mandatory)][ValidateSet('dark', 'light')][string]$Theme,
    [string]$OutDir
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "lib-hotkey.ps1")
$exe   = Join-Path $root "src/FrameCapture/bin/x64/Debug/net8.0-windows10.0.19041.0/FrameCapture.exe"
$shot  = Join-Path $root "tools/Shot/bin/x64/Debug/net8.0-windows/Shot.exe"
$uicap = Join-Path $root "tools/UICap/bin/Release/net8.0-windows/UICap.exe"
$trace = Join-Path $root "src/fc-trace.log"
if (-not $OutDir) { $OutDir = Join-Path $root "test-out/uiaudit" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$capHk = Get-FcCaptureHotkey

# —— trace 增量读取（应用端持续追加：FileShare.ReadWrite 打开，只读本阶段新增字节）——
function Get-TraceLen { (Get-Item $trace).Length }
function Get-TraceSince {
    param([long]$from)
    $fs = [System.IO.File]::Open($trace, 'Open', 'Read', 'ReadWrite')
    try {
        [void]$fs.Seek($from, 'Begin')
        (New-Object System.IO.StreamReader($fs)).ReadToEnd()
    } finally { $fs.Dispose() }
}

# —— 像素差分选帧器：burst 帧里挑与首帧差异最大的（HUD 文本在动），并给出差异像素数 ——
if (-not ('FcHud.Pick' -as [type])) {
    try { Add-Type -AssemblyName System.Drawing.Common } catch { Add-Type -AssemblyName System.Drawing }
    $src = @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
namespace FcHud
{
public class Pick
{
    static BitmapData Lock(Bitmap b) { return b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb); }
    // 与首帧比较（隔 2px 采样）。悬浮条 SizeToContent：HUD 文本一变，条宽与整组包围盒都变
    // （实测编码中每帧尺寸都不同，逐帧"尺寸全等才比"会全军覆没）——故先取全序列最小公共尺寸，
    // 每帧按中心对齐裁到该尺寸再比，尺寸增长/原点漂移一并消掉。
    public static int MostDiff(string[] files, out int bestCount)
    {
        bestCount = -1;
        int best = -1;
        if (files.Length < 2) return best;
        int cw = int.MaxValue, ch = int.MaxValue;
        foreach (var f in files)
            using (var b = new Bitmap(f)) { if (b.Width < cw) cw = b.Width; if (b.Height < ch) ch = b.Height; }
        if (cw < 8 || ch < 8) return best;
        using (var c0 = CenterCrop(files[0], cw, ch))
        {
            var d0 = Lock(c0);
            for (int i = 1; i < files.Length; i++)
            {
                using (var c = CenterCrop(files[i], cw, ch))
                {
                    var d = Lock(c);
                    int diff = 0;
                    for (int y = 0; y < ch; y += 2)
                    {
                        IntPtr p0 = IntPtr.Add(d0.Scan0, y * d0.Stride), p = IntPtr.Add(d.Scan0, y * d.Stride);
                        for (int x = 0; x < cw; x += 2)
                        {
                            if (Marshal.ReadInt32(p0, x * 4) != Marshal.ReadInt32(p, x * 4)) diff++;
                        }
                    }
                    c.UnlockBits(d);
                    if (diff > bestCount) { bestCount = diff; best = i; }
                }
            }
        }
        return best;
    }
    static Bitmap CenterCrop(string file, int cw, int ch)
    {
        using (var b = new Bitmap(file))
        {
            var c = new Bitmap(cw, ch, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(c))
                g.DrawImage(b, new Rectangle(0, 0, cw, ch),
                    new Rectangle((b.Width - cw) / 2, (b.Height - ch) / 2, cw, ch), GraphicsUnit.Pixel);
            return c;
        }
    }
}
}
'@
    $pwshDir = Split-Path ([System.Drawing.Image]).Assembly.Location
    $refs = @(
        ([object]).Assembly.Location,
        ([System.Collections.Generic.List[object]]).Assembly.Location,
        ([System.Drawing.Bitmap]).Assembly.Location,
        ([System.Drawing.Rectangle]).Assembly.Location,   # Rectangle 转发在 System.Drawing.Primitives
        ([System.Reflection.Assembly]::LoadFrom((Join-Path $pwshDir 'System.Private.Windows.GdiPlus.dll'))).Location,
        ([System.Reflection.Assembly]::LoadFrom((Join-Path $pwshDir 'System.Private.Windows.Core.dll'))).Location
    ) | Sort-Object -Unique
    Add-Type -TypeDefinition $src -ReferencedAssemblies $refs
}

function Start-Fc {
    param([bool]$EncodeFail = $false, [string]$RingBudgetMb = "3000")
    Get-Process FrameCapture -EA SilentlyContinue | Stop-Process -Force
    Start-Sleep 0.6
    $env:FC_THEME = $Theme
    $env:FC_TEST_NO_FG = "1"
    if ($RingBudgetMb) { $env:FC_TEST_RING_BUDGET_MB = $RingBudgetMb } else { Remove-Item Env:\FC_TEST_RING_BUDGET_MB -EA SilentlyContinue }
    if ($EncodeFail) { $env:FC_TEST_ENCODE_FAIL = "1" } else { Remove-Item Env:\FC_TEST_ENCODE_FAIL -EA SilentlyContinue }
    $p = Start-Process $exe -PassThru
    Remove-Item Env:\FC_THEME -EA SilentlyContinue
    Remove-Item Env:\FC_TEST_NO_FG -EA SilentlyContinue
    Remove-Item Env:\FC_TEST_RING_BUDGET_MB -EA SilentlyContinue
    Start-Sleep 1.6
    return $p
}
function Stop-Fc { param($p) if ($null -ne $p -and !$p.HasExited) { $p.Kill() }; Start-Sleep 0.4 }
function Test-EditorAlive {
    $ed = & $shot rect "FrameCapture Editor" 2>$null
    return ($ed -notmatch "not-found")
}
function Capture-Bars {
    param([string]$OutPng)
    # 控制面板位于编辑器窗口内，截图包含面板
    $r = & $uicap region "FrameCapture Editor" $OutPng 2>&1
    if ($r -notmatch "saved") { Write-Host "  bars capture FAIL: $r"; return $false }
    Write-Host "  saved $(Split-Path $OutPng -Leaf) ($($r -replace '^saved\s+\S+\s+', ''))"
    return $true
}

# —— settings.json：备份 → 抬高 MaxDurationMs（实例启动前改好，避免与热加载竞态）→ 结束后原样还原 ——
$cfgPath = Get-FcSettingsPath
$cfgBackup = Join-Path $OutDir "settings-d9-backup-$Theme.json"
# 设置文件可能还不存在（全新配置目录，应用没写过盘）：没有就没什么可备份，还原时也不回写
if (Test-Path $cfgPath) { Copy-Item $cfgPath $cfgBackup -Force } else { $cfgBackup = $null }
try {
    $cfg = if ($cfgBackup) { Get-Content $cfgPath -Raw | ConvertFrom-Json } else { [pscustomobject]@{} }
    # 用户没动过「最长录制时长」时，落盘的设置文件里根本没有这一项（应用只写非默认值）：
    # 属性不存在就加上，不能直接赋值——那会抛「property cannot be found」。
    if ($cfg.PSObject.Properties.Name -contains 'MaxDurationMs') { $cfg.MaxDurationMs = 26000 }
    else { $cfg | Add-Member -NotePropertyName MaxDurationMs -NotePropertyValue 26000 }
    $cfg | ConvertTo-Json -Depth 12 | Set-Content $cfgPath -Encoding UTF8
    Write-Host "settings: MaxDurationMs -> 26000 (临时，备份 $([IO.Path]::GetFileName($cfgBackup)))"

    $fail = 0
    Write-Host "== D9 HUD 边界态取证 (theme=$Theme, capture_hotkey=$capHk) =="

    # ---------- 实例 A：长按 25s（真丢帧）→ 丢帧警告 → 取消恢复 → 导出进度（同实例三连态） ----------
    # 时序事实（实测）：每次 Shot/UICap 进程冷启动约 0.5s——"Enter 后 350ms 投 Esc"实际落在 ~1s 后；
    # 2000MB 预算只留 163 帧时编码 ~1s 就完窗，Esc 会扑空。故预算钉 2500MB（≈204 帧，烘焙+编码 ~1.4s，
    # 交付 ~240 帧 → 必丢 ~36 帧），且 Enter/Esc 背靠背发出：两条消息按 FIFO 进同一窗口队列，
    # Esc 必然落在 Enter（同步置 _encoding=true）之后、编码期间——取消窗口不再靠 sleep 猜。
    $p = Start-Fc -RingBudgetMb "2500"
    $mA = Get-TraceLen
    Invoke-FcHotkey $shot $capHk 25000 | Out-Null   # ~240 帧 ≈ 2.96GB > 钉住的 2500MB 预算
    Start-Sleep 1.4
    if (-not (Test-EditorAlive)) {
        Write-Host "D9 SKIP: 编辑器未出现——游戏/反外挂可能屏蔽注入式热键，请关闭游戏后重跑（后续实例一并跳过）"
        Stop-Fc $p
        exit 0
    }
    if ((Get-TraceSince $mA) -match "ring dropped") {
        if (-not (Capture-Bars (Join-Path $OutDir "d9-hud-dropped-$Theme.png"))) { $fail++ }
    } else {
        Write-Host "  [dropped] trace 无 'ring dropped'——帧预算未被打爆"; $fail++
    }

    # 取消恢复：Enter 后立刻（背靠背）投 Esc——同队列消息保证 Esc 在 Enter 的同步副作用之后被处理
    & $shot keyw "FrameCapture Editor" 13 2>$null | Out-Null
    $mC = Get-TraceLen
    & $shot keyw "FrameCapture Editor" 27 2>$null | Out-Null
    Start-Sleep 1.2
    if ((Get-TraceSince $mC) -match "encode cancelled") {
        if (-not (Test-EditorAlive)) { Write-Host "  [cancel] 取消后编辑器应仍存活"; $fail++ }
        if (-not (Capture-Bars (Join-Path $OutDir "d9-hud-cancelled-$Theme.png"))) { $fail++ }
    } else {
        Write-Host "  [cancel] trace 无 'encode cancelled'——Esc 落点不在编码窗口期"; $fail++
    }

    # 导出进度：先起 8s burst 连拍（首帧=空闲 HUD，随后 Enter 入队），再投 Enter——
    # burst 必然覆盖 空闲→导出 GIF…→d/n→（完成关窗） 的整段
    $burstDir = Join-Path $OutDir "burst-d9-progress-$Theme"
    Remove-Item -Recurse -Force $burstDir -EA SilentlyContinue
    New-Item -ItemType Directory -Force -Path $burstDir | Out-Null
    $mP = Get-TraceLen
    # Start-Process -ArgumentList 不自动为含空格标题加引号，必须自带
    $bp = Start-Process $uicap -ArgumentList @('burstregion', '"FrameCapture Editor"', '8', '150', ('"' + (Join-Path $burstDir 'p') + '"')) -NoNewWindow -PassThru
    & $shot keyw "FrameCapture Editor" 13 2>$null | Out-Null
    $bp.WaitForExit()
    $frames = @(Get-ChildItem $burstDir -Filter 'p.*.png' | Sort-Object Name)
    if ($frames.Count -ge 2) {
        $paths = @($frames | ForEach-Object { $_.FullName })
        $diffCount = 0
        $idx = [FcHud.Pick]::MostDiff($paths, [ref]$diffCount)
        Write-Host "  burst=$($frames.Count) 帧，最大差分帧 #$idx (diffPixels=$diffCount)"
        if ($idx -ge 0 -and $diffCount -ge 40) {
            Copy-Item $paths[$idx] (Join-Path $OutDir "d9-hud-progress-$Theme.png")
            Write-Host "  saved d9-hud-progress-$Theme.png (源 $($frames[$idx].Name))"
        } else {
            Write-Host "  [progress] 差分过小($diffCount)——burst 可能错过编码窗口"; $fail++
        }
    } else {
        Write-Host "  [progress] burst 帧数不足($($frames.Count))"; $fail++
    }
    # 编码应已在 burst 8s 窗口内自然收尾（编辑器自动关闭 = 全链路顺带自检）
    if ((Get-TraceSince $mP) -notmatch "gif encoded bytes=") { Write-Host "  [progress] trace 无 'gif encoded bytes='——编码未正常收尾"; $fail++ }
    Stop-Fc $p

    # ---------- 实例 B：强制失败态（FC_TEST_ENCODE_FAIL=1，短按即可） ----------
    $p = Start-Fc -EncodeFail $true -RingBudgetMb $null
    Invoke-FcHotkey $shot $capHk 700 | Out-Null
    Start-Sleep 1.4
    if (Test-EditorAlive) {
        $mB = Get-TraceLen
        & $shot keyw "FrameCapture Editor" 13 2>$null | Out-Null   # Enter → 编码任务必然抛错
        Start-Sleep 1.0
        if ((Get-TraceSince $mB) -match "encode failed:") {
            if (-not (Capture-Bars (Join-Path $OutDir "d9-hud-fail-$Theme.png"))) { $fail++ }
            if (-not (Test-EditorAlive)) { Write-Host "  [fail] 失败后编辑器应仍存活"; $fail++ }
        } else {
            Write-Host "  [fail] trace 无 'encode failed:'——失败开关未生效"; $fail++
        }
        & $shot keyw "FrameCapture Editor" 27 2>$null | Out-Null   # 非编码期 Esc = 关闭
        Start-Sleep 0.6
        if (Test-EditorAlive) { Write-Host "  [fail] Esc 后编辑器应已关闭"; $fail++ }
    } else { Write-Host "  [fail] 编辑器未出现"; $fail++ }
    Stop-Fc $p
} finally {
    if ($cfgBackup) { Copy-Item $cfgBackup $cfgPath -Force }   # 原样还原（无论中途成败）
    Start-Sleep 0.8
    $back = if (Test-Path $cfgPath) { (Get-Content $cfgPath -Raw | ConvertFrom-Json).MaxDurationMs } else { "无设置文件" }
    Write-Host "settings: MaxDurationMs 已还原 $(if ($back) { $back } else { '（该项本就不在文件里）' })"
}

if ($fail -eq 0) { Write-Host "HUD-CAP PASS (theme=$Theme)"; exit 0 }
Write-Host "HUD-CAP FAIL (theme=$Theme, $fail 项)"; exit 1
