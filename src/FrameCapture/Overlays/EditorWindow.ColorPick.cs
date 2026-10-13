using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Theme = FrameCapture.Core.Theme;

namespace FrameCapture.Overlays;

/// <summary>
/// EditorWindow partial：调色板弹层宿主——视图在 ColorPickView（可离屏渲染取证），这里只管弹出与收口。
/// </summary>
public sealed partial class EditorWindow
{
    Popup? _colorPop;
    ColorPickView? _colorView;
    bool _pkDirty;

    void ShowColorPicker(FrameworkElement anchor)
    {
        _pkDirty = false;
        if (_colorPop == null)
        {
            _colorView = new ColorPickView();
            _colorView.Changed += c =>
            {
                // 即时生效：后续笔画当场用新色，所见即所得
                _customColor = c;
                _colorIdx = Swatches.Length;
                RefreshSwatches();
                _pkDirty = true;
            };
            _colorPop = new Popup
            {
                Child = FrameCapture.Core.Fluent.PopupCard(_colorView),
                Placement = PlacementMode.Top,
                StaysOpen = false,
                AllowsTransparency = true,
                PopupAnimation = PopupAnimation.Fade,
            };
            // 关板即收口：改过才写最近用色（点开没动不重排 recents）
            _colorPop.Closed += (_, _) =>
            {
                if (!_pkDirty || _customColor == null) return;
                var c = _customColor.Value;
                _settings.Mutate(s => s.CustomColors = FrameCapture.Core.CustomPick.RememberColor(s.CustomColors, FrameCapture.Core.HexColor.Format(c)));
                RebuildRecentSwatches();
                RestyleSelected(CurColor, null); // 选中项随取色即时改色（单条撤销）
            };
        }
        if (_colorView != null) _colorView.Color = _customColor ?? CurColor;
        _colorPop.PlacementTarget = anchor;
        OpenPopSticky(_colorPop, this);
    }
}
