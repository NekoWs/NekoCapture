using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using NekoCapture.Core;

namespace NekoCapture.Overlays;

/// <summary>动作卡片的增删、选中描边与布局过渡。</summary>
public sealed partial class SettingsWindow
{
    sealed partial class ChainEditor
    {
        sealed record ChipPose(Point At, Size Size, Color Stroke, BitmapSource? Image);

        Dictionary<(string Id, int Occurrence), ChipPose> CaptureChipLayout(List<string> next)
        {
            var result = new Dictionary<(string, int), ChipPose>();
            if (!_chips.IsLoaded || _dragging || _ghost != null) return result;
            var counts = new Dictionary<string, int>();
            var remaining = next.GroupBy(id => id).ToDictionary(group => group.Key, group => group.Count());
            for (int i = 0; i < _paintedIds.Length && i < _chips.Children.Count; i++)
            {
                if (_chips.Children[i] is not Border chip) continue;
                string id = _paintedIds[i];
                int occurrence = counts.TryGetValue(id, out var count) ? count : 0;
                counts[id] = occurrence + 1;
                bool removed = !remaining.TryGetValue(id, out int available) || occurrence >= available;
                result[(id, occurrence)] = new ChipPose(chip.TranslatePoint(new Point(), Root), chip.RenderSize,
                    (chip.BorderBrush as SolidColorBrush)?.Color ?? Colors.Transparent,
                    removed ? CaptureSettingsVisual(chip) : null);
            }
            return result;
        }

        void PlayChipChanges(Dictionary<(string Id, int Occurrence), ChipPose> previous, List<string> next, double oldHeight)
        {
            if (previous.Count == 0 && _paintedIds.Length != 0 || !Root.IsLoaded) return;
            bool changed = !_paintedIds.SequenceEqual(next);
            _chips.UpdateLayout();
            var counts = new Dictionary<string, int>();
            for (int i = 0; i < next.Count; i++)
            {
                var chip = (Border)_chips.Children[i];
                int occurrence = counts.TryGetValue(next[i], out int count) ? count : 0;
                counts[next[i]] = occurrence + 1;
                if (!previous.Remove((next[i], occurrence), out var old))
                {
                    if (changed) EnterChip(chip);
                    continue;
                }
                var stroke = (chip.BorderBrush as SolidColorBrush)?.Color ?? Colors.Transparent;
                if (old.Stroke != stroke)
                {
                    var brush = new SolidColorBrush(stroke); chip.BorderBrush = brush;
                    brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(old.Stroke, stroke,
                        TimeSpan.FromMilliseconds(Fluent.Motion.StateMs)));
                }
                if (!changed) continue;
                var position = chip.TranslatePoint(new Point(), Root);
                var shift = TransformOf(chip).Item1;
                var duration = new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.StateMs));
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(old.At.X - position.X, 0, duration) { EasingFunction = ease });
                shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(old.At.Y - position.Y, 0, duration) { EasingFunction = ease });
            }
            foreach (var old in previous.Values)
                if (old.Image != null) DepartingVisual(Root, old.Image, new Rect(old.At, old.Size), true);
            if (!changed) return;
            int generation = ++_chipMotionGeneration;
            Root.BeginAnimation(HeightProperty, null); Root.Height = double.NaN;
            Root.Measure(new Size(Math.Max(1, Root.ActualWidth), double.PositiveInfinity));
            double target = Root.DesiredSize.Height;
            var grow = new DoubleAnimation(oldHeight, target, TimeSpan.FromMilliseconds(Fluent.Motion.EnterMs))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            grow.Completed += (_, _) =>
            {
                if (generation != _chipMotionGeneration) return;
                Root.BeginAnimation(HeightProperty, null); Root.Height = double.NaN;
            };
            Root.BeginAnimation(HeightProperty, grow);
        }

        static void EnterChip(Border chip)
        {
            var scale = TransformOf(chip).Item2;
            var spring = new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(Fluent.Motion.SpringMs))
                { EasingFunction = Fluent.Spring(0.45) };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, spring);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, spring);
            chip.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(Fluent.Motion.QuickMs)));
        }
    }
}
