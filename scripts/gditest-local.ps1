Add-Type -Namespace GD -Name T -MemberDefinition @'
public class T {
 [System.Runtime.InteropServices.DllImport("user32.dll",SetLastError=true)] public static extern System.IntPtr GetDC(System.IntPtr h);
 [System.Runtime.InteropServices.DllImport("user32.dll",SetLastError=true)] public static extern int ReleaseDC(System.IntPtr h, System.IntPtr dc);
 [System.Runtime.InteropServices.DllImport("gdi32.dll",SetLastError=true)] public static extern System.IntPtr CreateCompatibleDC(System.IntPtr dc);
 [System.Runtime.InteropServices.DllImport("gdi32.dll",SetLastError=true)] public static extern System.IntPtr CreateCompatibleBitmap(System.IntPtr dc, int w, int h);
 [System.Runtime.InteropServices.DllImport("gdi32.dll",SetLastError=true)] public static extern System.IntPtr SelectObject(System.IntPtr dc, System.IntPtr o);
 [System.Runtime.InteropServices.DllImport("gdi32.dll",SetLastError=true)] public static extern bool BitBlt(System.IntPtr d,int dx,int dy,int w,int h,System.IntPtr s,int sx,int sy,uint rop);
 [System.Runtime.InteropServices.DllImport("gdi32.dll",SetLastError=true)] public static extern int GetDIBits(System.IntPtr hdc, System.IntPtr hbmp, uint first, uint lines, byte[] bmi, byte[] bits, uint usage);
 [System.Runtime.InteropServices.DllImport("gdi32.dll",SetLastError=true)] public static extern bool DeleteDC(System.IntPtr dc);
 [System.Runtime.InteropServices.DllImport("gdi32.dll",SetLastError=true)] public static extern bool DeleteObject(System.IntPtr o);
}
'@
function TryCap($w,$h) {
  $hsrc=[GD.T]::GetDC([System.IntPtr]::Zero)
  $dc=[GD.T]::CreateCompatibleDC($hsrc)
  $bmp=[GD.T]::CreateCompatibleBitmap($hsrc,$w,$h)
  [void][GD.T]::SelectObject($dc,$bmp)
  $ok=[GD.T]::BitBlt($dc,0,0,$w,$h,$hsrc,0,0,0xCC0020)
  $bmi=New-Object byte[] 40
  [BitConverter]::GetBytes(40).CopyTo($bmi,0)
  [BitConverter]::GetBytes($w).CopyTo($bmi,4)
  [BitConverter]::GetBytes(-$h).CopyTo($bmi,8)
  [BitConverter]::GetBytes([uint16]1).CopyTo($bmi,12)
  [BitConverter]::GetBytes([uint16]32).CopyTo($bmi,14)
  $dst=New-Object byte[] ($w*$h*4)
  $got=[GD.T]::GetDIBits($dc,$bmp,0,[uint32]$h,$bmi,$dst,0)
  $err=[Runtime.InteropServices.Marshal]::GetLastWin32Error()
  "cap ${w}x${h}: bitblt=$ok getdibits=$got err=$err sum=" + (($dst | Select-Object -First 4096 | Measure-Object -Sum).Sum)
  [void][GD.T]::DeleteDC($dc); [void][GD.T]::DeleteObject($bmp); [void][GD.T]::ReleaseDC([System.IntPtr]::Zero,$hsrc)
}
TryCap 800 600
