// Fluent partial：全局样式——主题化 ToolTip、细滚动条、菜单资源（ContextMenu/MenuItem/Separator）。
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace FrameCapture.Core;

public static partial class Fluent
{
    // ---------------- 全局样式：主题化 ToolTip（淡入卡片）+ 细滚动条 ----------------

    public static void ApplyGlobalStyles()
    {
        var app = System.Windows.Application.Current;
        if (app == null) return;
        app.Resources[typeof(System.Windows.Controls.ToolTip)] = ToolTipStyle();
        app.Resources[typeof(ScrollBar)] = ScrollBarStyle();
    }

    public static Style ToolTipStyle()
    {
        string xaml = $"""
            <Style TargetType="ToolTip"
                   xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Setter Property="OverridesDefaultStyle" Value="True"/>
              <Setter Property="HasDropShadow" Value="False"/>
              <Setter Property="FontSize" Value="12"/>
              <Setter Property="FontFamily" Value="Microsoft YaHei UI, Microsoft YaHei, Segoe UI"/>
              <Setter Property="TextBlock.Foreground" Value="{Hex(Theme.Text)}"/>
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="ToolTip">
                    <Border x:Name="bd" CornerRadius="8" Background="{Hex(Theme.CardBg)}"
                            BorderBrush="{Hex(Theme.Stroke)}" BorderThickness="1"
                            Padding="10,7,10,7" Opacity="0">
                      <Border.Effect><DropShadowEffect BlurRadius="14" ShadowDepth="3" Opacity="{(Theme.IsDark ? 0.35 : 0.14):0.00}"/></Border.Effect>
                      <ContentPresenter VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                      <EventTrigger RoutedEvent="FrameworkElement.Loaded">
                        <BeginStoryboard><Storyboard>
                          <DoubleAnimation Storyboard.TargetName="bd" Storyboard.TargetProperty="Opacity"
                                           From="0" To="1" Duration="0:0:0.14"><DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseOut"/></DoubleAnimation.EasingFunction></DoubleAnimation>
                        </Storyboard></BeginStoryboard>
                      </EventTrigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
            """;
        return (Style)System.Windows.Markup.XamlReader.Parse(xaml);
    }

    /// <summary>R161：主题 ScrollBar 样式的 MinWidth/MinHeight（=系统滚动条宽 17.33）经 DynamicResource
    /// 以直值形式泄漏到模板内 ScrollBar 上，样式 Setter 压不住（直值优先级更高）——细滚动条样式
    /// 「看起来没生效」的真凶。附加属性在样式应用瞬间把两者归零（本地值可压直值，探针实证）。</summary>
    public static readonly DependencyProperty SlimProperty = DependencyProperty.RegisterAttached(
        "Slim", typeof(bool), typeof(Fluent), new PropertyMetadata(false, (d, e) =>
        {
            if (d is ScrollBar sb && (bool)e.NewValue) { sb.MinWidth = 0; sb.MinHeight = 0; }
        }));
    public static void SetSlim(DependencyObject d, bool v) => d.SetValue(SlimProperty, v);
    public static bool GetSlim(DependencyObject d) => (bool)d.GetValue(SlimProperty);

