namespace FrameCapture.Core;

/// <summary>动作在链中的职责。</summary>
public enum VerbClass
{
    /// <summary>选择输入范围。</summary>
    Input,
    /// <summary>产出静图或录制。</summary>
    Product,
    /// <summary>消费产物或打开界面。</summary>
    Terminal,
}

/// <summary>动作链的数据值。</summary>
[Flags]
public enum RunValue
{
    /// <summary>无前置值。</summary>
    None = 0,
    /// <summary>捕获区域。</summary>
    Region = 1,
    /// <summary>区域命中的窗口。</summary>
    Window = 2,
    /// <summary>静图。</summary>
    Still = 4,
    /// <summary>录制产物。</summary>
    Video = 8,
    /// <summary>正在录制。</summary>
    Recording = 16,
}

/// <summary>动作的数据契约。</summary>
/// <param name="Class">动作职责。</param>
/// <param name="Needs">所需值。</param>
/// <param name="Produces">产出值。</param>
public readonly record struct VerbInfo(VerbClass Class, RunValue Needs, RunValue Produces);
