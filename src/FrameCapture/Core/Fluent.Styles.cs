// Fluent partial：全局样式——主题化 ToolTip、细滚动条、菜单资源（ContextMenu/MenuItem/Separator）。
using System.Windows;
using System.Windows.Controls.Primitives;

namespace FrameCapture.Core;

/// <summary>Fluent 工厂的样式 partial：全局 ToolTip、细滚动条与菜单资源字典。
/// 样式全部由 XAML 字符串现拼现解，颜色烘的是当次调用的主题色——主题切换后必须重新生成。</summary>
public static partial class Fluent
{
    #region 全局样式：主题化 ToolTip（淡入卡片）+ 细滚动条

    /// <summary>把 ToolTip 与 ScrollBar 的隐式样式写进 <c>Application.Resources</c>（键即类型）。
    /// 启动时与每次主题切换后都要重跑一次：样式里的颜色是生成时烘死的，不随主题自动更新。</summary>
    public static void ApplyGlobalStyles()
    {
        var app = System.Windows.Application.Current;
        if (app == null) return;
        app.Resources[typeof(System.Windows.Controls.ToolTip)] = ToolTipStyle();
        app.Resources[typeof(ScrollBar)] = ScrollBarStyle();
    }

    /// <summary>主题化 ToolTip 样式：去系统底、卡片圆角 + 淡入。</summary>
    /// <returns><c>TargetType=ToolTip</c> 的隐式样式，可直接作 <c>Resources[typeof(ToolTip)]</c> 的键值。</returns>
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

    /// <summary>把 ScrollBar 的 MinWidth/MinHeight 归零的附加属性。
    /// 主题 ScrollBar 样式的 MinWidth/MinHeight（=系统滚动条宽 17.33）经 DynamicResource 以直值形式
    /// 落到模板内 ScrollBar 上，样式 Setter 压不住直值——不归零则细滚动条样式看着像没生效。
    /// 本地值优先级高于直值，故在属性应用瞬间置零；由 <see cref="ScrollBarStyle"/> 用 DP 对象直挂。</summary>
    public static readonly DependencyProperty SlimProperty = DependencyProperty.RegisterAttached(
        "Slim", typeof(bool), typeof(Fluent), new PropertyMetadata(false, (d, e) =>
        {
            if (d is ScrollBar sb && (bool)e.NewValue) { sb.MinWidth = 0; sb.MinHeight = 0; }
        }));
    /// <summary>挂载 <see cref="SlimProperty"/>：置 true 的 ScrollBar 立刻放弃系统最小尺寸。</summary>
    /// <param name="d">目标依赖对象；只有 ScrollBar 会被真正改动。</param>
    /// <param name="v">是否启用细滚动条修正。</param>
    public static void SetSlim(DependencyObject d, bool v) => d.SetValue(SlimProperty, v);
    /// <summary>读回 <see cref="SlimProperty"/>。</summary>
    /// <param name="d">目标依赖对象。</param>
    /// <returns>该对象上记录的 Slim 值，未设置过为 false。</returns>
    public static bool GetSlim(DependencyObject d) => (bool)d.GetValue(SlimProperty);

    /// <summary>细滚动条样式：轨道 8DIP、滑块 4DIP 圆角胶囊、无箭头，竖/横两套模板
    /// （Track 方向必须与模板匹配，否则滑块拖不动）。附带 <see cref="SlimProperty"/> 压掉系统最小宽。</summary>
    /// <returns><c>TargetType=ScrollBar</c> 的隐式样式，可直接作 <c>Resources[typeof(ScrollBar)]</c> 的键值。</returns>
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

    /// <summary>主题化菜单资源字典：ContextMenu / MenuItem / Separator 三套样式。
    /// 菜单样式不进 <c>Application.Resources</c>（<see cref="ApplyGlobalStyles"/> 只注册 ToolTip 与 ScrollBar），
    /// 须逐个挂到 <c>ContextMenu.Resources</c>——托盘菜单与贴图右键菜单共用这一份。</summary>
    /// <returns>可直接赋给 <c>ContextMenu.Resources</c> 的字典；每次调用重新解析，返回新实例。</returns>
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
                      <!-- 外包透明 Border 右下留 24px：Popup 窗按模板 DesiredSize 开窗，
                           BackEase 过冲(>1.0)与投影会被窗缘裁掉，Q 弹弹到一半被切；留白后完整。
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
    #endregion

}
