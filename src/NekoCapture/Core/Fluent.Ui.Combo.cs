using System.Windows.Controls;

namespace NekoCapture.Core;

/// <summary>业务下拉框统一使用当前滚动选择与数字滚轮模板。</summary>
public static partial class Fluent
{
    /// <summary>按输入能力选择当前下拉框模板。</summary>
    /// <param name="control">选项、输入能力与布局参数。</param>
    /// <returns>已接入弹层、选中文字滚动、数字滚轮与配色更新的下拉框。</returns>
    public static ComboBox Ui(ComboBox control)
    {
        void Paint() { if (control.IsEditable) StyleComboEditable(control); else StyleCombo(control); }
        WireUiTheme(control, Paint);
        return control;
    }
}
