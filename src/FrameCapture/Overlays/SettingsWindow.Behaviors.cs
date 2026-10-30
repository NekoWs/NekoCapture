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
    StackPanel? _behaviorHeader, _behaviorContent, _behaviorBranchContent;
    string _behaviorFlow = "";
    readonly List<string> _behaviorPath = new();
    readonly List<ChainEditor> _behaviorEditors = new();
    ChainEditor? _behaviorRootEditor;
    TabControl? _behaviorFlowTabs, _behaviorBranchTabs;
    (string Id, string Name)[] _behaviorTabEntries = Array.Empty<(string, string)>();
    string[] _behaviorRenderedPath = Array.Empty<string>();
    bool _behaviorTabSync;
    int _behaviorSlideDirection = 1;
    string _behaviorBranchNode = "", _behaviorBranchPort = "";

    void BuildBehaviors(StackPanel panel)
    {
        _behaviorPanel = panel;
        _behaviorHeader = new StackPanel();
        _behaviorContent = new StackPanel();
        panel.Children.Add(_behaviorHeader); panel.Children.Add(_behaviorContent);
        _behaviorFlowTabs = null;
        RenderBehaviors();
    }

    void RenderBehaviors()
    {
        if (_behaviorPanel == null || _behaviorHeader == null || _behaviorContent == null) return;
        var cfg = _settings.Current.Gestures;
        var graph = cfg.Graph;
        var flow = graph.Flow(_behaviorFlow) ?? graph.Flows.FirstOrDefault();
        _behaviorFlow = flow?.Id ?? "";
        var entries = graph.Flows.Select(entry => (entry.Id, cfg.FlowName(entry))).ToArray();
        if (_behaviorFlowTabs == null || !_behaviorTabEntries.SequenceEqual(entries))
        {
            _behaviorTabEntries = entries;
            if (_cat == 1 && !_refreshingTheme) TransitionContent(_behaviorHeader, RenderBehaviorHeader);
            else RenderBehaviorHeader();
        }
        UpdateBehaviorTab(_behaviorFlowTabs!, graph.Flows.IndexOf(flow!));
        if (_cat == 1 && !_refreshingTheme) TransitionContent(_behaviorContent, () => RenderBehaviorBody(flow), _behaviorSlideDirection);
        else RenderBehaviorBody(flow);
    }

    void RenderBehaviorHeader()
    {
        if (_behaviorHeader == null) return;
        var cfg = _settings.Current.Gestures;
        var graph = cfg.Graph;
        _behaviorHeader.Children.Clear();
        var tabs = new DockPanel { Margin = new Thickness(0, 4, 0, 12), LastChildFill = true };
        var add = BehaviorButton(Loc.T(K.Behaviors.Add), () =>
        {
            var entry = new BehaviorFlow();
            _settings.Current.Gestures.Graph.Flows.Add(entry); _behaviorFlow = entry.Id; _behaviorPath.Clear();
            CommitBehaviorGraph(); RenderBehaviors();
        }, backing: Theme.WindowBgSolid);
        add.BorderBrush = Theme.StrokeSoftBrush;
        add.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(add, Dock.Right); tabs.Children.Add(add);
        _behaviorFlowTabs = BehaviorTabs(graph.Flows.Select(entry => (cfg.FlowName(entry), (Action)(() =>
            {
                if (_behaviorFlow == entry.Id) return;
                _behaviorFlow = entry.Id; _behaviorPath.Clear(); RenderBehaviors();
            }))), graph.Flows.FindIndex(entry => entry.Id == _behaviorFlow), Theme.WindowBgSolid);
        tabs.Children.Add(_behaviorFlowTabs);
        _behaviorHeader.Children.Add(tabs);
    }

    void RenderBehaviorBody(BehaviorFlow? flow)
    {
        if (_behaviorContent == null) return;
        _behaviorContent.Children.Clear(); _behaviorEditors.Clear();
        _behaviorRenderedPath = Array.Empty<string>();
        _behaviorBreadcrumbs = null; _behaviorCrumbs.Clear();
        _behaviorBranchNode = _behaviorBranchPort = "";
        var cfg = _settings.Current.Gestures;
        var graph = cfg.Graph;
        if (flow == null) { _behaviorDetails = null; return; }
        if (!graph.IsValid(flow.Id))
            _behaviorContent.Children.Add(new TextBlock { Text = Loc.T(K.Behaviors.Invalid),
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
        _behaviorContent.Children.Add(new Border { CornerRadius = new CornerRadius(10), Background = Theme.CardBgBrush,
            BorderBrush = Theme.StrokeSoftBrush, BorderThickness = new Thickness(1), Padding = new Thickness(12), Child = body });
        _behaviorContent.Children.Add(new TextBlock { Text = Loc.T(K.Behaviors.Description), Foreground = Theme.TextDimBrush,
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
            int depth = owner == null ? 0 : _behaviorPath.IndexOf(owner.Id) + 1;
            if (_behaviorPath.Count == depth + 1 && _behaviorPath[^1] == list[index]) return;
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
        var cfg = _settings.Current.Gestures;
        var graph = cfg.Graph;
        _behaviorPath.RemoveAll(id => graph.Node(id) == null);
        if (_behaviorPath.Count == 0 && graph.Flow(_behaviorFlow)?.Steps.FirstOrDefault() is { } first)
            _behaviorPath.Add(first);
        if (_behaviorPath.Count == 0 || graph.Node(_behaviorPath[^1]) is not { } node)
        {
            if (_behaviorDetails.Children.Count == 0) return;
            TransitionContent(_behaviorDetails, () => _behaviorDetails.Children.Clear());
            _behaviorBreadcrumbs = null; _behaviorCrumbs.Clear();
            _behaviorBranchNode = _behaviorBranchPort = "";
            _behaviorRenderedPath = Array.Empty<string>(); return;
        }
        if (_behaviorRenderedPath.SequenceEqual(_behaviorPath) && _behaviorBranchContent != null)
        {
            RenderBehaviorBranch(node); return;
        }
        int direction = _behaviorPath.Count < _behaviorRenderedPath.Length ? -1 : 1;
        _behaviorRenderedPath = _behaviorPath.ToArray();
        TransitionContent(_behaviorDetails, () =>
        {
            RenderBehaviorBreadcrumbs(direction);
            RenderBehaviorDetailsBody(node);
        }, direction, stableChildren: 2);
    }

    void RenderBehaviorDetailsBody(BehaviorNode node)
    {
        if (_behaviorDetails == null) return;
        if (_behaviorDetails.Children.Count > 2)
            _behaviorDetails.Children.RemoveRange(2, _behaviorDetails.Children.Count - 2);
        _behaviorEditors.RemoveAll(e => !ReferenceEquals(e, _behaviorRootEditor));
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
        var tabs = BehaviorTabs(ports.Select(port => (GestureActions.PortLabel(node.Action, port), (Action)(() =>
            { _behaviorPorts[node.Id] = port; RenderBehaviorDetails(); }))), Array.IndexOf(ports, selected), Theme.CardBg);
        _behaviorBranchTabs = tabs;
        tabs.Margin = new Thickness(0, 4, 0, 8);
        _behaviorDetails.Children.Add(tabs);
        _behaviorBranchContent = new StackPanel();
        _behaviorBranchNode = _behaviorBranchPort = "";
        _behaviorDetails.Children.Add(_behaviorBranchContent);
        RenderBehaviorBranch(node);
    }

    void RenderBehaviorBranch(BehaviorNode node)
    {
        if (_behaviorBranchContent == null || _behaviorBranchTabs == null) return;
        var ports = GestureActions.PortsOf(node.Action);
        string selected = _behaviorPorts.TryGetValue(node.Id, out var value) && ports.Contains(value) ? value : ports[0];
        int target = Array.IndexOf(ports, selected);
        UpdateBehaviorTab(_behaviorBranchTabs, target);
        if (_behaviorBranchNode == node.Id && _behaviorBranchPort == selected) return;
        _behaviorBranchNode = node.Id; _behaviorBranchPort = selected;
        TransitionContent(_behaviorBranchContent, () =>
        {
            _behaviorBranchContent.Children.Clear();
            _behaviorEditors.RemoveAll(e => !ReferenceEquals(e, _behaviorRootEditor));
            if (!node.Branches.ContainsKey(selected)) node.Branches[selected] = new();
            var chain = BehaviorChain(() => node.Branches[selected], list => node.Branches[selected] = list, node);
            _behaviorBranchContent.Children.Add(chain.Root);
            if (selected is BehaviorPorts.Cancel or BehaviorPorts.Failed)
                _behaviorBranchContent.Children.Add(ToggleRow("\uE76C", Loc.T(K.Behaviors.Continue),
                    Loc.T(K.Hotkeys.RunsInOrderDragToReorder), selected == BehaviorPorts.Failed ? node.ContinueOnFailure : node.ContinueOnCancel,
                    enabled => { if (selected == BehaviorPorts.Failed) node.ContinueOnFailure = enabled; else node.ContinueOnCancel = enabled;
                        CommitBehaviorGraph(); }));
        }, _behaviorSlideDirection);
    }

    readonly Dictionary<string, string> _behaviorPorts = new();

    void NavigateBehaviorPath(int depth)
    {
        if (_behaviorPath.Count <= depth) return;
        _behaviorPath.RemoveRange(depth, _behaviorPath.Count - depth);
        RenderBehaviorDetails();
        foreach (var editor in _behaviorEditors.ToArray()) editor.Render();
    }

    void CommitBehaviorGraph()
    {
        var cfg = _settings.Current.Gestures;
        cfg.Graph.Collect();
        _settings.Mutate(s => s.Gestures = cfg);
        AppHost.Current.InstallHotkeys();
        if (!cfg.Graph.IsValid()) NoticeToast.Show(Loc.T(K.Behaviors.Invalid));
    }

    FrameworkElement BehaviorEntryRow(string selected, Action<string> save)
    {
        var cfg = _settings.Current.Gestures;
        var combo = new ComboBox { MaxWidth = 260, HorizontalAlignment = HorizontalAlignment.Left };
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
        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Left };
        Fluent.StyleCombo(combo);
        if (includeDefault) combo.Items.Add(new ComboBoxItem { Content = Loc.T(K.Behaviors.NodeDefault), Tag = "" });
        combo.Items.Add(new ComboBoxItem { Content = Loc.T(K.Common.Capture), Tag = BehaviorPorts.Still });
        combo.Items.Add(new ComboBoxItem { Content = Loc.T(K.Common.Recording), Tag = BehaviorPorts.Record });
        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == selected) ?? combo.Items[0];
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is ComboBoxItem { Tag: string mode }) save(mode); };
        return combo;
    }
}
