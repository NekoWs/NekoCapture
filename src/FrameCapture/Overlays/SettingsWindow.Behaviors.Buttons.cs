using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>行为页按钮的圆角、状态过渡与文字对比度。</summary>
public sealed partial class SettingsWindow
{
    static Button BehaviorButton(string text, Action click, bool selected = false, Color? backing = null)
    {
        var button = new Button { Content = text, FontSize = 12,
            BorderBrush = selected ? Theme.AccentBrush : Brushes.Transparent,
            BorderThickness = new Thickness(1), Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(2, 1, 2, 1), Cursor = Cursors.Hand };
        button.Template = BehaviorButtonTemplate(backing ?? Theme.CardBg, selected, Theme.Text);
        button.Click += (_, _) => click();
        return button;
    }

    static Button BehaviorIcon(string glyph, Str tip, Action click)
    {
        var button = new Button { Content = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"),
            ToolTip = Loc.T(tip), Width = 30, Height = 30, Cursor = Cursors.Hand,
            BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(1),
            Padding = new Thickness(6), Margin = new Thickness(3, 0, 3, 0) };
        button.Template = BehaviorButtonTemplate(Theme.CardBg, false, Theme.TextDim);
        button.Click += (_, _) => click();
        return button;
    }

    static ControlTemplate BehaviorButtonTemplate(Color backing, bool selected, Color preferredInk)
    {
        var rest = selected ? Fluent.AccentOn(backing, 0.14) : Theme.Opaque(backing);
        var hover = Fluent.HoverOn(rest);
        var press = Fluent.PressOn(rest);
        Color Ink(Color color) => selected ? Fluent.AccentTextOn(color) : Fluent.InkOn(color, preferredInk);
        string Duration(int ms) => TimeSpan.FromMilliseconds(ms).ToString("c", CultureInfo.InvariantCulture);
        string State(string name, Color color, double opacity = 1) => $"""
            <VisualState x:Name="{name}"><Storyboard>
              <ColorAnimation Storyboard.TargetName="surface" Storyboard.TargetProperty="(Border.Background).(SolidColorBrush.Color)" To="{color}" Duration="0"/>
              <ColorAnimation Storyboard.TargetName="ink" Storyboard.TargetProperty="Color" To="{Ink(color)}" Duration="0"/>
              <DoubleAnimation Storyboard.TargetName="surface" Storyboard.TargetProperty="Opacity" To="{opacity.ToString(CultureInfo.InvariantCulture)}" Duration="0"/>
            </Storyboard></VisualState>
            """;
        var template = (ControlTemplate)XamlReader.Parse($$"""
            <ControlTemplate TargetType="Button"
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Border x:Name="surface" CornerRadius="10" BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="{TemplateBinding BorderThickness}" Padding="{TemplateBinding Padding}">
                <VisualStateManager.VisualStateGroups>
                  <VisualStateGroup x:Name="CommonStates">
                    <VisualStateGroup.Transitions>
                      <VisualTransition To="MouseOver" GeneratedDuration="{{Duration(Fluent.Motion.HoverMs)}}"/>
                      <VisualTransition To="Pressed" GeneratedDuration="{{Duration(Fluent.Motion.PressMs)}}"/>
                      <VisualTransition GeneratedDuration="{{Duration(Fluent.Motion.ExitMs)}}"/>
                    </VisualStateGroup.Transitions>
                    {{State("Normal", rest)}}
                    {{State("MouseOver", hover)}}
                    {{State("Pressed", press)}}
                    {{State("Disabled", rest, 0.45)}}
                  </VisualStateGroup>
                </VisualStateManager.VisualStateGroups>
                <Border.Background><SolidColorBrush Color="{{rest}}"/></Border.Background>
                <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                    VerticalAlignment="{TemplateBinding VerticalContentAlignment}" RecognizesAccessKey="True">
                  <TextElement.Foreground><SolidColorBrush x:Name="ink" Color="{{Ink(rest)}}"/></TextElement.Foreground>
                </ContentPresenter>
              </Border>
            </ControlTemplate>
            """);
        template.Seal();
        return template;
    }
}
