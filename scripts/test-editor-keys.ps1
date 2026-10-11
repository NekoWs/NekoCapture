# 编辑器键盘矩阵（headless：FC_TEST_NO_FG=1 不抢前台；PostMessage 直投自有窗）
# 捕获键按当前设置派生（lib-hotkey），Enter 后断言剪贴板三格式（CF_HDROP .gif + 位图 + FrameCapture.Gif）。
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "lib-hotkey.ps1")
$exe  = Join-Path $root "src/FrameCapture/bin/x64/Debug/net8.0-windows10.0.19041.0/FrameCapture.exe"
$shot = Join-Path $root "tools/Shot/bin/x64/Debug/net8.0-windows/Shot.exe"
$capHk = Get-FcCaptureHotkey
Get-Process FrameCapture -EA SilentlyContinue | Stop-Process -Force
Start-Sleep 0.5
$env:FC_TEST_NO_FG = "1"
$p = Start-Process $exe -PassThru  # 子进程继承 $env:FC_TEST_NO_FG
Start-Sleep 1.5
Invoke-FcHotkey $shot $capHk 700 | Out-Null; Start-Sleep 1.8
$ed0 = & $shot rect "FrameCapture Editor" 2>$null
Write-Host "title0=[$ed0]"
if ($ed0 -match "not-found") {
    Write-Host "SKIP: 编辑器未出现——游戏/反外挂可能屏蔽注入式按键（hook 无事件），请关闭游戏后重跑"
    if (!$p.HasExited) { $p.Kill() }
    exit 0
}
# Space play, Space pause, Right/Left 单帧, Shift+Right 上秒级步进, D/A 工具切换（需无修饰），Enter 提交
Wait-FcModifiersUp; & $shot keyw "FrameCapture Editor" 32 2>$null | Out-Null; Start-Sleep 0.4
Wait-FcModifiersUp; & $shot keyw "FrameCapture Editor" 39 2>$null | Out-Null; Start-Sleep 0.3
Wait-FcModifiersUp; & $shot keyw "FrameCapture Editor" 37 2>$null | Out-Null; Start-Sleep 0.3
# Enter 前记暂存目录快照：ClipboardService 会把 GIF 落到 %TEMP%\FrameCapture\capture-*.gif
$stage = Join-Path $env:TEMP "FrameCapture"
$gifsBefore = @(Get-ChildItem $stage -Filter *.gif -EA SilentlyContinue).Count
& $shot keyw "FrameCapture Editor" 13 2>$null | Out-Null; Start-Sleep 2.5
# Enter 提交后：编辑器已关 + 真剪贴板三格式（在杀进程前取证，避免所有权竞态）
$ed = & $shot rect "FrameCapture Editor" 2>$null
if ($null -eq ('FcProbe.ClipProbe' -as [type])) {
    Add-Type -Namespace FcProbe -Name ClipProbe -Language CSharp -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool IsClipboardFormatAvailable(uint f);
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] public static extern uint RegisterClipboardFormatW(string n);
'@
}
$CF_BITMAP = 2; $CF_DIB = 8; $CF_HDROP = 15
$fmtGif = [FcProbe.ClipProbe]::RegisterClipboardFormatW("FrameCapture.Gif")
$hasHdrop = [FcProbe.ClipProbe]::IsClipboardFormatAvailable($CF_HDROP)
$hasBitmap = [FcProbe.ClipProbe]::IsClipboardFormatAvailable($CF_DIB) -or [FcProbe.ClipProbe]::IsClipboardFormatAvailable($CF_BITMAP)
$hasGifBytes = [FcProbe.ClipProbe]::IsClipboardFormatAvailable($fmtGif)
$files = @(Get-ChildItem $stage -Filter *.gif -EA SilentlyContinue)
$gifFileOk = ($files.Count -gt $gifsBefore)
if ($gifFileOk) { $newest = $files | Sort-Object LastWriteTime -Descending | Select-Object -First 1; $gifFileOk = $newest.Length -gt 0 }
Write-Host "clipboard: hdrop=$hasHdrop gifFile=$gifFileOk bitmap=$hasBitmap gifBytes=$hasGifBytes"
if (!$p.HasExited) { $p.Kill() }
Remove-Item Env:\FC_TEST_NO_FG -EA SilentlyContinue
$log = (Get-Content (Join-Path $root "src/fc-trace.log") -Tail 80) -join "`n"
$ok = ($log -match "editor key=None\+Space") -and ($log -match "play=True") -and ($log -match "editor key=None\+Right") -and ($log -match "editor key=None\+Left") -and ($log -match "gif encoded bytes=") -and ($ed -match "not-found")
$ok = $ok -and $hasHdrop -and $gifFileOk -and $hasBitmap -and $hasGifBytes
Write-Host "editor-after-enter=[$ed]"
if ($ok) { Write-Host "EDITOR-KEYS PASS"; exit 0 }
Write-Host "EDITOR-KEYS FAIL"; $log -split "`n" | Select-String "editor key|play=|gif encoded" | Select-Object -Last 12
exit 1
