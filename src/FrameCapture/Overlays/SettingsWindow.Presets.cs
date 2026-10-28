using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using FrameCapture.Core;
using FrameCapture.Services;

namespace FrameCapture.Overlays;

/// <summary>快捷键页的行为预设卡片与自定义方案切换。</summary>
public sealed partial class SettingsWindow
{
    readonly Dictionary<string, Button> _presetCards = new();
    TextBlock? _customSchemeSummary;
    string _paintedPreset = "";

    FrameworkElement BuildBehaviorPresets()
    {
        _presetCards.Clear(); _paintedPreset = "";
        var body = new StackPanel { Margin = new Thickness(0, 6, 0, 12) };
        body.Children.Add(SectionHeader(Loc.T(K.Hotkeys.PresetBehaviors)));
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < BehaviorPresets.All.Count; i++)
        {
            string id = BehaviorPresets.All[i];
            var card = PresetCard(id);
            Grid.SetColumn(card, i % 2); Grid.SetRow(card, i / 2);
            grid.Children.Add(card); _presetCards[id] = card;
        }
        body.Children.Add(grid);
        body.Children.Add(new TextBlock { Text = Loc.T(K.Hotkeys.PresetHint), Foreground = Theme.TextDimBrush,
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(3, 5, 3, 0) });
        SyncBehaviorPresets();
        return body;
    }

    Button PresetCard(string id)
    {
        var contents = new StackPanel();
        contents.Children.Add(new TextBlock { Text = Loc.T(BehaviorPresets.Label(id)), FontSize = 13,
            FontWeight = FontWeights.SemiBold, Foreground = Theme.TextBrush, Margin = new Thickness(0, 0, 0, 5) });
        var description = id switch
        {
            BehaviorPresets.Dual => K.Hotkeys.PresetDualDesc,
            BehaviorPresets.Separate => K.Hotkeys.PresetSeparateDesc,
            BehaviorPresets.Single => K.Hotkeys.PresetSingleDesc,
            _ => K.Hotkeys.PresetCustomDesc,
        };
        contents.Children.Add(new TextBlock { Text = Loc.T(description), Foreground = Theme.TextDimBrush,
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 7) });
        if (id == BehaviorPresets.Custom)
        {
            _customSchemeSummary = new TextBlock { FontSize = 11.5, Foreground = Theme.TextBrush,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            contents.Children.Add(_customSchemeSummary);
        }
        else
        {
            contents.Children.Add(PresetFlowRow(GestureConfig.DefaultCaptureKey, id == BehaviorPresets.Separate
                ? Loc.T(K.Behaviors.SelectOnly) : id == BehaviorPresets.Dual ? Loc.T(K.Common.Capture)
                    : GestureActions.LabelOf(GestureActions.Select)));
            if (id != BehaviorPresets.Single)
                contents.Children.Add(PresetFlowRow(GestureConfig.DefaultRecordKey,
                    Loc.T(id == BehaviorPresets.Dual ? K.Common.Recording : K.Hotkeys.PresetGestures)));
            else contents.Children.Add(new TextBlock { Text = Loc.T(K.Hotkeys.PresetModeChoice), Foreground = Theme.TextBrush,
                FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
        }
        var button = new Button { Content = contents, Tag = id, Padding = new Thickness(10), MinHeight = 143,
            Margin = new Thickness(3), HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top, Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(1), BorderBrush = Theme.StrokeSoftBrush,
            Template = BehaviorButtonTemplate(Theme.CardBg, false, Theme.Text), ToolTip = Loc.T(description) };
        AutomationProperties.SetName(button, Loc.T(BehaviorPresets.Label(id)));
        button.Click += (_, _) => ApplyBehaviorPreset(id);
        return button;
    }

    static FrameworkElement PresetFlowRow(string chord, string result)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(Keycaps.Row(chord, new KeycapTheme { Face = Theme.RowBg, Text = Theme.Text,
            Stroke = Theme.StrokeSoft, Dim = Theme.TextDim, Size = 9.5 }));
        var arrow = new TextBlock { Text = "\uE76C", FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 8, Foreground = Theme.TextDimBrush, Margin = new Thickness(4, 4, 4, 0) };
        Grid.SetColumn(arrow, 1); grid.Children.Add(arrow);
        var text = new TextBlock { Text = result, FontSize = 11, Foreground = Theme.TextBrush,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) };
        Grid.SetColumn(text, 2); grid.Children.Add(text);
        return grid;
    }

    void SyncBehaviorPresets()
    {
        var cfg = _settings.Current.Gestures;
        if (_paintedPreset != cfg.ActivePreset)
        {
            foreach (var (id, card) in _presetCards)
            {
                bool selected = id == cfg.ActivePreset;
                card.BorderBrush = selected ? Theme.AccentBrush : Theme.StrokeSoftBrush;
                card.Template = BehaviorButtonTemplate(Theme.CardBg, selected, Theme.Text);
                AutomationProperties.SetItemStatus(card, selected ? Loc.T(BehaviorPresets.Label(id)) : "");
            }
            _paintedPreset = cfg.ActivePreset;
        }
        if (_customSchemeSummary != null)
            _customSchemeSummary.Text = Loc.F(K.Hotkeys.PresetCustomSummary,
                cfg.CustomScheme?.Bindings?.Count ?? cfg.Bindings.Count, cfg.CustomScheme?.Graph?.Flows?.Count ?? cfg.Graph.Flows.Count);
    }

    void OnPresetSettingsChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(SyncBehaviorPresets)); return; }
        SyncBehaviorPresets();
    }

    void ApplyBehaviorPreset(string id)
    {
        var cfg = _settings.Current.Gestures;
        if (cfg.ActivePreset == id) return;
        _settings.Mutate(settings => settings.Gestures.SelectPreset(id));
        _hotOpen = ""; _behaviorFlow = ""; _behaviorPath.Clear();
        AppHost.Current.InstallHotkeys();
        if (_hotList != null) TransitionContent(_hotList, RebuildHotkeys);
        RenderBehaviors();
    }
}
