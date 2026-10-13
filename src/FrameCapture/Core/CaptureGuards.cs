namespace FrameCapture.Core;

/// <summary>
/// 捕获状态机的入口守卫与轮廓决策（纯逻辑，可测）。
///
/// 两个全局热键在各状态下"该做什么"全部集中在这里，<c>CaptureController</c> 只负责执行意图，
/// 于是"哪条路径允许动轮廓"是一张可断言的决策表。
///
/// 不变量：<b>只有 StartCapture 允许隐藏范围轮廓</b>——轮廓绝不能入镜，故只有真正起录前必须隐藏它；
/// 编辑器已开、选取中、GDI 启动异常等分支一律不碰轮廓。
/// 录制结束后轮廓的去向由 <see cref="OnCaptureFinished"/> 单点决策。
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

    /// <summary>按下捕获热键时要做的意图。</summary>
    /// <param name="capturing">是否正在录制（录制中不打断）。</param>
    /// <param name="selecting">选取层是否打开（选区尚未定，不打断）。</param>
    /// <param name="editing">标注编辑器是否打开。</param>
    /// <returns>三者皆 false 才起录；否则按 Ignore / RefocusEditor 打断掉。</returns>
    public static CaptureIntent OnCaptureHotkey(bool capturing, bool selecting, bool editing)
        => capturing ? CaptureIntent.Ignore
         : selecting ? CaptureIntent.Ignore
         : editing ? CaptureIntent.RefocusEditor
         : CaptureIntent.StartCapture;

    /// <summary>
    /// 按下"设置范围"热键时要做的意图。选取态但窗已不在（Alt+F4 等异常关闭）视为卡死，复位后重新开选取。
    /// </summary>
    /// <param name="capturing">是否正在录制（录制中不打断）。</param>
    /// <param name="selecting">是否处于选取态。</param>
    /// <param name="pickerAlive">选取窗是否还活着，由调用方给出（<c>_picker != null</c>）。</param>
    /// <returns>录制中 Ignore；选取态且窗在 RefocusPicker（顶到最前）；其余 StartSelection。</returns>
    public static ScopeIntent OnScopeHotkey(bool capturing, bool selecting, bool pickerAlive)
        => capturing ? ScopeIntent.Ignore
         : selecting && pickerAlive ? ScopeIntent.RefocusPicker
         : ScopeIntent.StartSelection;

    /// <summary>录制结束后范围轮廓的去向。</summary>
    /// <param name="alwaysShowRegionOutline">用户是否开了"范围轮廓常显"。</param>
    /// <param name="hasStoredRegion">本次录的是不是已存范围（临时框不算）。</param>
    /// <returns>两者同时成立才 KeepPersistent；临时框或非常显模式一律 FadeOut，不把轮廓拖到编辑器上。</returns>
    public static AfterCaptureOutline OnCaptureFinished(bool alwaysShowRegionOutline, bool hasStoredRegion)
        => alwaysShowRegionOutline && hasStoredRegion ? AfterCaptureOutline.KeepPersistent
                                                      : AfterCaptureOutline.FadeOut;

    /// <summary>不变量护栏：只有真正起录才允许隐藏范围轮廓。</summary>
    /// <param name="intent">本次热键决策出的意图。</param>
    /// <returns>仅 <see cref="CaptureIntent.StartCapture"/> 为 true；调用方据此决定是否 HideOutline。</returns>
    public static bool MayHideOutline(CaptureIntent intent) => intent == CaptureIntent.StartCapture;
}
