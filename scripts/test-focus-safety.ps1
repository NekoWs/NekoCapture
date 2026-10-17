# 焦点安全自检：全局手势键注入期间，自有窗文本框不得被漏入任何字符；捕获照常发生。
# 本项是全套回归里唯一必须真注入 OS 级按键的：它测的就是「全局热键会不会漏字符进前台应用」，
# 不经真实钩子就测不到这件事。Shot swallow 内置注入 Ctrl+Shift+G（按住 900ms）。
# 因此它带安静窗口闸：连续 3 秒无物理按键/鼠标键才开跑，等不到就 SKIP——绝不把键打进你正用的软件。
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "lib-hotkey.ps1")
$exe  = Join-Path $root "src/FrameCapture/bin/x64/Debug/net8.0-windows10.0.19041.0/FrameCapture.exe"
$shot = Join-Path $root "tools/Shot/bin/x64/Debug/net8.0-windows/Shot.exe"
if (!(Test-Path $exe)) { Write-Host "missing $exe"; exit 1 }
$cfg = Get-FcSettingsPath
Get-Process FrameCapture -EA SilentlyContinue | Stop-Process -Force   # 先杀，避免运行实例回写覆盖
Start-Sleep 0.5
$bak = Set-FcTestGestureConfig   # 钉成统一测试手势集（Ctrl+Shift+G 已绑定），结束（含各 SKIP 出口）一律还原
try {
    $p = Start-Process $exe -PassThru
    Start-Sleep 1.5
    # 安静窗口闸：物理输入状态说了算。前台类名猜不出正在编辑器里打字的人。
    if (-not (Wait-FcQuiet -TimeoutSec 25 -QuietMs 3000)) {
        Write-Host "SKIP: 用户持续有输入，跳过本项注入测试（SKIP ≠ FAIL）"
        if (!$p.HasExited) { $p.Kill() }
        exit 0
    }
    $out = & $shot swallow 2>&1 | Out-String
    Start-Sleep 0.8
    $ed = & $shot rect "FrameCapture Editor" 2>$null
    Invoke-FcEditorKey $exe 27
    Start-Sleep 0.8
    if (!$p.HasExited) { $p.Kill() }
    $tr = Get-Content (Join-Path $root "src/fc-trace.log") -Tail 40 | Out-String
    if (-not ($tr -match "hook key=71")) {
        Write-Host "SKIP: 注入按键被拦截（游戏/反外挂），hook 无事件；关闭游戏后重跑"
        exit 0
    }
    $clean = $out -match "SWALLOW text=\[ABCDEF\]"
    # 捕获发生的证据：编辑器出现，或 trace 里有手势动作落地（录制/选区）
    $captured = ($ed -match "^rect") -or ($tr -match "gesture act (record\.start|capture\.selection)")
    Write-Host $out.Trim()
    Write-Host "editor=[$ed]"
    if ($clean -and $captured) { Write-Host "FOCUS-SAFETY PASS"; exit 0 }
    Write-Host "FOCUS-SAFETY FAIL"; exit 1
}
finally {
    Get-Process FrameCapture -EA SilentlyContinue | Stop-Process -Force
    if ($bak) { Set-Content $cfg -Value $bak -Encoding UTF8 }
}
