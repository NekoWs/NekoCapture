using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace NekoCapture.Core;

/// <summary>业务界面交互控件的统一入口；原生控件仅作为模板与输入行为的底层。</summary>
public static partial class Fluent
{
    private static readonly ConditionalWeakTable<Control, object> UiThemeControls = new();

    /// <summary>创建使用当前圆角模板的按钮，保留专用组件指定的模板。</summary>
    /// <param name="control">调用方提供的内容与布局参数。</param>
    /// <returns>已应用当前控件样式的按钮。</returns>
    public static Button Ui(Button control)
    {
        if (control.ReadLocalValue(Control.ForegroundProperty) == DependencyProperty.UnsetValue) control.Foreground = Theme.TextBrush;
        if (control.ReadLocalValue(Control.BackgroundProperty) == DependencyProperty.UnsetValue) control.Background = Theme.CardBgBrush;
        if (control.ReadLocalValue(Control.BorderBrushProperty) == DependencyProperty.UnsetValue) control.BorderBrush = Theme.StrokeFieldBrush;
        if (control.ReadLocalValue(Control.TemplateProperty) == DependencyProperty.UnsetValue)
        {
            var template = RoundedButtonTemplate(false);
            template.VisualTree.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            template.VisualTree.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            template.VisualTree.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            control.Template = template; ChipHover(control);
        }
        control.Cursor = Cursors.Hand;
        return control;
    }

    /// <summary>创建无系统装饰的文本输入，外部容器负责边框与焦点动效。</summary>
    /// <param name="control">调用方提供的文本、字体与布局参数。</param>
    /// <returns>使用当前输入模板的文本框。</returns>
    public static TextBox Ui(TextBox control)
    {
        if (control.ReadLocalValue(Control.TemplateProperty) == DependencyProperty.UnsetValue)
            control.Template = (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate TargetType="TextBox" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="{TemplateBinding BorderThickness}" Padding="{TemplateBinding Padding}" CornerRadius="10">
                <ScrollViewer x:Name="PART_ContentHost" Focusable="False"
                    VerticalContentAlignment="{TemplateBinding VerticalContentAlignment}"
                    HorizontalContentAlignment="{TemplateBinding HorizontalContentAlignment}"
                    HorizontalScrollBarVisibility="{TemplateBinding HorizontalScrollBarVisibility}"
                    VerticalScrollBarVisibility="{TemplateBinding VerticalScrollBarVisibility}"/>
              </Border>
            </ControlTemplate>
            """);
        control.FocusVisualStyle = null;
        control.SelectionBrush = Theme.AccentBrush; control.SelectionOpacity = 0.3;
        if (control.ReadLocalValue(TextBox.CaretBrushProperty) == DependencyProperty.UnsetValue) control.CaretBrush = Theme.TextBrush;
        InputMethod.SetIsInputMethodEnabled(control, true);
        return control;
    }

    /// <summary>创建使用当前细轨道与圆柄模板的滑杆。</summary>
    /// <param name="control">滑杆范围、步长与布局参数。</param>
    /// <returns>使用当前模板的滑杆。</returns>
    public static Slider Ui(Slider control) { StyleSlider(control); return control; }

    /// <summary>创建使用当前菜单资源的右键菜单。</summary>
    /// <param name="control">菜单行为与布局参数。</param>
    /// <returns>使用当前圆角菜单模板的菜单。</returns>
    public static ContextMenu Ui(ContextMenu control)
    {
        if (!control.Resources.Contains(typeof(MenuItem))) control.Resources = MenuResources();
        return control;
    }

    /// <summary>创建使用当前勾选样式的复选框。</summary>
    /// <param name="control">选项内容与布局参数。</param>
    /// <returns>使用当前模板并随配色更新的复选框。</returns>
    public static CheckBox Ui(CheckBox control)
    {
        void Paint() => StyleUiCheckBox(control);
        WireUiTheme(control, Paint); return control;
    }

    /// <summary>创建圆角进度条，忙碌状态使用呼吸动效。</summary>
    /// <param name="control">进度范围与布局参数。</param>
    /// <returns>使用当前模板的进度条。</returns>
    public static ProgressBar Ui(ProgressBar control)
    {
        if (control.ReadLocalValue(Control.ForegroundProperty) == DependencyProperty.UnsetValue) control.Foreground = Theme.AccentBrush;
        if (control.ReadLocalValue(Control.BackgroundProperty) == DependencyProperty.UnsetValue) control.Background = Theme.TrackBgBrush;
        control.Template = UiProgressTemplate();
        return control;
    }

    private static void WireUiTheme(Control control, Action paint)
    {
        paint();
        if (UiThemeControls.TryGetValue(control, out _)) return;
        UiThemeControls.Add(control, new object());
        control.Loaded += (_, _) => { Theme.Changed -= paint; Theme.Changed += paint; paint(); };
        control.Unloaded += (_, _) => Theme.Changed -= paint;
    }
}
