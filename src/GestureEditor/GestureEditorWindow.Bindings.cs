using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using GestureCore;
using GcMods = GestureCore.ModifierKeys;

namespace GestureEditor;

// 本 partial 承载绑定编辑交互：绑定增删改、详情编辑、组合键录键、校验与保存、列表刷新。
/// <summary>
/// <see cref="GestureEditorWindow"/> 的绑定编辑分部：左侧卡片列表的构建与三态刷新、右侧详情表单的逐条重建、
/// 组合键录键状态机（<see cref="ChordCaptureBegan"/>/<see cref="ChordCaptureEnded"/> 通知宿主暂停/恢复钩子）、
/// 以及校验驱动的保存按钮可用性。
/// </summary>
public sealed partial class GestureEditorWindow : Window
{
    #region 列表（卡片化：边框+底色，选中/问题/禁用逐行自绘）
    /// <summary>整表重建：清空后按 <c>_doc.Bindings</c> 顺序逐条建卡片，再统一刷三态。
    /// 重建期间置 <c>_suppress</c>，避免 Clear/Add 把选中位冲掉。</summary>
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
        var head = new WrapPanel();
        TextElement.SetFontWeight(head, FontWeights.SemiBold);
        TextElement.SetForeground(head, _pal.B(_pal.Text));
        var body = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        head.Children.Add(Keycaps.Row(b.Chord.Format(_namer), new KeycapTheme
        {
            Ghost = true,
            Text = _pal.Text,
            Stroke = _pal.Stroke,
            Dim = _pal.TextDim,
            Size = 12,
        }, T("未设键")));
        head.Children.Add(new TextBlock
        {
            Text = "· " + T(TriggerLabel(b.Trigger)),
            FontSize = 12.5,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var acts = b.Actions.Count == 0 ? T("无动作") : string.Join(" → ", b.Actions.Select(ActionLabel));
        if (b.OnComplete.Count > 0) acts += T("  ⟶ 完成后: ") + string.Join(" → ", b.OnComplete.Select(ActionLabel));
        body.Text = acts;
        var sp = new StackPanel(); sp.Children.Add(head); sp.Children.Add(body);
        var card = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(1, 3, 3, 3), Child = sp };
        return new BindingRow { Card = card, Head = head, Body = body, Enabled = b.Enabled };
    }

