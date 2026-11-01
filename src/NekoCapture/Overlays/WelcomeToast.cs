using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NekoCapture.Core;
using NekoCapture.Native;

namespace NekoCapture.Overlays;

/// <summary>启动就绪提示，不抢焦点；按配置自动消失，悬停暂停计时。</summary>
public sealed class WelcomeToast : Window
{
    static WelcomeToast? _current;
    readonly DispatcherTimer _dismiss;
    bool _closing;

    /// <summary>在 UI 线程显示就绪提示，同时只保留一张。</summary>
    /// <param name="captureHotkey">当前截图键位。</param>
    /// <param name="recordHotkey">当前录制键位。</param>
    public static void Show(string captureHotkey, string recordHotkey)
    {
        _current?.Close();
        _current = new WelcomeToast(captureHotkey, recordHotkey);
        _current.Show();
    }

    WelcomeToast(string captureHotkey, string recordHotkey)
    {
        Title = "NekoCapture Welcome";
        Fluent.BorderlessOverlay(this);
        var card = new Border
        {
            MinWidth = 244, MaxWidth = 372,
            Background = Theme.CardBgBrush, BorderBrush = Fluent.CardEdge(),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 14, 12, 14),
        };
        var root = new StackPanel();
        Grid.SetIsSharedSizeScope(root, true);
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new TextBlock
        {
            Text = Loc.T(K.Capture.NekoCaptureIsReady), FontSize = 15, FontWeight = FontWeights.Bold,
            Foreground = Theme.TextBrush, FontFamily = new FontFamily("Segoe UI"), VerticalAlignment = VerticalAlignment.Center,
        });
        var close = Fluent.IconButton("\uE711", Loc.T(K.Common.Close), 26, 12, Theme.TextDimBrush);
        close.Click += (_, _) => Dismiss();
        Grid.SetColumn(close, 1); head.Children.Add(close); root.Children.Add(head);
        if (SettingsService.Shared?.Current.WelcomeShowsHotkeys ?? true)
        {
            root.Children.Add(HotkeyLine(Loc.T(K.Common.Capture), captureHotkey));
            root.Children.Add(HotkeyLine(Loc.T(K.Common.Recording), recordHotkey));
        }
        card.Child = root; Content = card;
        ToastPlacement.Attach(this, card);
        ContentRendered += (_, _) => Fluent.PopIn(card);
        SourceInitialized += (_, _) =>
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Win32.SetRoundedCorners(handle); Fluent.ExcludeFromCapture(handle);
            if (PresentationSource.FromVisual(this) is System.Windows.Interop.HwndSource source)
                source.AddHook((hwnd, msg, w, l, ref handled) =>
                {
                    if ((msg == 0x0100 || msg == 0x0104) && w.ToInt32() == 0x1B)
                    { handled = true; Dispatcher.BeginInvoke(Dismiss); }
                    return IntPtr.Zero;
                });
        };
        double seconds = SettingsService.Shared?.Current.WelcomeToastSeconds ?? 5;
        _dismiss = new DispatcherTimer { Interval = TimeSpan.FromSeconds(double.IsFinite(seconds) ? Math.Clamp(seconds, 1, 60) : 5) };
        _dismiss.Tick += (_, _) => Dismiss();
        ContentRendered += (_, _) => { if (!_closing && !IsMouseOver) _dismiss.Start(); };
        MouseEnter += (_, _) => _dismiss.Stop();
        MouseLeave += (_, _) => { if (!_closing) _dismiss.Start(); };
        Closed += (_, _) => { _dismiss.Stop(); if (ReferenceEquals(_current, this)) _current = null; };
        Trace.Log("welcome shown");
        Fluent.FadeIn(this, Fluent.Motion.EnterMs, 8, offscreenFirstFrame: true);
    }

    static string[] KeySeparators { get; } = { Loc.T(K.Hotkeys.Comma), ", ", " / " };

    static FrameworkElement HotkeyLine(string label, string key)
    {
        var g = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "HotkeyLabel" });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lb = new TextBlock
        {
            Text = label,
            FontSize = 13,
            Foreground = Theme.TextDimBrush,
            FontFamily = new FontFamily("Segoe UI"),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(lb, 0);
        g.Children.Add(lb);
        var keys = Keycaps.ChordRow(key, Core.Fluent.KeycapTheme(12.5), KeySeparators,
            Loc.T(K.Common.NoKey));
        keys.HorizontalAlignment = HorizontalAlignment.Right;
        g.Children.Add(keys);
        Grid.SetColumn(keys, 1);
        return g;
    }

    void Dismiss()
    {
        if (_closing) return;
        _closing = true; _dismiss.Stop();
        Fluent.CloseWithFade(this);
    }
}
