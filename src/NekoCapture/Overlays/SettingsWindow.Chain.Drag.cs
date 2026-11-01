using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using NekoCapture.Core;

namespace NekoCapture.Overlays;

/// <summary>动作卡片的装饰层拖动、占位与弹性落入。</summary>
public sealed partial class SettingsWindow
{
    sealed partial class ChainEditor
    {
        /// <summary>拖动进行中：越过起拖阈值才把芯片抬起来，之后它跟着光标走，落点占位随光标换格。</summary>
        /// <param name="at">光标在 Root 里的坐标。</param>
        void DragMove(Point at)
        {
            if (_drag == null) return;
            double far = Math.Abs(at.X - _pressAt.X) + Math.Abs(at.Y - _pressAt.Y);
            if (!_dragging)
            {
                if (far < SystemParameters.MinimumHorizontalDragDistance * 2) return;
                BeginDrag();
            }
            _cursor = at;
            Lift(at);
            int want = SlotAt(at);
            if (want != _target) ShiftTo(want);
        }

        /// <summary>起拖：把芯片从这一行里摘出去挂上装饰层，再在原位摆出占位框。</summary>
        void BeginDrag()
        {
            var chip = _drag;
            if (chip == null) return;
            _dragging = true;
            Snapshot();
            // 芯片要跟着光标走，又不能继续替自己占位，就只能离开这一行。
            // 留在行里靠负外边距抵槽位是错的：负 margin 连排版带绘制一起改，
            // WrapPanel 那边槽位确实塌成 0，Arrange 这边却把负 margin 加回渲染矩形——
            // 芯片被摆到左上角外一格、尺寸涨成两倍，看着就是一张跟在手上的、大小不对的卡。
            // 装饰层不参与排版，芯片按自己的尺寸画，左上角就是光标按住的那个点。
            _chips.Children.Remove(chip);
            var layer = AdornerLayer.GetAdornerLayer(_chips);
            if (layer != null)
            {
                _lift = new DragAdorner(_chips, chip);
                layer.Add(_lift);
            }
            chip.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.45 };
            _chips.UpdateLayout();
            MeasureSlots();
            // 占位框底色取主题强调色的淡层：深色底下黑色淡层看不见，浅色底下又太脏
            var ac = Theme.Accent;
            _ghost = new Border
            {
                Width = Math.Max(40, chip.ActualWidth - 4),
                Height = Math.Max(20, chip.ActualHeight - 4),
                Margin = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Center,
                CornerRadius = new CornerRadius(6),
                BorderBrush = Theme.AccentBrush,
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(0x2E, ac.R, ac.G, ac.B)),
            };
            _target = Math.Clamp(_dragFrom, 0, _slots.Count);
            _chips.Children.Insert(_target, _ghost);
            _chips.UpdateLayout();
        }

        /// <summary>抬起时芯片放大的倍率，缩放动画的目标值。</summary>
        const double LiftScale = 1.06;

        /// <summary>把拖起的芯片按光标摆好：按住的那一点落在光标上，芯片不会在手里跳。</summary>
        /// <param name="at">光标在 Root 里的坐标。</param>
        void Lift(Point at)
        {
            var chip = _drag;
            if (chip == null) return;
            var tt = TransformOf(chip);
            var sz = chip.RenderSize;
            // 放大绕着按住的那一点，不绕中心：绕中心时按住点会被缩放推出去，手指不动、卡片在手里滑。
            if (sz.Width > 0 && sz.Height > 0)
                chip.RenderTransformOrigin = new Point(_grab.X / sz.Width, _grab.Y / sz.Height);
            // 位移按「现在画在哪」闭环修正，不按装饰层的排版基准推算：装饰层把这层摆在哪儿、
            // 变换原点怎么参与渲染，都不由这一行决定。量出来的差一步补平，卡片就锁在光标上。
            // 扣的是放大到位之后的按住点——卡片长开之后一直停在那儿，长开的这一百来毫秒才是过渡。
            // 让位与落入的动画可能还攥着这颗芯片的位移属性：动画的持有值压过本地值，
            // 本地值怎么写都不上屏，抬起的卡片就钉在装饰层原点不动。
            // 先把此刻生效的值落回本地再交还属性，芯片不会在手里跳一下。
            tt.Item1.X = (double)tt.Item1.GetValue(TranslateTransform.XProperty);
            tt.Item1.Y = (double)tt.Item1.GetValue(TranslateTransform.YProperty);
            tt.Item1.BeginAnimation(TranslateTransform.XProperty, null);
            tt.Item1.BeginAnimation(TranslateTransform.YProperty, null);
            var now = chip.TranslatePoint(new Point(0, 0), Root);
            tt.Item1.X += at.X - _grab.X * LiftScale - now.X;
            tt.Item1.Y += at.Y - _grab.Y * LiftScale - now.Y;
            if (tt.Item2.ScaleX < 1.01)
            {
                tt.Item2.BeginAnimation(ScaleTransform.ScaleXProperty, ScaleTo(LiftScale));
                tt.Item2.BeginAnimation(ScaleTransform.ScaleYProperty, ScaleTo(LiftScale));
            }
        }

        /// <summary>落点换格：占位框移到新位置，其余芯片把排版差值动画回 0，看起来就是它们让开了。</summary>
        /// <param name="want">新的落点下标。</param>
        void ShiftTo(int want)
        {
            if (_ghost == null) return;
            _chips.Children.Remove(_ghost);
            int to = Math.Clamp(want, 0, _slots.Count);
            _chips.Children.Insert(to, _ghost);
            _target = to;
            _chips.UpdateLayout();
            PlayShift();
        }

        /// <summary>记下当前每枚芯片在 Root 里的位置。</summary>
        void Snapshot()
        {
            _before.Clear();
            foreach (UIElement c in _chips.Children)
                _before[c] = c.TranslatePoint(new Point(0, 0), Root);
        }

        /// <summary>重排之后：每个元素从上一个位置滑到新位置。换行与不换行同一条路径，
        /// 不必按 WrapPanel 的换行细节算位移。上一次滑动可能还没跑完，起点要带上残余位移，
        /// 否则连换两格时元素会先跳回原位再走。</summary>
        void PlayShift()
        {
            foreach (UIElement c in _chips.Children)
            {
                if (ReferenceEquals(c, _drag)) continue;
                var now = c.TranslatePoint(new Point(0, 0), Root);
                var tt = TransformOf(c);
                var old = _before.TryGetValue(c, out var p) ? p : now;
                double dx = old.X + tt.Item1.X - now.X;
                double dy = old.Y + tt.Item1.Y - now.Y;
                _before[c] = now;
                if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5) continue;
                tt.Item1.X = dx;
                tt.Item1.Y = dy;
                var d = new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.StateMs));
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                tt.Item1.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dx, 0, d) { EasingFunction = ease });
                tt.Item1.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(dy, 0, d) { EasingFunction = ease });
            }
        }

        /// <summary>量一次落点尺子：按「被拖那枚已摘掉、占位框也不摆」时这一行的样子，
        /// 记下每枚芯片的中线。落点跟这把尺子比，不跟占位框摆好之后的实时位置比——
        /// 占位框一挪别的芯片就跟着挪，拿挪完的位置反查落点，指针停在原地也会来回换格。</summary>
        void MeasureSlots()
        {
            _slots.Clear();
            foreach (UIElement c in _chips.Children)
            {
                if (ReferenceEquals(c, _ghost) || ReferenceEquals(c, _add) || ReferenceEquals(c, _drag)) continue;
                var p = c.TranslatePoint(new Point(0, 0), Root);
                _slots.Add(new Slot(_slots.Count, p.X + c.RenderSize.Width / 2, p.Y, c.RenderSize.Height));
            }
        }

        /// <summary>光标此刻落在哪一格：与起拖那一刻定格的尺子比，光标在某枚芯片中线左侧就是它那一格。</summary>
        /// <param name="at">光标在 Root 里的坐标。</param>
        /// <returns>落点下标。</returns>
        int SlotAt(Point at)
        {
            int end = -1;   // 这一行上被光标越过的那一枚之后就是落点；-1 表示光标不在任何一行上
            foreach (var s in _slots)
            {
                if (at.Y < s.Top || at.Y > s.Top + s.Height) continue;
                if (at.X < s.CenterX) return s.Index;
                end = s.Index + 1;
            }
            // 光标不在任何一枚芯片所在的那一行上：保持当前落点，别一路跳到行尾
            return end < 0 ? Math.Clamp(_target, 0, _slots.Count) : end;
        }

        /// <summary>把芯片从装饰层摘回：投影与缩放一并清掉。重铺会造新的芯片，这里只保证不留残影。</summary>
        void DetachLift()
        {
            if (_lift != null)
            {
                AdornerLayer.GetAdornerLayer(_chips)?.Remove(_lift);
                _lift = null;
            }
            if (_drag is not { } chip) return;
            chip.Effect = null;
            chip.RenderTransform = null;
        }

        /// <summary>起拖那一刻定格的一格：落点下标与它在 Root 里的中线、上下边界。</summary>
        /// <param name="Index">这一格在序列里的下标。</param>
        /// <param name="CenterX">芯片中线在 Root 里的横坐标。</param>
        /// <param name="Top">芯片上边界在 Root 里的纵坐标。</param>
        /// <param name="Height">芯片高度。</param>
        readonly record struct Slot(int Index, double CenterX, double Top, double Height);

        /// <summary>拖起期间托着芯片的那一层。装饰层不参与排版：芯片按自己的尺寸画，
        /// 于是它既能跟着光标走，又不在芯片行里占位。</summary>
        sealed class DragAdorner : Adorner
        {
            readonly UIElement _child;

            /// <summary>装配一层。</summary>
            /// <param name="adorned">被装饰的元素，也就是芯片行——这一层跟着它走，也随它一起滚。</param>
            /// <param name="child">拖起的那枚芯片，已从芯片行里摘出来交到这里。</param>
            public DragAdorner(UIElement adorned, UIElement child) : base(adorned)
            {
                IsHitTestVisible = false;   // 卡片压在谁身上都不抢点击
                _child = child;
                AddVisualChild(child);
                AddLogicalChild(child);
            }

            /// <summary>这一层只有一个可视子。</summary>
            /// <returns>恒为 1。</returns>
            protected override int VisualChildrenCount => 1;

            /// <summary>取可视子。</summary>
            /// <param name="index">子下标。</param>
            /// <returns>手上那枚芯片。</returns>
            protected override Visual GetVisualChild(int index) => _child;

            /// <summary>按芯片自己的期望尺寸量，不接受来自行的任何约束。</summary>
            /// <param name="constraint">装饰层给的约束，不参与。</param>
            /// <returns>恒为 0×0：这一层不为芯片向行要地方。</returns>
            protected override Size MeasureOverride(Size constraint)
            {
                _child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                return new Size(0, 0);
            }

            /// <summary>把芯片摆在芯片行的原点，尺寸就是它自己量出来的那样。</summary>
            /// <param name="finalSize">装饰层给的矩形，不参与。</param>
            /// <returns>原样返回。</returns>
            protected override Size ArrangeOverride(Size finalSize)
            {
                // 装饰层把这层摆在哪儿由装饰层自己定，芯片的基准必须以芯片行的原点为准：
                // 否则编辑器给的位移是相对装饰层算的，芯片整层偏掉一截，看着就不在光标底下。
                var origin = AdornedElement?.TranslatePoint(new Point(0, 0), this) ?? new Point(0, 0);
                _child.Arrange(new Rect(origin, _child.DesiredSize));
                return finalSize;
            }
        }

        /// <summary>松手：芯片落进占位框占的那一格，序列按新顺序写回。</summary>
        /// <param name="at">松手处光标在 Root 里的坐标。</param>
        void Drop(Point at)
        {
            if (_drag == null) return;
            Root.ReleaseMouseCapture();
            DetachLift();
            _drag = null;
            if (!_dragging) { _openNode?.Invoke(_dragFrom); return; }
            _dragging = false;
            int from = _dragFrom, to = _target;
            // 落点尺子量的就是「被拖那枚已摘掉」之后的那一行，下标直接就是它在剩余序列里的位置
            var list = _get().ToList();
            if (from >= list.Count) { Render(); return; }
            var moved = list[from];
            list.RemoveAt(from);
            to = Math.Clamp(to, 0, list.Count);
            list.Insert(to, moved);
            _set(list.ToArray());
            Render();
            _ghost = null;
            // 落入：新芯片从松手处滑进它刚在预览里占的那一格，落点与预览必须一致
            _chips.UpdateLayout();
            if (to < _chips.Children.Count && _chips.Children[to] is FrameworkElement landed)
            {
                var slot = landed.TranslatePoint(new Point(0, 0), Root);
                var tt = TransformOf(landed);
                tt.Item1.X = at.X - _grab.X - slot.X;
                tt.Item1.Y = at.Y - _grab.Y - slot.Y;
                var d = new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.SpringMs));
                var ease = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut };
                tt.Item1.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(tt.Item1.X, 0, d) { EasingFunction = ease });
                tt.Item1.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(tt.Item1.Y, 0, d) { EasingFunction = ease });
            }
        }

        /// <summary>取元素自己的位移+缩放变换组，没有就现装一个。</summary>
        /// <param name="el">目标元素。</param>
        /// <returns>位移与缩放两个变换。</returns>
        static (TranslateTransform, ScaleTransform) TransformOf(UIElement el)
        {
            if (el.RenderTransform is TransformGroup tg
                && tg.Children.OfType<TranslateTransform>().FirstOrDefault() is { } t
                && tg.Children.OfType<ScaleTransform>().FirstOrDefault() is { } s)
                return (t, s);
            t = new TranslateTransform();
            s = new ScaleTransform(1, 1);
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            // 缩放必须排在位移前面：变换组按子项顺序逐个作用在点上，位移排在前面就会被后面的缩放
            // 一起放大——抬起的芯片离行原点越远偏得越多，怎么拖都追不上光标。
            el.RenderTransform = new TransformGroup { Children = { s, t } };
            return (t, s);
        }

        /// <summary>缩放动画：抬起与放回共用一条节拍。</summary>
        /// <param name="to">目标倍率。</param>
        /// <returns>已配好时长的缩放动画。</returns>
        static DoubleAnimation ScaleTo(double to) => new(to, new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.QuickMs)))
        { EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } };

    }
}
