using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GestureEditor;

/// <summary>
/// 跟随 <see cref="EditorPalette"/> 的控件样式（R122）。WPF 默认 ComboBox/ListBox 模板是浅色系统色，
/// 深色下文字与背景几乎同色不可读——这里用 XamlReader 现拼一套配色模板套上去，明/暗都正确。
/// 库自带、不依赖宿主 Fluent（保持可复用）。
/// </summary>
public static class EditorStyles
{
    const string Pres = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    const string X = "http://schemas.microsoft.com/winfx/2006/xaml";
    static string Hex(Color c) => c.ToString(); // Color.ToString 已是 #AARRGGBB

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
        // R161：弹层外包 16px 透明 Padding（Popup 负偏移补偿对齐）——过冲超出 1.0 的部分此前被 Popup
        // 边界裁掉，Q 弹变成「弹到一半被切」；留白后完整可见。
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

    public static void ApplyList(ListBox l, EditorPalette p)
    {
        l.Background = p.B(p.PanelBg);   // R161：列表底改 PanelBg，让 CardBg+描边的卡片浮出来（此前同色糊平）
        l.BorderBrush = p.B(p.Stroke);
        l.Foreground = p.B(p.Text);
        // R161：列表项改「卡片自绘」（选中/问题红框/禁用置灰由编辑器逐行刷），
        // ListBoxItem 本身退化为透明容器——否则其选中底色会垫在卡片后面糊成一片。
        l.Resources[typeof(ListBoxItem)] = PlainItemStyle(p, "ListBoxItem");
    }

    /// <summary>透明无装饰条目：悬停/选中底色交给卡片自己画（R161 卡片化列表）。</summary>
    static Style PlainItemStyle(EditorPalette p, string typeName) => (Style)XamlReader.Parse(
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

    /// <summary>调试用：返回条目样式 XAML 原文（供离线定位解析错误）。</summary>
    public static string ItemStyleXaml(EditorPalette p, string typeName, Color hover, bool selected)
    {
        string selTrigger = selected
            // R161：选中底=accent，前景按 accent 明度自适应（亮 accent→黑字、暗 accent→白字），
            // 此前写死黑字，用户选低明度主题色时选中项文字与底色糊在一起看不清。
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
}
