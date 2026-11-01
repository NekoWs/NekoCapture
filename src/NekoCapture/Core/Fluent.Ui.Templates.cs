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
        string ink = OnAccentColor(Theme.Accent).ToString();
        string xaml = $$"""
            <ControlTemplate TargetType="CheckBox" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid Background="Transparent">
                <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                <Border x:Name="box" Width="17" Height="17" CornerRadius="4" BorderThickness="1"
                    BorderBrush="{{Theme.Stroke}}" Background="{{Theme.RowBg}}" VerticalAlignment="Center">
                  <TextBlock x:Name="mark" Text="&#xE73E;" FontFamily="Segoe Fluent Icons" FontSize="11"
                      Foreground="{{ink}}" HorizontalAlignment="Center" VerticalAlignment="Center" Visibility="Collapsed"/>
                </Border>
                <ContentPresenter Grid.Column="1" Content="{TemplateBinding Content}" ContentTemplate="{TemplateBinding ContentTemplate}"
                    Margin="9,0,0,0" VerticalAlignment="Center" RecognizesAccessKey="True"/>
              </Grid>
              <ControlTemplate.Triggers>
                <Trigger Property="IsChecked" Value="True">
                  <Setter TargetName="box" Property="Background" Value="{{Theme.Accent}}"/>
                  <Setter TargetName="box" Property="BorderBrush" Value="{{Theme.Accent}}"/>
                  <Setter TargetName="mark" Property="Visibility" Value="Visible"/>
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
