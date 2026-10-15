namespace FrameCapture.Core;

/// <summary>
/// 一句界面文案：稳定 id、中文原文、英文译名，三种身份写在同一行里。
/// id 是这条文案的名字——改 id 等于换了一条文案，改文案只改后面两列，取词点不用跟着动。
/// 声明集中在 <c>Core/Strings/</c> 下按界面分组的 <c>K</c> 里。
/// </summary>
/// <param name="Id">稳定标识，形如 <c>hotkeys.enableLayer</c>；只出现在日志与护栏报告里，不上界面。</param>
/// <param name="Zh">中文原文。中文态显示它，英文态缺译时也退回它。</param>
/// <param name="En">英文译名。留空表示这条还没翻，护栏按缺陷处理。</param>
/// <returns>按给定三列构造的不可变值。</returns>
public readonly record struct Str(string Id, string Zh, string En)
{
    /// <summary>「这里没有文案」：取词结果为空串的场合用它，调用处不必为语言写分支。</summary>
    public static readonly Str Empty = new("core.empty", "", "");
}
