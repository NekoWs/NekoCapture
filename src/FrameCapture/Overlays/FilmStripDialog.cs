using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>「生成胶片条」参数小窗——一根滑杆定抽帧数，下面实时画出这条胶片条的缩略图，
/// 外加「每 N 帧取一次 · R 行 × C 格」一行摘要：抽出来长什么样，生成前一眼就能看到。
/// 本窗不写设置：调用方读回 <see cref="Frames"/> 后自行记忆，下次打开沿用。</summary>
public sealed class FilmStripDialog : Window
{
    /// <value>用户确认的抽帧数，钳制到 2–48；仅当 ShowDialog 返回 true 时有意义。</value>
    public int Frames { get; private set; }

    /// <summary>预览格子宽（像素），调用方渲染预览位图时用同一个值。缩略图只求看清排布与内容，
    /// 格子小才跟得上滑杆。</summary>
    public const int PreviewCell = 96;

    readonly int _total;
    readonly Func<int, CancellationToken, BitmapSource?> _renderPreview;
    readonly TextBlock _value = new();
    readonly TextBlock _summary = new();
    readonly Image _preview = new();
    readonly DispatcherTimer _debounce;
    /// <summary>预览代数，过期结果不更新界面。</summary>
    int _previewSeq;
    int? _pendingPreview;
    bool _previewRunning, _previewClosed;
    CancellationTokenSource? _previewCancellation;
    Task _previewTask = Task.CompletedTask;

    /// <summary>关闭后等待正在取消的预览结束，调用方随后可释放帧源。</summary>
    internal Task PreviewFinished => _previewTask;

    /// <summary>
    /// 构造预览式参数小窗：Enter=生成 / Esc=取消。
    /// 无边框 + 允许透明，靠 Owner 居中；必须用 ShowDialog 打开——按钮与 Esc 写的是 DialogResult。
    /// </summary>
    /// <param name="owner">宿主编辑器窗口，决定居中与父子层级。</param>
    /// <param name="totalFrames">当前时间轴（含裁剪范围）总帧数，决定滑杆上限与摘要里的步长。</param>
    /// <param name="desired">滑杆初值，通常是上次用过的抽帧数；越界时钳到 2 与总帧数之间。</param>
    /// <param name="renderPreview">按抽帧数渲染一张小胶片条的回调，在后台线程上调用，
    /// 因此它闭包里的帧与标注必须已在 UI 线程快照好。返回 null = 这一档画不出来，预览留空。</param>
    public FilmStripDialog(Window owner, int totalFrames, int desired, Func<int, BitmapSource?> renderPreview)
        : this(owner, totalFrames, desired, (count, _) => renderPreview(count)) { }

