# 状态机模糊测试：随机手势键 + 乱键注入，验证存活/无错误/手势键仍可用
# 仅允许工具：--synthetic（合成键，经消息专用窗喂进引擎）、keyw（PostMessage 到自有窗口）、rect/shot。
# 禁止鼠标，禁止任何 OS 级注入——乱键压测要在用户正用电脑时也能跑完。
param([int]$Seed = 98765, [int]$Steps = 120)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot
. (Join-Path $PSScriptRoot "lib-hotkey.ps1")
$exe = Join-Path $root "src\FrameCapture\bin\x64\Debug\net8.0-windows10.0.19041.0\FrameCapture.exe"
$shot = Join-Path $root "tools\Shot\bin\x64\Debug\net8.0-windows\Shot.exe"
$err = Join-Path $env:APPDATA "FrameCapture\error.log"
$e0 = if (Test-Path $err) { (Get-Item $err).Length } else { 0 }
# R126 B2：钉成统一测试手势集，让乱键压测 tap/hold/holdrelease 状态机（轻点→选区窗 / 长按→录制→编辑器）。
$capHk = 'Ctrl+Shift+G'
$scopeHk = 'Ctrl+Shift+R'
"capture_hotkey=$capHk scope_hotkey=$scopeHk"

Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 0.6
$bak = Set-FcTestGestureConfig   # 先杀后写
try {
Start-FcTestApp $exe | Out-Null
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
        if ($pick -eq 0) { Invoke-FcSynthetic $exe $capHk $rnd.Next(200, 1500) }
        elseif ($pick -eq 1) { Invoke-FcSynthetic $exe $scopeHk 300 }
        else { Invoke-FcSynthetic $exe 'Ctrl+Shift+K' 300 }  # 未绑定组合，应无副作用
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
        Invoke-FcSynthetic $exe $capHk 120
        if ($rnd.Next(3) -eq 0) { Invoke-FcSynthetic $exe $capHk 100 }
    }
    Start-Sleep -Milliseconds ($rnd.Next(30, 180))
}

# 受控收尾：多层 Esc（最多 6 层，跳过对话框）
for ($k = 0; $k -lt 6; $k++) {
    Invoke-FcEditorKey $exe 27
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
# 断言 3：收尾后手势键仍可用 → 再按住一次捕获键应出编辑器（长按→录制→edit.lastvideo）
Invoke-FcSynthetic $exe $capHk 900
Start-Sleep 2.2
$ed = & $shot rect "FrameCapture Editor" 2>$null
$pk = & $shot rect "FrameCapture Region" 2>$null
Invoke-FcEditorKey $exe 27
& $shot keyw "FrameCapture Region" 27 | Out-Null
Start-Sleep 0.4
Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force

# 手势键仍可用 = 出现编辑器（长按录制）或选区窗（时序抖动落到轻点）任一
$hotkeyWorks = ("$ed" -notmatch "not-found") -or ("$pk" -notmatch "not-found")
$ok = [bool]$alive -and ($e1 - $e0) -eq 0 -and $hotkeyWorks
"alive=$alive err_delta=$($e1 - $e0) final_hotkey=[ed=$ed pk=$pk]"
if ($ok) { "FUZZ-STATE MACHINE PASS ($Steps steps, seed=$Seed)" } else { "FUZZ FAIL"; exit 1 }
}
finally {
    Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-Item Env:\FC_TEST_NO_FG -EA SilentlyContinue
    if ($bak) { Set-Content (Join-Path $env:APPDATA "FrameCapture\settings.json") -Value $bak -Encoding UTF8 }
}
