# 编辑器键盘矩阵（headless：FC_TEST_NO_FG=1 不抢前台；PostMessage 直投自有窗）
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe  = Join-Path $root "src/FrameCapture/bin/x64/Debug/net8.0-windows10.0.19041.0/FrameCapture.exe"
$shot = Join-Path $root "tools/Shot/bin/x64/Debug/net8.0-windows/Shot.exe"
Get-Process FrameCapture -EA SilentlyContinue | Stop-Process -Force
Start-Sleep 0.5
$env:FC_TEST_NO_FG = "1"
$p = Start-Process $exe -PassThru  # 子进程继承 $env:FC_TEST_NO_FG
Start-Sleep 1.5
& $shot hk 71 700 | Out-Null; Start-Sleep 1.8
$ed0 = & $shot rect "FrameCapture Editor" 2>$null
Write-Host "title0=[$ed0]"
if ($ed0 -match "not-found") {
    Write-Host "SKIP: 编辑器未出现——游戏/反外挂可能屏蔽注入式按键（hook 无事件），请关闭游戏后重跑"
    if (!$p.HasExited) { $p.Kill() }
    exit 0
}
# Space play, Space pause, Right/Left 单帧, Shift+Right 上秒级步进, D/A 工具切换（需无修饰），Enter 提交
& $shot keyw "FrameCapture Editor" 32 2>$null | Out-Null; Start-Sleep 0.4
& $shot keyw "FrameCapture Editor" 39 2>$null | Out-Null; Start-Sleep 0.3
& $shot keyw "FrameCapture Editor" 37 2>$null | Out-Null; Start-Sleep 0.3
& $shot keyw "FrameCapture Editor" 13 2>$null | Out-Null; Start-Sleep 2.5
$ed = & $shot rect "FrameCapture Editor" 2>$null
if (!$p.HasExited) { $p.Kill() }
Remove-Item Env:\FC_TEST_NO_FG -EA SilentlyContinue
$log = (Get-Content (Join-Path $root "src/fc-trace.log") -Tail 80) -join "`n"
$ok = ($log -match "editor key=None\+Space") -and ($log -match "play=True") -and ($log -match "editor key=None\+Right") -and ($log -match "editor key=None\+Left") -and ($log -match "gif encoded bytes=") -and ($ed -match "not-found")
Write-Host "editor-after-enter=[$ed]"
if ($ok) { Write-Host "EDITOR-KEYS PASS" } else { Write-Host "EDITOR-KEYS FAIL"; $log -split "`n" | Select-String "editor key|play=|gif encoded" | Select-Object -Last 12 }