    public static Style ScrollBarStyle()
    {
        string thumb = Hex(Theme.Stroke), thumbHi = Hex(Theme.Press);
        // 轨道 8DIP、滑块 4DIP 圆角胶囊、无箭头；竖/横两套模板（Track 方向必须匹配，否则滑块拖不动）
        string Tmpl(string orient, string dirRev) => $"""
            <Setter.Value>
              <ControlTemplate TargetType="ScrollBar">
                <Grid Background="Transparent">
                  <Track x:Name="PART_Track" Orientation="{orient}" IsDirectionReversed="{dirRev}">
                  <Track.DecreaseRepeatButton>
                    <RepeatButton Focusable="False" IsTabStop="False">
                      <RepeatButton.Template>
                        <ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate>
                      </RepeatButton.Template>
                    </RepeatButton>
                  </Track.DecreaseRepeatButton>
                  <Track.IncreaseRepeatButton>
                    <RepeatButton Focusable="False" IsTabStop="False">
                      <RepeatButton.Template>
                        <ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate>
                      </RepeatButton.Template>
                    </RepeatButton>
                  </Track.IncreaseRepeatButton>
                  <Track.Thumb>
                    <Thumb Focusable="False" IsTabStop="False">
                      <Thumb.Template>
                        <ControlTemplate TargetType="Thumb">
                          <Border x:Name="tb" CornerRadius="2" Background="{thumb}" Margin="2"/>
                          <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                              <Setter TargetName="tb" Property="Background" Value="{thumbHi}"/>
                            </Trigger>
                          </ControlTemplate.Triggers>
                        </ControlTemplate>
                      </Thumb.Template>
                    </Thumb>
                  </Track.Thumb>
                </Track>
                </Grid>
              </ControlTemplate>
            </Setter.Value>
            """;
        string sbXaml = $"""
            <Style TargetType="ScrollBar"
                   xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Setter Property="Background" Value="Transparent"/>
              <Setter Property="Width" Value="8"/>
              <Setter Property="Template">{Tmpl("Vertical", "True")}</Setter>
              <Style.Triggers>
                <Trigger Property="Orientation" Value="Horizontal">
                  <Setter Property="Width" Value="Auto"/>
                  <Setter Property="Height" Value="8"/>
                  <Setter Property="Template">{Tmpl("Horizontal", "False")}</Setter>
                </Trigger>
              </Style.Triggers>
            </Style>
            """;
        var st = (Style)System.Windows.Markup.XamlReader.Parse(sbXaml);
        st.Setters.Add(new Setter(SlimProperty, true));   // 附加属性用 DP 对象直挂（XAML 里 clr-namespace 解析不了）
        return st;
    }

