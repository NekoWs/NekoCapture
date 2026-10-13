using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using GestureCore;
using GcMods = GestureCore.ModifierKeys;

namespace GestureEditor;

// 本 partial 承载绑定编辑交互：绑定增删改、详情编辑、组合键录键、校验与保存、列表刷新。
public sealed partial class GestureEditorWindow : Window
{
    // ---------- 列表（R161 卡片化：边框+底色，选中/问题/禁用逐行自绘） ----------
    void RefreshList()
    {
        _suppress = true;
        _list.Items.Clear();
        _rows.Clear();
        foreach (var b in _doc.Bindings)
        {
            var row = BuildRow(b);
            _rows.Add(row);
            _list.Items.Add(row.Card);
        }
        if (_selected >= _doc.Bindings.Count) _selected = _doc.Bindings.Count - 1;
        _list.SelectedIndex = _selected;
        _suppress = false;
        PaintRows();
    }

    BindingRow BuildRow(BindingEdit b)
    {
        var head = new TextBlock { FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        var body = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        var chord = b.Chord.Format(_namer);
        head.Text = (string.IsNullOrEmpty(chord) ? "未设键" : chord) + " · " + TriggerLabel(b.Trigger);
        var acts = b.Actions.Count == 0 ? "无动作" : string.Join(" → ", b.Actions.Select(ActionLabel));
        if (b.OnComplete.Count > 0) acts += "  ⟶ 完成后: " + string.Join(" → ", b.OnComplete.Select(ActionLabel));
        body.Text = acts;
        var sp = new StackPanel(); sp.Children.Add(head); sp.Children.Add(body);
        var card = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(1, 3, 3, 3), Child = sp };
        return new BindingRow { Card = card, Head = head, Body = body, Enabled = b.Enabled };
    }

