using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

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
    <Popup AllowsTransparency=""True"" Placement=""Bottom"" VerticalOffset=""2""
        IsOpen=""{{TemplateBinding IsDropDownOpen}}"" PopupAnimation=""Fade"">
      <Border Background=""{Hex(p.PanelBg)}"" BorderBrush=""{Hex(p.Stroke)}"" BorderThickness=""1"" CornerRadius=""6""
          MinWidth=""{{Binding ActualWidth, RelativeSource={{RelativeSource TemplatedParent}}}}"">
        <ScrollViewer MaxHeight=""260"" Padding=""2""><ItemsPresenter/></ScrollViewer>
      </Border>
    </Popup>
  </Grid>
</ControlTemplate>";
        c.Template = (ControlTemplate)XamlReader.Parse(xaml);
        c.Resources[typeof(ComboBoxItem)] = ItemStyle(p, "ComboBoxItem", p.Hover, selected: false);
    }

    public static void ApplyList(ListBox l, EditorPalette p)
    {
        l.Background = p.B(p.CardBg);
        l.BorderBrush = p.B(p.Stroke);
        l.Foreground = p.B(p.Text);
        l.Resources[typeof(ListBoxItem)] = ItemStyle(p, "ListBoxItem", p.Hover, selected: true);
    }

    /// <summary>条目样式：透明底 + 悬停高亮；selected=true 时选中用强调色底 + 反白字。</summary>
    static Style ItemStyle(EditorPalette p, string typeName, Color hover, bool selected)
    {
        return (Style)XamlReader.Parse(ItemStyleXaml(p, typeName, hover, selected));
    }

    /// <summary>调试用：返回条目样式 XAML 原文（供离线定位解析错误）。</summary>
    public static string ItemStyleXaml(EditorPalette p, string typeName, Color hover, bool selected)
    {
        string selTrigger = selected
            ? $@"<Trigger Property=""IsSelected"" Value=""True"">
                   <Setter TargetName=""b"" Property=""Background"" Value=""{Hex(p.Accent)}""/>
                   <Setter Property=""Foreground"" Value=""#FF000000""/>
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
