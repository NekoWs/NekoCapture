# 回归脚本共用：把设置里的热键串（"Ctrl+Shift+G"/"F6"/"Alt+F4"…）映射成 tools/Shot 的 hkm 注入。
# 语法与 src/FrameCapture/Core/HotkeySpec.cs 的 Parse 对齐；解析失败一律 throw（宁可炸，不注错键）。
# 不改变 Shot 的命令约定：只用 hkm <vk> <ctrl> <shift> <alt> <win> <holdMs>。

function Get-FcKeyVk {
    param([string]$Tok)
    $t = $Tok.ToLowerInvariant()
    if ($t -match '^f([1-9]|1[0-9]|2[0-4])$') { return 111 + [int]$Matches[1] }   # F1..F24 = 112..135
    if ($t -match '^[a-z]$') { return [int][char]($t.ToUpper())[0] }             # A..Z = 65..90
    if ($t -match '^[0-9]$') { return 48 + [int]$t }                              # 0..9 = 48..57（即 VK_D0..D9）
    switch ($t) {
        { $_ -in 'esc', 'escape' } { return 27 }
        { $_ -in 'enter', 'return' } { return 13 }
        'space' { return 32 }
        'tab' { return 9 }
        'back' { return 8 }
        'home' { return 36 }
        'end' { return 35 }
        { $_ -in 'up', 'pageup', 'prior' } { return 33 }
        { $_ -in 'down', 'pagedown', 'next' } { return 34 }
        { $_ -in 'left' } { return 37 }
        { $_ -in 'right' } { return 39 }
        { $_ -in 'insert', 'ins' } { return 45 }
        { $_ -in 'delete', 'del' } { return 46 }
        'capslock' { return 20 }
        { $_ -in 'printscreen', 'prtsc' } { return 44 }
        default { return $null }
    }
}

function Get-FcHotkeyParts {
    param([string]$Spec)
    $ctrl = 0; $shift = 0; $alt = 0; $win = 0; $vk = $null
    foreach ($raw in ($Spec -split '\+')) {
        $tok = $raw.Trim()
        if ($tok -eq '') { continue }
        $low = $tok.ToLowerInvariant()
        if ($low -in 'ctrl', 'control') { $ctrl = 1 }
        elseif ($low -eq 'alt') { $alt = 1 }
        elseif ($low -eq 'shift') { $shift = 1 }
        elseif ($low -in 'win', 'windows', 'super') { $win = 1 }
        else {
            if ($null -ne $vk) { throw "hotkey spec 有两个主键: '$Spec'" }
            $vk = Get-FcKeyVk $tok
            if ($null -eq $vk) { throw "hotkey 主键无法映射: '$tok' (spec='$Spec')" }
        }
    }
    if ($null -eq $vk) { throw "hotkey 缺主键: '$Spec'" }
    # 与 HotkeySpec.Parse 同规：裸键仅允许 F1..F24，其余必须带修饰键（设置里出现非法值说明文件被改坏，必须炸）
    if (($ctrl + $shift + $alt + $win) -eq 0 -and -not ($vk -ge 112 -and $vk -le 135)) {
        throw "hotkey 裸键非法（仅 F1..F24 可裸用）: '$Spec'"
    }
    return @{ vk = $vk; ctrl = $ctrl; shift = $shift; alt = $alt; win = $win }
}

# 设置目录：优先 FC_SETTINGS_DIR（护栏给 e2e 起的临时目录），没设才回 %APPDATA% 那份。
# 测试会改设置（主题、热键、手势都写在 settings.json），崩溃断言读的 error.log 也在这个目录里——
# 两边都跟着它走，用户自己那份从头到尾不该被碰一下。
# 手工单跑某个脚本时不设它，行为与过去一致：对着自己这台机器的设置来。
function Get-FcDir {
    if ($env:FC_SETTINGS_DIR) { return $env:FC_SETTINGS_DIR }
    return Join-Path $env:APPDATA 'FrameCapture'
}
function Get-FcSettingsPath { Join-Path (Get-FcDir) 'settings.json' }
function Get-FcLogPath([string]$name) { Join-Path (Get-FcDir) $name }

function Get-FcSettings {
    $cfg = Get-FcSettingsPath
    if (Test-Path $cfg) {
        try { return (Get-Content $cfg -Raw | ConvertFrom-Json) } catch { }
    }
    return [pscustomobject]@{}
}

# 键位由手势绑定决定。从 gestures.bindings 推导「截图」键位，与 C# 侧
# GestureConfig.CaptureKeyText 同规：先认 tap/hold 系列的键，再认截图动作所在的键。
function Get-FcCaptureHotkey {
    $g = (Get-FcSettings).gestures
    if ($g -and $g.Bindings) {
        foreach ($b in $g.Bindings) {
            if ($b.Enabled -and $b.Trigger -in 'Tap','Hold','HoldRelease') { return $b.Chord }
        }
        foreach ($b in $g.Bindings) {
            if ($b.Enabled -and ($b.Actions -contains 'capture.stored' -or $b.Actions -contains 'capture.frame')) { return $b.Chord }
        }
    }
    return 'Ctrl+Alt+A'   # 与 GestureConfig.DefaultCaptureKey 一致
}

