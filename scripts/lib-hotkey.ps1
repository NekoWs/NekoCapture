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

function Get-FcSettings {
    $cfg = Join-Path $env:APPDATA 'FrameCapture\settings.json'
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
#   Ctrl+Shift+G 轻点→选区+截图（弹选区窗）；按住→起录，松手→停止并进编辑器（OnComplete edit.lastvideo）
#   Ctrl+Shift+R 组合键→选区
# 返回原 settings.json 原文（调用方负责结束还原）。
function Set-FcTestGestureConfig {
    param([int]$RegionX = -1, [int]$RegionY = -1, [int]$RegionW = -1, [int]$RegionH = -1)
    $cfg = Join-Path $env:APPDATA 'FrameCapture\settings.json'
    New-Item -ItemType Directory -Force -Path (Split-Path $cfg) | Out-Null
    $bak = if (Test-Path $cfg) { Get-Content $cfg -Raw } else { $null }
    $s = if ($bak) { $bak | ConvertFrom-Json } else { [pscustomobject]@{} }
    $bindings = @(
        [pscustomobject]@{ Id='t.cap.tap'; Enabled=$true; Chord='Ctrl+Shift+G'; Trigger='Tap';
            Actions=@('scope.select','capture.stored'); OnComplete=@(); OnFail=@(); Fallback='Stop'; Priority=0; DoubleClickMs=400; LongPressMs=250 },
        [pscustomobject]@{ Id='t.cap.hold'; Enabled=$true; Chord='Ctrl+Shift+G'; Trigger='Hold';
            Actions=@('record.start'); OnComplete=@(); OnFail=@(); Fallback='Stop'; Priority=0; DoubleClickMs=400; LongPressMs=250 },
        [pscustomobject]@{ Id='t.cap.holdrelease'; Enabled=$true; Chord='Ctrl+Shift+G'; Trigger='HoldRelease';
            Actions=@('record.stop'); OnComplete=@('edit.lastvideo'); OnFail=@(); Fallback='Stop'; Priority=0; DoubleClickMs=400; LongPressMs=250 },
        [pscustomobject]@{ Id='t.scope'; Enabled=$true; Chord='Ctrl+Shift+R'; Trigger='Chord';
            Actions=@('scope.select'); OnComplete=@(); OnFail=@(); Fallback='Stop'; Priority=0; DoubleClickMs=400; LongPressMs=500 }
    )
    $gestures = [pscustomobject]@{ Enabled=$true; Bindings=$bindings; Seeded=$true }
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

# 幽灵修饰键护栏（R51）：keyw 是 PostMessage 裸键，而 WPF 修饰键读真实键盘状态——
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
'@
}
function Wait-FcModifiersUp {
    param([int]$TimeoutSec = 15)
    $t0 = Get-Date
    while ([FcMods.Kb]::AnyDown() -and ((Get-Date) - $t0).TotalSeconds -lt $TimeoutSec) { Start-Sleep -Milliseconds 120 }
}
