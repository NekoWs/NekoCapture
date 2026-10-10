namespace FrameCapture.Core;

/// <summary>
/// 捕获状态机的入口守卫与轮廓决策（纯逻辑，可测）。
///
/// 两个全局热键在各状态下"该做什么"全部集中在这里，<c>CaptureController</c> 只负责执行意图，
/// 不再在方法开头散落 <c>if-return</c>。这样"哪条路径允许动轮廓"变成可断言的决策表。
///
/// 修掉的历史缺陷（决策表即回归护栏）：
///  - BeginCapture() 第一行无条件 HideOutline()，守卫在其后。于是"编辑器已开时再按捕获键"
///    （RefocusEditor 路径）会把<b>常显范围轮廓永久弄没</b>，任何提前 return 的失败分支
///    （如 GDI 启动异常）同样误隐藏。
///    不变量：<b>只有 StartCapture 允许隐藏轮廓</b>（轮廓绝不能入镜，故起录前必须隐藏）。
///  - FinishCapture() 注释写着"常显轮廓则由后续分支重新点亮"，但没有任何分支重新点亮：
///    常显模式下一次录制就把轮廓淡出且不再回来。现由 OnCaptureFinished 明确决策。
/// </summary>
public static class CaptureGuards
{
    /// <summary>按下捕获热键的意图。</summary>
    public enum CaptureIntent
    {
        /// <summary>真正起录。<b>唯一</b>允许隐藏范围轮廓的意图。</summary>
        StartCapture,
        /// <summary>录制中 / 选取中：不打断，什么都不做。</summary>
        Ignore,
        /// <summary>编辑器已开：只把它带到前台，不重入捕获、不碰轮廓。</summary>
        RefocusEditor,
    }

    /// <summary>按下"设置范围"热键的意图。</summary>
    public enum ScopeIntent
    {
        /// <summary>打开选取层（Idle，或从编辑器切回选取，或选取态已卡死复位后）。</summary>
        StartSelection,
        /// <summary>再按一次：把已存在的选取层重新顶到所有窗口最前（游戏等霸屏窗口会盖住它）。</summary>
        RefocusPicker,
        /// <summary>录制中：不打断。</summary>
        Ignore,
    }

    /// <summary>录制结束后范围轮廓的去向。</summary>
    public enum AfterCaptureOutline
    {
        /// <summary>常显模式且录的就是已存范围：轮廓留在屏上（同一区域，淡出再亮起只会闪一下）。</summary>
        KeepPersistent,
        /// <summary>其余情况：立即淡出，不要拖到编辑器上。</summary>
        FadeOut,
    }

    public static CaptureIntent OnCaptureHotkey(bool capturing, bool selecting, bool editing)
        => capturing ? CaptureIntent.Ignore
         : selecting ? CaptureIntent.Ignore
         : editing ? CaptureIntent.RefocusEditor
         : CaptureIntent.StartCapture;

    /// <summary>
    /// 选取窗是否"活着"由调用方给出（<c>_picker != null</c>）。
    /// 选取态但窗已不在（Alt+F4 等异常关闭）视为卡死，复位后重新开选取。
    /// </summary>
    public static ScopeIntent OnScopeHotkey(bool capturing, bool selecting, bool pickerAlive)
        => capturing ? ScopeIntent.Ignore
         : selecting && pickerAlive ? ScopeIntent.RefocusPicker
         : ScopeIntent.StartSelection;

    public static AfterCaptureOutline OnCaptureFinished(bool alwaysShowRegionOutline, bool hasStoredRegion)
        => alwaysShowRegionOutline && hasStoredRegion ? AfterCaptureOutline.KeepPersistent
                                                      : AfterCaptureOutline.FadeOut;

    /// <summary>不变量护栏：只有真正起录才允许隐藏范围轮廓。</summary>
    public static bool MayHideOutline(CaptureIntent intent) => intent == CaptureIntent.StartCapture;
}
