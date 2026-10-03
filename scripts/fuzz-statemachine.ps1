# 状态机模糊测试：随机热键 + 乱键注入，验证存活/无错误/热键仍可用
# 仅允许工具：hk（全局热键）、keyw（PostMessage 到自有窗口）、rect/shot。禁止鼠标。
param([int]$Seed = 98765, [int]$Steps = 120)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot
. (Join-Path $PSScriptRoot "lib-hotkey.ps1")
$exe = Join-Path $root "src\FrameCapture\bin\x64\Debug\net8.0-windows\FrameCapture.exe"
$shot = Join-Path $root "tools\Shot\bin\x64\Debug\net8.0-windows\Shot.exe"
$err = Join-Path $env:APPDATA "FrameCapture\error.log"
$e0 = if (Test-Path $err) { (Get-Item $err).Length } else { 0 }
$capHk = Get-FcCaptureHotkey
$scopeHk = Get-FcScopeHotkey
"capture_hotkey=$capHk scope_hotkey=$scopeHk"

Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 0.6
Start-Process $exe
Start-Sleep 2

$rnd = [System.Random]::new($Seed)
# VK 池：方向/编辑/字母工具键/空格/倍频/撤销重做（避开 Enter=提交 与 Esc 收尾，它们由脚本受控使用）
$vks = 32, 0x25, 0x26, 0x27, 0x28, 0x24, 0x23, 0x2D, 0xDE, 0xBB, 0xBD, 0x59, 0x44, 0x4C, 0x4F, 0x41, 0x50, 0x30
$titles = "FrameCapture Editor", "FrameCapture Settings", "FrameCapture Help", "FrameCapture Region"

for ($i = 0; $i -lt $Steps; $i++) {
    $roll = $rnd.Next(100)
    if ($roll -lt 18) {
        # 随机全局动作：捕获（长按随机 0.2–1.5s）或设置范围或开设置窗
        $pick = $rnd.Next(3)
        if ($pick -eq 0) { Invoke-FcHotkey $shot $capHk $rnd.Next(200, 1500) | Out-Null }
        elseif ($pick -eq 1) { Invoke-FcHotkey $shot $scopeHk 300 | Out-Null }
        else { & $shot hk 187 300 | Out-Null }  # 未注册组合，应无副作用
        if ($roll -lt 4 -and $rnd.Next(2) -eq 0) { & $shot keyw "FrameCapture Region" 27 | Out-Null }
    }
    elseif ($roll -lt 75) {
        # 随机窗口乱键（窗口不存在时 keyw 打印 not-found，无害）
        $t = $titles | Select-Object -Index ($rnd.Next($titles.Count))
        $v = $vks | Select-Object -Index ($rnd.Next($vks.Count))
        & $shot keyw $t $v | Out-Null
    }
    else {
        # 连发捕获热键轰炸（含编辑器未关时的重入）
        Invoke-FcHotkey $shot $capHk 120 | Out-Null
        if ($rnd.Next(3) -eq 0) { Invoke-FcHotkey $shot $capHk 100 | Out-Null }
    }
    Start-Sleep -Milliseconds ($rnd.Next(30, 180))
}

# 受控收尾：多层 Esc（最多 6 层，跳过对话框）
for ($k = 0; $k -lt 6; $k++) {
    & $shot keyw "FrameCapture Editor" 27 | Out-Null
    & $shot keyw "FrameCapture Region Picker" 27 | Out-Null
    & $shot keyw "FrameCapture Settings" 27 | Out-Null
    & $shot keyw "FrameCapture Help" 27 | Out-Null
    Start-Sleep -Milliseconds 120
}
Start-Sleep 1

# 断言 1：进程存活
$alive = Get-Process FrameCapture -ErrorAction SilentlyContinue
# 断言 2：error.log 零增量
$e1 = if (Test-Path $err) { (Get-Item $err).Length } else { 0 }
# 断言 3：收尾后全局热键仍可用 → 再按一次捕获应出编辑器
Invoke-FcHotkey $shot $capHk 900 | Out-Null
Start-Sleep 2
$ed = & $shot rect "FrameCapture Editor" 2>$null
& $shot keyw "FrameCapture Editor" 27 | Out-Null
Start-Sleep 0.4
Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force

$ok = [bool]$alive -and ($e1 - $e0) -eq 0 -and ("$ed" -notmatch "not-found")
"alive=$alive err_delta=$($e1 - $e0) final_hotkey=[$ed]"
if ($ok) { "FUZZ-STATE MACHINE PASS ($Steps steps, seed=$Seed)" } else { "FUZZ FAIL"; exit 1 }
