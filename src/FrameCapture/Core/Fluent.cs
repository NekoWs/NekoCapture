// Fluent 交互细节：悬停淡入、按下回缩、滑杆/开关动画、窗口淡入、全局样式。
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace FrameCapture.Core;

public static class Fluent
{
    public const string IconFontChain = "Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol";

    static string Hex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    static readonly FontFamily IconFont = new(IconFontChain);

    // ---------------- 图标按钮：悬停淡入圆角高亮 + 按下回缩 ----------------

    public static Button IconButton(string glyph, string tip, double size, double fontSize = 15, Brush? fg = null)
    {
        var b = new Button
        {
            Content = glyph,
            Focusable = false,
            ToolTip = tip,
            Width = size,
            Height = size,
            Margin = new Thickness(2),
            FontFamily = IconFont,
            FontSize = fontSize,
            Foreground = fg ?? new SolidColorBrush(Theme.Text),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Template = IconButtonTemplate(),
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        b.RenderTransform = new ScaleTransform(1, 1);
        b.PreviewMouseLeftButtonDown += (_, _) => AnimateScale(b, 0.92, 70);
        b.MouseUp += (_, _) => AnimateScale(b, 1, 160);
        b.MouseLeave += (_, _) => AnimateScale(b, 1, 120);
        return b;
    }

    static void AnimateScale(UIElement el, double to, int ms)
    {
        if (el.RenderTransform is not ScaleTransform st) return;
        var dur = new Duration(TimeSpan.FromMilliseconds(ms));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        foreach (var p in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
        {
            var a = new DoubleAnimation(to, dur) { EasingFunction = ease };
            a.Freeze();
            st.BeginAnimation(p, a);
        }
    }

    public static ControlTemplate IconButtonTemplate()
    {
        string xaml = $"""
            <ControlTemplate TargetType="Button"
                             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid>
                <Border x:Name="bd" CornerRadius="7" Background="TBD"/>
                <Border x:Name="ho" CornerRadius="7" Background="{Hex(Theme.Hover)}" Opacity="0"/>
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
              </Grid>
              <ControlTemplate.Triggers>
                <EventTrigger RoutedEvent="UIElement.MouseEnter">
                  <BeginStoryboard><Storyboard>
                    <DoubleAnimation Storyboard.TargetName="ho" Storyboard.TargetProperty="Opacity"
                                     To="1" Duration="0:0:0.11"><DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseOut"/></DoubleAnimation.EasingFunction></DoubleAnimation>
                  </Storyboard></BeginStoryboard>
                </EventTrigger>
                <EventTrigger RoutedEvent="UIElement.MouseLeave">
                  <BeginStoryboard><Storyboard>
                    <DoubleAnimation Storyboard.TargetName="ho" Storyboard.TargetProperty="Opacity"
                                     To="0" Duration="0:0:0.16"/>
                  </Storyboard></BeginStoryboard>
                </EventTrigger>
                <Trigger Property="IsPressed" Value="True">
                  <Setter TargetName="ho" Property="Background" Value="PRESSCLR"/>
                </Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """;
        xaml = xaml.Replace("PRESSCLR", Hex(Theme.Press)).Replace("TBD", "{TemplateBinding Background}");
        var tpl = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
        tpl.Seal();
        return tpl;
    }

    // ---------------- 滑杆：细轨道 + 圆形滑块（悬停放大、光晕） ----------------

    public static void StyleSlider(Slider sl) => StyleSlider(sl, Theme.TrackBg, Theme.Accent);

    public static void StyleSlider(Slider sl, Color trackColor, Color accentColor)
    {
        string track = Hex(trackColor), acc = Hex(accentColor);
        string xaml = $"""
            <ControlTemplate TargetType="Slider"
                             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid VerticalAlignment="Center" Height="20">
                <Border Height="3" CornerRadius="2" Background="{track}"/>
                <Track x:Name="PART_Track">
                  <Track.DecreaseRepeatButton>
                    <RepeatButton Focusable="False">
                      <RepeatButton.Template>
                        <ControlTemplate TargetType="RepeatButton">
                          <Border Height="3" CornerRadius="2" Background="{acc}" Opacity="0.9"/>
                        </ControlTemplate>
                      </RepeatButton.Template>
                    </RepeatButton>
                  </Track.DecreaseRepeatButton>
                  <Track.IncreaseRepeatButton>
                    <RepeatButton Focusable="False">
                      <RepeatButton.Template>
                        <ControlTemplate TargetType="RepeatButton">
                          <Border Background="Transparent"/>
                        </ControlTemplate>
                      </RepeatButton.Template>
                    </RepeatButton>
                  </Track.IncreaseRepeatButton>
                  <Track.Thumb>
                    <Thumb x:Name="th" Focusable="False">
                      <Thumb.Template>
                        <ControlTemplate TargetType="Thumb">
                          <Grid>
                            <Ellipse x:Name="halo" Width="22" Height="22" Fill="{acc}" Opacity="0"/>
                            <Ellipse x:Name="dot" Width="13" Height="13" Fill="{acc}" Stroke="{track}" StrokeThickness="1"/>
                          </Grid>
                          <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                              <Trigger.EnterActions>
                                <BeginStoryboard><Storyboard>
                                  <DoubleAnimation Storyboard.TargetName="dot" Storyboard.TargetProperty="Width" To="16" Duration="0:0:0.12"><DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseOut"/></DoubleAnimation.EasingFunction></DoubleAnimation>
                                  <DoubleAnimation Storyboard.TargetName="dot" Storyboard.TargetProperty="Height" To="16" Duration="0:0:0.12"><DoubleAnimation.EasingFunction><CubicEase EasingMode="EaseOut"/></DoubleAnimation.EasingFunction></DoubleAnimation>
                                  <DoubleAnimation Storyboard.TargetName="halo" Storyboard.TargetProperty="Opacity" To="0.25" Duration="0:0:0.18"/>
                                </Storyboard></BeginStoryboard>
                              </Trigger.EnterActions>
                              <Trigger.ExitActions>
                                <BeginStoryboard><Storyboard>
                                  <DoubleAnimation Storyboard.TargetName="dot" Storyboard.TargetProperty="Width" To="13" Duration="0:0:0.14"/>
                                  <DoubleAnimation Storyboard.TargetName="dot" Storyboard.TargetProperty="Height" To="13" Duration="0:0:0.14"/>
                                  <DoubleAnimation Storyboard.TargetName="halo" Storyboard.TargetProperty="Opacity" To="0" Duration="0:0:0.14"/>
                                </Storyboard></BeginStoryboard>
                              </Trigger.ExitActions>
                            </Trigger>
                          </ControlTemplate.Triggers>
                        </ControlTemplate>
                      </Thumb.Template>
                    </Thumb>
                  </Track.Thumb>
                </Track>
              </Grid>
            </ControlTemplate>
            """;
        var tpl = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
        tpl.Seal();
        sl.Template = tpl;
        sl.Height = 20;
    }

    // ---------------- 开关：滑钮平滑滑动 + 轨道渐变色 ----------------

    public static void SlideToggle(FrameworkElement knob, Border track, bool on, double travel)
    {
        if (knob.RenderTransform is not TranslateTransform)
            knob.RenderTransform = new TranslateTransform();
        var tt = (TranslateTransform)knob.RenderTransform;
        tt.BeginAnimation(TranslateTransform.XProperty, null);
        tt.X = on ? travel : 0;
        var anim = new DoubleAnimation(on ? travel : 0.0, new Duration(TimeSpan.FromMilliseconds(150)))
        {
            EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut },
        };
        tt.BeginAnimation(TranslateTransform.XProperty, anim);
        if (track.Background is SolidColorBrush bgOld)
        {
            var bg = new SolidColorBrush(bgOld.Color);
            track.Background = bg;
            bg.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(on ? Theme.Accent : Theme.TrackBg, new Duration(TimeSpan.FromMilliseconds(150))));
        }
        if (track.BorderBrush is SolidColorBrush bbOld)
        {
            var bb = new SolidColorBrush(bbOld.Color);
            track.BorderBrush = bb;
            bb.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(on ? Theme.Accent : Theme.Stroke, new Duration(TimeSpan.FromMilliseconds(150))));
        }
        if (knob is Shape sh && sh.Fill is SolidColorBrush kfOld)
        {
            var kf = new SolidColorBrush(kfOld.Color);
            sh.Fill = kf;
            kf.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(on ? Color.FromRgb(0, 0, 0) : Theme.Text, new Duration(TimeSpan.FromMilliseconds(150))));
        }
    }

    // ---------------- 窗口淡入 ----------------

    public static void FadeIn(Window w, int ms = 160)
    {
        w.Opacity = 0;
        w.ContentRendered += (_, _) =>
        {
            var a = new DoubleAnimation(1, new Duration(TimeSpan.FromMilliseconds(ms)));
            w.BeginAnimation(UIElement.OpacityProperty, a);
        };
    }

    // ---------------- 工具按钮选中态：背景淡入 ----------------

    public static void HighlightBtn(Button b, bool on)
    {
        Color to = on ? Theme.Accent : Colors.Transparent;
        var holder = b.Background is SolidColorBrush cur ? new SolidColorBrush(cur.Color) : new SolidColorBrush(Colors.Transparent);
        b.Background = holder;
        holder.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, new Duration(TimeSpan.FromMilliseconds(120))));
        b.Foreground = new SolidColorBrush(on ? Color.FromRgb(0, 0, 0) : Theme.Text);
    }

    // ---------------- 芯片（颜色选择）：悬停放大 + 按下回缩 ----------------

    public static void ChipHover(FrameworkElement el, double scale = 1.18)
    {
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = new ScaleTransform(1, 1);
        el.MouseEnter += (_, _) => AnimateScale(el, scale, 120);
        el.MouseLeave += (_, _) => AnimateScale(el, 1, 140);
        el.MouseUp += (_, _) => AnimateScale(el, scale, 120);
        el.PreviewMouseLeftButtonDown += (_, _) => AnimateScale(el, 0.9, 70);
    }

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
                      <Border.Effect><DropShadowEffect BlurRadius="14" ShadowDepth="3" Opacity="0.35"/></Border.Effect>
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

    public static Style ScrollBarStyle()
    {
        string thumb = Hex(Theme.Stroke), thumbHi = Hex(Theme.Press);
        string sbXaml = $"""
            <Style TargetType="ScrollBar"
                   xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Setter Property="Background" Value="Transparent"/>
              <Setter Property="Width" Value="10"/>
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="ScrollBar">
                    <Grid Background="Transparent">
                      <Track x:Name="PART_Track" IsDirectionReversed="True">
                        <Track.DecreaseRepeatButton>
                          <RepeatButton Focusable="False">
                            <RepeatButton.Template>
                              <ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate>
                            </RepeatButton.Template>
                          </RepeatButton>
                        </Track.DecreaseRepeatButton>
                        <Track.IncreaseRepeatButton>
                          <RepeatButton Focusable="False">
                            <RepeatButton.Template>
                              <ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate>
                            </RepeatButton.Template>
                          </RepeatButton>
                        </Track.IncreaseRepeatButton>
                        <Track.Thumb>
                          <Thumb Focusable="False">
                            <Thumb.Template>
                              <ControlTemplate TargetType="Thumb">
                                <Border x:Name="tb" CornerRadius="4" Background="{thumb}" Margin="2,1,3,1"/>
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
              </Setter>
              <Style.Triggers>
                <Trigger Property="Orientation" Value="Horizontal">
                  <Setter Property="Width" Value="Auto"/>
                  <Setter Property="Height" Value="10"/>
                </Trigger>
              </Style.Triggers>
            </Style>
            """;
        var st = (Style)System.Windows.Markup.XamlReader.Parse(sbXaml);
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
                <Setter Property="MinWidth" Value="190"/>
                <Setter Property="Template">
                  <Setter.Value>
                    <ControlTemplate TargetType="ContextMenu">
                      <Border CornerRadius="8" Background="{Hex(Theme.CardBg)}"
                              BorderBrush="{Hex(Theme.Stroke)}" BorderThickness="1" Padding="1,5,1,5">
                        <Border.Effect><DropShadowEffect BlurRadius="16" ShadowDepth="4" Opacity="0.4"/></Border.Effect>
                        <ItemsPresenter/>
                      </Border>
                    </ControlTemplate>
                  </Setter.Value>
                </Setter>
              </Style>
              <Style TargetType="Separator">
                <Setter Property="Template">
                  <Setter.Value>
                    <ControlTemplate TargetType="Separator">
                      <Border Height="1" Background="{Hex(Theme.Stroke)}" Margin="10,5,10,5"/>
                    </ControlTemplate>
                  </Setter.Value>
                </Setter>
              </Style>
              <Style TargetType="MenuItem">
                <Setter Property="OverridesDefaultStyle" Value="True"/>
                <Setter Property="FontSize" Value="13"/>
                <Setter Property="Template">
                  <Setter.Value>
                    <ControlTemplate TargetType="MenuItem">
                      <Border x:Name="bd" CornerRadius="6" Background="Transparent">
                        <Grid Height="34">
                          <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="34"/>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="Auto"/>
                          </Grid.ColumnDefinitions>
                          <TextBlock x:Name="chk" Grid.Column="0" Text="" FontFamily="Segoe Fluent Icons"
                                     FontSize="12" HorizontalAlignment="Center" VerticalAlignment="Center"
                                     Foreground="{Hex(Theme.Accent)}"/>
                          <ContentPresenter Grid.Column="1" ContentSource="Header" VerticalAlignment="Center"/>
                        </Grid>
                      </Border>
                      <ControlTemplate.Triggers>
                        <Trigger Property="IsHighlighted" Value="True">
                          <Setter TargetName="bd" Property="Background" Value="{Hex(Theme.Hover)}"/>
                        </Trigger>
                        <Trigger Property="IsChecked" Value="True">
                          <Setter TargetName="chk" Property="Text" Value="&#xE73E;"/>
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
