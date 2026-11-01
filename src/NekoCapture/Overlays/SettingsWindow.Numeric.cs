using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using NekoCapture.Core;

namespace NekoCapture.Overlays;

/// <summary>数值候选在 WPF 完成选择同步后提交，文本输入独立提交。</summary>
public sealed partial class SettingsWindow
{
    private Action BindNumericField(ComboBox combo, Func<double> read, Action<double> save,
        Func<string, double?> parse, Func<double, string> format, Action<double>? painted = null)
    {
        bool syncing = false, chose = false;
        double? pending = null;
        int generation = 0;
        double? Choice(object? item) => item is ComboBoxItem { Tag: double value } ? value
            : parse((item is ComboBoxItem choice ? choice.Content : item)?.ToString() ?? "");
        void Show()
        {
            syncing = true;
            double value = read();
            combo.SelectedItem = combo.Items.Cast<object>().FirstOrDefault(item => Choice(item) is double candidate && Math.Abs(candidate - value) < 0.0001);
            combo.Text = format(value); painted?.Invoke(value);
            syncing = false;
        }
        void Typed()
        {
            if (syncing || pending != null || combo.IsDropDownOpen) return;
            if (parse(combo.Text) is double value && Math.Abs(value - read()) > 0.0001) save(value);
            Show();
        }
        combo.DropDownOpened += (_, _) => chose = false;
        combo.SelectionChanged += (_, e) =>
        {
            if (syncing || e.OriginalSource != combo || Choice(combo.SelectedItem) is not double value) return;
            pending = value; chose = true;
            Fluent.PrepareComboNumber(combo);
            int request = ++generation;
            string previous = format(read());
            combo.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (request != generation || pending is not double selected) return;
                pending = null;
                if (Math.Abs(selected - read()) > 0.0001) save(selected);
                Show(); Fluent.RollComboNumber(combo, previous, format(read()));
            });
        };
        combo.DropDownClosed += (_, _) => { if (!chose) Typed(); };
        combo.LostKeyboardFocus += (_, e) =>
        {
            if (e.NewFocus is System.Windows.DependencyObject target && SettingsInputContains(combo, target)) return;
            Typed();
        };
        combo.PreviewTextInput += (_, _) => { generation++; pending = null; chose = false; };
        combo.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { generation++; pending = null; Show(); combo.IsDropDownOpen = false; e.Handled = true; }
            else if (e.Key == Key.Enter) { Typed(); combo.IsDropDownOpen = false; e.Handled = true; }
        };
        Show(); return Show;
    }
}
