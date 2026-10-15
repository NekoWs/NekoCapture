// Fluent partial：下拉框配色模板。WPF 默认 ComboBox 模板是浅色系统色，深色主题下文字与背景几乎同色。
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FrameCapture.Core;

/// <summary>Fluent 的下拉框 partial：现拼一套配色模板套上去，明暗主题都可读，弹层带弹性入场。</summary>
public static partial class Fluent
{
    const string XamlPres = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    const string XamlX = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>给下拉框套上跟随主题的模板：卡片底 + 描边 + 自绘弹层，明暗主题都可读。
    /// 每次展开弹层做一次 0.94→1.0 的 BackEase 缩放；同一实例重复调用会再挂一个展开动画处理器，只应调一次。</summary>
    /// <param name="c">目标下拉框，就地改其前景、底色、描边、模板与条目样式资源。</param>
    public static void StyleCombo(ComboBox c)
    {
        string hexCard = Theme.CardBg.ToString(), hexStroke = Theme.Stroke.ToString();
        string hexText = Theme.Text.ToString(), hexDim = Theme.TextDim.ToString();
        string hexPanel = Theme.PanelBg.ToString(), hexHover = Theme.Hover.ToString();
        string hexAccent = Theme.Accent.ToString(), hexOnAccent = OnAccentColor(Theme.Accent).ToString();
        c.Foreground = new SolidColorBrush(Theme.Text);
        c.Background = new SolidColorBrush(Theme.CardBg);
        c.BorderBrush = new SolidColorBrush(Theme.Stroke);
        string xaml =
            $@"<ControlTemplate TargetType=""ComboBox"" xmlns=""{XamlPres}"" xmlns:x=""{XamlX}"">
  <Grid>
    <ToggleButton Focusable=""False"" ClickMode=""Press""
        IsChecked=""{{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={{RelativeSource AncestorType={{x:Type ComboBox}}}}}}"">
      <ToggleButton.Template>
        <ControlTemplate TargetType=""ToggleButton"">
          <Border Background=""{hexCard}"" BorderBrush=""{hexStroke}"" BorderThickness=""1"" CornerRadius=""6"" Padding=""10,7,10,7""
              TextElement.Foreground=""{hexText}"">
            <Grid>
              <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
              <ContentPresenter Grid.Column=""0"" VerticalAlignment=""Center""
                  Content=""{{Binding SelectionBoxItem, RelativeSource={{RelativeSource AncestorType={{x:Type ComboBox}}}}}}""
                  ContentTemplate=""{{Binding SelectionBoxItemTemplate, RelativeSource={{RelativeSource AncestorType={{x:Type ComboBox}}}}}}""/>
              <TextBlock Grid.Column=""1"" Text=""&#xE70D;"" FontFamily=""Segoe Fluent Icons, Segoe MDL2 Assets""
                  FontSize=""10"" Foreground=""{hexDim}"" VerticalAlignment=""Center"" Margin=""6,0,0,0""/>
            </Grid>
          </Border>
        </ControlTemplate>
      </ToggleButton.Template>
    </ToggleButton>
    <Popup x:Name=""PART_Popup"" AllowsTransparency=""True"" Placement=""Bottom"" VerticalOffset=""-14"" HorizontalOffset=""-16""
        IsOpen=""{{TemplateBinding IsDropDownOpen}}"" PopupAnimation=""Fade"">
      <Border Padding=""16"">
        <Border Background=""{hexPanel}"" BorderBrush=""{hexStroke}"" BorderThickness=""1"" CornerRadius=""6""
            MinWidth=""{{Binding ActualWidth, RelativeSource={{RelativeSource TemplatedParent}}}}"">
          <ScrollViewer MaxHeight=""260"" Padding=""2""><ItemsPresenter/></ScrollViewer>
        </Border>
      </Border>
    </Popup>
  </Grid>
</ControlTemplate>";
        c.Template = (ControlTemplate)XamlReader.Parse(xaml);
        // 弹层外包 16px 透明 Padding（Popup 负偏移补偿对齐）：过冲超出 1.0 的那部分会被 Popup 边界裁掉，
        // 留白把它画进去，Q 弹才完整可见。
        c.DropDownOpened += (_, _) =>
        {
            if (c.Template?.FindName("PART_Popup", c) is Popup pop
                && pop.Child is Border outer && outer.Child is Border dd)
            {
                var st = new ScaleTransform(0.94, 0.94);
                dd.RenderTransformOrigin = new Point(0, 0);
                dd.RenderTransform = st;
                var a = new DoubleAnimation(0.94, 1.0, new Duration(TimeSpan.FromMilliseconds(Motion.EnterMs)))
                { EasingFunction = new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut } };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
        };
        // 条目：透明底 + 悬停高亮；选中底 = accent 实心，前景按 accent 明度自适应（亮 accent→黑字、暗 accent→白字）。
        c.Resources[typeof(ComboBoxItem)] = (Style)XamlReader.Parse(
            $@"<Style TargetType=""ComboBoxItem"" xmlns=""{XamlPres}"" xmlns:x=""{XamlX}"">
  <Setter Property=""Foreground"" Value=""{hexText}""/>
  <Setter Property=""Padding"" Value=""9,7""/>
  <Setter Property=""HorizontalContentAlignment"" Value=""Stretch""/>
  <Setter Property=""Template"">
    <Setter.Value>
      <ControlTemplate TargetType=""ComboBoxItem"">
        <Border x:Name=""b"" Background=""Transparent"" CornerRadius=""4"" Padding=""{{TemplateBinding Padding}}"">
          <ContentPresenter VerticalAlignment=""Center""/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsMouseOver"" Value=""True""><Setter TargetName=""b"" Property=""Background"" Value=""{hexHover}""/></Trigger>
          <Trigger Property=""IsSelected"" Value=""True"">
            <Setter TargetName=""b"" Property=""Background"" Value=""{hexAccent}""/>
            <Setter Property=""Foreground"" Value=""{hexOnAccent}""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>");
    }
}
