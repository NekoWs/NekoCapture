# 自定义热键端到端测试：改配置→旧键失效/新键生效→恢复默认→回归
# 依赖：tools/Shot 的 hk（Ctrl+Shift+vk）与 hkm（自定义修饰键 vk c s a w hold）
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot
. (Join-Path $PSScriptRoot "lib-hotkey.ps1")
$exe = Join-Path $root "src\FrameCapture\bin\x64\Debug\net8.0-windows\FrameCapture.exe"
$shot = Join-Path $root "tools\Shot\bin\x64\Debug\net8.0-windows\Shot.exe"
$cfg = Join-Path $env:APPDATA "FrameCapture\settings.json"
$err = Join-Path $env:APPDATA "FrameCapture\error.log"
$e0 = if (Test-Path $err) { (Get-Item $err).Length } else { 0 }
$bak = if (Test-Path $cfg) { Get-Content $cfg -Raw } else { $null }

New-Item -ItemType Directory -Force -Path (Split-Path $cfg) | Out-Null
$s = if ($bak) { $bak | ConvertFrom-Json } else { [pscustomobject]@{} }
$s | Add-Member -Force CaptureHotkey "Ctrl+Alt+G"
$s | Add-Member -Force ScopeHotkey "Ctrl+Alt+R"
$s | ConvertTo-Json | Set-Content $cfg -Encoding UTF8

Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 0.6
Start-Process $exe; Start-Sleep 2

# 注意：这里的 hk 71（Ctrl+Shift+G）是故意的——刚把热键改成 Ctrl+Alt+G，断言"旧默认已失效"，勿改成派生
& $shot hk 71 700 | Out-Null; Start-Sleep 1.5
$old = & $shot rect "FrameCapture Editor" 2>$null; "old_hotkey=[$old]"

& $shot hkm 71 1 0 1 0 900 | Out-Null; Start-Sleep 2
$new = & $shot rect "FrameCapture Editor" 2>$null; "new_capture_hotkey=[$new]"
& $shot keyw "FrameCapture Editor" 27 2>$null | Out-Null; Start-Sleep 0.5

& $shot hkm 82 1 0 1 0 400 | Out-Null; Start-Sleep 1
$reg = & $shot rect "FrameCapture Region" 2>$null; "new_scope_hotkey=[$reg]"
& $shot keyw "FrameCapture Region" 27 2>$null | Out-Null; Start-Sleep 0.5

if ($bak) { Set-Content $cfg -Value $bak -Encoding UTF8 } else { Remove-Item $cfg -ErrorAction SilentlyContinue }
Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 0.6
Start-Process $exe; Start-Sleep 2
# 恢复的是用户原设置，其捕获键未必是默认 Ctrl+Shift+G——按恢复值派生注入
$capBack = if ($bak) { ($bak | ConvertFrom-Json).CaptureHotkey } else { $null }
if ([string]::IsNullOrWhiteSpace($capBack)) { $capBack = 'Ctrl+Shift+G' }
"restored_capture_hotkey=$capBack"
Invoke-FcHotkey $shot $capBack 700 | Out-Null; Start-Sleep 1.8
$def = & $shot rect "FrameCapture Editor" 2>$null; "restored_default=[$def]"
& $shot keyw "FrameCapture Editor" 27 2>$null | Out-Null
Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force

$e1 = if (Test-Path $err) { (Get-Item $err).Length } else { 0 }
"err_delta=$($e1 - $e0)"
$ok = ("$old" -match "not-found") -and ("$new" -notmatch "not-found") -and ("$reg" -notmatch "not-found") -and ("$def" -notmatch "not-found") -and (($e1 - $e0) -eq 0)
if ($ok) { "HOTKEY-E2E PASS" } else { "HOTKEY-E2E FAIL"; exit 1 }
