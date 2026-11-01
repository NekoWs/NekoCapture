Add-Type -TypeDefinition @"
using System;using System.Runtime.InteropServices;using System.Text;using System.Collections.Generic;
public class WQ{
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
 [DllImport("user32.dll")] public static extern int GetWindowTextW(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern int GetWindowLongW(IntPtr h,int i);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 delegate bool EnumProc(IntPtr h,IntPtr l);
 public static string Go(){
  var sb2=new StringBuilder();
  EnumWindows((h,l)=>{
   var sb=new StringBuilder(512); GetWindowTextW(h,sb,512); var t=sb.ToString();
   if(t.Contains("Region")||t.Contains("NekoCapture")){
    var ex=(GetWindowLongW(h,-20) & 0x80000)!=0;
    sb2.AppendLine((IsWindowVisible(h)?"vis":"hid")+(ex?" top ":" non ")+"| "+t);
   }
   return true;
  },IntPtr.Zero);
  var fg=GetForegroundWindow(); var fsb=new StringBuilder(512); GetWindowTextW(fg,fsb,512);
  sb2.AppendLine("FG: "+fsb.ToString());
  return sb2.ToString();
 }
}
"@
[WQ]::Go()
