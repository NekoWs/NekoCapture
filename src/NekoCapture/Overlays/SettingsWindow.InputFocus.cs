using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace NekoCapture.Overlays;

/// <summary>文本输入区域保留焦点，点击设置空白处结束输入。</summary>
public sealed partial class SettingsWindow
{
    private static DependencyObject? SettingsInputParent(DependencyObject node)
    {
        if (node is ComboBoxItem item && ItemsControl.ItemsControlFromItemContainer(item) is ComboBox combo) return combo;
        return node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
    }

    private static bool SettingsInputContains(DependencyObject container, DependencyObject? node)
    {
        for (; node != null; node = SettingsInputParent(node))
            if (ReferenceEquals(container, node)) return true;
        return false;
    }

    private void OnSettingsBackgroundMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.FocusedElement is not TextBoxBase editing || e.OriginalSource is not DependencyObject source) return;
        DependencyObject boundary = editing;
        if (ReferenceEquals(editing, _settingsSearchBox))
        {
            for (var node = SettingsInputParent(editing); node != null; node = SettingsInputParent(node))
                if (node is Border { Tag: "settings-search-field" }) { boundary = node; break; }
        }
        else
        {
            for (var node = SettingsInputParent(editing); node != null; node = SettingsInputParent(node))
                if (node is ComboBox) { boundary = node; break; }
        }
        if (SettingsInputContains(boundary, source)) return;
        for (var node = source; node != null; node = SettingsInputParent(node))
            if (node is TextBoxBase or ComboBox or MenuItem || node is ButtonBase { Focusable: true }) return;
        FocusManager.SetFocusedElement(this, this);
        Keyboard.Focus(this);
    }
}
