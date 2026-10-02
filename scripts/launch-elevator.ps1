# 一次性 UAC：以管理员启动提权载体
$e = "D:\Backup\Capture\tools\elev\elevator.ps1"
Start-Process -FilePath "powershell.exe" -ArgumentList "-NoProfile","-ExecutionPolicy","Bypass","-WindowStyle","Hidden","-File","`"$e`"" -Verb RunAs
"UAC triggered - please approve the prompt"
