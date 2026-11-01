# 焦点安全输入库（夜间长跑规则）：任何点击/按键前必须证明目标窗口是本软件窗口，
# 否则中止该操作——绝不把输入盲落到其他软件上。
# 原语来自 tools/Shot：rect <标题> / fg / mouse x1 y1 x2 y2 [holdMs] / keyw <标题> <vk>。

$script:FcTitles = @(
    'NekoCapture Editor', 'NekoCapture Region', 'NekoCapture Settings',
    'NekoCapture Help', 'NekoCapture Toast', 'NekoCapture Scope'
)

function Get-FcWinRect {
    param([string]$Shot, [string]$Title)
    $out = (& $Shot rect $Title 2>&1) -join ''
    # Shot 输出格式: "rect L,T WxH"（物理像素）
    if ($out -notmatch '(-?\d+)\s*,\s*(-?\d+)\s+(\d+)\s*x\s*(\d+)') { return $null }
    return @{ x = [int]$Matches[1]; y = [int]$Matches[2]; w = [int]$Matches[3]; h = [int]$Matches[4] }
}

function Get-FcForeground {
    param([string]$Shot)
    try { return ((& $Shot fg 2>&1) -join '') } catch { return '' }
}

function Test-FcForegroundIs {
    param([string]$Shot, [string]$Title)
    $fg = Get-FcForeground $Shot
    return ($fg -like "*$Title*")
}

# 激活目标窗（SetForegroundWindow），随后必须再核前台——系统可能拒绝抢焦点
function Invoke-FcActivate {
    param([string]$Shot, [string]$Title, [int]$Tries = 3)
    for ($i = 0; $i -lt $Tries; $i++) {
        & $Shot activate $Title | Out-Null
        Start-Sleep -Milliseconds 250
        if (Test-FcForegroundIs $Shot $Title) { return $true }
        Start-Sleep -Milliseconds 300
    }
    return (Test-FcForegroundIs $Shot $Title)
}

# 点击窗口内相对坐标（DIP→物理按 1:1 处理；本机 DPI 缩放 100%，若改缩放需换算）
# 护栏：点击前校验前台=目标标题 且 点落在窗口矩形内；不满足直接 throw（调用方决定跳过还是重试）。
# -ExtraRect：本软件弹层（Popup 独立 HWND，可超出宿主窗矩形）的合法扩展区 x,y,w,h（物理）。
function Invoke-FcClickIn {
    param([string]$Shot, [string]$Title, [double]$RelX, [double]$RelY, [int]$HoldMs = 60, [switch]$NoFgCheck, [int[]]$ExtraRect)
    $r = Get-FcWinRect $Shot $Title
    if ($null -eq $r) { throw "FC-FOCUS: 窗口不存在: $Title" }
    $px = [int]($r.x + $RelX); $py = [int]($r.y + $RelY)
    $inWin = $px -ge $r.x -and $px -le ($r.x + $r.w) -and $py -ge $r.y -and $py -le ($r.y + $r.h)
    $inExtra = $false
    if ($ExtraRect -and $ExtraRect.Count -eq 4) {
        $inExtra = $px -ge $ExtraRect[0] -and $px -le ($ExtraRect[0] + $ExtraRect[2]) -and
                   $py -ge $ExtraRect[1] -and $py -le ($ExtraRect[1] + $ExtraRect[3])
    }
    if (-not ($inWin -or $inExtra)) {
        throw "FC-FOCUS: 点($px,$py) 不在 $Title 矩形或扩展区内 ($($r.x),$($r.y),$($r.w)x$($r.h))"
    }
    if (-not $NoFgCheck -and -not (Test-FcForegroundIs $Shot $Title)) {
        throw "FC-FOCUS: 前台不是 $Title（实际: $(Get-FcForeground $Shot)），拒绝盲操"
    }
    & $Shot mouse $px $py $px $py $HoldMs | Out-Null
    return $true
}

# 拖拽窗口内两点（同样先过前台+矩形双闸；-ExtraRect 同 Invoke-FcClickIn）
function Invoke-FcDragIn {
    param([string]$Shot, [string]$Title, [double]$X1, [double]$Y1, [double]$X2, [double]$Y2, [int]$HoldMs = 350, [int[]]$ExtraRect)
    $r = Get-FcWinRect $Shot $Title
    if ($null -eq $r) { throw "FC-FOCUS: 窗口不存在: $Title" }
    $ax = [int]($r.x + $X1); $ay = [int]($r.y + $Y1); $bx = [int]($r.x + $X2); $by = [int]($r.y + $Y2)
    function Inside([int]$x, [int]$y) {
        if ($x -ge $r.x -and $x -le ($r.x + $r.w) -and $y -ge $r.y -and $y -le ($r.y + $r.h)) { return $true }
        if ($ExtraRect -and $ExtraRect.Count -eq 4 -and
            $x -ge $ExtraRect[0] -and $x -le ($ExtraRect[0] + $ExtraRect[2]) -and
            $y -ge $ExtraRect[1] -and $y -le ($ExtraRect[1] + $ExtraRect[3])) { return $true }
        return $false
    }
    foreach ($p in @(@($ax, $ay), @($bx, $by))) {
        if (-not (Inside $p[0] $p[1])) {
            throw "FC-FOCUS: 拖拽点($($p[0]),$($p[1])) 不在 $Title 矩形或扩展区内"
        }
    }
    if (-not (Test-FcForegroundIs $Shot $Title)) {
        throw "FC-FOCUS: 前台不是 $Title（实际: $(Get-FcForeground $Shot)），拒绝盲操"
    }
    & $Shot mouse $ax $ay $bx $by $HoldMs | Out-Null
    return $true
}

# 等待窗口出现/消失（返回矩形或 $null）
function Wait-FcWindow {
    param([string]$Shot, [string]$Title, [int]$TimeoutSec = 8, [switch]$Gone)
    $t0 = Get-Date
    while (((Get-Date) - $t0).TotalSeconds -lt $TimeoutSec) {
        $r = Get-FcWinRect $Shot $Title
        if ($Gone -and $null -eq $r) { return $true }
        if (-not $Gone -and $null -ne $r) { return $r }
        Start-Sleep -Milliseconds 150
    }
    return $null
}
