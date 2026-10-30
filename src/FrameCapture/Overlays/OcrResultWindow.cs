using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FrameCapture.Core;
using FrameCapture.Encoding;

namespace FrameCapture.Overlays;

/// <summary>在原图上选择识别文字，按选区或全文复制；消息模式显示识别反馈。</summary>
public sealed class OcrResultWindow : Window
{
    readonly OcrImageView? _view;

    /// <summary>打开非模态原图选字窗口。</summary>
    /// <param name="result">带原图和文字坐标的成功识别结果。</param>
    public static void Show(OcrOutcome result)
    {
        if (result.Image == null || result.Lines.Count == 0) { ShowMessage(Loc.T(K.Capture.NoTextRecognized)); return; }
        var window = new OcrResultWindow(result, null);
        window.Show(); window.Activate();
    }

    /// <summary>打开识别反馈消息。</summary>
    /// <param name="message">待显示的短句。</param>
    public static void ShowMessage(string message)
    {
        var window = new OcrResultWindow(null, message);
        window.Show(); window.Activate();
    }

    OcrResultWindow(OcrOutcome? result, string? message)
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; Topmost = true; Title = "FrameCapture OCR";
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = result == null ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
        var area = SystemParameters.WorkArea;
        Width = result == null ? 400 : Math.Min(Math.Clamp(result.Value.Image!.PixelWidth + 30, 620, 1040), area.Width * 0.85);
        double fit = result == null ? 1 : Math.Min(1, (Width - 30) / result.Value.Image!.PixelWidth);
        Height = result == null ? 210 : Math.Min(Math.Clamp(result.Value.Image!.PixelHeight * fit + 100, 340, 760), area.Height * 0.85);
        if (result != null) { MinWidth = Math.Min(560, Width); MinHeight = Math.Min(340, Height); }
        var root = new Border { CornerRadius = new CornerRadius(12), Background = Theme.WindowBgSolidBrush,
            BorderBrush = Theme.StrokeBrush, BorderThickness = new Thickness(1) };
        var dock = new DockPanel();
        var (bar, close) = Fluent.TitleBar("\uE8C1", Loc.T(K.Common.RecognizeText), Loc.T(K.Common.Close));
        close.Click += (_, _) => Fluent.CloseWithFade(this);
        DockPanel.SetDock(bar, Dock.Top); dock.Children.Add(bar);
        if (result is { } document)
        {
            _view = new OcrImageView(document);
            BuildImageContent(dock, document);
        }
        else dock.Children.Add(new TextBlock { Text = message, FontSize = 14, Foreground = Theme.TextBrush,
            TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(28, 10, 28, 18),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        root.Child = dock; Content = root;
        Fluent.InstallCaptionDrag(this, close);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Fluent.CloseWithFade(this); } };
        Fluent.PopupEnter(this);
    }

    void BuildImageContent(DockPanel dock, OcrOutcome document)
    {
        var view = _view!;
        var scroll = new ScrollViewer { Content = view, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Theme.PanelBgSolidBrush,
            CanContentScroll = false, Margin = new Thickness(14, 0, 14, 6) };
        var footer = new DockPanel { Margin = new Thickness(14, 6, 14, 14), LastChildFill = true };
        var commands = new StackPanel { Orientation = Orientation.Horizontal };
        var copy = Fluent.RoundButton(Loc.T(K.Capture.CopySelection), Theme.Accent, Fluent.OnAccentColor(Theme.Accent), 12, true);
        copy.IsEnabled = false; copy.Opacity = 0.45; copy.Margin = new Thickness(0, 0, 8, 0);
        var all = Fluent.RoundButton(Loc.T(K.Capture.CopyAll), Theme.CardBg, Theme.Text, 12, true);
        commands.Children.Add(copy); commands.Children.Add(all);
        DockPanel.SetDock(commands, Dock.Right); footer.Children.Add(commands);
        var zoom = new ComboBox { MinWidth = 88, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        Fluent.StyleCombo(zoom);
        zoom.Items.Add(new ComboBoxItem { Content = Loc.T(K.Capture.FitImage), Tag = 0.0 });
        foreach (double value in new[] { 1.0, 1.5, 2.0, 3.0 })
            zoom.Items.Add(new ComboBoxItem { Content = value.ToString("P0"), Tag = value });
        DockPanel.SetDock(zoom, Dock.Left); footer.Children.Add(zoom);
        var status = new TextBlock { Text = Loc.T(K.Capture.SelectTextOnImage), Foreground = Theme.TextDimBrush,
            FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        footer.Children.Add(status); DockPanel.SetDock(footer, Dock.Bottom); dock.Children.Add(footer);
        dock.Children.Add(scroll);
        copy.Click += async (_, _) => await Copy(view.SelectedText);
        all.Click += async (_, _) => await Copy(document.Text);
        void RefreshZoom()
        {
            if (zoom.SelectedItem is not ComboBoxItem { Tag: double scale }) return;
            view.Zoom = scale > 0 ? scale : Math.Min(1, Math.Min(Math.Max(1, scroll.ViewportWidth - 2) / document.Image!.PixelWidth,
                Math.Max(1, scroll.ViewportHeight - 2) / document.Image.PixelHeight));
        }
        zoom.SelectionChanged += (_, _) => RefreshZoom();
        scroll.SizeChanged += (_, _) => RefreshZoom();
        view.SelectionChanged += () =>
        {
            copy.IsEnabled = view.SelectedText.Length > 0;
            copy.Opacity = copy.IsEnabled ? 1 : 0.45;
            status.Text = copy.IsEnabled ? Loc.F(K.Capture.SelectedCharacters, view.SelectedText.Length) : Loc.T(K.Capture.SelectTextOnImage);
        };
        async Task Copy(string text)
        {
            if (text.Length == 0) return;
            bool copied = await ClipboardService.SetTextAsync(text);
            if (IsVisible) status.Text = Loc.T(copied ? K.Common.Copied : K.Common.ClipboardUnavailable);
        }
        PreviewKeyDown += async (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || e.Key != Key.C) return;
            e.Handled = true; await Copy(view.SelectedText);
        };
        var menu = new ContextMenu { Background = Theme.PanelBgSolidBrush, Foreground = Theme.TextBrush };
        var menuCopy = new MenuItem { Header = Loc.T(K.Capture.CopySelection) };
        menuCopy.Click += async (_, _) => await Copy(view.SelectedText);
        var menuAll = new MenuItem { Header = Loc.T(K.Capture.CopyAll) };
        menuAll.Click += async (_, _) => await Copy(document.Text);
        var selectAll = new MenuItem { Header = Loc.T(K.Common.SelectAll) };
        selectAll.Click += (_, _) => view.SelectAll();
        menu.Items.Add(menuCopy); menu.Items.Add(menuAll); menu.Items.Add(new Separator()); menu.Items.Add(selectAll);
        menu.Opened += (_, _) => menuCopy.IsEnabled = view.SelectedText.Length > 0;
        view.ContextMenu = menu;
        Loaded += (_, _) => { zoom.SelectedIndex = 0; RefreshZoom(); view.Focus(); };
    }
}
