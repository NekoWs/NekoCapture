using System;
using System.Collections.Generic;
using System.Globalization;

namespace FrameCapture.Core;

/// <summary>
/// 界面文案的取词入口。中文是源语言：**取词键就是界面上那句中文原文**，英文表缺译时原样返回中文——
/// 半迁移状态下宁可夹一行中文，也不给读者一个没有名字的按钮。
/// 日志、<c>Trace</c>、异常消息不进本表：那些写给开发者读，不是界面。
/// 带参数的文案用 <see cref="F"/>，表里的英文值自己决定参数顺序，不靠调用侧拼词序。
/// </summary>
public static class Loc
{
    /// <summary>语言偏好取值：跟随系统 / 中文 / English。与 settings.json 的 <c>UiLanguage</c> 字面量一致。</summary>
    public const string PrefSystem = "system", PrefZh = "zh", PrefEn = "en";

    static readonly Dictionary<string, string> None = new(0, StringComparer.Ordinal);

    /// <summary>当前取词用的表。英文态挂英文表，中文态挂空表——取词路径上只查一次字典，不判文化。</summary>
    static volatile Dictionary<string, string> _table = None;

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
        bool en = pref switch
        {
            PrefZh => false,
            PrefEn => true,
            _ => !string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "zh", StringComparison.OrdinalIgnoreCase),
        };
        if (en == IsEnglish) return en;
        IsEnglish = en;
        _table = en ? StringsEn.Table : None;
        return en;
    }

    /// <summary>取一句界面文案：英文态且表里有译名给英文，否则原样给中文。</summary>
    /// <param name="zh">界面上的中文原文，同时充当取词键。</param>
    /// <returns>当前语言下的文案；缺译回退中文。</returns>
    public static string T(string zh) => IsEnglish && _table.TryGetValue(zh, out var s) ? s : zh;

    /// <summary>带占位符的文案：先按当前语言取词，再按表里的占位符填参数。</summary>
    /// <param name="zh">含 <c>{0}</c> 等占位符的中文原文。</param>
    /// <param name="args">按占位符顺序填入的参数。</param>
    /// <returns>填好参数的文案。</returns>
    public static string F(string zh, params object[] args) => string.Format(T(zh), args);
}
