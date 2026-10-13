namespace FrameCapture.Core;

/// <summary>
/// 截图完成后行为的纯决策——把设置字符串解析成动作枚举，非法/空值一律回落 <see cref="AfterCaptureAction.Toast"/>。
/// 与 WPF/捕获管线解耦，SelfTest 可断言（三态映射 + 脏值回落）。
/// </summary>
public enum AfterCaptureAction { Toast, Editor, Copy, Pin }

/// <summary>「截图后」设置串的解析表。</summary>
public static class AfterCapture
{
    /// <summary>设置串 → 完成后动作。</summary>
    /// <param name="s">设置里的原始串，区分大小写（写入侧恒为小写）。</param>
    /// <returns>"editor"/"copy"/"pin" 各对应一个动作；其余（含 null 与脏值）回落 Toast——设置坏了也要有反馈。</returns>
    public static AfterCaptureAction Resolve(string? s) => s switch
    {
        "editor" => AfterCaptureAction.Editor,
        "copy" => AfterCaptureAction.Copy,
        "pin" => AfterCaptureAction.Pin,
        _ => AfterCaptureAction.Toast,
    };
}
