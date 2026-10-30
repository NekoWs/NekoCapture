using System.Globalization;

namespace FrameCapture.Core;

/// <summary>安装器按启动语言读取共享文案。</summary>
internal static class Loc
{
    /// <summary>是否使用英文文案。</summary>
    internal static bool IsEnglish { get; set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "zh";
    /// <summary>读取当前语言文案。</summary>
    /// <param name="text">共享词条。</param>
    /// <returns>当前语言文字。</returns>
    internal static string T(Str text) => IsEnglish ? text.En : text.Zh;
    /// <summary>格式化当前语言文案。</summary>
    /// <param name="text">共享词条。</param>
    /// <param name="args">格式化参数。</param>
    /// <returns>格式化文字。</returns>
    internal static string F(Str text, params object[] args) => string.Format(T(text), args);
}

