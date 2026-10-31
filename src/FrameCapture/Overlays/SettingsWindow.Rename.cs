using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>快捷键与行为名称的内联编辑、焦点反馈和提交。</summary>
public sealed partial class SettingsWindow
{
    static FrameworkElement RenameBox(string current, Action<string> commit, Action cancel)
    {
        var stroke = new SolidColorBrush(Theme.StrokeField);
        var measure = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold };
        var host = new Grid { HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 320, ClipToBounds = true };
        var box = Fluent.Ui(new TextBox
        {
            Text = current, FontSize = 13, FontWeight = FontWeights.SemiBold,
            Background = Theme.CardBgBrush, BorderBrush = stroke, BorderThickness = new Thickness(1),
            CaretBrush = Theme.AccentBrush, Foreground = Theme.TextBrush,
            SelectionBrush = Theme.AccentBrush, SelectionOpacity = 0.3, SelectionTextBrush = Theme.TextBrush,
            Padding = new Thickness(8, 4, 8, 4), MinHeight = 32,
            VerticalContentAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch,
            FocusVisualStyle = null, ToolTip = Loc.T(K.Hotkeys.RenameThisHotkey),
            Template = (ControlTemplate)XamlReader.Parse("""
                <ControlTemplate TargetType="TextBox" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                  <Border CornerRadius="10" Background="{TemplateBinding Background}"
                      BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}">
                    <ScrollViewer x:Name="PART_ContentHost" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                        Margin="{TemplateBinding Padding}" VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                        Focusable="False" HorizontalScrollBarVisibility="Hidden" VerticalScrollBarVisibility="Hidden"/>
                  </Border>
                </ControlTemplate>
                """),
        });
        box.SetBinding(MaxWidthProperty, new System.Windows.Data.Binding(nameof(ActualWidth)) { Source = host });
        void Fit()
        {
            measure.Text = box.Text;
            measure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            host.Width = box.Width = Math.Clamp(measure.DesiredSize.Width + 18, 96, 320);
        }
        box.TextChanged += (_, _) => Fit(); Fit();
        host.Children.Add(box);
        InputMethod.SetIsInputMethodEnabled(box, true);
        void Paint()
        {
            var target = box.IsKeyboardFocusWithin ? Theme.Accent : box.IsMouseOver ? Theme.Stroke : Theme.StrokeField;
            stroke.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(target,
                TimeSpan.FromMilliseconds(Fluent.Motion.StateMs)));
        }
        box.MouseEnter += (_, _) => Paint(); box.MouseLeave += (_, _) => Paint();
        box.GotKeyboardFocus += (_, _) => Paint(); box.LostKeyboardFocus += (_, _) => Paint();
        box.MouseLeftButtonUp += (_, e) => e.Handled = true;
        bool done = false;
        void Save()
        {
            if (done) return;
            done = true;
            if (box.Text.Trim() == current.Trim()) cancel();
            else commit(box.Text);
        }
        box.Loaded += (_, _) =>
        {
            Theme.Changed -= Paint; Theme.Changed += Paint;
            box.Focus(); box.SelectAll(); Paint();
        };
        box.Unloaded += (_, _) => Theme.Changed -= Paint;
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Save(); e.Handled = true; }
            else if (e.Key == Key.Escape) { if (!done) { done = true; cancel(); } e.Handled = true; }
        };
        box.LostFocus += (_, _) => Save();
        return host;
    }
}
