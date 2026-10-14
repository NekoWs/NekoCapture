using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FrameCapture.Core;

namespace FrameCapture.Setup;

/// <summary>
/// 一行确认：问题 + 一句后果 + 「确定」「取消」。卸载前问一次，问完才去要管理员权限。
/// 样式与主窗口同源（同一套 Fluent 源文件），不引入系统对话框的观感。
/// </summary>
internal static class ConfirmDialog
{
    /// <summary>弹出模态确认框。</summary>
    /// <param name="owner">父窗口，对话框居中于它。</param>
    /// <param name="question">问题正文。</param>
    /// <param name="detail">一句后果说明，让用户知道「确定」会动到哪些东西；传 null 则不出这行。</param>
    /// <returns>true = 用户点了「确定」。</returns>
    internal static bool Ask(Window owner, string question, string? detail)
    {
        // ShowDialog 返回时窗口已随 DialogResult 关闭，这里不需要再处置什么
        var dlg = new ConfirmWindow(owner, question, detail);
        return dlg.ShowDialog() == true;
    }

    /// <summary>确认框本体。Enter 等价「确定」、Esc 等价「取消」，两条键路都有落点。</summary>
    sealed class ConfirmWindow : Window
    {
        /// <summary>建确认框。</summary>
        /// <param name="owner">父窗口。</param>
        /// <param name="question">问题正文。</param>
        /// <param name="detail">后果说明。</param>
        internal ConfirmWindow(Window owner, string question, string? detail)
        {
            Title = "FrameCapture";
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Width = 420;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            // 背板要在 BorderlessNative 之后申请，与主窗口同一顺序
            Fluent.BorderlessNative(this);
            var mica = Fluent.MicaBackdrop(this);
            Background = mica ? Brushes.Transparent : new SolidColorBrush(Theme.WindowBg);

            var body = new StackPanel { Margin = new Thickness(24, 20, 24, 18) };
            body.Children.Add(new TextBlock
            {
                Text = question,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Theme.Text),
            });
            if (!string.IsNullOrEmpty(detail))
                body.Children.Add(new TextBlock
                {
                    Text = detail,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 7, 0, 0),
                    Foreground = new SolidColorBrush(Theme.TextDim),
                });

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0),
            };
            var ok = Fluent.RoundButton("确定", Theme.Accent, Theme.WindowBg, 20, true);
            ok.IsDefault = true;
            ok.Click += (_, _) => DialogResult = true;
            buttons.Children.Add(ok);

            var no = Fluent.RoundButton("取消", Theme.CardBg, Theme.Text, 18, true);
            no.Margin = new Thickness(10, 0, 0, 0);
            no.IsCancel = true;
            no.Click += (_, _) => DialogResult = false;
            buttons.Children.Add(no);

            body.Children.Add(buttons);
            Content = body;
            Fluent.FadeIn(this);
        }
    }
}
