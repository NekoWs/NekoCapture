// Fluent partial：下拉框配色模板。WPF 默认 ComboBox 模板是浅色系统色，深色主题下文字与背景几乎同色。
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FrameCapture.Core;

/// <summary>Fluent 的下拉框 partial：现拼一套配色模板套上去，明暗主题都可读，弹层带弹性入场。
/// 模板里的颜色是拼字符串时写死进去的，换配色后调用方要重新调一次才跟得上。</summary>
public static partial class Fluent
{
    const string XamlPres = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    const string XamlX = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>给下拉框套上跟随主题的模板：卡片底 + 描边 + 自绘弹层，明暗主题都可读。
    /// 每次展开弹层做一次 0.94→1.0 的 BackEase 缩放，条目再逐条淡入。
    /// 可重复调用：换配色后重新套一遍即可，展开动画只挂一次。</summary>
    /// <param name="c">目标下拉框，就地改其前景、底色、描边、模板与条目样式资源。</param>
    public static void StyleCombo(ComboBox c) => ComboSkin(c, editable: false);

    /// <summary>可输入下拉框的模板：左侧是一枚可以打字的文本框，右侧是展开箭头，候选条目照样从下方落下来。
    /// 调用方须先置 <c>IsEditable = true</c>——模板里的 <c>PART_EditableTextBox</c> 只在可编辑时才被接管。</summary>
    /// <param name="c">目标下拉框，就地改其模板与条目样式资源。</param>
    public static void StyleComboEditable(ComboBox c)
    {
        c.IsEditable = true;
        c.IsTextSearchEnabled = false;
        c.StaysOpenOnEdit = true;
        ComboSkin(c, editable: true);
    }

