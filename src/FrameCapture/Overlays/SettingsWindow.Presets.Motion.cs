using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>预设卡片的持久模板、选中渐变与不透明列表过渡。</summary>
public sealed partial class SettingsWindow
{
    static ControlTemplate PresetCardTemplate()
    {
        string duration = TimeSpan.FromMilliseconds(Fluent.Motion.HoverMs).ToString("c", CultureInfo.InvariantCulture);
        return (ControlTemplate)XamlReader.Parse($$"""
            <ControlTemplate TargetType="Button"
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid>
                <VisualStateManager.VisualStateGroups>
                  <VisualStateGroup x:Name="CommonStates">
                    <VisualStateGroup.Transitions><VisualTransition GeneratedDuration="{{duration}}"/></VisualStateGroup.Transitions>
                    <VisualState x:Name="Normal"><Storyboard>
                      <DoubleAnimation Storyboard.TargetName="hover" Storyboard.TargetProperty="Opacity" To="0" Duration="0"/>
                    </Storyboard></VisualState>
                    <VisualState x:Name="MouseOver"><Storyboard>
                      <DoubleAnimation Storyboard.TargetName="hover" Storyboard.TargetProperty="Opacity" To="1" Duration="0"/>
                    </Storyboard></VisualState>
                    <VisualState x:Name="Pressed"><Storyboard>
                      <DoubleAnimation Storyboard.TargetName="hover" Storyboard.TargetProperty="Opacity" To="0.6" Duration="0"/>
                    </Storyboard></VisualState>
                  </VisualStateGroup>
                </VisualStateManager.VisualStateGroups>
                <Border CornerRadius="10" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                    BorderThickness="{TemplateBinding BorderThickness}"/>
                <Border x:Name="hover" CornerRadius="10" Background="{DynamicResource preset-hover}" Opacity="0" IsHitTestVisible="False"/>
                <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="Stretch" VerticalAlignment="Top"/>
              </Grid>
            </ControlTemplate>
            """);
    }

    static void TintPresetCard(Button card, bool selected)
    {
        void Tint(SolidColorBrush brush, Color target)
        {
            Color current = brush.Color;
            brush.BeginAnimation(SolidColorBrush.ColorProperty, null); brush.Color = target;
            if (card.IsLoaded && current != target)
                brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(current, target,
                    TimeSpan.FromMilliseconds(Fluent.Motion.StateMs)));
        }
        Tint((SolidColorBrush)card.Background, selected ? Fluent.AccentOn(Theme.CardBg, 0.14) : Theme.CardBg);
        Tint((SolidColorBrush)card.BorderBrush, selected ? Theme.Accent : Theme.StrokeSoft);
    }

    static void TransitionPresetBindings(Panel host, Action render)
    {
        double previousHeight = host.ActualHeight;
        var state = ContentMotionFor(host);
        int generation = ++state.Generation;
        state.Outgoing?.Remove(); state.Outgoing = null;
        host.BeginAnimation(HeightProperty, null); host.Height = double.NaN;
        render();
        if (!host.IsVisible || host.ActualWidth <= 0) return;
        host.Measure(new Size(host.ActualWidth, double.PositiveInfinity));
        host.ClipToBounds = true;
        var duration = new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.EnterMs));
        var height = new DoubleAnimation(previousHeight, host.DesiredSize.Height, duration)
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        height.Completed += (_, _) =>
        {
            if (generation != state.Generation) return;
            host.BeginAnimation(HeightProperty, null); host.Height = double.NaN;
        };
        host.BeginAnimation(HeightProperty, height);
        foreach (FrameworkElement row in host.Children)
        {
            var shift = new TranslateTransform(0, 6); row.RenderTransform = shift;
            var slide = new DoubleAnimation(0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            slide.Completed += (_, _) => { if (ReferenceEquals(row.RenderTransform, shift)) row.RenderTransform = null; };
            shift.BeginAnimation(TranslateTransform.YProperty, slide);
        }
    }
}
