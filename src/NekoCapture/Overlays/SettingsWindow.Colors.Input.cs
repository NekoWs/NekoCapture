using System.Windows.Input;
using System.Windows.Threading;

namespace NekoCapture.Overlays;

/// <summary>预览在同一部位完成点击后打开取色器，拖出则取消。</summary>
public sealed partial class SettingsWindow
{
    private PalettePart? _palettePressed;
    private int _paletteOpenGeneration;

    private void BeginPalettePointer(PalettePart part)
    {
        SelectPalettePart(part);
        _palettePressed = part;
        _paletteStage!.CaptureMouse();
    }

    private void EndPalettePointer(PalettePart? part)
    {
        var pressed = _palettePressed;
        _palettePressed = null;
        if (_paletteStage!.IsMouseCaptured) _paletteStage.ReleaseMouseCapture();
        if (pressed != null && pressed == part) QueuePalettePart(pressed);
    }

    private void QueuePalettePart(PalettePart part)
    {
        int generation = ++_paletteOpenGeneration;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (generation != _paletteOpenGeneration || !IsLoaded || _currentSettingsPage != "colors") return;
            OpenPalettePart(part);
        });
    }
}
