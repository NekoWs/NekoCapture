using System.Windows;

[assembly:ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]

// SelfTest 要按字节校验 Native.Win32 的 INPUT 在 x64 下的布局，内部 P/Invoke 声明对它可见。
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SelfTest")]
