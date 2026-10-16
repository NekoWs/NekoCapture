using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

        /// <summary>正在拖的那枚芯片；null 表示没在拖。</summary>
        Border? _drag;

        /// <summary>落点占位框：虚线框出现在哪，松手就落在哪。</summary>
        Border? _ghost;

        /// <summary>拖起前各芯片在 Root 里的位置，重排后据此把差值动画回 0。</summary>
        readonly Dictionary<UIElement, Point> _before = new();

        int _dragFrom = -1, _target = -1;
        Point _pressAt, _grab, _cursor;
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
            Root = new StackPanel();
            Root.Children.Add(_chips);
            Render();
        }

        /// <summary>编辑器的根控件。</summary>
        /// <value>挂进设置行即可用。</value>
        public StackPanel Root { get; }

        /// <summary>按当前序列重铺芯片。写回之后由编辑器自己调用，外部只在切换简单/高级时调用一次。</summary>
        public void Render()
        {
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
            var body = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new TextBlock
                    {
                        Text = text,
                        FontSize = 12.5,
                        Foreground = new SolidColorBrush(Theme.Text),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    CloseGlyph(remove),
                },
            };
            var chip = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 5, 8, 5),
                Margin = new Thickness(2),
                Background = new SolidColorBrush(Theme.Hover),
                Cursor = Cursors.SizeAll,
                Child = body,
                ToolTip = Loc.T(K.Settings.DragThisStepToReorder),
            };
            Fluent.ChipHover(chip);
            chip.MouseLeftButtonDown += (_, e) =>
            {
                _drag = chip; _dragFrom = index; _target = index;
                _pressAt = e.GetPosition(Root);
                _grab = e.GetPosition(chip);
                _dragging = false;
                chip.CaptureMouse();
            };
            chip.MouseMove += (_, e) => DragMove(e.GetPosition(Root));
            chip.MouseLeftButtonUp += (_, e) => Drop(e.GetPosition(Root));
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

        /// <summary>起拖：记下所有芯片的当前位置，把占位框插在原位，芯片从排版里「浮」起来。</summary>
        void BeginDrag()
        {
            var chip = _drag;
            if (chip == null) return;
            _dragging = true;
            Snapshot();
            // 占位框底色取主题强调色的淡层：深色底下黑色淡层看不见，浅色底下又太脏
            var ac = Theme.Accent;
            _ghost = new Border
            {
                Width = Math.Max(40, chip.ActualWidth - 4),
                Height = Math.Max(20, chip.ActualHeight - 4),
                Margin = new Thickness(2),
                CornerRadius = new CornerRadius(6),
                BorderBrush = new SolidColorBrush(Theme.Accent),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(0x2E, ac.R, ac.G, ac.B)),
            };
            _chips.Children.Insert(_dragFrom, _ghost);
            chip.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.45 };
            Panel.SetZIndex(chip, 1);
        }

        /// <summary>把拖起的芯片按光标摆好：左上角落在光标减去起拖时按到的那个点，芯片不会在手里跳。</summary>
        /// <param name="at">光标在 Root 里的坐标。</param>
        void Lift(Point at)
        {
            var chip = _drag;
            if (chip == null || !_before.TryGetValue(chip, out var home)) return;
            var tt = TransformOf(chip);
            double dx = at.X - _grab.X - home.X;
            double dy = at.Y - _grab.Y - home.Y;
            tt.Item1.X = dx;
            tt.Item1.Y = dy;
            if (tt.Item2.ScaleX < 1.01)
            {
                tt.Item2.BeginAnimation(ScaleTransform.ScaleXProperty, ScaleTo(1.06));
                tt.Item2.BeginAnimation(ScaleTransform.ScaleYProperty, ScaleTo(1.06));
            }
        }

        /// <summary>落点换格：占位框移到新位置，其余芯片把排版差值动画回 0，看起来就是它们让开了。</summary>
        /// <param name="want">新的落点下标。</param>
        void ShiftTo(int want)
        {
            if (_ghost == null) return;
            _chips.Children.Remove(_ghost);
            _chips.Children.Insert(Math.Clamp(want, 0, _chips.Children.Count), _ghost);
            _target = want;
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
        /// 不必按 WrapPanel 的换行细节算位移。</summary>
        void PlayShift()
        {
            foreach (UIElement c in _chips.Children)
            {
                if (ReferenceEquals(c, _drag)) continue;
                var now = c.TranslatePoint(new Point(0, 0), Root);
                var old = _before.TryGetValue(c, out var p) ? p : now;
                double dx = old.X - now.X, dy = old.Y - now.Y;
                if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5) continue;
                var tt = TransformOf(c);
                tt.Item1.X = dx;
                tt.Item1.Y = dy;
                var d = new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.StateMs));
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                tt.Item1.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(dx, 0, d) { EasingFunction = ease });
                tt.Item1.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(dy, 0, d) { EasingFunction = ease });
            }
        }

        /// <summary>光标此刻落在哪一格：按每枚芯片的中线判，光标在中线左侧就是这一格。</summary>
        /// <param name="at">光标在 Root 里的坐标。</param>
        /// <returns>落点下标。</returns>
        int SlotAt(Point at)
        {
            int n = _chips.Children.Count;
            for (int i = 0; i < n; i++)
            {
                if (ReferenceEquals(_chips.Children[i], _drag)) continue;
                var p = _chips.Children[i].TranslatePoint(new Point(0, 0), Root);
                var sz = _chips.Children[i].RenderSize;
                if (at.Y < p.Y || at.Y > p.Y + sz.Height) continue;
                if (at.X < p.X + sz.Width / 2) return i;
            }
            // 加号永远在最后，落点最多排到它前面
            return Math.Max(0, n - 1);
        }

        /// <summary>松手：芯片落进占位框占的那一格，序列按新顺序写回。</summary>
        /// <param name="at">松手处光标在 Root 里的坐标。</param>
        void Drop(Point at)
        {
            if (_drag == null) return;
            var chip = _drag;
            _drag = null;
            chip.ReleaseMouseCapture();
            if (!_dragging) return;
            _dragging = false;
            int from = _dragFrom, to = _target;
            // 占位框占的那一格在移除原芯片后不算一格：往后挪一位时要还回来
            if (to > from) to--;
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
        /// <returns>位移与缩放两个变换，顺序固定：先位移后缩放。</returns>
        static (TranslateTransform, ScaleTransform) TransformOf(UIElement el)
        {
            if (el.RenderTransform is TransformGroup tg
                && tg.Children.OfType<TranslateTransform>().FirstOrDefault() is { } t
                && tg.Children.OfType<ScaleTransform>().FirstOrDefault() is { } s)
                return (t, s);
            t = new TranslateTransform();
            s = new ScaleTransform(1, 1);
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            el.RenderTransform = new TransformGroup { Children = { t, s } };
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
                Foreground = new SolidColorBrush(Theme.TextDim),
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
                Padding = new Thickness(9, 5, 9, 5),
                Margin = new Thickness(2),
                Background = new SolidColorBrush(Theme.Hover),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = "\uE710",
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Theme.TextDim),
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
                BorderBrush = new SolidColorBrush(Theme.Stroke),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Theme.PanelBg),
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
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = a.Label,
                            FontSize = 12.5,
                            Foreground = new SolidColorBrush(Theme.Text),
                        },
                        new TextBlock
                        {
                            Text = a.Description,
                            FontSize = 11.5,
                            Foreground = new SolidColorBrush(Theme.TextDim),
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
