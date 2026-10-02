# 值守：等前台不再是游戏/注入被拦截时，补跑 editor-keys 与 focus-safety，结果写 test-out/
$ErrorActionPreference = "Continue"
$root = Split-Path -Parent $PSScriptRoot
$shot = Join-Path $root "tools/Shot/bin/x64/Debug/net8.0-windows/Shot.exe"
$out = Join-Path $root "test-out"
New-Item -ItemType Directory -Force -Path $out | Out-Null
$deadline = (Get-Date).AddHours(3)
while ((Get-Date) -lt $deadline) {
    $fg = & $shot fg 2>$null
    if ($fg -notmatch "UnityWndClass") {
        # 游戏不在前台：再验证注入是否可达（反外挂可能仍在驻留）
        $exe = Join-Path $root "src/FrameCapture/bin/x64/Debug/net8.0-windows10.0.19041.0/FrameCapture.exe"
        $userRunning = Get-Process FrameCapture -EA SilentlyContinue
        $p = $null
        if (-not $userRunning) {
            $p = Start-Process $exe -PassThru; Start-Sleep 1.3
        } else {
            # 用户实例在运行：物理/注入事件都会进它的 trace；仅探测，绝不杀用户实例
            & $shot hk 71 200 | Out-Null; Start-Sleep 0.8
            $tr = (Get-Content (Join-Path $root "src/fc-trace.log") -Tail 8) -join "`n"
            if ($tr -match "hook key=71") { break }
            Start-Sleep 30
            continue
        }
        & $shot hk 71 200 | Out-Null; Start-Sleep 0.8
        $tr = (Get-Content (Join-Path $root "src/fc-trace.log") -Tail 8) -join "`n"
        if ($p.HasExited -eq $false) { Start-Sleep 0 }
        if ($tr -match "hook key=71") { if (!$p.HasExited) { $p.Kill() }; break }
        if (!$p.HasExited) { $p.Kill() }
    }
    Start-Sleep 30
}
if ((Get-Date) -ge $deadline) { "TIMEOUT: 前台一直不可用" | Set-Content (Join-Path $out "injection-tests.txt"); exit 0 }
"== editor-keys ==" | Set-Content (Join-Path $out "injection-tests.txt")
& (Join-Path $root "scripts/test-editor-keys.ps1") 2>&1 | Tee-Object -FilePath (Join-Path $out "injection-tests.txt") -Append | Out-Null
"== focus-safety ==" | Add-Content (Join-Path $out "injection-tests.txt")
& (Join-Path $root "scripts/test-focus-safety.ps1") 2>&1 | Tee-Object -FilePath (Join-Path $out "injection-tests.txt") -Append | Out-Null
"== hotkey-e2e ==" | Add-Content (Join-Path $out "injection-tests.txt")
& (Join-Path $root "scripts/test-hotkey-e2e.ps1") 2>&1 | Tee-Object -FilePath (Join-Path $out "injection-tests.txt") -Append | Out-Null
"== tap-semantics ==" | Add-Content (Join-Path $out "injection-tests.txt")
& (Join-Path $root "scripts/test-tap-semantics.ps1") 2>&1 | Tee-Object -FilePath (Join-Path $out "injection-tests.txt") -Append | Out-Null
"== fuzz ==" | Add-Content (Join-Path $out "injection-tests.txt")
& (Join-Path $root "scripts/fuzz-statemachine.ps1") 2>&1 | Tee-Object -FilePath (Join-Path $out "injection-tests.txt") -Append | Out-Null
"ALL DONE $(Get-Date -Format HH:mm:ss)" | Add-Content (Join-Path $out "injection-tests.txt")
