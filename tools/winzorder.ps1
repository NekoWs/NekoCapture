Add-Type -TypeDefinition @"
using System;using System.Runtime.InteropServices;using System.Text;using System.Collections.Generic;
public class WZ{
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern int GetWindowLongW(IntPtr h,int i);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 public delegate bool EnumProc(IntPtr h,IntPtr l);
 public static string Go(){
  var sb2=new StringBuilder(); int idx=0;
  EnumWindows((h,l)=>{
   idx++;
   var sb=new StringBuilder(512); GetWindowTextW(h,sb,512); var t=sb.ToString().Replace("\r"," ").Replace("\n"," ");
   if(t.Length==0) return true;
   if(IsWindowVisible(h)){
    var top=(GetWindowLongW(h,-20) & 0x80000)!=0;
    var fg = h==GetForegroundWindow();
    sb2.AppendLine(string.Format("#{0,2} {1} {2}",idx,top?"TOP":"   ",t)+(fg?"  <=FOREGROUND":""));
   }
   return true;
  },IntPtr.Zero);
  return sb2.ToString();
 }
}
"@
[WZ]::Go()
