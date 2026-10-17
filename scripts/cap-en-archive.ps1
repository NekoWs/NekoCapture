# D11 档案：某个语言的全界面像素档案一次跑齐（UIAUDIT.md D 节的英文补齐；中文用 -Lang zh 对照）。
#
# 为什么单独跑一遍英文：双语探针查的是「装不装得下、漏没漏翻」，量得出溢出与中文残留，
# 量不出版面好不好看、说法顺不顺。首启卡英文标签被键位盖掉那种缺陷，探针当时也查不出
# （靠后来补的「叠字」才查得出来）——所以档案要留人眼能回看的图，两种语言各一套。
#
# 机制（全在自动化边界内，绝不用 OS 级鼠标自动化）：
#   · FC_LANG=<语言> 起进程（语言在启动时定，改设置不会让已起的实例换语言）；
#   · FC_SETTINGS_DIR 指向 test-out/en-archive-settings，用户 %APPDATA% 那份只读不写；
#   · FC_TOAST_NOEXCLUDE=1 让浮窗不再从捕获里排除，否则截回来是空的桌面；
#   · 界面全靠自家 IPC 开关叫出来（--settings / --help / --welcome / --notice / --ocr / --delay），
#     帮助翻页用 PostMessage 投左右键到自家窗口，不碰系统输入队列；
#   · 编辑器要读一张样图，而 --editor 带参数、不走转发，所以它单独作为首个实例起。
#
# 用法: pwsh -NoProfile -File scripts/cap-en-archive.ps1 [-Lang en|zh] [-Theme dark|light] [-OutDir test-out/uiaudit]
# 前置：会杀掉运行中的 FrameCapture，跑完留一个测试态实例（要恢复用户实例：直接重开程序）。
param(
    [ValidateSet('en', 'zh')][string]$Lang = 'en',
    [string]$OutDir = "test-out/uiaudit",
    [ValidateSet('dark', 'light')][string]$Theme = 'dark'
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "src/FrameCapture/bin/x64/Debug/net8.0-windows10.0.19041.0/FrameCapture.exe"
$uicap = Join-Path $root "tools/UICap/bin/Debug/net8.0-windows/UICap.exe"
$shot = Join-Path $root "tools/Shot/bin/Debug/net8.0-windows/Shot.exe"
foreach ($p in @($exe, $uicap, $shot)) { if (-not (Test-Path $p)) { throw "缺产物：$p（先构建）" } }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$scratch = Join-Path $root "test-out/en-archive-settings"
if (Test-Path $scratch) { Remove-Item $scratch -Recurse -Force }
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
# 起点是用户那份的副本：档案要拍他真实的手势键位，不是出厂默认
$userCfg = Join-Path $env:APPDATA 'FrameCapture\settings.json'
if (Test-Path $userCfg) { Copy-Item $userCfg (Join-Path $scratch 'settings.json') -Force }
$env:FC_SETTINGS_DIR = $scratch
$env:FC_LANG = $Lang
$env:FC_THEME = $Theme
$env:FC_TOAST_NOEXCLUDE = "1"

function Stop-App { Get-Process FrameCapture -EA SilentlyContinue | Stop-Process -Force; Start-Sleep -Milliseconds 700 }
function Start-App([string[]]$args2 = @()) {
    if ($args2.Count) { Start-Process $exe -ArgumentList $args2 -WindowStyle Hidden }
    else { Start-Process $exe -WindowStyle Hidden }
    Start-Sleep -Seconds 3
}
# 按 OS 标题截单个窗口：窗口级截图不带桌面杂物，档案里只看这块版面
function Cap-Win([string]$title, [string]$name) {
    $out = Join-Path $OutDir $name
    $r = & $uicap win $title $out 2>&1 | Out-String
    if ($r -match 'not-found') { Write-Host "  MISS  $name  （找不到窗口：$title）"; return $false }
    Write-Host ("  {0,-26} {1}" -f $name, ($r.Trim() -replace '^saved ', ''))
    return $true
}

$got = 0
Write-Host "界面像素档案（$Lang / $Theme）→ $OutDir"

# 1) 编辑器：--editor 带样图，只能当首个实例起
Stop-App
$sample = Join-Path $root "test-out/en-archive-sample.png"
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap 1280, 800
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::FromArgb(255, 246, 247, 250))
$dark = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 32, 38, 52))
$g.FillRectangle($dark, 60, 60, 520, 220)
$f1 = New-Object System.Drawing.Font("Segoe UI", 28, [System.Drawing.FontStyle]::Bold)
$g.DrawString("FrameCapture — English archive", $f1, [System.Drawing.Brushes]::White, 88, 130)
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 76, 194, 255)), 6
$g.DrawEllipse($pen, 700, 110, 320, 170)
$g.DrawLine($pen, 60, 370, 1180, 370)
$f2 = New-Object System.Drawing.Font("Segoe UI", 16)
$g.DrawString("A quiet background with a few shapes to annotate.", $f2, $dark, 60, 410)
$g.Dispose(); $bmp.Save($sample, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
Start-App @("--editor", $sample)
if (Cap-Win "FrameCapture Editor" "$Lang-editor-$Theme.png") { $got++ }
Stop-App

# 2) 其余界面走 IPC，一个实例叫全部
Start-App
foreach ($t in @(
    @{ flag = "--settings"; title = "FrameCapture Settings"; name = "$Lang-settings" },
    @{ flag = "--welcome";  title = "FrameCapture Welcome";  name = "$Lang-welcome" },
    @{ flag = "--notice";   title = "FrameCapture Notice";   name = "$Lang-notice" },
    @{ flag = "--ocr";      title = "FrameCapture OCR";      name = "$Lang-ocr" },
    @{ flag = "--delay";    title = "FrameCapture Countdown"; name = "$Lang-countdown" }
)) {
    Start-Process $exe -ArgumentList $t.flag
    Start-Sleep -Seconds 2
    if (Cap-Win $t.title "$($t.name)-$Theme.png") { $got++ }
    # 倒计时卡到点自己走，其余按 Esc 收起，免得叠在一起影响下一张
    & $shot keyw $t.title 27 2>$null | Out-Null
    Start-Sleep -Milliseconds 400
}

# 3) 帮助窗九页：左右键在自家窗口里翻页（PostMessage，不进系统输入队列）
Start-Process $exe -ArgumentList "--help"
Start-Sleep -Seconds 2
for ($p = 1; $p -le 9; $p++) {
    if (Cap-Win "FrameCapture Help" "$Lang-help$p-$Theme.png") { $got++ }
    & $shot keyw "FrameCapture Help" 39 2>$null | Out-Null   # VK_RIGHT = 下一页
    Start-Sleep -Milliseconds 500
}
& $shot keyw "FrameCapture Help" 27 2>$null | Out-Null

# 4) 托盘菜单：复用 D8 的取证脚本（PrintWindow 才拿得到分层窗内容）
& (Join-Path $PSScriptRoot "cap-tray-menu.ps1") -Theme $Theme -OutPng (Join-Path $OutDir "$Lang-tray-menu-$Theme.png") | Out-Null
if (Test-Path (Join-Path $OutDir "$Lang-tray-menu-$Theme.png")) { $got++; Write-Host ("  {0,-26} 见 cap-tray-menu.ps1" -f "$Lang-tray-menu-$Theme.png") }

Remove-Item Env:\FC_LANG, Env:\FC_THEME, Env:\FC_TOAST_NOEXCLUDE, Env:\FC_SETTINGS_DIR -EA SilentlyContinue
Write-Host ""
Write-Host "ARCHIVE 共 $got 张（$Lang / $Theme）→ $OutDir"
