namespace FrameCapture.Core;

/// <summary>
/// 英文译名 · 设置窗的分页正文：产物、编辑器、贴图、系统四页的行标签与整段提示。
/// 提示是完整句子，句内保留句号断句，末尾不加句号——与中文原文的收法一致。
/// </summary>
static partial class StringsEn
{
    /// <summary>产物、编辑器、贴图、系统四页的译名。</summary>
    static readonly (string Zh, string En)[] Pages =
    {
        // 产物分页
        ("自动保存副本", "Auto-save a copy"),
        ("捕获完成后，除复制到剪贴板外，再存一份文件到本地目录：截图存 PNG，录制按上面的「录制格式」存 MP4/WebM，未装 ffmpeg 则回退 GIF。视频在后台编码、文件稍后出现。写盘失败不影响剪贴板，捕获永远成功",
            "After a capture, on top of the clipboard copy, a file also goes to a local folder: stills as PNG, recordings as MP4 or WebM in the recording format above, falling back to GIF when ffmpeg is missing. Video encodes in the background, so the file shows up a moment later. A failed write never touches the clipboard — the capture always succeeds"),
        ("保存文件夹：自动保存副本存到这里，编辑器/贴图「另存为」对话框也默认打开这里",
            "Save folder: auto-saved copies go here, and the editor and pin Save As dialogs open here by default"),
        ("点击选择保存文件夹，自动保存副本和导出对话框都用它",
            "Click to choose the save folder; auto-saved copies and export dialogs all use it"),
        ("选择保存文件夹，自动保存副本和导出对话框都用它",
            "Choose the save folder; auto-saved copies and export dialogs all use it"),
        ("保存位置", "Save location"),
        (@"图片\FrameCapture · 默认位置", @"Pictures\FrameCapture · default location"),
        ("更改", "Change"),

        // 编辑器分页
        ("底部工具栏入场动效", "Toolbar entrance animation"),
        ("打开编辑器时，底部两排图标逐项落定，视线跟着落到工具上。关闭后工具栏随窗口直接出现，一个动画都不放",
            "When the editor opens, the two rows of icons at the bottom settle one by one and the eye lands on the tools with them. Off, the toolbar appears with the window and no animation plays"),
        ("记住上次的工具/颜色/粗细", "Remember the last tool, color, and width"),
        ("关闭编辑器时记住你最后用的标注工具、颜色和线宽，下次打开直接是它们，省去每次重选。关闭后每次打开都回到默认：浏览 / 红 / 中",
            "Closing the editor remembers the annotation tool, color, and line width you last used, so the next opening starts with them instead of making you pick again. Off, every opening returns to the default: Pan / Red / Medium"),
        ("识别文字后显示结果窗", "Show the result window after recognizing text"),
        ("「识别文字」成功后弹出结果窗预览：可查看/编辑识别文本、鼠标悬停部分选中复制、一键复制全部——预览即选取入口，此时不再自动写剪贴板，要哪段选哪段。关闭则退回快速路径：直接写剪贴板 + 轻提示，不弹窗",
            "After a successful recognition a result window previews the text: read it, edit it, hover to select part of it and copy, or copy everything at once. The preview is the selection entry, so nothing is written to the clipboard on its own — take the part you want. Off, the fast path returns: straight to the clipboard with a toast, no window"),

        // 贴图分页
        ("贴图窗口吸附", "Pin snapping"),
        ("拖动贴图时实时吸附对齐：贴近所在屏幕边缘则停靠四边/四角；贴近同屏其他贴图、以及浏览器或资源管理器等任何应用窗口的边或中线则与之对齐，把贴图摆齐像吸附网格一样自然。阈值调低为「贴边才吸」，不会隔空乱跳。关闭则松手停在原地",
            "Dragging a pin snaps it into alignment: near a screen edge it docks to an edge or a corner, and near another pin on the same screen or the edge or center line of any app window — a browser or File Explorer included — it lines up with it. A lower threshold means it only grabs when close, so nothing jumps across the screen. Off, a pin stops where you let go"),

        // 系统分页
        ("登录 Windows 时自动启动 FrameCapture", "Starts FrameCapture when you sign in to Windows"),
        ("启动气泡提示", "Startup notification"),
        ("启动完成后在屏幕右下角弹一条「FrameCapture 已启动」，确认它真的在后台待命了。关掉后启动静默进托盘，只剩托盘图标",
            "After startup a notice appears in the bottom-right corner saying FrameCapture has started, confirming it is waiting in the background. Off, startup goes quietly to the tray and only the icon remains"),
    };
}
