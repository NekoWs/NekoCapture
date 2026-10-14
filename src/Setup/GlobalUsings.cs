// 链入的主程序源文件依赖 SDK 隐式 using（未写 using System 等）。
// 这里复刻 .NET 默认隐式集，但不含 System.Drawing 与 System.Windows.Forms——
// 那两个会跟 WPF 的 Button / Color / Brush 撞名。Win32.cs 里的 WinForms 用法是限定名，不受影响。
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
