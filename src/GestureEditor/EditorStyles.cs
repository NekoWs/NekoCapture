using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GestureEditor;

/// <summary>
/// 跟随 <see cref="EditorPalette"/> 的控件样式。WPF 默认 ComboBox/ListBox 模板是浅色系统色，
/// 深色下文字与背景几乎同色不可读——这里用 XamlReader 现拼一套配色模板套上去，明/暗都正确。
/// 库自带、不依赖宿主 Fluent（保持可复用）。
/// </summary>
public static class EditorStyles
{
    const string Pres = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    const string X = "http://schemas.microsoft.com/winfx/2006/xaml";
    static string Hex(Color c) => c.ToString(); // Color.ToString 已是 #AARRGGBB

    /// <summary>给 ComboBox 套上跟随调色板的模板：卡片底 + 描边 + 自绘下拉弹层，明/暗主题都可读。
    /// 每次展开弹层做一次 0.94→1.0 的 BackEase 缩放；同一实例重复调用会再挂一个展开动画处理器，只应调一次。</summary>
    /// <param name="c">目标下拉框，就地改其 Foreground/Background/BorderBrush/Template 与条目样式资源。</param>
    /// <param name="p">配色来源。</param>
    public static void ApplyCombo(ComboBox c, EditorPalette p)
    {
        c.Foreground = p.B(p.Text);
        c.Background = p.B(p.CardBg);
        c.BorderBrush = p.B(p.Stroke);
        string xaml =
            $@"<ControlTemplate TargetType=""ComboBox"" xmlns=""{Pres}"" xmlns:x=""{X}"">
  <Grid>
    <ToggleButton Focusable=""False"" ClickMode=""Press""
        IsChecked=""{{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={{RelativeSource AncestorType={{x:Type ComboBox}}}}}}"">
      <ToggleButton.Template>
        <ControlTemplate TargetType=""ToggleButton"">
          <Border Background=""{Hex(p.CardBg)}"" BorderBrush=""{Hex(p.Stroke)}"" BorderThickness=""1"" CornerRadius=""6"" Padding=""10,7,10,7""
              TextElement.Foreground=""{Hex(p.Text)}"">
            <Grid>
              <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
              <ContentPresenter Grid.Column=""0"" VerticalAlignment=""Center""
                  Content=""{{Binding SelectionBoxItem, RelativeSource={{RelativeSource AncestorType={{x:Type ComboBox}}}}}}""
                  ContentTemplate=""{{Binding SelectionBoxItemTemplate, RelativeSource={{RelativeSource AncestorType={{x:Type ComboBox}}}}}}""/>
              <TextBlock Grid.Column=""1"" Text=""&#xE70D;"" FontFamily=""Segoe Fluent Icons, Segoe MDL2 Assets""
                  FontSize=""10"" Foreground=""{Hex(p.TextDim)}"" VerticalAlignment=""Center"" Margin=""6,0,0,0""/>
            </Grid>
          </Border>
        </ControlTemplate>
      </ToggleButton.Template>
    </ToggleButton>
    <Popup x:Name=""PART_Popup"" AllowsTransparency=""True"" Placement=""Bottom"" VerticalOffset=""-14"" HorizontalOffset=""-16""
        IsOpen=""{{TemplateBinding IsDropDownOpen}}"" PopupAnimation=""Fade"">
      <Border Padding=""16"">
        <Border Background=""{Hex(p.PanelBg)}"" BorderBrush=""{Hex(p.Stroke)}"" BorderThickness=""1"" CornerRadius=""6""
            MinWidth=""{{Binding ActualWidth, RelativeSource={{RelativeSource TemplatedParent}}}}"">
          <ScrollViewer MaxHeight=""260"" Padding=""2""><ItemsPresenter/></ScrollViewer>
        </Border>
      </Border>
    </Popup>
  </Grid>
</ControlTemplate>";
        c.Template = (ControlTemplate)XamlReader.Parse(xaml);
        // 下拉弹层弹性入场：每次展开从 0.94 缩放弹到 1（BackEase 轻微过冲），与宿主 ContextMenu 同款手感。
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
                var a = new DoubleAnimation(0.94, 1.0, new Duration(TimeSpan.FromMilliseconds(200)))
                { EasingFunction = new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut } };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
        };
        c.Resources[typeof(ComboBoxItem)] = ItemStyle(p, "ComboBoxItem", p.Hover, selected: false);
    }

    /// <summary>给 ListBox 套调色板配色，并把条目退化为透明容器：选中/问题红框/禁用置灰由编辑器逐行自绘，
    /// ListBoxItem 自带的选中底色若留着，会垫在卡片后面糊成一片。</summary>
    /// <param name="l">目标列表，就地改其 Background/BorderBrush/Foreground 与 ListBoxItem 样式资源。</param>
    /// <param name="p">配色来源。</param>
    public static void ApplyList(ListBox l, EditorPalette p)
    {
        l.Background = p.B(p.PanelBg);   // 列表底用 PanelBg，CardBg+描边的卡片才浮得出来
        l.BorderBrush = p.B(p.Stroke);
        l.Foreground = p.B(p.Text);
        l.Resources[typeof(ListBoxItem)] = PlainItemStyle("ListBoxItem");
    }

    /// <summary>透明无装饰条目：悬停/选中底色交给卡片自己画（卡片化列表）。</summary>
    static Style PlainItemStyle(string typeName) => (Style)XamlReader.Parse(
        $@"<Style TargetType=""{typeName}"" xmlns=""{Pres}"" xmlns:x=""{X}"">
  <Setter Property=""Padding"" Value=""0""/>
  <Setter Property=""Margin"" Value=""0""/>
  <Setter Property=""Background"" Value=""Transparent""/>
  <Setter Property=""Template"">
    <Setter.Value>
      <ControlTemplate TargetType=""{typeName}"">
        <Border Background=""Transparent"" Padding=""{{TemplateBinding Padding}}""><ContentPresenter/></Border>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>");

    /// <summary>条目样式：透明底 + 悬停高亮；selected=true 时选中用强调色底 + 反白字。</summary>
    static Style ItemStyle(EditorPalette p, string typeName, Color hover, bool selected)
    {
        return (Style)XamlReader.Parse(ItemStyleXaml(p, typeName, hover, selected));
    }

    /// <summary>生成条目样式 XAML 原文：<see cref="ItemStyle"/> 解析它；公开是为了能取出原文离线定位解析错误。</summary>
    /// <param name="p">配色来源：前景取 Text，选中底取 Accent，选中前景取 OnAccent。</param>
    /// <param name="typeName">样式的 TargetType 类型名，如 "ComboBoxItem"。</param>
    /// <param name="hover">悬停底色。</param>
    /// <param name="selected">是否生成选中触发器；false 时条目只有悬停高亮。</param>
    /// <returns>可直接交给 <c>XamlReader.Parse</c> 的 Style 文本。</returns>
    public static string ItemStyleXaml(EditorPalette p, string typeName, Color hover, bool selected)
    {
        string selTrigger = selected
            // 选中底=accent，前景按 accent 明度自适应（亮 accent→黑字、暗 accent→白字）：
            // 写死黑字会在低明度主题色下与底色糊在一起，选中项反而看不清。
            ? $@"<Trigger Property=""IsSelected"" Value=""True"">
                   <Setter TargetName=""b"" Property=""Background"" Value=""{Hex(p.Accent)}""/>
                   <Setter Property=""Foreground"" Value=""{Hex(p.OnAccent())}""/>
                 </Trigger>"
            : "";
        string xaml =
            $@"<Style TargetType=""{typeName}"" xmlns=""{Pres}"" xmlns:x=""{X}"">
  <Setter Property=""Foreground"" Value=""{Hex(p.Text)}""/>
  <Setter Property=""Padding"" Value=""9,7""/>
  <Setter Property=""HorizontalContentAlignment"" Value=""Stretch""/>
  <Setter Property=""Template"">
    <Setter.Value>
      <ControlTemplate TargetType=""{typeName}"">
        <Border x:Name=""b"" Background=""Transparent"" CornerRadius=""4"" Padding=""{{TemplateBinding Padding}}"">
          <ContentPresenter VerticalAlignment=""Center""/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsMouseOver"" Value=""True""><Setter TargetName=""b"" Property=""Background"" Value=""{Hex(hover)}""/></Trigger>
          {selTrigger}
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";
        return xaml;
    }

    /// <summary>同宿主 Fluent.Slim——主题 ScrollBar 样式的 MinWidth/MinHeight（系统滚动条宽）
    /// 经 DynamicResource 以直值泄漏到模板内 ScrollBar，样式 Setter 压不住；附加属性在样式应用时归零。</summary>
    public static readonly DependencyProperty SlimProperty = DependencyProperty.RegisterAttached(
        "Slim", typeof(bool), typeof(EditorStyles), new PropertyMetadata(false, (d, e) =>
        {
            if (d is ScrollBar sb && (bool)e.NewValue) { sb.MinWidth = 0; sb.MinHeight = 0; }
        }));
    /// <summary>附加属性写入器：置 true 时把该滚动条的 MinWidth/MinHeight 归零。</summary>
    /// <param name="d">目标依赖对象；只有 <see cref="ScrollBar"/> 且值为 true 时才动手，其余静默忽略。</param>
    /// <param name="v">是否瘦身。</param>
    public static void SetSlim(DependencyObject d, bool v) => d.SetValue(SlimProperty, v);

    /// <summary>读取 <see cref="SlimProperty"/> 的当前值。</summary>
    /// <param name="d">目标依赖对象。</param>
    /// <returns>未显式设置过时为 false。</returns>
    public static bool GetSlim(DependencyObject d) => (bool)d.GetValue(SlimProperty);

    /// <summary>细滚动条样式：轨道 8DIP、滑块 4DIP 圆角胶囊、无箭头，取色随调色板；竖/横两套模板
    /// （Track 方向必须与滚动条方向匹配）。挂到窗口 Resources[typeof(ScrollBar)] 即覆盖列表/详情/下拉弹层内所有滚动条。</summary>
    /// <param name="p">滑块与悬停高亮的取色来源。</param>
    /// <returns>已附带 <see cref="SlimProperty"/>=true 的 ScrollBar 样式，可直接放进资源字典。</returns>
    public static Style ScrollBarStyle(EditorPalette p)
    {
        string thumb = Hex(p.TextDim), thumbHi = Hex(p.Text);
        string Tmpl(string orient, string dirRev) =>
            $@"<Setter.Value>
  <ControlTemplate TargetType=""ScrollBar"">
    <Grid Background=""Transparent"">
      <Track x:Name=""PART_Track"" Orientation=""{orient}"" IsDirectionReversed=""{dirRev}"">
        <Track.DecreaseRepeatButton>
          <RepeatButton Focusable=""False"" IsTabStop=""False"">
            <RepeatButton.Template><ControlTemplate TargetType=""RepeatButton""><Border Background=""Transparent""/></ControlTemplate></RepeatButton.Template>
          </RepeatButton>
        </Track.DecreaseRepeatButton>
        <Track.IncreaseRepeatButton>
          <RepeatButton Focusable=""False"" IsTabStop=""False"">
            <RepeatButton.Template><ControlTemplate TargetType=""RepeatButton""><Border Background=""Transparent""/></ControlTemplate></RepeatButton.Template>
          </RepeatButton>
        </Track.IncreaseRepeatButton>
        <Track.Thumb>
          <Thumb Focusable=""False"" IsTabStop=""False"">
            <Thumb.Template>
              <ControlTemplate TargetType=""Thumb"">
                <Border x:Name=""tb"" CornerRadius=""2"" Background=""{thumb}"" Margin=""2""/>
                <ControlTemplate.Triggers>
                  <Trigger Property=""IsMouseOver"" Value=""True""><Setter TargetName=""tb"" Property=""Background"" Value=""{thumbHi}""/></Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </Thumb.Template>
          </Thumb>
        </Track.Thumb>
      </Track>
    </Grid>
  </ControlTemplate>
</Setter.Value>";
        var st = (Style)XamlReader.Parse(
            $@"<Style TargetType=""ScrollBar"" xmlns=""{Pres}"" xmlns:x=""{X}"">
  <Setter Property=""Background"" Value=""Transparent""/>
  <Setter Property=""Width"" Value=""8""/>
  <Setter Property=""Template"">{Tmpl("Vertical", "True")}</Setter>
  <Style.Triggers>
    <Trigger Property=""Orientation"" Value=""Horizontal"">
      <Setter Property=""Width"" Value=""Auto""/>
      <Setter Property=""Height"" Value=""8""/>
      <Setter Property=""Template"">{Tmpl("Horizontal", "False")}</Setter>
    </Trigger>
  </Style.Triggers>
</Style>");
        st.Setters.Add(new Setter(SlimProperty, true));
        return st;
    }
}
