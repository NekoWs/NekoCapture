using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>行为与事件出口的页签导航。</summary>
public sealed partial class SettingsWindow
{
    static void FocusBehaviorTab(TabControl tabs)
    {
        tabs.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (tabs.IsLoaded && tabs.SelectedItem is TabItem selected) selected.Focus();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    static TabControl BehaviorTabs(IEnumerable<(string Label, Action Select)> entries, int selected, Color backing)
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
              <Border BorderBrush="{{Theme.StrokeSoft}}" BorderThickness="0,0,0,1">
                <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/>
              </Border>
            </ControlTemplate>
            """);
        var panel = new FrameworkElementFactory(typeof(WrapPanel));
        tabs.ItemsPanel = new ItemsPanelTemplate(panel);
        tabs.ItemContainerStyle = BehaviorTabStyle(backing);
        foreach (var choice in choices) tabs.Items.Add(new TabItem { Header = choice.Label });
        tabs.SelectedIndex = Math.Clamp(selected, -1, choices.Length - 1);
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.OriginalSource == tabs && tabs.SelectedIndex >= 0) choices[tabs.SelectedIndex].Select();
        };
        return tabs;
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
                      <Border x:Name="indicator" Height="3" MinWidth="20" Margin="10,0" CornerRadius="1.5"
                          VerticalAlignment="Bottom" Background="{{Theme.Accent}}" Opacity="0"/>
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
                          <DoubleAnimation Storyboard.TargetName="indicator" Storyboard.TargetProperty="Opacity" To="1" Duration="{{duration}}"/>
                        </Storyboard></BeginStoryboard></Trigger.EnterActions>
                        <Trigger.ExitActions><BeginStoryboard><Storyboard>
                          <DoubleAnimation Storyboard.TargetName="active" Storyboard.TargetProperty="Opacity" To="0" Duration="{{duration}}"/>
                          <DoubleAnimation Storyboard.TargetName="indicator" Storyboard.TargetProperty="Opacity" To="0" Duration="{{duration}}"/>
                        </Storyboard></BeginStoryboard></Trigger.ExitActions>
                      </Trigger>
                      <Trigger Property="IsKeyboardFocused" Value="True">
                        <Setter TargetName="indicator" Property="Opacity" Value="1"/>
                      </Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
            """);
    }
}