# e2e 统一测试手势集——把 settings.json 的 gestures 段钉成一套已知绑定，
# 让注入键位与断言与用户自定义解耦。一套喂饱全部 e2e：
#   Ctrl+Shift+G 轻点→选区+截图（弹选区窗）；按住→起录，松手→停止并进编辑器，链尾打开编辑器
#   Ctrl+Shift+R 组合键→选区
# 返回原 settings.json 原文（调用方负责结束还原）。
function Set-FcTestGestureConfig {
    param([int]$RegionX = -1, [int]$RegionY = -1, [int]$RegionW = -1, [int]$RegionH = -1)
    $cfg = Get-FcSettingsPath
    New-Item -ItemType Directory -Force -Path (Split-Path $cfg) | Out-Null
    $bak = if (Test-Path $cfg) { Get-Content $cfg -Raw } else { $null }
    $s = if ($bak) { $bak | ConvertFrom-Json } else { [pscustomobject]@{} }
    $bindings = @(
        [pscustomobject]@{ Id='t.cap.tap'; Enabled=$true; Chord='Ctrl+Shift+G'; Trigger='Tap';
            Actions=@('select','grab.still'); OnFail=@(); Fallback='Stop'; Priority=0; DoubleClickMs=400; LongPressMs=250 },
        [pscustomobject]@{ Id='t.cap.hold'; Enabled=$true; Chord='Ctrl+Shift+G'; Trigger='Hold';
            Actions=@('scope.stored','record.start'); OnFail=@(); Fallback='Stop'; Priority=0; DoubleClickMs=400; LongPressMs=250 },
        [pscustomobject]@{ Id='t.cap.holdrelease'; Enabled=$true; Chord='Ctrl+Shift+G'; Trigger='HoldRelease';
            Actions=@('record.stop','edit.lastvideo'); OnFail=@(); Fallback='Stop'; Priority=0; DoubleClickMs=400; LongPressMs=250 },
        [pscustomobject]@{ Id='t.scope'; Enabled=$true; Chord='Ctrl+Shift+R'; Trigger='Chord';
            Actions=@('select'); OnFail=@(); Fallback='Stop'; Priority=0; DoubleClickMs=400; LongPressMs=500 }
    )
    $gestures = [pscustomobject]@{ Enabled=$true; Bindings=$bindings; Seeded=$true; ChainRevision=2 }
    # 注意：app 的 JsonSerializer 大小写敏感，属性名须 PascalCase（"Gestures"），否则整段被忽略→回退默认播种集。
    $s | Add-Member -Force Gestures $gestures
    # 首启引导会吞掉进编辑器后的头几次按键：测试直投按键测的是键位本身，先把引导记成已看过
    $s | Add-Member -Force EditorGuideShown $true
    if ($RegionW -gt 0) {   # 固定捕获区域（缩放/编辑器类测试需大区域，避免 End=1:1 变无操作）
        $s | Add-Member -Force RegionX $RegionX
        $s | Add-Member -Force RegionY $RegionY
        $s | Add-Member -Force RegionW $RegionW
        $s | Add-Member -Force RegionH $RegionH
    }
    $s | ConvertTo-Json -Depth 8 | Set-Content $cfg -Encoding UTF8
    return $bak
}

function Invoke-FcHotkey {
    param([string]$Shot, [string]$Spec, [int]$HoldMs)
    $p = Get-FcHotkeyParts $Spec
    Write-Verbose ("hkm {0} {1} {2} {3} {4} {5}  # {6}" -f $p.vk, $p.ctrl, $p.shift, $p.alt, $p.win, $HoldMs, $Spec)
    & $Shot hkm $p.vk $p.ctrl $p.shift $p.alt $p.win $HoldMs
}