    /// <summary>两种下拉框共用的配色与弹层：可编辑的那种把内容区换成文本框，其余一模一样。</summary>
    /// <param name="c">目标下拉框。</param>
    /// <param name="editable">true = 内容区是一枚可输入的文本框。</param>
    static void ComboSkin(ComboBox c, bool editable)
    {
        string hexCard = Theme.CardBg.ToString(), hexStroke = Theme.Stroke.ToString(), hexField = Theme.StrokeField.ToString();
        string hexText = Theme.Text.ToString(), hexDim = Theme.TextDim.ToString();
        string hexPanel = Theme.PanelBg.ToString(), hexHover = Theme.Hover.ToString();
        string hexAccent = Theme.Accent.ToString(), hexOnAccent = OnAccentColor(Theme.Accent).ToString();
        c.Foreground = Theme.TextBrush;
        c.Background = Theme.CardBgBrush;
        c.BorderBrush = Theme.StrokeFieldBrush;
        // 可编辑的那种：文本框占满左列，箭头是一枚只负责开关弹层的按钮。
        // 文本框自己不带底色与描边，圆角与描边由外层这一圈画，聚焦时整圈转强调色。
        string content = editable
            ? $@"<TextBox x:Name=""PART_EditableTextBox"" Grid.Column=""0"" Background=""Transparent"" BorderThickness=""0""
        Foreground=""{hexText}"" CaretBrush=""{hexAccent}"" FontSize=""12.5"" Height=""28""
        Padding=""9,0,0,0"" VerticalContentAlignment=""Center""/>"
            : $@"<ContentPresenter Grid.Column=""0"" Margin=""10,0,0,0"" VerticalAlignment=""Center""
        Content=""{{Binding SelectionBoxItem, RelativeSource={{RelativeSource AncestorType={{x:Type ComboBox}}}}}}""
        ContentTemplate=""{{Binding SelectionBoxItemTemplate, RelativeSource={{RelativeSource AncestorType={{x:Type ComboBox}}}}}}""/>";
        string xaml =
            $@"<ControlTemplate TargetType=""ComboBox"" xmlns=""{XamlPres}"" xmlns:x=""{XamlX}"">
  <Grid>
    <Border x:Name=""root"" Background=""{hexCard}"" BorderBrush=""{hexField}"" BorderThickness=""1"" CornerRadius=""6""
        TextElement.Foreground=""{hexText}"">
      <Grid>
        <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
        {content}
        <ToggleButton Grid.Column=""1"" Focusable=""False"" ClickMode=""Press""
            IsChecked=""{{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={{RelativeSource AncestorType={{x:Type ComboBox}}}}}}"">
          <ToggleButton.Template>
            <ControlTemplate TargetType=""ToggleButton"">
              <Border Background=""Transparent"" Padding=""9,9,9,9"">
                <TextBlock Text=""&#xE70D;"" FontFamily=""Segoe Fluent Icons, Segoe MDL2 Assets""
                    FontSize=""10"" Foreground=""{hexDim}"" VerticalAlignment=""Center""/>
              </Border>
            </ControlTemplate>
          </ToggleButton.Template>
        </ToggleButton>
      </Grid>
    </Border>
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
  <ControlTemplate.Triggers>
    <Trigger Property=""IsKeyboardFocusWithin"" Value=""True"">
      <Setter TargetName=""root"" Property=""BorderBrush"" Value=""{hexAccent}""/>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";
        c.Template = (ControlTemplate)XamlReader.Parse(xaml);
        WireDropDown(c);
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

    /// <summary>已经挂过展开动画的下拉框。模板可以重套，展开动画只该挂一次。</summary>
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ComboBox, object> ComboWired = new();

    /// <summary>给下拉框挂上展开那一段：弹层缩放 + 条目逐条淡入。</summary>
    /// <param name="c">目标下拉框。</param>
    static void WireDropDown(ComboBox c)
    {
        if (ComboWired.TryGetValue(c, out _)) return;
        ComboWired.Add(c, new object());
        c.DropDownOpened += (_, _) => PopDropDown(c);
        c.DropDownClosed += (_, _) => ResetItems(c, 1);
    }

    /// <summary>弹层入场：卡片从 0.94 弹到 1，一帧之后条目按序淡入。
    /// 条目容器在弹层开出来的那一帧才生成，当场数常常一个都没有，所以排队到下一渲染节拍再数。</summary>
    /// <param name="c">正在展开的下拉框。</param>
    static void PopDropDown(ComboBox c)
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
        c.Dispatcher.BeginInvoke(new Action(() => FadeItems(c)),
            System.Windows.Threading.DispatcherPriority.Render);
    }

    /// <summary>候选条目逐条淡入：第 i 条比第一条晚 <c>14ms × i</c> 起，整列落下来才读得出先后。
    /// 只动透明度不动位移——条目离开弹层时位移动画不会有人来收尾，残留的位移会让下次展开的条目整体偏一截。</summary>
    /// <param name="c">刚展开的下拉框。</param>
    static void FadeItems(ComboBox c)
    {
        for (int i = 0; i < c.Items.Count; i++)
        {
            if (c.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement fe) continue;
            fe.BeginAnimation(UIElement.OpacityProperty, null);
            fe.Opacity = 0;
            fe.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1,
                new Duration(TimeSpan.FromMilliseconds(Motion.QuickMs)))
            { BeginTime = TimeSpan.FromMilliseconds(14 * i) });
        }
    }

    /// <summary>收起之后把条目的透明度放回全亮：淡入被打断在半夜时，条目会停在半透明上，
    /// 下一次展开就是从一个更暗的起点再淡一遍。</summary>
    /// <param name="c">刚收起或刚重套模板的下拉框。</param>
    /// <param name="to">要落回的透明度。</param>
    static void ResetItems(ComboBox c, double to)
    {
        for (int i = 0; i < c.Items.Count; i++)
        {
            if (c.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement fe) continue;
            fe.BeginAnimation(UIElement.OpacityProperty, null);
            fe.Opacity = to;
        }
    }
}
