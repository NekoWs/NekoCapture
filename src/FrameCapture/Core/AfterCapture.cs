namespace FrameCapture.Core;

/// <summary>
/// R83：截图完成后行为的纯决策——把设置字符串解析成动作枚举，非法/空值一律回落 <see cref="AfterCaptureAction.Toast"/>。
/// 与 WPF/捕获管线解耦，SelfTest 可断言（三态映射 + 脏值回落）。
/// </summary>
public enum AfterCaptureAction { Toast, Editor, Copy }

public static class AfterCapture
{
    public static AfterCaptureAction Resolve(string? s) => s switch
    {
        "editor" => AfterCaptureAction.Editor,
        "copy" => AfterCaptureAction.Copy,
        _ => AfterCaptureAction.Toast,
    };
}
