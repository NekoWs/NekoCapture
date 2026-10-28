using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FrameCapture.Core;
using FrameCapture.Services;

namespace FrameCapture.Overlays;

/// <summary>共享行为入口、动作卡片与逐层事件出口的编辑。</summary>
public sealed partial class SettingsWindow
{
    StackPanel? _behaviorPanel, _behaviorDetails;
    string _behaviorFlow = "";
    readonly List<string> _behaviorPath = new();
    readonly List<ChainEditor> _behaviorEditors = new();
    ChainEditor? _behaviorRootEditor;

    void BuildBehaviors(StackPanel panel)
    {
        _behaviorPanel = panel;
        RenderBehaviors();
    }

    void RenderBehaviors()
    {
        if (_behaviorPanel == null) return;
        var cfg = _settings.Current.Gestures;
        var graph = cfg.Graph;
        var flow = graph.Flow(_behaviorFlow) ?? graph.Flows.FirstOrDefault();
        _behaviorFlow = flow?.Id ?? "";
        _behaviorPanel.Children.Clear();
        _behaviorEditors.Clear();
        var tabs = new WrapPanel { Margin = new Thickness(0, 4, 0, 12) };
        foreach (var entry in graph.Flows)
            tabs.Children.Add(BehaviorButton(cfg.FlowName(entry), () =>
            {
                _behaviorFlow = entry.Id; _behaviorPath.Clear(); RenderBehaviors();
            }, entry.Id == _behaviorFlow));
        tabs.Children.Add(BehaviorButton(Loc.T(K.Behaviors.Add), () =>
        {
            var entry = new BehaviorFlow();
            graph.Flows.Add(entry); _behaviorFlow = entry.Id; _behaviorPath.Clear();
            CommitBehaviorGraph(); RenderBehaviors();
        }));
        _behaviorPanel.Children.Add(tabs);
        if (flow == null) return;
        if (!graph.IsValid(flow.Id))
            _behaviorPanel.Children.Add(new TextBlock { Text = Loc.T(K.Behaviors.Invalid),
                Foreground = new SolidColorBrush(Theme.Danger), TextWrapping = TextWrapping.Wrap,
                FontSize = 11.5, Margin = new Thickness(2, 0, 2, 10) });
        var body = new StackPanel();
        var header = new DockPanel { Margin = new Thickness(2, 0, 2, 10), LastChildFill = true };
        var right = new WrapPanel { MaxWidth = 230, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(BehaviorIcon("\uE74D", K.Behaviors.Delete, () =>
        {
            graph.Flows.Remove(flow);
            _behaviorPath.Clear(); _behaviorFlow = "";
            CommitBehaviorGraph(); RebuildHotkeys(); RenderBehaviors();
        }));
        foreach (var binding in cfg.Bindings.Where(b => b.EntryId == flow.Id && b.Enabled))
        {
            var caps = Keycaps.Row(binding.Chord, new KeycapTheme { Face = Theme.RowBg, Text = Theme.Text,
                Stroke = Theme.StrokeSoft, Dim = Theme.TextDim, Size = 11 });
            var hit = new Border { Child = caps, MinHeight = 28, MinWidth = 28,
                Padding = new Thickness(3), Margin = new Thickness(5, 0, 0, 0), Cursor = Cursors.Hand };
            hit.MouseLeftButtonUp += (_, _) => GotoPage("hotkeys");
            right.Children.Add(hit);
        }
        DockPanel.SetDock(right, Dock.Right); header.Children.Add(right);
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        var name = new TextBlock { Text = cfg.FlowName(flow), Foreground = Theme.TextBrush, FontSize = 13,
            FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 180, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = cfg.FlowName(flow) };
        title.Children.Add(name);
        var nameHost = new ContentControl { Content = title };
        var rename = RenameIconButton(() => nameHost.Content = RenameBox(cfg.FlowName(flow), text =>
        {
            flow.Name = text.Trim(); CommitBehaviorGraph(); RenderBehaviors();
        }, RenderBehaviors));
        rename.Width = 28; rename.Height = 28;
        title.Children.Add(rename);
        header.Children.Add(nameHost); body.Children.Add(header);
        var rootEditor = BehaviorChain(() => flow.Steps, list => flow.Steps = list, null);
        _behaviorRootEditor = rootEditor;
        body.Children.Add(LabeledRow(Loc.T(K.Hotkeys.ChainTitle), rootEditor.Root, Loc.T(K.Behaviors.Description)));
        _behaviorDetails = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        body.Children.Add(_behaviorDetails);
        _behaviorPanel.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = Theme.CardBgBrush,
            BorderBrush = Theme.StrokeSoftBrush, BorderThickness = new Thickness(1), Padding = new Thickness(12), Child = body });
        _behaviorPanel.Children.Add(new TextBlock { Text = Loc.T(K.Behaviors.Description), Foreground = Theme.TextDimBrush,
            FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 10, 2, 0) });
        if (_behaviorPath.Count == 0 && flow.Steps.FirstOrDefault() is { } first) _behaviorPath.Add(first);
        rootEditor.Render();
        RenderBehaviorDetails();
    }

    ChainEditor BehaviorChain(Func<List<string>> get, Action<List<string>> set, BehaviorNode? owner)
    {
        var graph = _settings.Current.Gestures.Graph;
        var editor = new ChainEditor(() => get().ToArray(), _ => ActionCatalog(), values =>
        {
            set(values.Select(id => graph.Node(id)?.Id ?? AddBehaviorNode(id).Id).ToList());
            CommitBehaviorGraph();
            if (_behaviorPath.Any(id => graph.Node(id) == null))
            {
                _behaviorPath.RemoveAll(id => graph.Node(id) == null);
                RenderBehaviorDetails();
            }
        }, id => GestureActions.LabelOf(graph.Node(id)?.Action), index =>
        {
            var list = get(); if (index < 0 || index >= list.Count) return;
            if (owner == null) _behaviorPath.Clear();
            else
            {
                int at = _behaviorPath.IndexOf(owner.Id);
                if (at >= 0) _behaviorPath.RemoveRange(at + 1, _behaviorPath.Count - at - 1);
            }
            _behaviorPath.Add(list[index]); RenderBehaviorDetails();
            foreach (var view in _behaviorEditors.ToArray()) view.Render();
        }, id => _behaviorPath.Contains(id));
        _behaviorEditors.Add(editor);
        editor.Root.ToolTip = get().Count == 0 ? Loc.T(K.Hotkeys.DefaultNothing) : Loc.T(K.Behaviors.Description);
        return editor;
    }

    BehaviorNode AddBehaviorNode(string action)
    {
        var graph = _settings.Current.Gestures.Graph;
        var node = graph.Add(action);
        if (action == GestureActions.Select)
        {
            node.Branches[BehaviorPorts.Still].Add(graph.Add(GestureActions.GrabStill).Id);
            node.Branches[BehaviorPorts.Record].Add(graph.Add(GestureActions.RecordStart).Id);
        }
        return node;
    }

    void RenderBehaviorDetails()
    {
        if (_behaviorDetails == null) return;
        _behaviorDetails.Children.Clear();
        _behaviorEditors.RemoveAll(e => !ReferenceEquals(e, _behaviorRootEditor));
        var cfg = _settings.Current.Gestures;
        var graph = cfg.Graph;
        _behaviorPath.RemoveAll(id => graph.Node(id) == null);
        if (_behaviorPath.Count == 0 || graph.Node(_behaviorPath[^1]) is not { } node) return;
        _behaviorDetails.Children.Add(new Border { Height = 1, Background = Theme.StrokeSoftBrush,
            Margin = new Thickness(2, 0, 2, 8) });
        var path = new WrapPanel { Margin = new Thickness(0, 0, 0, 7) };
        if (graph.Flow(_behaviorFlow) is { } flow)
            path.Children.Add(BehaviorButton(cfg.FlowName(flow), () => { _behaviorPath.Clear(); RenderBehaviorDetails(); }));
        for (int i = 0; i < _behaviorPath.Count; i++)
        {
            int at = i;
            path.Children.Add(new TextBlock { Text = "\uE76C", FontFamily = new FontFamily("Segoe Fluent Icons"),
                Foreground = Theme.TextDimBrush, FontSize = 9, Margin = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center });
            path.Children.Add(BehaviorButton(GestureActions.LabelOf(graph.Node(_behaviorPath[i])!.Action), () =>
            { _behaviorPath.RemoveRange(at + 1, _behaviorPath.Count - at - 1); RenderBehaviorDetails(); }, i + 1 == _behaviorPath.Count));
        }
        _behaviorDetails.Children.Add(path);
        if (node.Action == GestureActions.Select)
        {
            _behaviorDetails.Children.Add(ToggleRow("\uE7A8", Loc.T(K.Behaviors.ShowSwitch),
                Loc.T(K.Actions.ScopeSelectDesc), node.ShowModeSwitch, value =>
                { node.ShowModeSwitch = value; CommitBehaviorGraph(); }));
            _behaviorDetails.Children.Add(BehaviorModeRow(node));
        }
        if (node.Action == GestureActions.CallBehavior)
            _behaviorDetails.Children.Add(BehaviorEntryRow(node.TargetId, id => { node.TargetId = id; CommitBehaviorGraph(); }));
        var ports = GestureActions.PortsOf(node.Action);
        string selected = _behaviorPorts.TryGetValue(node.Id, out string? value) && ports.Contains(value) ? value : ports[0];
        var tabs = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
        foreach (string port in ports)
            tabs.Children.Add(BehaviorButton(GestureActions.PortLabel(node.Action, port), () =>
            { _behaviorPorts[node.Id] = port; RenderBehaviorDetails(); }, selected == port));
        _behaviorDetails.Children.Add(tabs);
        if (!node.Branches.ContainsKey(selected)) node.Branches[selected] = new();
        var chain = BehaviorChain(() => node.Branches[selected], list => node.Branches[selected] = list, node);
        _behaviorDetails.Children.Add(chain.Root);
        if (selected is BehaviorPorts.Cancel or BehaviorPorts.Failed)
            _behaviorDetails.Children.Add(ToggleRow("\uE76C", Loc.T(K.Behaviors.Continue),
                Loc.T(K.Hotkeys.RunsInOrderDragToReorder), selected == BehaviorPorts.Failed ? node.ContinueOnFailure : node.ContinueOnCancel,
                enabled => { if (selected == BehaviorPorts.Failed) node.ContinueOnFailure = enabled; else node.ContinueOnCancel = enabled;
                    CommitBehaviorGraph(); }));
    }

    readonly Dictionary<string, string> _behaviorPorts = new();

    void CommitBehaviorGraph()
    {
        var cfg = _settings.Current.Gestures;
        cfg.Graph.Collect();
        _settings.Mutate(s => s.Gestures = cfg);
        AppHost.Current.InstallHotkeys();
        if (!cfg.Graph.IsValid()) NoticeToast.Show(Loc.T(K.Behaviors.Invalid));
    }

    static Button BehaviorButton(string text, Action click, bool selected = false)
    {
        var button = new Button { Content = text, FontSize = 12, Foreground = Theme.TextBrush,
            Background = selected ? Theme.RowBgBrush : Brushes.Transparent, BorderBrush = selected ? Theme.AccentBrush : Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, selected ? 2 : 0), Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(2, 1, 2, 1), Cursor = Cursors.Hand };
        button.Click += (_, _) => click();
        return button;
    }

    static Button BehaviorIcon(string glyph, Str tip, Action click)
    {
        var button = new Button { Content = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"),
            ToolTip = Loc.T(tip), Foreground = Theme.TextDimBrush, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Padding = new Thickness(6), Margin = new Thickness(3, 0, 3, 0) };
        button.Click += (_, _) => click();
        return button;
    }

    FrameworkElement BehaviorEntryRow(string selected, Action<string> save)
    {
        var cfg = _settings.Current.Gestures;
        var combo = new ComboBox { MinWidth = 145, MaxWidth = 260, HorizontalAlignment = HorizontalAlignment.Left };
        Fluent.StyleCombo(combo);
        combo.Items.Add(new ComboBoxItem { Content = Loc.T(K.Behaviors.Empty), Tag = "" });
        foreach (var flow in cfg.Graph.Flows)
            combo.Items.Add(new ComboBoxItem { Content = cfg.FlowName(flow), Tag = flow.Id });
        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == selected) ?? combo.Items[0];
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is ComboBoxItem { Tag: string id }) save(id); };
        return LabeledRow(Loc.T(K.Behaviors.Entry), combo, Loc.T(K.Behaviors.Description));
    }

    FrameworkElement BehaviorModeRow(BehaviorNode node)
    {
        var combo = ModeCombo(node.RecordMode ? BehaviorPorts.Record : BehaviorPorts.Still, false, mode =>
        { node.RecordMode = mode == BehaviorPorts.Record; CommitBehaviorGraph(); });
        return LabeledRow(Loc.T(K.Behaviors.InitialMode), combo, Loc.T(K.Actions.ScopeSelectDesc));
    }

    static ComboBox ModeCombo(string selected, bool includeDefault, Action<string> save)
    {
        var combo = new ComboBox { Width = 150, HorizontalAlignment = HorizontalAlignment.Left };
        Fluent.StyleCombo(combo);
        if (includeDefault) combo.Items.Add(new ComboBoxItem { Content = Loc.T(K.Behaviors.NodeDefault), Tag = "" });
        combo.Items.Add(new ComboBoxItem { Content = Loc.T(K.Common.Capture), Tag = BehaviorPorts.Still });
        combo.Items.Add(new ComboBoxItem { Content = Loc.T(K.Common.Recording), Tag = BehaviorPorts.Record });
        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == selected) ?? combo.Items[0];
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is ComboBoxItem { Tag: string mode }) save(mode); };
        return combo;
    }
}
