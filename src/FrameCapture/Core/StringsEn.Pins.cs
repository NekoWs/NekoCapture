namespace FrameCapture.Core;

/// <summary>
/// 英文译名 · 贴图窗口：右键菜单、轻提示与保存对话框。
/// 菜单项是动作，英文用动词原形；轻提示是状态词，短到一眼读完。
/// </summary>
static partial class StringsEn
{
    /// <summary>贴图窗口的译名。</summary>
    static readonly (string Zh, string En)[] Pins =
    {
        ("复制", "Copy"),
        ("另存为…", "Save as…"),
        ("识别文字", "Recognize Text"),
        ("识别文字…", "Recognize text…"),
        ("旋转 90°", "Rotate 90°"),
        ("更新内容", "Refresh content"),
        ("不透明度", "Opacity"),
        ("关闭全部贴图 ({0})", "Close all pins ({0})"),
        ("PNG 图片|*.png", "PNG image|*.png"),

        // 轻提示
        ("已复制", "Copied"),
        ("已保存", "Saved"),
        ("保存失败", "Save failed"),
        ("已更新", "Updated"),
        ("更新失败", "Refresh failed"),
        ("无法更新", "Nothing to refresh"),
        ("识别失败", "Recognition failed"),
    };
}