    /// <summary>逐行刷三态：选中（accent 底 + OnAccent 字色）、问题（红框）、禁用（muted 灰字，不再加「已禁用 ·」前缀）。
    /// R162：三态切换走 140ms 颜色过渡（此前直接换刷子，选中/取消选中颜色硬跳）。</summary>
    void PaintRows()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            var r = _rows[i];
            bool sel = i == _list.SelectedIndex;
            bool has = i < _doc.Bindings.Count;
            r.Problem = has && _problemIds.Contains(_doc.Bindings[i].Id);
            r.Enabled = !has || _doc.Bindings[i].Enabled;
            AnimBrush(r.Card.Background as SolidColorBrush, sel ? _pal.Accent : _pal.CardBg, br => r.Card.Background = br);
            AnimBrush(r.Card.BorderBrush as SolidColorBrush, r.Problem ? Danger : (sel ? _pal.Accent : _pal.Stroke), br => r.Card.BorderBrush = br);
            AnimBrush(r.Head.Foreground as SolidColorBrush, sel ? _pal.OnAccent() : (r.Enabled ? _pal.Text : _pal.TextDim), br => r.Head.Foreground = br);
            AnimBrush(r.Body.Foreground as SolidColorBrush, sel ? _pal.OnAccent() : _pal.TextDim, br => r.Body.Foreground = br);
            r.Body.Opacity = r.Enabled || sel ? 1.0 : 0.6;
        }
    }

    string ActionLabel(string actionName) => _catalog.Actions().FirstOrDefault(a => a.Name == actionName)?.Label ?? actionName;

    // R157：截图后行为的中文标签（取值与宿主 AfterCapture 一致；空=跟随默认弹提示）
    static string AfterCaptureLabel(string v) => v switch
    {
        "" => "跟随默认",
        "editor" => "进编辑器",
        "copy" => "只复制",
        "pin" => "贴图",
        _ => "弹提示",
    };

    void AddBinding()
    {
        _doc.Bindings.Add(new BindingEdit { Id = "b" + Guid.NewGuid().ToString("N").Substring(0, 6), Trigger = TriggerKind.Chord });
        _selected = _doc.Bindings.Count - 1;
        RefreshList(); _list.SelectedIndex = _selected; ShowDetail(); RefreshValidation();
    }

    void DeleteSelected()
    {
        if (_selected < 0 || _selected >= _doc.Bindings.Count) return;
        _doc.Bindings.RemoveAt(_selected);
        _selected = Math.Min(_selected, _doc.Bindings.Count - 1);
        RefreshList(); if (_selected >= 0) _list.SelectedIndex = _selected; ShowDetail(); RefreshValidation();
    }

    // ---------- 详情 ----------
    void ShowDetail()
    {
        _detail.Children.Clear();
        if (_selected < 0 || _selected >= _doc.Bindings.Count)
        {
            _detail.Children.Add(new TextBlock { Text = "选择左侧一条绑定进行编辑，或点「添加绑定」。", Foreground = _pal.B(_pal.TextDim), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
            return;
        }
        var b = _doc.Bindings[_selected];

        // 启用/禁用此绑定（A1）：禁用=保留配置但不触发，无需删除
        _detail.Children.Add(MakeToggle("启用此绑定", v => { b.Enabled = v; RefreshList(); RefreshValidation(); }, out _, b.Enabled));

        // 组合键捕获
        _detail.Children.Add(Label("组合键"));
        _chordBox = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Background = _pal.B(_pal.CardBg), Padding = new Thickness(12, 9, 12, 9), Cursor = Cursors.Hand, Focusable = true };
        _chordStroke = new SolidColorBrush(_pal.Stroke); _chordBox.BorderBrush = _chordStroke;
        _chordHover = _chordFocus = false;
        _chordText = new TextBlock { Foreground = _pal.B(_pal.Text), FontSize = 14 };
        _chordBox.Child = _chordText;
        UpdateChordText();
        _chordBox.MouseLeftButtonDown += (_, _) => BeginCapture();
        _chordBox.KeyDown += (_, e) => { if (!_capturing && e.Key is Key.Space or Key.Enter) { BeginCapture(); e.Handled = true; } };
        // hover 与键盘焦点共用一套描边刷新：聚焦或悬停任一成立即转 accent；R161 改颜色过渡（此前硬切换生硬）
        void RefreshChordBorder() { if (!_capturing) AnimateColor(_chordStroke, (_chordHover || _chordFocus) ? _pal.Accent : _pal.Stroke, 140); }
        _chordBox.MouseEnter += (_, _) => { _chordHover = true; RefreshChordBorder(); };
        _chordBox.MouseLeave += (_, _) => { _chordHover = false; RefreshChordBorder(); };
        _chordBox.GotKeyboardFocus += (_, _) => { _chordFocus = true; RefreshChordBorder(); };
        _chordBox.LostKeyboardFocus += (_, _) => { _chordFocus = false; RefreshChordBorder(); };
        _detail.Children.Add(_chordBox);

        // 触发方式
        _detail.Children.Add(Label("触发方式"));
        var trig = MakeCombo(Enum.GetValues<TriggerKind>().Cast<object>(), TriggerLabel, b.Trigger, v => { b.Trigger = (TriggerKind)v; RefreshList(); RefreshValidation(); });
        _detail.Children.Add(trig);

        // 动作序列 + 完成后序列（A3）：同一套列表编辑器，复用于两处
        StackPanel BuildActionList(List<string> list, string title, string addLabel)
        {
            var root = new StackPanel();
            root.Children.Add(Label(title));
            var panel = new StackPanel();
            void Rebuild()
            {
                panel.Children.Clear();
                for (int i = 0; i < list.Count; i++)
                {
                    int idx = i;
                    // R161：已配置动作行卡片化——与旁边动作下拉等高（MinHeight 34）、序号做成独立徽标、
                    // 加边框+底色；此前是裸文本行「1. xxx」，太不显眼且序号和内容糊在一起。
                    var row = new Border
                    {
                        Margin = new Thickness(0, 2, 0, 2),
                        CornerRadius = new CornerRadius(6),
                        BorderThickness = new Thickness(1),
                        BorderBrush = _pal.B(_pal.Stroke),
                        Background = _pal.B(_pal.CardBg),
                        Padding = new Thickness(8, 0, 6, 0),
                        MinHeight = 34,
                    };
                    var g = new Grid();
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var badge = new Border
                    {
                        Width = 20, Height = 20, CornerRadius = new CornerRadius(5),
                        Background = _pal.B(_pal.TrackBg), VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock { Text = (i + 1).ToString(), FontSize = 11, Foreground = _pal.B(_pal.TextDim), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                    };
                    var txt = new TextBlock { Text = ActionLabel(list[i]), Foreground = _pal.B(_pal.Text), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                    var ctl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    var up = MiniBtn("\uE70E", () => { if (idx > 0) { (list[idx - 1], list[idx]) = (list[idx], list[idx - 1]); Rebuild(); RefreshList(); } });
                    var dn = MiniBtn("\uE70D", () => { if (idx < list.Count - 1) { (list[idx + 1], list[idx]) = (list[idx], list[idx + 1]); Rebuild(); RefreshList(); } });
                    var rm = MiniBtn("\uE74D", () => { list.RemoveAt(idx); Rebuild(); RefreshList(); RefreshValidation(); });
                    ctl.Children.Add(up); ctl.Children.Add(dn); ctl.Children.Add(rm);
                    Grid.SetColumn(badge, 0); Grid.SetColumn(txt, 1); Grid.SetColumn(ctl, 2);
                    g.Children.Add(badge); g.Children.Add(txt); g.Children.Add(ctl);
                    row.Child = g;
                    panel.Children.Add(row);
                }
                UpdateAfterSec();   // R161：序列增删/换序后重判「截图后」是否还该显示
            }
            Rebuild();
            root.Children.Add(panel);
            var addRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var actCombo = new ComboBox { Width = 200 };
            EditorStyles.ApplyCombo(actCombo, _pal);
            foreach (var a in _catalog.Actions()) actCombo.Items.Add(new ComboBoxItem { Content = a.Label, Tag = a.Name, ToolTip = a.Description });
            if (actCombo.Items.Count > 0) actCombo.SelectedIndex = 0;
            var addAct = MakeButton(addLabel, ghost: true);
            addAct.Margin = new Thickness(8, 0, 0, 0);   // R161：与下拉隔开边距（此前右外边距，按钮直接黏住下拉框）
            addAct.Click += (_, _) => { if (actCombo.SelectedItem is ComboBoxItem it && it.Tag is string n) { list.Add(n); Rebuild(); RefreshList(); RefreshValidation(); } };
            addRow.Children.Add(actCombo); addRow.Children.Add(addAct);
            root.Children.Add(addRow);
            return root;
        }
        _detail.Children.Add(BuildActionList(b.Actions, "动作序列", "添加动作"));
        _detail.Children.Add(BuildActionList(b.OnComplete, "完成后序列", "添加完成动作"));

        // 截图后（R157）：本条手势触发截图完成后的行为；空=跟随默认（弹提示）。
        // R161：序列里没有截图类动作时整段隐藏（录制/编辑器/OCR 之外的手势配它没意义，徒增困惑）。
        _afterSec = new StackPanel();
        _afterSec.Children.Add(Label("截图后"));
        var afterOpts = new object[] { "", "toast", "editor", "copy", "pin" };
        _afterSec.Children.Add(MakeCombo(afterOpts, v => AfterCaptureLabel((string)v), b.AfterCapture, v => { b.AfterCapture = (string)v; RefreshList(); }));
        _detail.Children.Add(_afterSec);
        UpdateAfterSec();

        // 失败策略 + 优先级
        var two = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var fbCol = new StackPanel(); fbCol.Children.Add(Label("失败策略"));
        fbCol.Children.Add(MakeCombo(Enum.GetValues<FallbackPolicy>().Cast<object>(), FallbackLabel, b.Fallback, v => b.Fallback = (FallbackPolicy)v));
        var prCol = new StackPanel(); prCol.Children.Add(Label("优先级"));
        prCol.Children.Add(MakeNum(b.Priority.ToString(), s => { if (int.TryParse(s, out var p)) b.Priority = p; }));
        Grid.SetColumn(fbCol, 0); Grid.SetColumn(prCol, 2);
        two.Children.Add(fbCol); two.Children.Add(prCol);
        _detail.Children.Add(two);

        // 时间参数
        var two2 = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        two2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        two2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        two2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var dcCol = new StackPanel(); dcCol.Children.Add(Label("双击间隔 ms"));
        dcCol.Children.Add(MakeNum(b.DoubleClickMs.ToString(), s => { if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) b.DoubleClickMs = v; RefreshValidation(); }));
        var lpCol = new StackPanel(); lpCol.Children.Add(Label("长按阈值 ms"));
        lpCol.Children.Add(MakeNum(b.LongPressMs.ToString(), s => { if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) b.LongPressMs = v; RefreshValidation(); }));
        Grid.SetColumn(dcCol, 0); Grid.SetColumn(lpCol, 2);
        two2.Children.Add(dcCol); two2.Children.Add(lpCol);
        _detail.Children.Add(two2);
    }

    /// <summary>R161：序列（动作+完成后）是否含截图类动作——由宿主目录的 IsCapture 标记决定，库不硬编码动作名。</summary>
    bool HasCaptureAction(BindingEdit b)
    {
        var caps = new HashSet<string>(_catalog.Actions().Where(a => a.IsCapture).Select(a => a.Name));
        return b.Actions.Any(caps.Contains) || b.OnComplete.Any(caps.Contains);
    }

    void UpdateAfterSec()
    {
        if (_afterSec == null) return;   // ShowDetail 首建期间（动作列表先于本段构建）
        bool show = _selected >= 0 && _selected < _doc.Bindings.Count && HasCaptureAction(_doc.Bindings[_selected]);
        _afterSec.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    void UpdateChordText()
    {
        if (_selected < 0 || _selected >= _doc.Bindings.Count) return;
        var b = _doc.Bindings[_selected];
        var s = b.Chord.Format(_namer);
        _chordText.Text = _capturing ? "按下新键…（Esc 取消）" : (string.IsNullOrEmpty(s) ? "点击设置" : s);
        _chordText.Foreground = _pal.B(_capturing ? _pal.Accent : _pal.Text);
        if (_chordStroke != null)
            AnimateColor(_chordStroke, _capturing || _chordHover || _chordFocus ? _pal.Accent : _pal.Stroke, 140);
    }

    // ---------- 组合键捕获 ----------
    void BeginCapture()
    {
        EndCapture();
        _capturing = true;
        ChordCaptureBegan?.Invoke();   // 宿主暂停全局钩子，否则组合键被吞、编辑器收不到
        UpdateChordText();
        _chordBox.Focus();
    }

    void EndCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        ChordCaptureEnded?.Invoke();   // 宿主恢复全局钩子
        UpdateChordText();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_capturing)
        {
            e.Handled = true;
            if (e.Key == Key.Escape) { EndCapture(); return; }
            if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
                or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return; // 等主键
            var mods = e.KeyboardDevice.Modifiers;
            bool isF = e.Key >= Key.F1 && e.Key <= Key.F24;
            if (!isF && mods == System.Windows.Input.ModifierKeys.None) return; // 非 F 键须带修饰键
            if (isF) mods = System.Windows.Input.ModifierKeys.None;
            if (_selected >= 0 && _selected < _doc.Bindings.Count)
            {
                var b = _doc.Bindings[_selected];
                b.Mods = ToGcMods(mods);
                b.Key = KeyInterop.VirtualKeyFromKey(e.Key);
            }
            EndCapture();
            RefreshList(); ShowDetail(); RefreshValidation();
            return;
        }
        // 非录键态：Esc = 取消关闭（走 Closing 淡出退场），与「取消」钮及退场注释一致（R154）
        if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }
        base.OnPreviewKeyDown(e);
    }

    static GcMods ToGcMods(System.Windows.Input.ModifierKeys m)
    {
        GcMods r = GcMods.None;
        if (m.HasFlag(System.Windows.Input.ModifierKeys.Control)) r |= GcMods.Control;
        if (m.HasFlag(System.Windows.Input.ModifierKeys.Alt)) r |= GcMods.Alt;
        if (m.HasFlag(System.Windows.Input.ModifierKeys.Shift)) r |= GcMods.Shift;
        if (m.HasFlag(System.Windows.Input.ModifierKeys.Windows)) r |= GcMods.Win;
        return r;
    }

    // ---------- 校验 + 保存 ----------
    void RefreshValidation()
    {
        var problems = GestureValidation.Validate(_doc, _catalog, _namer);
        bool errs = GestureValidation.HasErrors(problems);
        _saveBtn.IsEnabled = !errs;
        // R161：左下角不再显示「✓ 配置有效」——有问题的那条绑定整卡红框标出，无问题即静默。
        _problemIds.Clear();
        foreach (var p in problems) _problemIds.Add(p.BindingId);
        PaintRows();
        if (problems.Count == 0)
        {
            _valText.Text = _doc.Enabled ? "" : "手势层当前关闭";
            _valText.Foreground = _pal.B(_pal.TextDim);
        }
        else
        {
            _valText.Text = string.Join("；", problems.Select(p => p.Message).Distinct().Take(3));
            _valText.Foreground = _pal.B(errs ? Danger : _pal.TextDim);
        }
    }

    void Save()
    {
        var problems = GestureValidation.Validate(_doc, _catalog, _namer);
        if (GestureValidation.HasErrors(problems)) return;
        _store.Save(_doc);
        Close();
    }
}
