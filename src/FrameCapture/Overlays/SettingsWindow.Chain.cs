using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

// 本 partial 职责：动作序列的芯片编辑器——快捷键页的「触发时 / 完成后 / 失败时」三行共用这一套控件。
/// <summary>
/// 设置窗里编辑动作序列的那部分。
/// </summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>
    /// 一条动作序列的芯片编辑器：横向芯片，末尾一枚加号。点芯片换这一步，点加号在后面加一步，
    /// 芯片上的叉只删这一步。候选动作就地铺在序列下方——一眼看全，不必在弹层里翻。
    /// </summary>
    sealed class ChainEditor
    {
        readonly Func<string[]> _get;
        readonly Func<int, IEnumerable<GestureAction>> _candidates;
        readonly Action<string[]> _set;
        readonly WrapPanel _chips = new();
        readonly WrapPanel _picker = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };

        /// <summary>正在挑动作的位置：-2 = 没在挑，-1 = 往末尾追加，非负 = 换掉这一步。</summary>
        int _picking = -2;

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
            Root.Children.Add(_picker);
            Render();
        }

        /// <summary>编辑器的根控件，含芯片行与就地铺开的候选行。</summary>
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
                var chip = StepChip(GestureActions.LabelOf(list[i]), () =>
                {
                    var now = _get().ToList();
                    if (idx < now.Count) now.RemoveAt(idx);
                    Close(now);
                });
                chip.MouseLeftButtonUp += (_, e) => { OpenPicker(idx); e.Handled = true; };
                _chips.Children.Add(chip);
            }
            _chips.Children.Add(AddChip());
        }

        /// <summary>一枚已选步骤的芯片：动作短名 + 一个删掉这一步的叉；点芯片本体是换掉这一步。</summary>
        /// <param name="text">动作短名。</param>
        /// <param name="remove">点叉时要跑的删除动作。</param>
        /// <returns>已接好删除与悬停的芯片，换步骤的点击由调用方接。</returns>
        static Border StepChip(string text, Action remove)
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
                Cursor = Cursors.Hand,
                Child = body,
                ToolTip = Loc.T(K.Settings.SwapThisStepForAnother),
            };
            Fluent.ChipHover(chip);
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
                Foreground = new SolidColorBrush(Theme.TextDim),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 1, 0),
                Cursor = Cursors.Hand,
            };
            x.MouseLeftButtonUp += (_, e) => { remove(); e.Handled = true; };
            return x;
        }

        /// <summary>序列末尾的加号芯片：点开候选，往末尾追加一步。</summary>
        /// <returns>已接事件的加号。</returns>
        Border AddChip()
        {
            var add = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(9, 5, 9, 5),
                Margin = new Thickness(2, 2, 2, 2),
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
            add.MouseLeftButtonUp += (_, e) => { OpenPicker(-1); e.Handled = true; };
            return add;
        }

        /// <summary>就地铺开候选动作。</summary>
        /// <param name="at">-1 表示往末尾追加，非负表示换掉这一步。</param>
        void OpenPicker(int at)
        {
            _picking = at;
            _picker.Children.Clear();
            foreach (var a in _candidates(at < 0 ? _get().Length : at))
            {
                var name = a.Name;
                var c = Chip(a.Label, a.Description, false);
                WireChip(c, () =>
                {
                    var now = _get().ToList();
                    if (_picking < 0) now.Add(name);
                    else if (_picking < now.Count) now[_picking] = name;
                    else now.Add(name);
                    Close(now);
                });
                _picker.Children.Add(c);
            }
            var cancel = Chip(Loc.T(K.Common.Cancel), Loc.T(K.Settings.LeaveThisStepAlone), false);
            WireChip(cancel, () => Close(_get().ToList()));
            _picker.Children.Add(cancel);
            _picker.Visibility = Visibility.Visible;
        }

        /// <summary>收候选、写回并重铺。</summary>
        /// <param name="list">改定的序列。</param>
        void Close(List<string> list)
        {
            _picking = -2;
            _picker.Visibility = Visibility.Collapsed;
            _set(list.ToArray());
            Render();
        }
    }
}
