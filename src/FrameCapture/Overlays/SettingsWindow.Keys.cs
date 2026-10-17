using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

// 本 partial 职责：快捷键页的编辑器键位一节——一条命令一行，按法可改、可减、可加。
/// <summary>
/// 设置窗的编辑器键位部分：整节由 <see cref="EditorCommands"/> 命令表驱动，一行一条命令。
/// 键位框点一下进录入态、按下新键即改，按 Esc 把这一枚按法收掉；加号在行尾再加一枚正在等键的框。
/// 改动经 <see cref="SettingsService.Mutate"/> 落盘，编辑器侧的分派表随即重建，无需重开窗口。
/// </summary>
public sealed partial class SettingsWindow : Window
{
    /// <summary>把编辑器键位一节装进快捷键页：按 <see cref="EditorCommands.Groups"/> 的顺序一个分组一张卡。</summary>
    /// <param name="panel">快捷键页的内容容器。</param>
    void BuildEditorKeys(StackPanel panel)
    {
        // 每行注册一个刷新动作：改一条键位可能让别的行冒出撞键提示，整表刷一遍最省事
        var ekRefresh = new List<Action>();
        bool firstCard = true;
        string ConflictNote(EditorCommand c)
        {
            var hits = new List<string>();
            foreach (var (chord, ids) in EditorCommands.Conflicts(_settings.Current))
                if (ids.Contains(c.Id))
                {
                    // 认不出的 Id 直接露出标识本身：撞键提示是给人排查的，比空一个名字有用
                    var other = ids.Where(i => i != c.Id).Select(i => EditorCommands.Find(i) is { } f ? Loc.T(f.Name) : i);
                    // 顿号与书名号是中文里的列举记法，英文换成逗号加弯引号，句子结构整个交给译名
                    hits.Add(Loc.F(K.Settings.SharesAKeyWith,
                        string.Join(Loc.T(K.Settings.ListSeparator), other.Select(n => Loc.F(K.Settings.QuotedName, n))), chord));
                }
            return string.Join(Loc.T(K.Settings.Semicolon), hits.Distinct());
        }
        Border MiniButton(string text, Action onClick)
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Theme.Stroke),
                Background = Brushes.Transparent,
                Padding = new Thickness(8, 3, 8, 3),
                MinHeight = 30,   // 按内容排出来只有 23 逻辑像素高，手指点在字上才命中；撑到 30 让整颗按钮点得着。
                Cursor = Cursors.Hand,
                Child = new TextBlock { Text = text, FontSize = 11.5, Foreground = new SolidColorBrush(Theme.TextDim) },
            };
            Fluent.ChipHover(b);
            b.MouseLeftButtonUp += (_, _) => onClick();
            return b;
        }
        void EditorKeyGroup(Str group)
        {
            var cmds = EditorCommands.All.Where(c => c.Group == group).ToList();
            if (cmds.Count == 0) return;
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = Loc.T(group),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Theme.TextDim),
                Margin = new Thickness(0, 0, 0, 4),
            });
            // 键位区按内容占宽：一枚键帽占一枚的宽度，放不下才换行
            const double BoxGap = 6;
            foreach (var c in cmds)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                nameStack.Children.Add(new TextBlock
                {
                    Text = Loc.T(c.Name),
                    FontSize = 12.5,
                    Foreground = new SolidColorBrush(Theme.Text),
                    TextWrapping = TextWrapping.Wrap,
                });
                var note = new TextBlock
                {
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Theme.Danger),
                    TextWrapping = TextWrapping.Wrap,
                    Visibility = Visibility.Collapsed,
                };
                nameStack.Children.Add(note);
                // 靠右摆：加号那一列上下对齐，按法多的行往左长，名字列始终在左边
                var boxes = new WrapPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right,
                };
                var reset = MiniButton(Loc.T(K.Settings.Reset), () =>
                {
                    _settings.Mutate(s => s.EditorKeys.Remove(c.Id));
                    foreach (var f in ekRefresh) f();
                });
                reset.Margin = new Thickness(8, 0, 0, 0);
                reset.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(nameStack, 0);
                Grid.SetColumn(boxes, 1);
                Grid.SetColumn(reset, 3);
                row.Children.Add(nameStack);
                row.Children.Add(boxes);
                row.Children.Add(reset);
                // 一行一枚胶囊：与快捷键行同一层描边。整排都是能拖的行，底色跟着指针亮起来
                // 反而像在暗示只有亮的那一行能碰，所以静止与悬停同一张脸；名字离左边框留一段呼吸。
                var pill = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    BorderBrush = new SolidColorBrush(Theme.StrokeSoft),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(16, 7, 8, 7),
                    Margin = new Thickness(0, 2, 0, 2),
                    Child = row,
                };
                stack.Children.Add(pill);

                // 加号按钮：多一个按法不必挑默认那几个槽位，随时往上加
                var add = new Border
                {
                    Width = 30,
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Theme.Stroke),
                    Background = Brushes.Transparent,
                    Padding = new Thickness(0, 6, 0, 6),
                    Margin = new Thickness(0, 1, 0, 1),
                    Cursor = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = "+",
                        FontSize = 14,
                        TextAlignment = TextAlignment.Center,
                        Foreground = new SolidColorBrush(Theme.TextDim),
                    },
                };
                Fluent.ChipHover(add);
                Grid.SetColumn(add, 2);
                row.Children.Add(add);
                // 点加号 = 当场多出一枚正在等键的框：按下键才落进列表，中途走开就当没加过
                add.MouseLeftButtonUp += (_, _) => RenderRow(true);

                // 一行里所有键位框由当前设置画出来：加一枚、解一枚、别的行撞了键，都靠重画跟上
                void RenderRow(bool pending)
                {
                    var s = _settings.Current;
                    var list = EditorCommands.BindingsFor(s, c);
                    boxes.Children.Clear();
                    for (int i = 0; i < list.Count; i++)
                    {
                        int at = i;
                        var box = new ChordCaptureBox
                        {
                            Chord = list[at],
                            Margin = new Thickness(0, 1, BoxGap, 1),
                        };
                        box.Picked += v =>
                        {
                            _settings.Mutate(s2 =>
                            {
                                var l = EditorCommands.BindingsFor(s2, c);
                                if (v.Length == 0) { if (at < l.Count) l.RemoveAt(at); }
                                else if (at < l.Count) l[at] = v;
                                else l.Add(v);
                                EditorCommands.WriteBindings(s2, c, l);
                            });
                            foreach (var f in ekRefresh) f();
                        };
                        boxes.Children.Add(box);
                    }
                    if (pending)
                    {
                        // 新键位框一出来就在等键：按下键才落进列表，中途走开就当没加过
                        var fresh = new ChordCaptureBox { Margin = new Thickness(0, 1, BoxGap, 1) };
                        fresh.Picked += v =>
                        {
                            if (v.Length > 0)
                                _settings.Mutate(s2 =>
                                {
                                    var l = EditorCommands.BindingsFor(s2, c);
                                    l.Add(v);
                                    EditorCommands.WriteBindings(s2, c, l);
                                });
                            foreach (var f in ekRefresh) f();
                        };
                        fresh.Cancelled += () => { foreach (var f in ekRefresh) f(); };
                        boxes.Children.Add(fresh);
                        fresh.BeginCapture();
                    }
                    else if (list.Count == 0)
                    {
                        boxes.Children.Add(new TextBlock
                        {
                            Text = Loc.T(K.Common.NoKey),
                            FontSize = 12.5,
                            Foreground = new SolidColorBrush(Theme.TextDim),
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(0, 0, BoxGap, 0),
                        });
                    }
                    reset.Visibility = EditorCommands.IsCustomized(s, c) ? Visibility.Visible : Visibility.Collapsed;
                    note.Text = ConflictNote(c);
                    note.Visibility = note.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                }
                ekRefresh.Add(() => RenderRow(false));
                RenderRow(false);
            }
            var card = new Border
            {
                // 第一张卡离上面的快捷键卡片远一点：这一节没有标题也没有说明文字，分组靠留白说
                Margin = new Thickness(0, firstCard ? 16 : 2, 0, 2),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Theme.Stroke),
                Background = new SolidColorBrush(Theme.CardBg),
                Padding = new Thickness(12, 10, 12, 10),
                Child = stack,
            };
            Fluent.Elevate(card, blur: 24, depth: 6);
            panel.Children.Add(card);
            firstCard = false;
        }
        foreach (var g in EditorCommands.Groups) EditorKeyGroup(g);
        var ekResetAll = MiniButton(Loc.T(K.Settings.RestoreAllDefaultKeys), () =>
        {
            _settings.Mutate(s => s.EditorKeys.Clear());
            foreach (var f in ekRefresh) f();
        });
        ekResetAll.HorizontalAlignment = HorizontalAlignment.Left;
        ekResetAll.Margin = new Thickness(2, 6, 0, 2);
        panel.Children.Add(ekResetAll);
        foreach (var f in ekRefresh) f();
    }
}
