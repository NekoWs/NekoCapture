using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>行为与事件出口的页签导航。</summary>
public sealed partial class SettingsWindow
{
    void UpdateBehaviorTab(TabControl tabs, int index)
    {
        _behaviorTabSync = true;
        try { tabs.SelectedIndex = index; }
        finally { _behaviorTabSync = false; }
    }

    TabControl BehaviorTabs(IEnumerable<(string Label, Action Select)> entries, int selected, Color backing)
    {
        var choices = entries.ToArray();
        var tabs = new TabControl { BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Left, FontSize = 12,
            Foreground = new SolidColorBrush(Fluent.InkOn(backing, Theme.Text)),
            Padding = new Thickness(0), Margin = new Thickness(0, 0, 8, 0) };
        tabs.Template = (ControlTemplate)XamlReader.Parse($$"""
            <ControlTemplate TargetType="TabControl"
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid>
                <Border BorderBrush="{{Theme.StrokeSoft}}" BorderThickness="0,0,0,1">
                  <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/>
                </Border>
                <Canvas IsHitTestVisible="False">
                  <Border x:Name="selectionIndicator" Height="3" Width="0" CornerRadius="1.5"
                      Background="{{Theme.Accent}}" Visibility="Collapsed">
                    <Border.RenderTransform><TranslateTransform/></Border.RenderTransform>
                  </Border>
                </Canvas>
              </Grid>
            </ControlTemplate>
            """);
        var panel = new FrameworkElementFactory(typeof(WrapPanel));
        tabs.ItemsPanel = new ItemsPanelTemplate(panel);
        tabs.ItemContainerStyle = BehaviorTabStyle(backing);
        foreach (var choice in choices) tabs.Items.Add(new TabItem { Header = choice.Label });
        tabs.SelectedIndex = Math.Clamp(selected, -1, choices.Length - 1);
        int active = tabs.SelectedIndex;
        void Move() => tabs.Dispatcher.BeginInvoke(new Action(() => MoveBehaviorIndicator(tabs)),
            System.Windows.Threading.DispatcherPriority.Loaded);
        tabs.Loaded += (_, _) => Move();
        tabs.SizeChanged += (_, _) => Move();
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.OriginalSource != tabs || tabs.SelectedIndex < 0) return;
            int next = tabs.SelectedIndex;
            _behaviorSlideDirection = next < active ? -1 : 1;
            active = next; Move();
            if (!_behaviorTabSync) choices[next].Select();
        };
        return tabs;
    }

    static void MoveBehaviorIndicator(TabControl tabs)
    {
        if (!tabs.IsLoaded || tabs.SelectedItem is not TabItem selected || selected.ActualWidth <= 0
            || tabs.Template.FindName("selectionIndicator", tabs) is not Border indicator) return;
        var at = selected.TranslatePoint(new Point(10, selected.ActualHeight - 3), tabs);
        double width = Math.Max(20, selected.ActualWidth - 20);
        var shift = (TranslateTransform)indicator.RenderTransform;
        if (shift.IsFrozen) { shift = shift.CloneCurrentValue(); indicator.RenderTransform = shift; }
        bool animate = indicator.Visibility == Visibility.Visible;
        double fromWidth = indicator.Width, fromX = shift.X, fromY = shift.Y;
        indicator.BeginAnimation(WidthProperty, null);
        shift.BeginAnimation(TranslateTransform.XProperty, null); shift.BeginAnimation(TranslateTransform.YProperty, null);
        indicator.Width = width; shift.X = at.X; shift.Y = at.Y; indicator.Visibility = Visibility.Visible;
        if (!animate) return;
        var duration = new Duration(TimeSpan.FromMilliseconds(Fluent.Motion.StateMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        indicator.BeginAnimation(WidthProperty, new DoubleAnimation(fromWidth, width, duration) { EasingFunction = ease });
        shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, at.X, duration) { EasingFunction = ease });
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, at.Y, duration) { EasingFunction = ease });
    }

    static Style BehaviorTabStyle(Color backing)
    {
        string duration = TimeSpan.FromMilliseconds(Fluent.Motion.HoverMs).ToString("c", CultureInfo.InvariantCulture);
        return (Style)XamlReader.Parse($$"""
            <Style TargetType="TabItem"
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Setter Property="Padding" Value="12,8"/>
              <Setter Property="Cursor" Value="Hand"/>
              <Setter Property="Foreground" Value="{{Fluent.InkOn(backing, Theme.Text)}}"/>
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="TabItem">
                    <Grid Background="Transparent" Margin="1,0">
                      <Border x:Name="hover" CornerRadius="10,10,0,0" Background="{{Fluent.HoverOn(backing)}}" Opacity="0"/>
                      <Border x:Name="active" CornerRadius="10,10,0,0" Background="{{Fluent.AccentOn(backing, 0.09)}}" Opacity="0"/>
                      <ContentPresenter x:Name="label" ContentSource="Header" Margin="{TemplateBinding Padding}"
                          HorizontalAlignment="Center" VerticalAlignment="Center" TextElement.Foreground="{TemplateBinding Foreground}"/>
                      <Border x:Name="focus" CornerRadius="10,10,0,0" BorderBrush="{{Theme.StrokeSoft}}" BorderThickness="1" Opacity="0"/>
                    </Grid>
                    <ControlTemplate.Triggers>
                      <Trigger Property="IsMouseOver" Value="True">
                        <Setter TargetName="label" Property="TextElement.Foreground" Value="{{Fluent.InkOn(Fluent.HoverOn(backing), Theme.Text)}}"/>
                        <Trigger.EnterActions><BeginStoryboard><Storyboard>
                          <DoubleAnimation Storyboard.TargetName="hover" Storyboard.TargetProperty="Opacity" To="1" Duration="{{duration}}"/>
                        </Storyboard></BeginStoryboard></Trigger.EnterActions>
                        <Trigger.ExitActions><BeginStoryboard><Storyboard>
                          <DoubleAnimation Storyboard.TargetName="hover" Storyboard.TargetProperty="Opacity" To="0" Duration="{{duration}}"/>
                        </Storyboard></BeginStoryboard></Trigger.ExitActions>
                      </Trigger>
                      <Trigger Property="IsSelected" Value="True">
                        <Setter TargetName="label" Property="TextElement.FontWeight" Value="SemiBold"/>
                        <Setter TargetName="label" Property="TextElement.Foreground" Value="{{Fluent.AccentTextOn(Fluent.AccentOn(backing, 0.09))}}"/>
                        <Trigger.EnterActions><BeginStoryboard><Storyboard>
                          <DoubleAnimation Storyboard.TargetName="active" Storyboard.TargetProperty="Opacity" To="1" Duration="{{duration}}"/>
                        </Storyboard></BeginStoryboard></Trigger.EnterActions>
                        <Trigger.ExitActions><BeginStoryboard><Storyboard>
                          <DoubleAnimation Storyboard.TargetName="active" Storyboard.TargetProperty="Opacity" To="0" Duration="{{duration}}"/>
                        </Storyboard></BeginStoryboard></Trigger.ExitActions>
                      </Trigger>
                      <Trigger Property="IsKeyboardFocused" Value="True">
                        <Setter TargetName="focus" Property="Opacity" Value="1"/>
                      </Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
            """);
    }
}
