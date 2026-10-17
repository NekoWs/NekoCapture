# D5 取证：选区层的「吸附态」与「拖框态」像素档案（UIAUDIT.md D5，审计里最后一条待办）。
#
# 为什么以前标成「自动化边界外」：这两个态要鼠标悬停与拖框，而仓库硬约束禁止 OS 级鼠标自动化
# （SendInput 会搬动用户真光标）。D8 已经开过路子：对自家窗口 PostMessage 鼠标消息不算 OS 级注入——
# 真光标不动、系统输入队列不进，只有这个窗口自己收到。Shot 的 postmouse 就是这个能力。
#
# 机制：--picker 叫出选区层（与热键命中同一入口）→ postmouse move 到某个窗口上（选区层按窗口吸附）
#       → 截图；再 down + 若干 drag 拖出一块选区（工具条与尺寸读数只在按住期间存在）→ 截图 → Esc 收起。
# 坐标是客户区物理像素：选区层是全屏覆盖窗，客户区与屏幕重合，直接填屏幕坐标。
#
# 用法: pwsh -NoProfile -File scripts/cap-picker-states.ps1 [-Lang en|zh] [-Theme dark|light]
param(
    [ValidateSet('en', 'zh')][string]$Lang = 'zh',
    [ValidateSet('dark', 'light')][string]$Theme = 'dark'
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "src/FrameCapture/bin/x64/Debug/net8.0-windows10.0.19041.0/FrameCapture.exe"
$uicap = Join-Path $root "tools/UICap/bin/Debug/net8.0-windows/UICap.exe"
$shot = Join-Path $root "tools/Shot/bin/Debug/net8.0-windows/Shot.exe"
foreach ($p in @($exe, $uicap, $shot)) { if (-not (Test-Path $p)) { throw "缺产物：$p（先构建）" } }
$out = Join-Path $root "test-out/uiaudit"
New-Item -ItemType Directory -Force -Path $out | Out-Null

$scratch = Join-Path $root "test-out/picker-state-settings"
if (Test-Path $scratch) { Remove-Item $scratch -Recurse -Force }
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
$userCfg = Join-Path $env:APPDATA 'FrameCapture\settings.json'
if (Test-Path $userCfg) { Copy-Item $userCfg (Join-Path $scratch 'settings.json') -Force }
$env:FC_SETTINGS_DIR = $scratch
$env:FC_LANG = $Lang
$env:FC_THEME = $Theme
$env:FC_TOAST_NOEXCLUDE = "1"

Get-Process FrameCapture -EA SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 700
Start-Process $exe -WindowStyle Hidden
Start-Sleep -Seconds 3
Start-Process $exe -ArgumentList "--picker"
Start-Sleep -Seconds 2

$pick = "FrameCapture Region"
$rect = & $shot rect $pick 2>&1 | Out-String
if ($rect -match 'not-found') { throw "选区层没起来：$($rect.Trim())" }
Write-Host "选区层矩形：$($rect.Trim())"

# 吸附态：光标落在某个真实窗口上，选区层应吸附到那个窗口并给出窗口标签 + 放大镜读数
& $shot postmouse $pick 1900 1100 move 400 2>&1 | Out-Null
Start-Sleep -Milliseconds 500
# 必须按窗口截（PrintWindow），不能截桌面：选区层带 WDA_EXCLUDEFROMCAPTURE，
# 它本来就不该出现在捕获产物里，于是桌面 BitBlt 里也看不见它——不是没画，是屏幕 DC 不含它。
& $uicap win $pick (Join-Path $out "d5-picker-snap-$Lang-$Theme.png") 2>&1 | Select-Object -Last 1

# 拖框态：按住拖出一块区域，工具条与尺寸读数只在按住期间存在
& $shot postmouse $pick 1250 780 down 200 2>&1 | Out-Null
foreach ($p in @(@(1400, 900), @(1560, 1040), @(1720, 1180), @(1880, 1320))) {
    & $shot postmouse $pick $p[0] $p[1] drag 120 2>&1 | Out-Null
}
Start-Sleep -Milliseconds 400
& $uicap win $pick (Join-Path $out "d5-picker-drag-$Lang-$Theme.png") 2>&1 | Select-Object -Last 1

# 收场：选区窗是非激活窗，Esc/Enter/C/F/Tab 全部由 HotkeyManager 全局钩子代收，
# PostMessage 的 Esc 到不了它（合成键也到不了），它也没有右键取消。拖框没投 up，
# 所以不会真的截下这块区域；直接停进程最干净——别让全屏遮罩留在屏幕上。
Get-Process FrameCapture -EA SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600

Remove-Item Env:\FC_LANG, Env:\FC_THEME, Env:\FC_TOAST_NOEXCLUDE, Env:\FC_SETTINGS_DIR -EA SilentlyContinue
Write-Host "D5-CAP 完成（$Lang / $Theme）→ $out"
