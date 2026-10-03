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

function Get-FcCaptureHotkey {
    $v = (Get-FcSettings).CaptureHotkey
    if ([string]::IsNullOrWhiteSpace($v)) { return 'Ctrl+Shift+G' }  # 与 AppSettings 默认一致
    return $v
}

function Get-FcScopeHotkey {
    $v = (Get-FcSettings).ScopeHotkey
    if ([string]::IsNullOrWhiteSpace($v)) { return 'Ctrl+Shift+R' }
    return $v
}

function Invoke-FcHotkey {
    param([string]$Shot, [string]$Spec, [int]$HoldMs)
    $p = Get-FcHotkeyParts $Spec
    Write-Verbose ("hkm {0} {1} {2} {3} {4} {5}  # {6}" -f $p.vk, $p.ctrl, $p.shift, $p.alt, $p.win, $HoldMs, $Spec)
    & $Shot hkm $p.vk $p.ctrl $p.shift $p.alt $p.win $HoldMs
}
