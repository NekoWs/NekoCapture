$shot="D:/Backup/Capture/tools/Shot/bin/x64/Debug/net8.0-windows/Shot.exe"
$app='D:/Backup/Capture/src/FrameCapture/bin/x64/Debug/net8.0-windows/FrameCapture.exe'
function SetTheme($t){
  $dir=Join-Path $env:APPDATA 'FrameCapture'; $cfg=Join-Path $dir 'settings.json'
  if(!(Test-Path $dir)){ New-Item -ItemType Directory -Path $dir -Force | Out-Null }
  if(Test-Path $cfg){ $j=Get-Content $cfg -Raw | ConvertFrom-Json } else { $j=[pscustomobject]@{} }
  $j | Add-Member -Force NoteProperty Theme $t
  $j | ConvertTo-Json -Depth 8 | Set-Content $cfg -Encoding utf8
}
function Cycle($t){
    Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep 0.8
    SetTheme $t
    Start-Process $app -ArgumentList '--settings'
    Start-Sleep 3
    & $shot shot "D:/Backup/Capture/scripts/p3-$t.png" | Out-Null
    & $shot keyw "FrameCapture Settings" 27 | Out-Null
    Start-Sleep 0.8
    & $shot hk 71 1000 | Out-Null
    Start-Sleep 1.6
    & $shot shot "D:/Backup/Capture/scripts/p3-$t-editor.png" | Out-Null
    & $shot keyw "FrameCapture Editor" 27 | Out-Null
    Start-Sleep 0.8
}
Cycle 'light'
Cycle 'dark'
Get-Process FrameCapture -ErrorAction SilentlyContinue | Stop-Process -Force
SetTheme 'system'
"polish3 done" | Out-File "D:/Backup/Capture/scripts/polish3.flag"
