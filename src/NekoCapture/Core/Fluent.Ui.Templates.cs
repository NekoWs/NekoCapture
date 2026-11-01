using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace NekoCapture.Core;

/// <summary>统一复选框与进度反馈模板。</summary>
public static partial class Fluent
{
    private static void StyleUiCheckBox(CheckBox control)
    {
        control.FocusVisualStyle = null;
        control.Cursor = System.Windows.Input.Cursors.Hand;
        if (control.ReadLocalValue(Control.FontSizeProperty) == DependencyProperty.UnsetValue) control.FontSize = 12.5;
        string ink = OnAccentColor(Theme.Accent).ToString();
        string duration = TimeSpan.FromMilliseconds(Motion.StateMs).ToString("c", CultureInfo.InvariantCulture);
        string xaml = $$"""
            <ControlTemplate TargetType="CheckBox" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid Background="Transparent" MinHeight="28" SnapsToDevicePixels="True" UseLayoutRounding="True">
                <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                <Border x:Name="box" Width="20" Height="20" CornerRadius="5" BorderThickness="1.5"
                    BorderBrush="{{Theme.StrokeField}}" Background="{{Theme.RowBg}}" VerticalAlignment="Center">
                  <TextBlock x:Name="mark" Text="&#xE73E;" FontFamily="Segoe Fluent Icons" FontSize="12"
                      Foreground="{{ink}}" HorizontalAlignment="Center" VerticalAlignment="Center" Opacity="0"
                      RenderTransformOrigin="0.5,0.5"><TextBlock.RenderTransform><ScaleTransform ScaleX="0.7" ScaleY="0.7"/></TextBlock.RenderTransform></TextBlock>
                </Border>
                <ContentPresenter Grid.Column="1" Content="{TemplateBinding Content}" ContentTemplate="{TemplateBinding ContentTemplate}"
                    Margin="10,0,0,0" VerticalAlignment="Center" RecognizesAccessKey="True"/>
              </Grid>
              <ControlTemplate.Triggers>
                <Trigger Property="IsChecked" Value="True">
                  <Setter TargetName="box" Property="Background" Value="{{Theme.Accent}}"/>
                  <Setter TargetName="box" Property="BorderBrush" Value="{{Theme.Accent}}"/>
                  <Trigger.EnterActions><BeginStoryboard><Storyboard>
                    <DoubleAnimation Storyboard.TargetName="mark" Storyboard.TargetProperty="Opacity" To="1" Duration="{{duration}}"/>
                    <DoubleAnimation Storyboard.TargetName="mark" Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleX)" To="1" Duration="{{duration}}"/>
                    <DoubleAnimation Storyboard.TargetName="mark" Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleY)" To="1" Duration="{{duration}}"/>
                  </Storyboard></BeginStoryboard></Trigger.EnterActions>
                  <Trigger.ExitActions><BeginStoryboard><Storyboard>
                    <DoubleAnimation Storyboard.TargetName="mark" Storyboard.TargetProperty="Opacity" To="0" Duration="{{duration}}"/>
                    <DoubleAnimation Storyboard.TargetName="mark" Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleX)" To="0.7" Duration="{{duration}}"/>
                    <DoubleAnimation Storyboard.TargetName="mark" Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleY)" To="0.7" Duration="{{duration}}"/>
                  </Storyboard></BeginStoryboard></Trigger.ExitActions>
                </Trigger>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="box" Property="BorderBrush" Value="{{Theme.Accent}}"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="box" Property="BorderBrush" Value="{{Theme.Accent}}"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """;
        control.Template = (ControlTemplate)XamlReader.Parse(xaml);
    }

    private static ControlTemplate UiProgressTemplate()
    {
        string duration = TimeSpan.FromMilliseconds(Motion.DissolveMs).ToString("c", CultureInfo.InvariantCulture);
        return (ControlTemplate)XamlReader.Parse($$"""
            <ControlTemplate TargetType="ProgressBar" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid x:Name="PART_Track" ClipToBounds="True">
                <Border Background="{TemplateBinding Background}" CornerRadius="3"/>
                <Border x:Name="PART_Indicator" Background="{TemplateBinding Foreground}" CornerRadius="3" HorizontalAlignment="Left"/>
              </Grid>
              <ControlTemplate.Triggers>
                <Trigger Property="IsIndeterminate" Value="True">
                  <Trigger.EnterActions><BeginStoryboard x:Name="busy"><Storyboard>
                    <DoubleAnimation Storyboard.TargetName="PART_Indicator" Storyboard.TargetProperty="Opacity"
                        From="0.35" To="1" Duration="{{duration}}" AutoReverse="True" RepeatBehavior="Forever"/>
                  </Storyboard></BeginStoryboard></Trigger.EnterActions>
                  <Trigger.ExitActions><StopStoryboard BeginStoryboardName="busy"/></Trigger.ExitActions>
                </Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """);
    }
}
