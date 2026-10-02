$ErrorActionPreference = "Continue"
$root = "D:\Backup\Capture\tools\elev"
$pidFile = "$root\elevator.pid"
$log = "$root\elevator.log"
Set-Content -Path $pidFile -Value $PID
"[$(Get-Date -Format HH:mm:ss.fff)] elevator pid=$PID elevated=$(([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))" | Add-Content $log
while ($true) {
  try {
    $q = Get-ChildItem "$root\queue\*.ps1" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime
    foreach ($f in $q) {
      $run = Join-Path $root ("running_" + $f.Name)
      Move-Item $f.FullName $run -Force
      "[{0}] run {1}" -f (Get-Date -Format HH:mm:ss.fff), $f.Name | Add-Content $log
      $out = ""
      try {
        $out = & powershell -NoProfile -ExecutionPolicy Bypass -File $run 2>&1 | Out-String
      } catch { $out = "THROWN: $_" }
      $outFile = Join-Path $root ("results\" + $f.Name.Replace(".ps1", "") + ".out")
      Set-Content -Path $outFile -Value ("EXIT=OK`n" + $out)
      "[{0}] done {1}" -f (Get-Date -Format HH:mm:ss.fff), $f.Name | Add-Content $log
      Remove-Item $run -Force -ErrorAction SilentlyContinue
    }
  } catch {
    "[{0}] loop-err {1}" -f (Get-Date -Format HH:mm:ss.fff), $_ | Add-Content $log
  }
  Start-Sleep -Milliseconds 400
}
