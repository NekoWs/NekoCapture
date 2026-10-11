# 冒烟：启动应用、发送 Ctrl+Shift+G（长按 500ms）、检测编辑器窗口、截屏
param([switch]$KeepRunning)
$ErrorActionPreference='Stop'
$env:FC_DEBUG="1"
Remove-Item "$env:APPDATA\FrameCapture	race.log" -ErrorAction SilentlyContinue
$exe = "D:\Backup\Capture\src\FrameCapture\bin\x64\Debug\net8.0-windows10.0.19041.0\FrameCapture.exe"
Add-Type -Namespace W32 -Name U32 -MemberDefinition @'
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, System.UIntPtr extra);
[DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern System.IntPtr FindWindowW(string cls, string title);
[DllImport("user32.dll")] public static extern bool GetCursorInfo(out CURSORINFO pci);
[StructLayout(LayoutKind.Sequential)] public struct CURSORINFO { public int cb; public int flags; public System.IntPtr hCursor; public POINTL ptScreenPos; }
[StructLayout(LayoutKind.Sequential)] public struct POINTL { public long x, y; }
'@
Start-Process -FilePath $exe
Start-Sleep 2.5
"hotkey: press"
[W32.U32]::keybd_event(0x11,0,0,[UIntPtr]::Zero)   # Ctrl down
[W32.U32]::keybd_event(0x10,0,0,[UIntPtr]::Zero)   # Shift down
[W32.U32]::keybd_event(0x47,0,0,[UIntPtr]::Zero)   # G down
Start-Sleep -Milliseconds 600
[W32.U32]::keybd_event(0x47,0,2,[UIntPtr]::Zero)   # G up
[W32.U32]::keybd_event(0x10,0,2,[UIntPtr]::Zero)   # Shift up
[W32.U32]::keybd_event(0x11,0,2,[UIntPtr]::Zero)   # Ctrl up
Start-Sleep 1.5
$hwnd=[W32.U32]::FindWindowW($null,"FrameCapture Editor")
"editor hwnd = $hwnd"
# 移动光标到屏幕中间并截屏
Add-Type -AssemblyName System.Drawing,System.Windows.Forms
$bounds=[System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp=New-Object System.Drawing.Bitmap $bounds.Width,$bounds.Height
$g=[System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($bounds.Location,[System.Drawing.Point]::Empty,$bounds.Size)
$out="D:\Backup\Capture\scripts\smoke-shot.png"
$bmp.Save($out)
"shot: $out"
if (-not $KeepRunning) { Stop-Process -Name FrameCapture -Force -ErrorAction SilentlyContinue }
