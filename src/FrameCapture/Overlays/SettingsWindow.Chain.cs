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
using FrameCapture.Core;

namespace FrameCapture.Overlays;

// 本 partial 职责：动作序列的芯片编辑器——快捷键页的「触发时 / 完成后 / 失败时」三行共用这一套控件。
/// <summary>
/// 设置窗里编辑动作序列的那部分。
/// 芯片按顺序排开，末尾一枚加号：加号在右侧点开一张候选卡，选中即追加到末尾。
/// 芯片本身只用来拖动排序——点它不改这一步，要换一步就删掉它再从加号里挑一个。
/// </summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>
    /// 一条动作序列的芯片编辑器：横向芯片，末尾一枚加号。
    /// </summary>
    sealed class ChainEditor
    {
        readonly Func<string[]> _get;
        readonly Func<int, IEnumerable<GestureAction>> _candidates;
        readonly Action<string[]> _set;
        readonly WrapPanel _chips = new();
        readonly Popup _pop = new() { AllowsTransparency = true, StaysOpen = true, Placement = PlacementMode.Right };
        Border _add = null!;

        /// <summary>芯片与加号统一的行高：两者都按它取最小高度，一行里只剩加号时高度不会掉下来。</summary>
        const double ChipHeight = 27;

        /// <summary>正在拖的那枚芯片；null 表示没在拖。</summary>
        Border? _drag;

        /// <summary>落点占位框：虚线框出现在哪，松手就落在哪。</summary>
        Border? _ghost;

        /// <summary>拖起前各芯片在 Root 里的位置，重排后据此把差值动画回 0。</summary>
        readonly Dictionary<UIElement, Point> _before = new();

        /// <summary>拖起期间托着芯片的装饰层宿主。松手、异常、重铺都得摘掉，否则芯片挂在层上不走。</summary>
        DragAdorner? _lift;

        /// <summary>起拖那一刻定格的落点尺子，见 <see cref="MeasureSlots"/>。</summary>
        readonly List<Slot> _slots = new();

        int _dragFrom = -1, _target = -1;
        Point _pressAt, _grab, _cursor;

        /// <summary>候选卡开着时盯着的宿主窗：切到别的应用是窗口级的事，卡在自己的视觉树里收不到。</summary>
        Window? _hostWin;

        bool _dragging;

        /// <summary>装配一个序列编辑器。</summary>
        /// <param name="get">取当前序列。</param>
        /// <param name="candidates">给定插入位返回可挑的动作。</param>
        /// <param name="set">改定后的写回动作，内部负责落盘。</param>
        public ChainEditor(Func<string[]> get, Func<int, IEnumerable<GestureAction>> candidates, Action<string[]> set)
        {
            _get = get;
            _candidates = candidates;
            _set = set;
            Root = new StackPanel { Background = Brushes.Transparent };
            Root.Children.Add(_chips);
            // 拖动中的移动与松手挂在 Root 上：它全程待在视觉树里，芯片抬起来、挂上装饰层都影响不到它。
            // 挂在这两种地方之外都会断线——芯片一离开这一行就再也收不到鼠标事件。
            Root.MouseMove += (_, e) => DragMove(e.GetPosition(Root));
            Root.MouseLeftButtonUp += (_, e) => Drop(e.GetPosition(Root));
            Render();
        }

        /// <summary>编辑器的根控件。</summary>
        /// <value>挂进设置行即可用。</value>
        public StackPanel Root { get; }

        /// <summary>按当前序列重铺芯片。写回之后由编辑器自己调用，外部只在切换简单/高级时调用一次。</summary>
        public void Render()
        {
            // 重铺清空芯片行，而正在手里的那枚早已不在行里：不先从装饰层摘掉，它会悬在屏幕上不走
            DetachLift();
            _drag = null;
            _dragging = false;
            _ghost = null;
            // 重铺之后手上那枚已经不存在了，捕获留在 Root 上会把之后的鼠标事件都吸过来
            Root.ReleaseMouseCapture();
            _chips.Children.Clear();
            var list = _get().ToList();
            for (int i = 0; i < list.Count; i++)
            {
                int idx = i;
                _chips.Children.Add(StepChip(GestureActions.LabelOf(list[i]), () =>
                {
                    var now = _get().ToList();
                    if (idx < now.Count) now.RemoveAt(idx);
                    _set(now.ToArray());
                    Render();
                }, idx));
            }
            _add = AddChip();
            _chips.Children.Add(_add);
        }

        /// <summary>一枚已选步骤的芯片：动作短名 + 一个删掉这一步的叉。芯片本体是拖动手柄。</summary>
        /// <param name="text">动作短名。</param>
        /// <param name="remove">点叉时要跑的删除动作。</param>
        /// <param name="index">这一步在序列里的位置，拖动排序据此起算。</param>
        /// <returns>已接好删除、悬停与拖动的芯片。</returns>
        Border StepChip(string text, Action remove, int index)
        {
            var close = CloseGlyph(remove);
            var body = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock
                    {
                        Text = text,
                        FontSize = 12.5,
                        Foreground = Theme.TextBrush,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    close,
                },
            };
            var chip = new Border
            {
                CornerRadius = new CornerRadius(6),
                MinHeight = ChipHeight,   // 与加号芯片同高：行里空了也不塌
                Padding = new Thickness(10, 5, 8, 5),
                Margin = new Thickness(2),
                Background = Theme.RowBgBrush,
                Cursor = Cursors.SizeAll,
                Child = body,
                ToolTip = Loc.T(K.Settings.DragThisStepToReorder),
            };
            Fluent.ChipHover(chip);
            chip.MouseLeftButtonDown += (_, e) =>
            {
                // 点叉号那一下归叉号：不做起拖，也不去抢 Root 的捕获
                if (IsInside(close, e.OriginalSource as DependencyObject)) return;
                _drag = chip; _dragFrom = index; _target = index;
                _pressAt = e.GetPosition(Root);
                _grab = e.GetPosition(chip);
                _dragging = false;
                // 捕获握在 Root 上而不是芯片上：芯片抬起来就离开这一行，
                // 离开视觉树的那一刻它手上的捕获会被收回，卡片从此收不到移动，钉在抬起处不动。
                Root.CaptureMouse();
            };
            return chip;
        }

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
            if (!_dragging) return;
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

        /// <summary>芯片尾部那个叉。</summary>
        /// <param name="remove">点叉时要跑的删除动作。</param>
        /// <returns>叉字形，已接好点击。</returns>
        static TextBlock CloseGlyph(Action remove)
        {
            var x = new TextBlock
            {
                Text = "\uE711",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"),
                FontSize = 10,
                Foreground = Theme.TextDimBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 1, 0),
                Cursor = Cursors.Hand,
            };
            x.MouseLeftButtonUp += (_, e) => { remove(); e.Handled = true; };
            return x;
        }

        /// <summary>序列末尾的加号芯片：在右侧点开候选卡，往末尾追加一步。</summary>
        /// <returns>已接事件的加号。</returns>
        Border AddChip()
        {
            var add = new Border
            {
                CornerRadius = new CornerRadius(6),
                MinHeight = ChipHeight,   // 没有步骤时也占满一行的高度：加号矮一档，卡片跟着一缩一伸
                Padding = new Thickness(9, 5, 9, 5),
                Margin = new Thickness(2),
                Background = Theme.RowBgBrush,
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = "\uE710",
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"),
                    FontSize = 12,
                    Foreground = Theme.TextDimBrush,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                ToolTip = Loc.T(K.Settings.AddAStepAfterThis),
            };
            Fluent.ChipHover(add);
            add.MouseLeftButtonUp += (_, e) => { if (_pop.IsOpen) ClosePicker(); else OpenPicker(); e.Handled = true; };
            return add;
        }

        /// <summary>在加号右侧点开候选卡：卡片先就位，条目再从右逐条滑进来。</summary>
        void OpenPicker()
        {
            var list = new StackPanel { Margin = new Thickness(4) };
            foreach (var a in _candidates(_get().Length))
            {
                var name = a.Name;
                list.Children.Add(PickerItem(a, () =>
                {
                    var now = _get().ToList();
                    now.Add(name);
                    ClosePicker();
                    _set(now.ToArray());
                    Render();
                }));
            }
            var card = new Border
            {
                Focusable = true,
                CornerRadius = new CornerRadius(6),
                BorderBrush = Theme.StrokeBrush,
                BorderThickness = new Thickness(1),
                Background = Theme.PanelBgBrush,
                Child = new ScrollViewer
                {
                    MaxHeight = 280,
                    Padding = new Thickness(2),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = list,
                },
            };
            Fluent.Elevate(card, blur: 24, depth: 8);
            // 焦点离开这张卡就是「不挑了」：卡是独立视觉树，Esc 与点击都先落在卡里，
            // 设置窗那道 Esc 关窗的闸收不到这一记。
            card.PreviewLostKeyboardFocus += (_, e) =>
            {
                if (!_pop.IsOpen) return;
                e.Handled = true;
                ClosePicker();
            };
            _pop.Child = card;
            _pop.PlacementTarget = _add;
            _pop.HorizontalOffset = 6;
            _pop.IsOpen = true;
            card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            card.Width = Math.Ceiling(card.DesiredSize.Width);
            card.Height = Math.Ceiling(card.DesiredSize.Height);
            PlayPopIn(card, list);
            card.Focus();
            // 切到别的应用不是「挑好了」，是用户已经不在这一窗了。候选卡在自己的视觉树里，
            // 设置窗那道失焦收不到这一记，只能由宿主窗盯着去激活与窗外的点击。
            _hostWin = Window.GetWindow(Root);
            if (_hostWin != null)
            {
                _hostWin.Deactivated += OnHostGone;
                _hostWin.PreviewMouseDown += OnHostPressedOutside;
            }
        }

        /// <summary>宿主窗去了后台：带着候选卡走没意义，收。</summary>
        /// <param name="s">事件源。</param>
        /// <param name="e">事件参数。</param>
        void OnHostGone(object? s, EventArgs e) { if (_pop.IsOpen) ClosePicker(); }

        /// <summary>按在设置窗里的别处：卡是 <c>StaysOpen</c> 的弹层，窗外点击它自己不收，这里替它收。
        /// 加号自己那一按交给它的开关逻辑，别在这里抢着关掉再被它重新打开。</summary>
        /// <param name="s">事件源。</param>
        /// <param name="e">鼠标事件，取命中的那个元素。</param>
        void OnHostPressedOutside(object? s, MouseButtonEventArgs e)
        {
            if (!_pop.IsOpen || _pop.Child is not Border card) return;
            var src = e.OriginalSource as DependencyObject;
            if (IsInside(card, src) || IsInside(_add, src)) return;
            ClosePicker();
        }

        /// <summary>命中的元素是否落在给定容器之内（含它自己）。</summary>
        /// <param name="host">容器。</param>
        /// <param name="src">命中的元素，可为空。</param>
        /// <returns>在容器里为 true。</returns>
        static bool IsInside(DependencyObject host, DependencyObject? src)
        {
            for (var o = src; o != null; o = VisualTreeHelper.GetParent(o))
                if (ReferenceEquals(o, host)) return true;
            return false;
        }

        /// <summary>候选卡入场：整张卡做一次弹性缩放，条目随后从右逐条滑进来。</summary>
        /// <param name="card">卡片。</param>
        /// <param name="list">条目面板。</param>
        static void PlayPopIn(Border card, Panel list)
        {
            card.RenderTransformOrigin = new Point(0, 0);
            var st = new ScaleTransform(0.9, 0.9);
            card.RenderTransform = st;
            var grow = new DoubleAnimation(0.9, 1.0, new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.EnterMs)))
            { EasingFunction = new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut } };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            int i = 0;
            foreach (UIElement c in list.Children)
            {
                var tt = new TranslateTransform(18, 0);
                c.RenderTransform = tt;
                c.Opacity = 0;
                var begin = TimeSpan.FromMilliseconds(Fluent.Motion.EnterMs * 0.5 + i * RowStep.TotalMilliseconds);
                c.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1,
                    new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.QuickMs))) { BeginTime = begin });
                tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(18, 0,
                    new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.EnterMs)))
                { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                i++;
            }
        }

        /// <summary>收候选卡：条目先往右退出，卡片再淡掉，走完才真的关掉弹层。</summary>
        void ClosePicker()
        {
            if (_hostWin != null)
            {
                _hostWin.Deactivated -= OnHostGone;
                _hostWin.PreviewMouseDown -= OnHostPressedOutside;
                _hostWin = null;
            }
            if (!_pop.IsOpen || _pop.Child is not Border card) { _pop.IsOpen = false; return; }
            if (card.Child is ScrollViewer sv && sv.Content is Panel list)
            {
                int i = 0;
                foreach (UIElement c in list.Children)
                {
                    if (c.RenderTransform is not TranslateTransform tt) continue;
                    var begin = TimeSpan.FromMilliseconds(i * (RowStep.TotalMilliseconds / 2));
                    c.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0,
                        new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.QuickMs))) { BeginTime = begin });
                    tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(tt.X, 18,
                        new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.QuickMs)))
                    { BeginTime = begin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
                    i++;
                }
            }
            var fade = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.ExitMs)))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fade.Completed += (_, _) => { _pop.IsOpen = false; _pop.Child = null; };
            card.BeginAnimation(OpacityProperty, fade);
        }

        /// <summary>候选卡里的一条：动作短名在上，说明在下，整条可点。</summary>
        /// <param name="a">这个动作。</param>
        /// <param name="apply">选中要跑的动作。</param>
        /// <returns>已接事件的条目。</returns>
        static Border PickerItem(GestureAction a, Action apply)
        {
            var bd = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(9, 6, 9, 6),
                Margin = new Thickness(1),
                // 底色取卡片同色而不是全透明：悬停是在底色上叠一层半透明强调，
                // 基色自己 alpha 为 0 时叠完还是全透明，指针进来什么也不会亮。
                Background = Theme.PanelBgBrush,
                Cursor = Cursors.Hand,
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = a.Label,
                            FontSize = 12.5,
                            Foreground = Theme.TextBrush,
                        },
                        new TextBlock
                        {
                            Text = a.Description,
                            FontSize = 11.5,
                            Foreground = Theme.TextDimBrush,
                            TextWrapping = TextWrapping.Wrap,
                            MaxWidth = 260,
                            Margin = new Thickness(0, 2, 0, 0),
                        },
                    },
                },
            };
            Fluent.ChipHover(bd);
            bd.MouseLeftButtonUp += (_, e) => { apply(); e.Handled = true; };
            return bd;
        }
    }
}
