Add-Type -TypeDefinition @"
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Collections.Generic;
public class WE3 {
  delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EP p, IntPtr l);
  [DllImport("user32.dll")] static extern void GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out R r);
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Rr, B; }
  public static List<string> All() {
    var outp = new List<string>();
    EnumWindows((h, l) => {
      var sb = new StringBuilder(512);
      GetWindowTextW(h, sb, 512);
      string t = sb.ToString();
      if (t.StartsWith("FrameCapture")) {
        R r; GetWindowRect(h, out r);
        outp.Add(t + " vis=" + IsWindowVisible(h) + " " + r.L + "," + r.T + " " + (r.Rr - r.L) + "x" + (r.B - r.T));
      }
      return true;
    }, IntPtr.Zero);
    return outp;
  }
}
"@
[WE3]::All() | ForEach-Object { $_ }