# 修饰键护栏：keyw 是 PostMessage 裸键，而 WPF 修饰键读真实键盘状态——
# 用户正在打字（中文 IME 常按 Shift/Ctrl）时注入的裸键会被记成 Shift+End 之类，断言假失败。
# 注入裸键前等物理修饰键全部松开；超时仍注入（宁可红一次，不死等；也不主动替用户"松开"按键）。
if (-not ('FcMods.Kb' -as [type])) {
    Add-Type -Namespace FcMods -Name Kb -Language CSharp -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
public static bool AnyDown() {
    foreach (int vk in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
        if ((GetAsyncKeyState(vk) & 0x8000) != 0) return true;
    return false;
}
public static bool AnyInputDown() {
    foreach (int vk in new[] { 0x01, 0x02, 0x10, 0x11, 0x12, 0x1B, 0x5B, 0x5C })
        if ((GetAsyncKeyState(vk) & 0x8000) != 0) return true;
    return false;
}
'@
}
function Wait-FcModifiersUp {
    param([int]$TimeoutSec = 15)
    $t0 = Get-Date
    while ([FcMods.Kb]::AnyDown() -and ((Get-Date) - $t0).TotalSeconds -lt $TimeoutSec) { Start-Sleep -Milliseconds 120 }
}

# 安静窗口：连续 -QuietMs 毫秒内没有任何物理按键或鼠标键按下，才算用户暂时没在用电脑。
# 唯一必须真注入的那一项（焦点安全）用它把关——等不到就返回 false，调用方跳过，绝不硬闯。
# 判据是物理输入状态本身，不是「前台窗口类名像不像游戏」：后者看不见正在编辑器里打字的人。
$script:FcQuietDiag = ""
function Wait-FcQuiet {
    param([int]$TimeoutSec = 25, [int]$QuietMs = 3000)
    # 跳过要说得出是哪个键按着：只报「用户在输入」会把卡住的按键状态说成有人在打字，
    # 而这两种的处置完全不同（再等等 vs 去查谁把键按住了）。
    $names = @{ 0x01 = "左键"; 0x02 = "右键"; 0x10 = "Shift"; 0x11 = "Ctrl"; 0x12 = "Alt"; 0x1B = "Esc"; 0x5B = "LWin"; 0x5C = "RWin" }
    $down = @{}; $run = @{}; $maxRun = @{}
    foreach ($vk in $names.Keys) { $down[$vk] = 0; $run[$vk] = 0; $maxRun[$vk] = 0 }
    $samples = 0
    $rounds = 0
    $t0 = Get-Date
    while (((Get-Date) - $t0).TotalSeconds -lt $TimeoutSec) {
        $rounds++
        $busy = $false
        $sw = [Diagnostics.Stopwatch]::StartNew()
        # 一整窗采到底再判：一量到按下就 break，「按下次数」永远是每轮 1 次，量不出比例。
        while ($sw.ElapsedMilliseconds -lt $QuietMs) {
            $samples++
            foreach ($vk in $names.Keys) {
                if (([FcMods.Kb]::GetAsyncKeyState($vk) -band 0x8000) -ne 0) {
                    $down[$vk]++; $run[$vk]++
                    if ($run[$vk] -gt $maxRun[$vk]) { $maxRun[$vk] = $run[$vk] }
                    $busy = $true
                } else { $run[$vk] = 0 }
            }
            Start-Sleep -Milliseconds 60
        }
        if (-not $busy) { $script:FcQuietDiag = ""; return $true }
        Start-Sleep -Milliseconds 250
    }
    $who = foreach ($vk in $names.Keys) {
        if ($down[$vk] -gt 0) {
            "{0} 按下 {1}/{2} 次采样（最长连续 {3} 次）" -f $names[$vk], $down[$vk], $samples, $maxRun[$vk]
        }
    }
    # 一个键都没量到却等不到安静窗口，是闸自己的问题，不许把账算到用户头上。
    $script:FcQuietDiag = if ($who) { "看到的输入：" + ($who -join "；") + "（等了 $rounds 轮）" }
                          else { "等 $rounds 轮一个键都没量到按下——闸自己有问题，不是有人在输入" }
    return $false
}
# 合成一次组合键的按下与抬起：二次实例把参数转发给主实例的消息专用窗（HWND_MESSAGE），
# 由它喂进手势引擎的 KeySink——与低级键盘钩子同一个入口，只是不产生 OS 级输入。
# 用户正用电脑时也能跑完整链路，按键不会落到前台的其他软件上。
# 前提：主实例以 FC_TEST_NO_FG=1 启动（发行进程不接受合成键）。
function Invoke-FcSynthetic {
    param([string]$Exe, [string]$Spec, [int]$HoldMs)
    Start-Process $Exe -ArgumentList @('--synthetic', $Spec, "$HoldMs") -WindowStyle Hidden | Out-Null
}

# 以测试形态拉起主实例：FC_TEST_NO_FG=1 让它不抢前台，并允许受理 --synthetic 合成键。
function Start-FcTestApp {
    param([string]$Exe)
    $env:FC_TEST_NO_FG = "1"
    return Start-Process $Exe -PassThru
}
# 把一次裸键直投编辑器分派表。编辑器窗未激活时 WPF 不会把 PostMessage 的键路由进来，
# 而激活窗口本身就是一次抢前台——键盘焦点与前台在 Win32 里绑死。这条通道经主实例的
# 消息专用窗进入，落进与真实按键同一个分派入口。前提同样是主实例带 FC_TEST_NO_FG=1。
function Invoke-FcEditorKey {
    param([string]$Exe, [int]$Vk)
    Start-Process $Exe -ArgumentList @('--editorkey', "$Vk") -WindowStyle Hidden | Out-Null
}