    /// <summary>创建可逐格取消、只渲染最新请求的预览窗口。</summary>
    /// <param name="owner">宿主编辑器。</param>
    /// <param name="totalFrames">可抽样帧数。</param>
    /// <param name="desired">初始抽样帧数。</param>
    /// <param name="renderPreview">后台渲染入口与取消信号。</param>
    internal FilmStripDialog(Window owner, int totalFrames, int desired, Func<int, CancellationToken, BitmapSource?> renderPreview)
    {
        Owner = owner;
        _total = Math.Max(1, totalFrames);
        _renderPreview = renderPreview;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SizeToContent = SizeToContent.WidthAndHeight;
        InputMethod.SetIsInputMethodEnabled(this, false);

        int hi = Math.Clamp(Math.Min(_total, 48), 2, 48);
        var slider = Fluent.Ui(new Slider
        {
            Minimum = 2,
            Maximum = hi,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Value = Math.Clamp(desired, 2, hi),
            Foreground = Theme.AccentBrush,
            Margin = new Thickness(2, 0, 0, 0),
            // 吸附开启时步长必须=档位：默认 SmallChange=1 会被吸回原档，方向键看似失灵
            SmallChange = 1,
            LargeChange = 2,
        });

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        int take = (int)slider.Value;
        _debounce.Tick += (_, _) => { _debounce.Stop(); RequestPreview(take = (int)slider.Value); };
        slider.ValueChanged += (_, e) => { Refresh((int)e.NewValue, now: false); };

        var card = new Border
        {
            Background = Theme.PanelBgSolidBrush,
            BorderBrush = Theme.StrokeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(20, 16, 20, 16),
            Child = Build(slider),
        };
        Fluent.Elevate(card, blur: 26, depth: 7);
        Content = card;
        Fluent.PopIn(card, 0.9);
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

        // Enter=生成 / Esc=取消：小窗无需 Tab 到按钮
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Frames = (int)slider.Value; DialogResult = true; }
            else if (e.Key == Key.Escape) { DialogResult = false; }
        };
        Refresh(take, now: true);
        Loaded += (_, _) => slider.Focus();
        Closed += (_, _) =>
        {
            _previewClosed = true; _pendingPreview = null; _previewSeq++;
            _debounce.Stop(); _previewCancellation?.Cancel(); _preview.Source = null;
        };
    }

    UIElement Build(Slider slider)
    {
        var root = new StackPanel();
        root.Children.Add(new TextBlock
        {
            Text = Loc.T(K.Editor.FilmStrip), FontSize = 15, FontWeight = FontWeights.SemiBold,
            Foreground = Theme.TextBrush,
        });
        root.Children.Add(new TextBlock
        {
            Text = Loc.F(K.Editor.SamplesTheCurrentRecordingInto, _total),
            FontSize = 12, Foreground = Theme.TextDimBrush,
            Margin = new Thickness(0, 3, 0, 12), TextWrapping = TextWrapping.Wrap,
        });

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = new TextBlock
        {
            Text = Loc.T(K.Editor.HowManyFrames), FontSize = 13,
            Foreground = Theme.TextBrush, VerticalAlignment = VerticalAlignment.Center,
        };
        _value.FontSize = 13;
        _value.FontWeight = FontWeights.SemiBold;
        _value.Foreground = new SolidColorBrush(Core.Fluent.AccentText());
        _value.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(lbl, 0);
        Grid.SetColumn(_value, 1);
        head.Children.Add(lbl);
        head.Children.Add(_value);
        root.Children.Add(head);
        root.Children.Add(slider);

        _summary.FontSize = 11.5;
        _summary.Foreground = Theme.TextDimBrush;
        _summary.Margin = new Thickness(2, 3, 0, 10);
        root.Children.Add(_summary);

        // 预览框尺寸固定：抽帧数变了图的比例也变，但弹窗不许跟着抖
        var box = new Border
        {
            Width = 460,
            Height = 196,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)),
            BorderBrush = Theme.StrokeBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Child = _preview,
        };
        _preview.Stretch = Stretch.Uniform;
        root.Children.Add(box);

        var btns = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var cancel = ChipBtn(Loc.T(K.Common.Cancel), accent: false);
        cancel.Click += (_, _) => { DialogResult = false; };
        var ok = ChipBtn(Loc.T(K.Editor.Create), accent: true);
        ok.Click += (_, _) => { Frames = (int)slider.Value; DialogResult = true; };
        btns.Children.Add(cancel);
        btns.Children.Add(ok);
        root.Children.Add(btns);
        return root;
    }

    /// <summary>刷新读数与摘要；<paramref name="now"/> 为 true 时立刻出图，否则重排防抖。</summary>
    void Refresh(int k, bool now)
    {
        int take = Math.Clamp(k, 2, Math.Clamp(Math.Min(_total, 48), 2, 48));
        int n = Math.Min(take, _total);
        int cols = Math.Min(n, 8), rows = (n + cols - 1) / cols;
        // 抽样是均匀铺开 n 个下标，平均间隔就是总帧数除以取出的帧数
        int step = n > 1 ? Math.Max(1, (int)Math.Round(_total / (double)n)) : 1;
        _value.Text = Loc.F(K.Editor.FramesValue, n);
        _summary.Text = Loc.F(K.Editor.EveryNFramesRowsCols, step, rows, cols);
        if (now) RequestPreview(take);
        else { _debounce.Stop(); _debounce.Start(); }
    }

    /// <summary>替换尚未执行的请求并取消当前预览，后台最多同时执行一个渲染。</summary>
    void RequestPreview(int take)
    {
        if (_previewClosed) return;
        _pendingPreview = take; _previewSeq++; _previewCancellation?.Cancel();
        if (_previewRunning) return;
        _previewRunning = true;
        _previewTask = Dispatcher.InvokeAsync(new Func<Task>(RenderPreviewsAsync)).Task.Unwrap();
    }

    private async Task RenderPreviewsAsync()
    {
        try
        {
            while (!_previewClosed && _pendingPreview is { } take)
            {
                _pendingPreview = null;
                int generation = _previewSeq;
                using var cancellation = new CancellationTokenSource();
                _previewCancellation = cancellation;
                BitmapSource? image = null;
                try { image = await Services.BackgroundWork.Run(() => _renderPreview(take, cancellation.Token), cancellation.Token); }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                catch (Exception ex) { Trace.Log("filmstrip preview: " + ex.Message); }
                finally { _previewCancellation = null; }
                if (!_previewClosed && generation == _previewSeq && image != null) _preview.Source = image;
            }
        }
        finally { _previewRunning = false; }
    }

    Button ChipBtn(string text, bool accent)
    {
        var b = Fluent.Ui(new Button
        {
            Content = text,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(14, 6, 14, 6),
            FontSize = 13,
            Cursor = Cursors.Hand,
            BorderThickness = new Thickness(1),
            Foreground = new SolidColorBrush(accent ? Fluent.OnAccentColor(Theme.Accent) : Theme.Text),
            Background = new SolidColorBrush(accent ? Theme.Accent : Theme.CardBg),
            BorderBrush = new SolidColorBrush(accent ? Theme.Accent : Theme.Stroke),
        });
        Fluent.Springy(b);
        b.Template = Fluent.RoundedButtonTemplate(false);
        return b;
    }

}
