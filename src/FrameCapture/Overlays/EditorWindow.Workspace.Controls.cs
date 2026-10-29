using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>编辑器按钮、主题模板与当前键位提示。</summary>
public sealed partial class EditorWindow
{
    readonly List<(Button Button, string? Command, Str Label, bool Primary)> _workspaceButtons = new();
    readonly List<Border> _workspacePanels = new();
    readonly List<TextBlock> _workspaceLabels = new();
    ControlTemplate? _workspaceButtonTemplate;

    Button IconBtn(string glyph, string tip, double size = 32)
    {
        var button = new Button
        {
            Content = glyph, ToolTip = tip, Width = size, Height = size, Margin = new Thickness(2),
            FontFamily = new FontFamily(Fluent.IconFontChain), FontSize = 14,
            Foreground = Theme.TextBrush, Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand, Template = WorkspaceButtonTemplate(), FocusVisualStyle = null,
        };
        WireButtonMotion(button);
        AutomationProperties.SetName(button, tip);
        _workspaceButtons.Add((button, null, Str.Empty, false));
        return button;
    }

    Button CommandIcon(string glyph, Str label, string command, Action execute)
    {
        var button = IconBtn(glyph, Loc.T(label));
        _workspaceButtons.RemoveAt(_workspaceButtons.Count - 1);
        _workspaceButtons.Add((button, command, label, false));
        button.Click += (_, _) => { if (!_encoding) execute(); };
        button.ToolTip = CommandTip(command, label);
        return button;
    }

    Button TextButton(Str label, Action execute, string? command = null, bool primary = false)
    {
        var button = new Button
        {
            Content = Loc.T(label), Height = 32, MinWidth = 32, Padding = new Thickness(12, 0, 12, 0),
            Margin = new Thickness(2), FontFamily = new FontFamily("Segoe UI"), FontSize = 12.5,
            FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = Theme.TextBrush, Background = Brushes.Transparent,
            BorderBrush = Theme.StrokeSoftBrush, BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand, Template = WorkspaceButtonTemplate(), FocusVisualStyle = null,
        };
        WireButtonMotion(button);
        button.Click += (_, _) => execute();
        button.ToolTip = CommandTip(command, label);
        AutomationProperties.SetName(button, Loc.T(label));
        _workspaceButtons.Add((button, command, label, primary));
        if (primary) Fluent.HighlightBtn(button, true);
        return button;
    }

    Button TextSizeBtn(string text, string tip)
    {
        var button = TextButton(K.Editor.SmallerText, () => { });
        button.Content = text;
        button.ToolTip = tip;
        button.Padding = new Thickness(6, 0, 6, 0);
        AutomationProperties.SetName(button, tip);
        return button;
    }

    void WireButtonMotion(Button button)
    {
        var scale = new ScaleTransform(1, 1);
        button.RenderTransform = scale;
        button.RenderTransformOrigin = new Point(0.5, 0.5);
        button.MouseEnter += (_, _) => Animate(1.04, Fluent.Motion.HoverMs);
        button.PreviewMouseLeftButtonDown += (_, _) => Animate(0.94, Fluent.Motion.PressMs);
        button.PreviewMouseLeftButtonUp += (_, _) => Animate(1.04, Fluent.Motion.SpringMs);
        button.MouseLeave += (_, _) => Animate(1, Fluent.Motion.QuickMs);
        void Animate(double value, int duration)
        {
            foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
                scale.BeginAnimation(property, new DoubleAnimation(value, TimeSpan.FromMilliseconds(duration))
                { EasingFunction = Fluent.Spring(0.45) });
        }
    }

