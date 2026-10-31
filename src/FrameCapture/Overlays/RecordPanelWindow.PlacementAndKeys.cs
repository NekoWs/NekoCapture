using System.Windows;
using System.Windows.Input;
using FrameCapture.Core;
using GC = GestureCore;

namespace FrameCapture.Overlays;

/// <summary>录制面板的选区定位、尺寸测量与准备阶段按键。</summary>
public sealed partial class RecordPanelWindow
{
    readonly string _confirmChord, _cancelChord;
    readonly (uint Vk, GC.ModifierKeys Mods) _confirmKey, _cancelKey;

    static string ValidPanelChord(string? chord, string fallback) => EditorChord.Parse(chord) != null ? chord! : fallback;

    bool HandleReadyKey(uint vk, GC.ModifierKeys modifiers)
    {
        if (!_armed || _finished) return false;
        if (_cancelKey == (vk, modifiers)) { Dispatcher.BeginInvoke(new Action(() => { if (_armed) StopRequested(); })); return true; }
        if (_confirmKey != (vk, modifiers)) return false;
        Dispatcher.BeginInvoke(new Action(StartRequested)); return true;
    }

    static (uint, GC.ModifierKeys) PanelChord(string text)
    {
        var chord = EditorChord.Parse(text)!.Value;
        var mods = GC.ModifierKeys.None;
        if (chord.Mods.HasFlag(ModifierKeys.Control)) mods |= GC.ModifierKeys.Control;
        if (chord.Mods.HasFlag(ModifierKeys.Alt)) mods |= GC.ModifierKeys.Alt;
        if (chord.Mods.HasFlag(ModifierKeys.Shift)) mods |= GC.ModifierKeys.Shift;
        if (chord.Mods.HasFlag(ModifierKeys.Windows)) mods |= GC.ModifierKeys.Win;
        return ((uint)KeyInterop.VirtualKeyFromKey(chord.Key), mods);
    }

    void ReleaseReadyKeys()
    {
        if (Services.HotkeyManager.ArmedRecordKey == HandleReadyKey) Services.HotkeyManager.ArmedRecordKey = null;
        RefreshReadyKeys();
    }

    void ResizePanel()
    {
        _stop.InvalidateMeasure();
        if (_stop.Parent is FrameworkElement row) row.InvalidateMeasure();
        if (_card.Child is FrameworkElement content) content.InvalidateMeasure();
        _card.InvalidateMeasure();
        _card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var target = _card.DesiredSize;
        BeginAnimation(WidthProperty, null); BeginAnimation(HeightProperty, null);
        if (IsLoaded)
        {
            // HWND 尺寸一次提交，避免宽高分别更新时呈现中间布局。
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Native.Win32.SetWindowPos(hwnd, IntPtr.Zero, 0, 0,
                (int)Math.Ceiling(target.Width * dpi.DpiScaleX), (int)Math.Ceiling(target.Height * dpi.DpiScaleY),
                Native.Win32.SWP_NOMOVE | Native.Win32.SWP_NOZORDER | Native.Win32.SWP_NOACTIVATE);
        }
        Height = target.Height; Width = target.Width;
        if (IsLoaded) UpdateLayout();
        PlacePanel();
    }

    void PlacePanel()
    {
        double width = double.IsNaN(Width) ? 324 : Width;
        double height = double.IsNaN(Height) ? 140 : Height;
        var bounds = SeparatePanel(PanelBounds(_regionDip, _workDip, new Size(width, height)));
        Left = bounds.X; Top = bounds.Y;
    }

    static Rect PanelBounds(Rect region, Rect work, Size size)
    {
        const double gap = 10;
        double x = region.X + (region.Width - size.Width) / 2;
        double y = region.Y - size.Height - gap;
        if (y < work.Top + 8) y = region.Bottom + gap;
        return new Rect(Math.Clamp(x, work.Left + 8, Math.Max(work.Left + 8, work.Right - size.Width - 8)),
            Math.Clamp(y, work.Top + 8, Math.Max(work.Top + 8, work.Bottom - size.Height - 8)), size.Width, size.Height);
    }
}