    /// <summary>逐行刷三态：选中（accent 底 + OnAccent 字色）、问题（红框）、禁用（muted 灰字，无文字前缀）。
    /// 三态切换一律走 140ms 颜色过渡；底色/描边若是 <c>EditorPalette.B</c> 造的冻结画刷，AnimBrush 会直接换成目标色新画刷（这一次不过渡）。</summary>
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
            AnimBrush(TextElement.GetForeground(r.Head) as SolidColorBrush, sel ? _pal.OnAccent() : (r.Enabled ? _pal.Text : _pal.TextDim), br => TextElement.SetForeground(r.Head, br));
            AnimBrush(r.Body.Foreground as SolidColorBrush, sel ? _pal.OnAccent() : _pal.TextDim, br => r.Body.Foreground = br);
            r.Body.Opacity = r.Enabled || sel ? 1.0 : 0.6;
        }
    }

    string ActionLabel(string actionName) => _catalog.Actions().FirstOrDefault(a => a.Name == actionName)?.Label ?? actionName;

    // 截图后落点的中文标签（取值与宿主 AfterCapture 一致；空=跟随默认弹提示）。
    // 只描述"截完之后打开什么"，写不写剪贴板是另一条独立开关。
    static string AfterCaptureLabel(string v) => v switch
    {
        "" => "跟随默认", // lockeys
        "editor" => "进编辑器", // lockeys
        "copy" => "什么都不做", // lockeys
        "pin" => "贴图", // lockeys
        _ => "弹提示", // lockeys
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
    #endregion

    #region 详情
    /// <summary>重建右侧详情表单：整段清空后按选中绑定重新搭一遍，控件在闭包里直接捕获这条绑定。
    /// 不做增量同步——切换选中或改键后一律重建：每个控件的闭包与它编辑的那条绑定同源，不会错位。</summary>
    /// <summary>重建详情区，并决定这次重建走不走入场：在已开窗内切换绑定是一次状态切换，
    /// 新内容逐项错位进场；构造期那一次交给窗口入场的整窗淡入，首帧就是完整内容。</summary>
    void ShowDetail()
    {
        FillDetail();
        if (IsLoaded) Cascade.In(_detail, risePx: 6);
        else Cascade.Reset(_detail);
    }

    void FillDetail()
    {
        _detail.Children.Clear();
        if (_selected < 0 || _selected >= _doc.Bindings.Count)
        {
            _detail.Children.Add(new TextBlock { Text = T("选择左侧一条绑定进行编辑，或点「添加绑定」。"), Foreground = _pal.B(_pal.TextDim), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
            return;
        }
        var b = _doc.Bindings[_selected];

        // 启用/禁用此绑定（A1）：禁用=保留配置但不触发，无需删除
        _detail.Children.Add(MakeToggle(T("启用此绑定"), v => { b.Enabled = v; RefreshList(); RefreshValidation(); }, out _, b.Enabled));

        // 组合键捕获
        _detail.Children.Add(Label(T("组合键")));
        _chordBox = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Background = _pal.B(_pal.CardBg), Padding = new Thickness(12, 9, 12, 9), Cursor = Cursors.Hand, Focusable = true };
        _chordStroke = new SolidColorBrush(_pal.Stroke); _chordBox.BorderBrush = _chordStroke;
        _chordHover = _chordFocus = false;
        _chordText = new TextBlock { Foreground = _pal.B(_pal.Text), FontSize = 14 };
        _chordHost = new Grid();
        _chordBox.Child = _chordHost;
        UpdateChordText();
        _chordBox.MouseLeftButtonDown += (_, _) => BeginCapture();
        _chordBox.KeyDown += (_, e) => { if (!_capturing && e.Key is Key.Space or Key.Enter) { BeginCapture(); e.Handled = true; } };
        // hover 与键盘焦点共用一套描边刷新：聚焦或悬停任一成立即转 accent，140ms 颜色过渡
        void RefreshChordBorder() { if (!_capturing) AnimateColor(_chordStroke, (_chordHover || _chordFocus) ? _pal.Accent : _pal.Stroke, Motion.HoverMs); }
        _chordBox.MouseEnter += (_, _) => { _chordHover = true; RefreshChordBorder(); };
        _chordBox.MouseLeave += (_, _) => { _chordHover = false; RefreshChordBorder(); };
        _chordBox.GotKeyboardFocus += (_, _) => { _chordFocus = true; RefreshChordBorder(); };
        _chordBox.LostKeyboardFocus += (_, _) => { _chordFocus = false; RefreshChordBorder(); };
        _detail.Children.Add(_chordBox);

        // 触发方式
        _detail.Children.Add(Label(T("触发方式")));
        var trig = MakeCombo(Enum.GetValues<TriggerKind>().Cast<object>(), x => T(TriggerLabel(x)), b.Trigger, v => { b.Trigger = (TriggerKind)v; RefreshList(); RefreshValidation(); });
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
                    // 已配置动作行卡片化——与旁边动作下拉等高（MinHeight 34）、序号做成独立徽标、
                    // 加边框+底色：裸文本行会让序号和内容糊在一起，也看不出这是一行可操作项。
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
                UpdateAfterSec();   // 序列增删/换序后重判「截图后」是否还该显示
            }
            Rebuild();
            root.Children.Add(panel);
            var addRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var actCombo = new ComboBox { Width = 200 };
            EditorStyles.ApplyCombo(actCombo, _pal);
            foreach (var a in _catalog.Actions()) actCombo.Items.Add(new ComboBoxItem { Content = a.Label, Tag = a.Name, ToolTip = a.Description });
            if (actCombo.Items.Count > 0) actCombo.SelectedIndex = 0;
            var addAct = MakeButton(addLabel, ghost: true);
            addAct.Margin = new Thickness(8, 0, 0, 0);   // 左边距 8px：贴住下拉框会被读成同一个控件
            addAct.Click += (_, _) => { if (actCombo.SelectedItem is ComboBoxItem it && it.Tag is string n) { list.Add(n); Rebuild(); RefreshList(); RefreshValidation(); } };
            addRow.Children.Add(actCombo); addRow.Children.Add(addAct);
            root.Children.Add(addRow);
            return root;
        }
        _detail.Children.Add(BuildActionList(b.Actions, T("动作序列"), T("添加动作")));
        _detail.Children.Add(BuildActionList(b.OnComplete, T("完成后序列"), T("添加完成动作")));

        // 截图后：本条手势触发截图完成后的落点；空=跟随默认（弹提示）。
        // 序列里没有截图类动作时整段隐藏（录制/编辑器/OCR 之外的手势配它没意义，徒增困惑）。
        // 写不写剪贴板单独一条：贴图、进编辑器都不必覆盖用户剪贴板里原有的东西。
        _afterSec = new StackPanel();
        _afterSec.Children.Add(Label(T("截图后")));
        var afterOpts = new object[] { "", "toast", "editor", "copy", "pin" };
        _afterSec.Children.Add(MakeCombo(afterOpts, v => T(AfterCaptureLabel((string)v)), b.AfterCapture, v => { b.AfterCapture = (string)v; RefreshList(); }));
        _afterSec.Children.Add(Label(T("复制到剪贴板")));
        var copyOpts = new object[] { true, false };
        _afterSec.Children.Add(MakeCombo(copyOpts, v => (bool)v ? T("复制") : T("不复制"), b.CopyToClipboard, v => b.CopyToClipboard = (bool)v));
        _detail.Children.Add(_afterSec);
        UpdateAfterSec();

        // 失败策略 + 优先级
        var two = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        two.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var fbCol = new StackPanel(); fbCol.Children.Add(Label(T("失败策略")));
        fbCol.Children.Add(MakeCombo(Enum.GetValues<FallbackPolicy>().Cast<object>(), x => T(FallbackLabel(x)), b.Fallback, v => b.Fallback = (FallbackPolicy)v));
        var prCol = new StackPanel(); prCol.Children.Add(Label(T("优先级")));
        prCol.Children.Add(MakeNum(b.Priority.ToString(), s => { if (int.TryParse(s, out var p)) b.Priority = p; }));
        Grid.SetColumn(fbCol, 0); Grid.SetColumn(prCol, 2);
        two.Children.Add(fbCol); two.Children.Add(prCol);
        _detail.Children.Add(two);

        // 时间参数
        var two2 = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        two2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        two2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        two2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var dcCol = new StackPanel(); dcCol.Children.Add(Label(T("双击间隔 ms")));
        dcCol.Children.Add(MakeNum(b.DoubleClickMs.ToString(), s => { if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) b.DoubleClickMs = v; RefreshValidation(); }));
        var lpCol = new StackPanel(); lpCol.Children.Add(Label(T("长按阈值 ms")));
        lpCol.Children.Add(MakeNum(b.LongPressMs.ToString(), s => { if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) b.LongPressMs = v; RefreshValidation(); }));
        Grid.SetColumn(dcCol, 0); Grid.SetColumn(lpCol, 2);
        two2.Children.Add(dcCol); two2.Children.Add(lpCol);
        _detail.Children.Add(two2);
    }

    /// <summary>序列（动作+完成后）是否含截图类动作——由宿主目录的 IsCapture 标记决定，库不硬编码动作名。</summary>
    bool HasCaptureAction(BindingEdit b)
    {
        var caps = new HashSet<string>(_catalog.Actions().Where(a => a.IsCapture).Select(a => a.Name));
        return b.Actions.Any(caps.Contains) || b.OnComplete.Any(caps.Contains);
    }

    /// <summary>「截图后」小节只在选中绑定的序列含截图类动作时出现；隐藏不删数据，配过的值仍留在绑定里。</summary>
    void UpdateAfterSec()
    {
        if (_afterSec == null) return;   // ShowDetail 首建期间（动作列表先于本段构建）
        bool show = _selected >= 0 && _selected < _doc.Bindings.Count && HasCaptureAction(_doc.Bindings[_selected]);
        _afterSec.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>回显当前组合键：已设键渲染成键帽排，捕获中显示等待提示并转 accent，未设键显示占位文案，
    /// 描边随捕获/悬停/聚焦转 accent。</summary>
    void UpdateChordText()
    {
        if (_selected < 0 || _selected >= _doc.Bindings.Count) return;
        var b = _doc.Bindings[_selected];
        var s = b.Chord.Format(_namer);
        _chordHost.Children.Clear();
        if (_capturing)
        {
            _chordText.Text = T("按下新键 · Esc 解绑");
            _chordText.Foreground = _pal.B(_pal.Accent);
            _chordHost.Children.Add(_chordText);
        }
        else if (string.IsNullOrEmpty(s))
        {
            _chordText.Text = T("点击设置");
            _chordText.Foreground = _pal.B(_pal.TextDim);
            _chordHost.Children.Add(_chordText);
        }
        else
        {
            _chordHost.Children.Add(Keycaps.Row(s, new KeycapTheme
            {
                Face = Keycaps.Lift(_pal.CardBg, _pal.Hover),
                Text = _pal.Text,
                Stroke = _pal.Stroke,
                Dim = _pal.TextDim,
                Size = 13,
            }));
        }
        if (_chordStroke != null)
            AnimateColor(_chordStroke, _capturing || _chordHover || _chordFocus ? _pal.Accent : _pal.Stroke, Motion.HoverMs);
    }
    #endregion

    #region 组合键捕获
    /// <summary>进入录键态：先收掉上一次捕获，再通知宿主暂停全局钩子（否则组合键被宿主吞掉、编辑器收不到），
    /// 并把焦点抢回录键框。捕获到的键只写进当前选中的那条绑定，编辑器不留任何按键记录。</summary>
    void BeginCapture()
    {
        EndCapture();
        _capturing = true;
        ChordCaptureBegan?.Invoke();   // 宿主暂停全局钩子，否则组合键被吞、编辑器收不到
        UpdateChordText();
        _chordBox.Focus();
    }

    /// <summary>退出录键态并通知宿主恢复全局钩子。非捕获态直接返回，重复调用无副作用。</summary>
    void EndCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        ChordCaptureEnded?.Invoke();   // 宿主恢复全局钩子
        UpdateChordText();
    }

    /// <summary>录键态下整段吞掉按键，按「修饰键 + 一个主键」判定是否录成：
    /// Esc 解绑这条绑定；纯修饰键不算录成，继续等主键；F1–F24 允许裸按且强制清空修饰键；
    /// 其余键必须带至少一个修饰键，否则等于把普通输入劫持成热键。录成即写入选中绑定并退出捕获。
    /// 非录键态只拦 Esc：取消关闭，走 Closing 的淡出退场，与「取消」钮同一路径。</summary>
    /// <param name="e">预览按键事件；录键态下一律置 Handled，不让按键冒泡到其它控件。</param>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_capturing)
        {
            e.Handled = true;
            if (e.Key == Key.Escape)
            {
                // Esc 解绑：清空这条绑定的主键与修饰键，保存后这条动作不占任何键
                if (_selected >= 0 && _selected < _doc.Bindings.Count)
                {
                    var un = _doc.Bindings[_selected];
                    un.Mods = GcMods.None;
                    un.Key = 0;
                }
                EndCapture();
                RefreshList(); ShowDetail(); RefreshValidation();
                return;
            }
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
        // 非录键态：Esc = 取消关闭，走 Closing 的淡出退场，与「取消」钮同一路径
        if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }
        base.OnPreviewKeyDown(e);
    }

    /// <summary>WPF 修饰键 → GestureCore 自持枚举的逐位映射。两边数值恰好相同，但仍逐位写而不强转：
    /// 强转会把「数值一致」变成隐式契约，任一侧改值就静默错位。</summary>
    static GcMods ToGcMods(System.Windows.Input.ModifierKeys m)
    {
        GcMods r = GcMods.None;
        if (m.HasFlag(System.Windows.Input.ModifierKeys.Control)) r |= GcMods.Control;
        if (m.HasFlag(System.Windows.Input.ModifierKeys.Alt)) r |= GcMods.Alt;
        if (m.HasFlag(System.Windows.Input.ModifierKeys.Shift)) r |= GcMods.Shift;
        if (m.HasFlag(System.Windows.Input.ModifierKeys.Windows)) r |= GcMods.Win;
        return r;
    }
    #endregion

    #region 校验 + 保存
    /// <summary>重跑整份配置的校验：错误级问题禁用保存按钮；全部问题（含提示级）按绑定 Id 落到对应卡片红框。</summary>
    void RefreshValidation()
    {
        var problems = GestureValidation.Validate(_doc, _catalog, _namer, T);
        bool errs = GestureValidation.HasErrors(problems);
        _saveBtn.IsEnabled = !errs;
        // 有问题的那条绑定整卡红框标出，无问题即静默：底部只留最多三条去重后的文案，不做「配置有效」回执。
        // 全通过时唯一可能出现的文案是手势层关闭提示，免得整窗看着像没渲染。
        _problemIds.Clear();
        foreach (var p in problems) _problemIds.Add(p.BindingId);
        PaintRows();
        if (problems.Count == 0)
        {
            _valText.Text = _doc.Enabled ? "" : T("手势层当前关闭");
            _valText.Foreground = _pal.B(_pal.TextDim);
        }
        else
        {
            _valText.Text = string.Join(T("；"), problems.Select(p => p.Message).Distinct().Take(3));
            _valText.Foreground = _pal.B(errs ? Danger : _pal.TextDim);
        }
    }

    /// <summary>保存并关窗。落盘前再校验一次：校验是保存的唯一闸门，有错误级问题就什么都不写。</summary>
    void Save()
    {
        var problems = GestureValidation.Validate(_doc, _catalog, _namer, T);
        if (GestureValidation.HasErrors(problems)) return;
        _store.Save(_doc);
        Close();
    }
    #endregion

}