    static void StyleAnnotationInput(TextBox box)
    {
        var template = new ControlTemplate(typeof(TextBox));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(BorderThicknessProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        border.AppendChild(new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost"));
        template.VisualTree = border;
        box.Template = template;
        box.FocusVisualStyle = null;
    }

    TextBlock WorkspaceLabel(string text, double size = 12)
    {
        var label = new TextBlock
        {
            Text = text, FontSize = size, FontFamily = new FontFamily("Segoe UI"),
            Foreground = Theme.TextDimBrush, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _workspaceLabels.Add(label);
        return label;
    }

    Border Panel(FrameworkElement child) => WorkspacePanel(child, new Thickness(10, 5, 10, 5));

    Border WorkspacePanel(FrameworkElement child, Thickness padding)
    {
        var panel = new Border
        {
            Background = _mica ? Brushes.Transparent : Theme.PanelBgBrush,
            BorderBrush = Theme.StrokeSoftBrush, BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = padding, Child = child,
        };
        _workspacePanels.Add(panel);
        return panel;
    }

    static FrameworkElement BarDivider() => new System.Windows.Shapes.Rectangle
    {
        Width = 1, Height = 18, Fill = Theme.StrokeSoftBrush,
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0),
    };

    string CommandTip(string? command, Str label)
    {
        string name = Loc.T(label);
        if (command == null || EditorCommands.Find(command) is not { } entry) return name;
        string? chord = EditorCommands.BindingsFor(_settings.Current, entry).FirstOrDefault();
        return string.IsNullOrEmpty(chord) ? name : $"{name} · {chord}";
    }

    ControlTemplate WorkspaceButtonTemplate()
    {
        if (_workspaceButtonTemplate != null) return _workspaceButtonTemplate;
        var template = new ControlTemplate(typeof(Button));
        var grid = new FrameworkElementFactory(typeof(Grid));
        var baseLayer = new FrameworkElementFactory(typeof(Border));
        baseLayer.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        baseLayer.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        baseLayer.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
        baseLayer.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(BorderThicknessProperty));
        grid.AppendChild(baseLayer);
        var hover = new FrameworkElementFactory(typeof(Border), "Hover");
        hover.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        hover.SetValue(Border.BackgroundProperty, new SolidColorBrush(Theme.Hover));
        hover.SetValue(OpacityProperty, 0.0);
        grid.AppendChild(hover);
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(PaddingProperty));
        grid.AppendChild(content);
        var focus = new FrameworkElementFactory(typeof(Border), "Focus");
        focus.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
        focus.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Theme.Accent));
        focus.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
        focus.SetValue(OpacityProperty, 0.0);
        focus.SetValue(IsHitTestVisibleProperty, false);
        grid.AppendChild(focus);
        template.VisualTree = grid;
        AddHover(MouseEnterEvent, 1, Fluent.Motion.HoverMs);
        AddHover(MouseLeaveEvent, 0, Fluent.Motion.QuickMs);
        var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Theme.Press), "Hover"));
        pressed.Setters.Add(new Setter(OpacityProperty, 1.0, "Hover"));
        template.Triggers.Add(pressed);
        var focused = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        focused.Setters.Add(new Setter(OpacityProperty, 1.0, "Focus"));
        template.Triggers.Add(focused);
        var disabled = new Trigger { Property = IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(OpacityProperty, 0.4));
        template.Triggers.Add(disabled);
        _workspaceButtonTemplate = template;
        return template;

        void AddHover(RoutedEvent routedEvent, double value, int duration)
        {
            var storyboard = new Storyboard();
            var animation = new DoubleAnimation(value, TimeSpan.FromMilliseconds(duration));
            Storyboard.SetTargetName(animation, "Hover");
            Storyboard.SetTargetProperty(animation, new PropertyPath(OpacityProperty));
            storyboard.Children.Add(animation);
            var trigger = new EventTrigger(routedEvent);
            trigger.Actions.Add(new BeginStoryboard { Storyboard = storyboard });
            template.Triggers.Add(trigger);
        }
    }

    void RefreshWorkspaceButtons()
    {
        _workspaceButtonTemplate = null;
        foreach (var (button, command, label, primary) in _workspaceButtons)
        {
            button.Template = WorkspaceButtonTemplate();
            button.Foreground = Theme.TextBrush;
            if (button.BorderThickness.Left > 0) button.BorderBrush = Theme.StrokeSoftBrush;
            if (command != null) button.ToolTip = CommandTip(command, label);
            if (primary) Fluent.HighlightBtn(button, true);
        }
        foreach (var panel in _workspacePanels)
        {
            panel.Background = _mica ? Brushes.Transparent : Theme.PanelBgBrush;
            panel.BorderBrush = Theme.StrokeSoftBrush;
        }
        foreach (var label in _workspaceLabels) label.Foreground = Theme.TextDimBrush;
        if (_titleLabel != null) _titleLabel.Foreground = Theme.TextBrush;
        if (_activeToolLabel != null) _activeToolLabel.Foreground = Theme.TextBrush;
    }
}