    public static ResourceDictionary MenuResources()
    {
        string xaml = $"""
            <ResourceDictionary
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Style TargetType="ContextMenu">
                <Setter Property="OverridesDefaultStyle" Value="True"/>
                <Setter Property="TextElement.Foreground" Value="{Hex(Theme.Text)}"/>
                <Setter Property="MinWidth" Value="176"/>
                <Setter Property="Template">
                  <Setter.Value>
                    <ControlTemplate TargetType="ContextMenu">
                      <!-- R161：外包透明 Border 右下留 24px——Popup 窗按模板 DesiredSize 开窗，
                           BackEase 过冲(>1.0)与投影此前都被窗缘裁掉，Q 弹弹到一半被切；留白后完整。
                           只留右下（缩放原点 0,0，过冲只向右下扩），卡片仍贴鼠标点，不留缝。 -->
                      <Border Padding="0,0,24,24">
                        <Border x:Name="bd" CornerRadius="8" Background="{Hex(Theme.CardBg)}"
                                BorderBrush="{Hex(Theme.Stroke)}" BorderThickness="1" Padding="3,4,3,4" MinWidth="176"
                                RenderTransformOrigin="0,0" Opacity="0" HorizontalAlignment="Left" VerticalAlignment="Top">
                          <Border.RenderTransform><ScaleTransform ScaleX="0.92" ScaleY="0.92"/></Border.RenderTransform>
                          <Border.Effect><DropShadowEffect BlurRadius="16" ShadowDepth="4" Opacity="{(Theme.IsDark ? 0.4 : 0.15):0.00}"/></Border.Effect>
                          <ItemsPresenter/>
                          <Border.Triggers>
                            <EventTrigger RoutedEvent="FrameworkElement.Loaded">
                              <BeginStoryboard><Storyboard>
                                <DoubleAnimation Storyboard.TargetName="bd" Storyboard.TargetProperty="Opacity"
                                                 To="1" Duration="0:0:0.11"><DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseOut"/></DoubleAnimation.EasingFunction></DoubleAnimation>
                                <DoubleAnimation Storyboard.TargetName="bd" Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleX)"
                                                 To="1" Duration="0:0:0.2"><DoubleAnimation.EasingFunction><BackEase Amplitude="0.45" EasingMode="EaseOut"/></DoubleAnimation.EasingFunction></DoubleAnimation>
                                <DoubleAnimation Storyboard.TargetName="bd" Storyboard.TargetProperty="(UIElement.RenderTransform).(ScaleTransform.ScaleY)"
                                                 To="1" Duration="0:0:0.2"><DoubleAnimation.EasingFunction><BackEase Amplitude="0.45" EasingMode="EaseOut"/></DoubleAnimation.EasingFunction></DoubleAnimation>
                              </Storyboard></BeginStoryboard>
                            </EventTrigger>
                          </Border.Triggers>
                        </Border>
                      </Border>
                    </ControlTemplate>
                  </Setter.Value>
                </Setter>
              </Style>
              <Style TargetType="Separator">
                <Setter Property="Template">
                  <Setter.Value>
                    <ControlTemplate TargetType="Separator">
                      <Border Height="1" Background="{Hex(Theme.Stroke)}" Margin="8,3,8,3"/>
                    </ControlTemplate>
                  </Setter.Value>
                </Setter>
              </Style>
              <Style TargetType="MenuItem">
                <Setter Property="OverridesDefaultStyle" Value="True"/>
                <Setter Property="FontSize" Value="12"/>
                <Setter Property="Template">
                  <Setter.Value>
                    <ControlTemplate TargetType="MenuItem">
                      <Grid><Border x:Name="bd" CornerRadius="5" Background="Transparent">
                        <Grid Height="27">
                          <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="24"/>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="Auto"/>
                            <ColumnDefinition Width="Auto"/>
                          </Grid.ColumnDefinitions>
                          <TextBlock x:Name="chk" Grid.Column="0" Text="" FontFamily="Segoe Fluent Icons"
                                     FontSize="11" HorizontalAlignment="Center" VerticalAlignment="Center"
                                     Foreground="{Hex(Theme.Accent)}"/>
                          <ContentPresenter Grid.Column="1" ContentSource="Header" VerticalAlignment="Center" Margin="0,0,14,0"/><TextBlock Grid.Column="2" Text="&#123;TemplateBinding InputGestureText&#125;" FontSize="11" Foreground="{Hex(Theme.TextDim)}" VerticalAlignment="Center" Margin="0,0,10,0"/><TextBlock x:Name="arrow" Grid.Column="3" Text="&#xE76C;" FontFamily="Segoe Fluent Icons" FontSize="10" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,10,0" Foreground="{Hex(Theme.Text)}" Visibility="Collapsed"/>
                        </Grid>
                      </Border>
                      <Popup x:Name="PART_Popup" AllowsTransparency="True" Focusable="False" Placement="Right"
                             HorizontalOffset="-4" PopupAnimation="Fade">
                        <Border CornerRadius="8" Background="{Hex(Theme.CardBg)}" BorderBrush="{Hex(Theme.Stroke)}"
                                BorderThickness="1" Padding="3,4,3,4" Margin="0,0,10,10">
                          <Border.Effect><DropShadowEffect BlurRadius="16" ShadowDepth="4" Opacity="{(Theme.IsDark ? 0.4 : 0.15):0.00}"/></Border.Effect>
                          <ItemsPresenter/>
                        </Border>
                      </Popup>
                      </Grid>
                      <ControlTemplate.Triggers>
                        <Trigger Property="IsHighlighted" Value="True">
                          <Setter TargetName="bd" Property="Background" Value="{Hex(Theme.Hover)}"/>
                        </Trigger>
                        <Trigger Property="IsChecked" Value="True">
                          <Setter TargetName="chk" Property="Text" Value="&#xE73E;"/></Trigger><Trigger Property="Role" Value="SubmenuHeader"><Setter TargetName="arrow" Property="Visibility" Value="Visible"/>
                        </Trigger>
                      </ControlTemplate.Triggers>
                    </ControlTemplate>
                  </Setter.Value>
                </Setter>
              </Style>
            </ResourceDictionary>
            """;
        return (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(xaml);
    }
}
