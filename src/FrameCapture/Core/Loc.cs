using System;
using System.Globalization;

namespace FrameCapture.Core;

/// <summary>
/// 界面文案的取词入口。每条文案是一个 <see cref="Str"/>：id、中文原文、英文译名写在一起，
/// 取词就是按当前语言读其中一列——没有字典，也没有「键写错了要到运行时才知道」这种失败方式。
/// 英文列留空时退回中文：半迁移状态下宁可夹一行中文，也不给读者一个没有名字的按钮。
/// 日志、<c>Trace</c>、异常消息不走本入口：那些写给开发者读，不是界面。
/// 带参数的文案用 <see cref="F"/>，英文值自己决定参数顺序，不靠调用侧拼词序。
/// </summary>
public static class Loc
{
    /// <summary>语言偏好取值：跟随系统 / 中文 / English。与 settings.json 的 <c>UiLanguage</c> 字面量一致。</summary>
    public const string PrefSystem = "system", PrefZh = "zh", PrefEn = "en";

    /// <summary>当前是否按英文取词。取词路径只读这个布尔。</summary>
    public static bool IsEnglish { get; private set; }

    /// <summary>
    /// 把偏好解析成实际语言并启用。<c>system</c> 看 <see cref="CultureInfo.CurrentUICulture"/>
    /// 的两字母语言名是不是 <c>zh</c>，其余照指定；无法识别的取值按 <c>system</c> 处理。
    /// 重复应用同一结果是空操作，不产生任何副作用。
    /// </summary>
    /// <param name="pref"><c>system</c> / <c>zh</c> / <c>en</c>；null 按 <c>system</c>。</param>
    /// <returns>解析后的语言，true 为英文。</returns>
    public static bool Apply(string? pref)
    {
        IsEnglish = pref switch
        {
            PrefZh => false,
            PrefEn => true,
            _ => !string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase),
        };
        return IsEnglish;
    }

    /// <summary>取一句界面文案：英文态且这条有译名给英文，否则给中文原文。</summary>
    /// <param name="s">要取的那条文案。</param>
    /// <returns>当前语言下的文案；缺译回退中文。</returns>
    public static string T(Str s) => IsEnglish && !string.IsNullOrEmpty(s.En) ? s.En : s.Zh;

    /// <summary>带占位符的文案：先按当前语言取词，再按该语言自己的占位符填参数。</summary>
    /// <param name="s">含 <c>{0}</c> 等占位符的那条文案。</param>
    /// <param name="args">按占位符顺序填入的参数。</param>
    /// <returns>填好参数的文案。</returns>
    public static string F(Str s, params object[] args) => string.Format(T(s), args);
}
