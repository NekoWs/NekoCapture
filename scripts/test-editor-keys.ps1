# 编辑器键盘矩阵（headless：FC_TEST_NO_FG=1 不抢前台；键经 --editorkey 直投编辑器分派表）
# 进入编辑器走合成键——统一测试手势集下按住 Ctrl+Shift+G → 录制 → edit.lastvideo → 编辑器。
# 全程零 OS 级输入：合成键经消息专用窗喂进引擎的 KeySink，用户正用电脑时也能跑。
# Enter 后断言剪贴板三格式（CF_HDROP .gif + 位图 + FrameCapture.Gif）。结束一律还原用户设置。
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "lib-hotkey.ps1")
$exe  = Join-Path $root "src/FrameCapture/bin/x64/Debug/net8.0-windows10.0.19041.0/FrameCapture.exe"
$shot = Join-Path $root "tools/Shot/bin/x64/Debug/net8.0-windows/Shot.exe"
$capHk = 'Ctrl+Shift+G'
Get-Process FrameCapture -EA SilentlyContinue | Stop-Process -Force
Start-Sleep 0.5
$bak = Set-FcTestGestureConfig   # 先杀后写，避免运行实例回写覆盖
try {
    $env:FC_TEST_NO_FG = "1"
    $p = Start-Process $exe -PassThru  # 子进程继承 $env:FC_TEST_NO_FG
    Start-Sleep 1.5
    Invoke-FcSynthetic $exe $capHk 700; Start-Sleep 2.2
    $ed0 = & $shot rect "FrameCapture Editor" 2>$null
    Write-Host "title0=[$ed0]"
    # 合成键经消息通道抵达引擎，不经任何 OS 级注入——没有「被游戏屏蔽」这类外因可怪，
    # 编辑器没出现就是链路断了，必须红。
    if ($ed0 -match "not-found") {
        Write-Host "FAIL: 编辑器未出现——合成键未经消息通道抵达引擎（主实例需带 FC_TEST_NO_FG=1 启动）"
        if (!$p.HasExited) { $p.Kill() }
        exit 1
    }
    # Space play, Space pause, Right/Left 单帧, Shift+Right 上秒级步进, D/A 工具切换（需无修饰），Enter 提交
    Invoke-FcEditorKey $exe 32; Start-Sleep 0.4
    Invoke-FcEditorKey $exe 39; Start-Sleep 0.3
    Invoke-FcEditorKey $exe 37; Start-Sleep 0.3
    # Enter 前记暂存目录快照：ClipboardService 会把 GIF 落到 %TEMP%\FrameCapture\capture-*.gif
    $stage = Join-Path $env:TEMP "FrameCapture"
    $gifsBefore = @(Get-ChildItem $stage -Filter *.gif -EA SilentlyContinue).Count
    Invoke-FcEditorKey $exe 13; Start-Sleep 2.5
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
    $log = (Get-Content (Join-Path $root "src/fc-trace.log") -Tail 80) -join "`n"
    $ok = ($log -match "editor key=None\+Space") -and ($log -match "play=True") -and ($log -match "editor key=None\+Right") -and ($log -match "editor key=None\+Left") -and ($log -match "gif encoded bytes=") -and ($ed -match "not-found")
    $ok = $ok -and $hasHdrop -and $gifFileOk -and $hasBitmap -and $hasGifBytes
    Write-Host "editor-after-enter=[$ed]"
    if ($ok) { Write-Host "EDITOR-KEYS PASS"; exit 0 }
    Write-Host "EDITOR-KEYS FAIL"; $log -split "`n" | Select-String "editor key|play=|gif encoded" | Select-Object -Last 12
    exit 1
}
finally {
    Get-Process FrameCapture -EA SilentlyContinue | Stop-Process -Force
    Remove-Item Env:\FC_TEST_NO_FG -EA SilentlyContinue
    if ($bak) { Set-Content (Get-FcSettingsPath) -Value $bak -Encoding UTF8 }
}
