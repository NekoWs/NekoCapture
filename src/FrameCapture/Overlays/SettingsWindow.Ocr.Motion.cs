using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>模型卸载按钮的宽度展开、淡入与退场。</summary>
public sealed partial class SettingsWindow
{
    sealed class OcrDeleteMotion
    {
        readonly Button _button;
        readonly double _width;
        readonly TranslateTransform _shift = new();
        bool _shown, _initialized;
        int _generation;

        /// <summary>建立独立于图标悬停缩放的动画容器。</summary>
        /// <param name="button">模型卸载按钮。</param>
        public OcrDeleteMotion(Button button)
        {
            _button = button; button.Margin = new Thickness(2, 0, 6, 0);
            _width = button.Width + button.Margin.Left + button.Margin.Right;
            Host = new Grid();
            Host.Children.Add(button); Host.ClipToBounds = true; Host.RenderTransform = _shift;
            Host.Tag = "ocr-model-delete-host";
            Host.Unloaded += (_, _) => Finish();
        }

        /// <summary>占用布局宽度的动画容器。</summary>
        public Grid Host { get; }

        /// <summary>按需展开或收起；相同可见状态保持原动画。</summary>
        /// <param name="shown">是否展示卸载按钮。</param>
        /// <param name="animate">是否播放过渡。</param>
        public void SetVisible(bool shown, bool animate)
        {
            if (_initialized && shown == _shown) return;
            _initialized = true; _shown = shown; int generation = ++_generation;
            double width = double.IsNaN(Host.Width) ? 0 : Host.Width, opacity = Host.Opacity, x = _shift.X;
            if (!animate || !Host.IsLoaded) { Finish(); return; }
            Host.Visibility = Visibility.Visible; _button.Visibility = Visibility.Visible; Host.IsHitTestVisible = shown;
            Host.BeginAnimation(WidthProperty, null); Host.BeginAnimation(OpacityProperty, null);
            _shift.BeginAnimation(TranslateTransform.XProperty, null);
            Host.Width = shown ? _width : 0; Host.Opacity = shown ? 1 : 0; _shift.X = shown ? 0 : -6;
            var duration = TimeSpan.FromMilliseconds(shown ? Fluent.Motion.EnterMs : Fluent.Motion.ExitMs);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var size = new DoubleAnimation(width, Host.Width, duration) { EasingFunction = ease };
            size.Completed += (_, _) => { if (_generation == generation) Finish(); };
            Host.BeginAnimation(WidthProperty, size);
            Host.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, Host.Opacity, duration) { EasingFunction = ease });
            _shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, _shift.X, duration) { EasingFunction = ease });
        }

        void Finish()
        {
            _generation++; Host.BeginAnimation(WidthProperty, null); Host.BeginAnimation(OpacityProperty, null);
            _shift.BeginAnimation(TranslateTransform.XProperty, null);
            Host.Width = _shown ? _width : 0; Host.Opacity = _shown ? 1 : 0; _shift.X = _shown ? 0 : -6;
            Host.Visibility = _shown ? Visibility.Visible : Visibility.Collapsed;
            _button.Visibility = Host.Visibility; Host.IsHitTestVisible = _shown;
        }
    }
}
