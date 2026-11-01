using System.Windows;
using System.Windows.Threading;

namespace NekoCapture.Overlays;

/// <summary>提示卡按实际尺寸锚定主屏工作区右下角，尺寸变化时保持任务栏间距。</summary>
internal static class ToastPlacement
{
    /// <summary>预先测量卡片，并在首帧回位和后续布局完成后校准位置。</summary>
    /// <param name="window">按内容自适应的提示窗口。</param>
    /// <param name="card">窗口内容。</param>
    internal static void Attach(Window window, FrameworkElement card)
    {
        const double gap = 12;
        bool rendered = false, closed = false;
        var work = SystemParameters.WorkArea;
        card.Measure(new Size(Math.Max(1, work.Width - gap * 2), double.PositiveInfinity));
        void Position(bool measured = false)
        {
            if (closed) return;
            var area = SystemParameters.WorkArea;
            double width = measured || window.ActualWidth <= 0 ? card.DesiredSize.Width : window.ActualWidth;
            double height = measured || window.ActualHeight <= 0 ? card.DesiredSize.Height : window.ActualHeight;
            window.Left = Math.Max(area.Left, area.Right - Math.Ceiling(width) - gap);
            window.Top = Math.Max(area.Top, area.Bottom - Math.Ceiling(height) - gap);
        }
        Position(measured: true);
        window.SizeChanged += (_, _) => { if (rendered) Position(); };
        window.ContentRendered += (_, _) =>
        {
            rendered = true;
            window.Dispatcher.BeginInvoke(() => Position(), DispatcherPriority.Loaded);
        };
        void WorkAreaChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        { if (rendered && args.PropertyName == nameof(SystemParameters.WorkArea)) Position(); }
        window.Loaded += (_, _) => SystemParameters.StaticPropertyChanged += WorkAreaChanged;
        window.Closed += (_, _) => { closed = true; SystemParameters.StaticPropertyChanged -= WorkAreaChanged; };
    }
}
