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

// 本 partial 职责：动作序列的芯片编辑器——动作链与失败链共用控件。
/// <summary>
/// 设置窗里编辑动作序列的那部分。
/// 芯片按顺序排开，末尾一枚加号：加号在右侧点开一张候选卡，选中即追加到末尾。
/// 芯片可拖动或通过箭头改序，叉号删除，数据徽标显示前置值与产出。
/// </summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>
    /// 一条动作序列的芯片编辑器：横向芯片，末尾一枚加号。
    /// </summary>
    sealed partial class ChainEditor
    {
        readonly Func<string[]> _get;
        readonly Func<int, IEnumerable<GestureAction>> _candidates;
        readonly Action<string[]> _set;
        readonly Func<string, string>? _nodeLabel;
        readonly Action<int>? _openNode;
        readonly Func<string, bool>? _isSelected;
        readonly WrapPanel _chips = new();
        readonly Popup _pop = new() { AllowsTransparency = true, StaysOpen = true, Placement = PlacementMode.Right };
        Border _add = null!;
        string[] _paintedIds = Array.Empty<string>();
        int _chipMotionGeneration, _pickerGeneration;
        bool _pickerClosing;

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

        /// <summary>装配动作目录中的有序链。</summary>
        /// <param name="get">读取动作名。</param>
        /// <param name="candidates">可添加的动作。</param>
        /// <param name="set">保存顺序。</param>
        public ChainEditor(Func<string[]> get, Func<int, IEnumerable<GestureAction>> candidates, Action<string[]> set)
            : this(get, candidates, set, null, null, null) { }

        /// <summary>装配一个序列编辑器。</summary>
        /// <param name="get">取当前序列。</param>
        /// <param name="candidates">给定插入位返回可挑的动作。</param>
        /// <param name="set">改定后的写回动作，内部负责落盘。</param>
        /// <param name="nodeLabel">图节点的显示名称；为空时按动作目录显示。</param>
        /// <param name="openNode">点击未拖动的节点时进入其事件出口。</param>
        /// <param name="isSelected">节点是否位于当前编辑路径。</param>
        public ChainEditor(Func<string[]> get, Func<int, IEnumerable<GestureAction>> candidates, Action<string[]> set,
            Func<string, string>? nodeLabel = null, Action<int>? openNode = null, Func<string, bool>? isSelected = null)
        {
            _get = get;
            _candidates = candidates;
            _set = set;
            _nodeLabel = nodeLabel; _openNode = openNode; _isSelected = isSelected;
            Root = new StackPanel { Background = Brushes.Transparent };
            Root.Children.Add(_chips);
            // 拖动中的移动与松手挂在 Root 上：它全程待在视觉树里，芯片抬起来、挂上装饰层都影响不到它。
            // 挂在这两种地方之外都会断线——芯片一离开这一行就再也收不到鼠标事件。
            Root.MouseMove += (_, e) => DragMove(e.GetPosition(Root));
            Root.MouseLeftButtonUp += (_, e) => Drop(e.GetPosition(Root));
            Root.Unloaded += (_, _) =>
            {
                ClosePicker(); DetachLift(); Root.ReleaseMouseCapture();
                _chipMotionGeneration++; Root.BeginAnimation(HeightProperty, null); Root.Height = double.NaN;
            };
            Render();
        }

        /// <summary>编辑器的根控件。</summary>
        /// <value>挂进设置行即可用。</value>
        public StackPanel Root { get; }

        /// <summary>按当前序列重铺芯片。写回之后由编辑器自己调用，外部可在绑定内容变化后调用。</summary>
        public void Render()
        {
            var list = _get().ToList();
            var previous = CaptureChipLayout(list);
            double oldHeight = Root.ActualHeight;
            // 重铺清空芯片行，而正在手里的那枚早已不在行里：不先从装饰层摘掉，它会悬在屏幕上不走
            DetachLift();
            _drag = null;
            _dragging = false;
            _ghost = null;
            // 重铺之后手上那枚已经不存在了，捕获留在 Root 上会把之后的鼠标事件都吸过来
            Root.ReleaseMouseCapture();
            _chips.Children.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                int idx = i;
                var chip = StepChip(_nodeLabel?.Invoke(list[i]) ?? GestureActions.LabelOf(list[i]), () =>
                {
                    var now = _get().ToList();
                    if (idx < now.Count) now.RemoveAt(idx);
                    _set(now.ToArray());
                    Render();
                }, idx);
                if (_nodeLabel == null) DecorateChip(chip, list, idx);
                else
                {
                    chip.ToolTip = Loc.T(K.Behaviors.Description);
                    var body = (StackPanel)chip.Child;
                    body.Children.Insert(1, new TextBlock { Text = "\uE76C", FontFamily = new FontFamily("Segoe Fluent Icons"),
                        Foreground = Theme.TextDimBrush, FontSize = 9, Margin = new Thickness(7, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center });
                    chip.BorderThickness = new Thickness(1);
                    chip.BorderBrush = _isSelected?.Invoke(list[i]) == true ? Theme.AccentBrush : Brushes.Transparent;
                }
                _chips.Children.Add(chip);
            }
            _add = AddChip();
            _chips.Children.Add(_add);
            PaintEmptyHint(list.Count == 0);
            PlayChipChanges(previous, list, oldHeight);
            _paintedIds = list.ToArray();
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
                Padding = new Thickness(10, 1.5, 8, 1.5),
                Margin = new Thickness(2),
                Background = Theme.RowBgBrush,
                Cursor = Cursors.Hand,
                Child = body,
                ToolTip = Loc.T(K.Hotkeys.RunsInOrderDragToReorder),
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
                Width = 24, Height = 24, Padding = new Thickness(7, 6, 0, 0),
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
                ToolTip = Loc.T(K.Hotkeys.AddStep),
            };
            Fluent.ChipHover(add);
            add.MouseLeftButtonUp += (_, e) =>
            { if (_pop.IsOpen && !_pickerClosing) ClosePicker(); else OpenPicker(); e.Handled = true; };
            return add;
        }

        /// <summary>在加号右侧点开候选卡：卡片先就位，条目再从右逐条滑进来。</summary>
        void OpenPicker()
        {
            int generation = ++_pickerGeneration; _pickerClosing = false;
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
                if (!_pop.IsOpen || generation != _pickerGeneration || !ReferenceEquals(_pop.Child, card)
                    || IsInside(card, e.NewFocus as DependencyObject)) return;
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
            if (_pickerClosing) return;
            _pickerClosing = true;
            int generation = ++_pickerGeneration;
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
            fade.Completed += (_, _) =>
            {
                if (generation != _pickerGeneration || !ReferenceEquals(_pop.Child, card)) return;
                _pop.IsOpen = false; _pop.Child = null; _pickerClosing = false;
            };
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
