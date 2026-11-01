using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using NekoCapture.Core;
using NekoCapture.Drawing;
using NekoCapture.Encoding;

namespace NekoCapture.Overlays;

/// <summary>编辑器上下文、菜单、导出反馈与主题同步。</summary>
public sealed partial class EditorWindow
{
    bool _maskDark = Theme.IsDark;
    Color _maskFrom = Theme.WindowBgSolid;
    ResourceDictionary? _workspaceMenuResources;

    void OnPaletteChanging(bool dark)
    {
        if (dark != _maskDark) _maskFrom = Theme.WindowBgSolid;
    }

    void Editor_OnTheme()
    {
        bool flip = Theme.IsDark != _maskDark;
        _maskDark = Theme.IsDark;
        if (flip) Fluent.MaskFade(this, _maskFrom);
        Background = _mica ? Brushes.Transparent : new SolidColorBrush(Theme.WindowBg);
        _hud.Foreground = Theme.TextBrush;
        RefreshWorkspaceButtons();
        RefreshToolHighlight();
        if (_bPlay != null) Fluent.HighlightBtn(_bPlay, _playing);
        RefreshSwatches();
        RefreshWidths();
        foreach (var swatch in _swatchBtns)
            if (swatch.Child is System.Windows.Shapes.Ellipse circle)
                circle.Stroke = new SolidColorBrush(Color.FromArgb(0x66, Theme.Text.R, Theme.Text.G, Theme.Text.B));
        if (_fontLabel != null) _fontLabel.Foreground = Theme.TextBrush;
        if (_exportProgress != null) _exportProgress.Foreground = new SolidColorBrush(Theme.Accent);
        if (_workspaceMenu != null) _workspaceMenu.IsOpen = false;
        _workspaceMenuResources = null;
        RefreshToolOptions();
        UpdateEditorStatus();
        DrawRange();
    }

    void SetSelection(Annotation? annotation)
    {
        _sel = annotation;
        if (annotation != null)
        {
            var color = annotation.Color;
            ApplyColor(Color.FromArgb(color.A, color.R, color.G, color.B));
            ApplyWidth(annotation.Thickness);
        }
        RedrawAnns();
        RefreshToolOptions();
    }

    void RefreshToolOptions()
    {
        if (_activeToolLabel == null) return;
        string name = _tool == Tool.Browse ? Loc.T(K.Editor.Browse)
            : Loc.T(ToolCatalog.Tools[Array.IndexOf(ToolOrder, _tool)].Tip);
        if (_activeToolLabel.Text != name)
        {
            _activeToolLabel.Text = name;
            if (IsLoaded)
                _activeToolLabel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(Fluent.Motion.QuickMs)));
        }
        bool style = _sel != null || _tool is not (Tool.Browse or Tool.Select or Tool.Crop);
        bool text = _tool == Tool.Text || _sel?.Kind is AnnKind.Text or AnnKind.Number;
        bool mosaic = _tool == Tool.Mosaic || _sel?.Kind == AnnKind.Mosaic;
        if (_colorOptions != null) _colorOptions.Visibility = style && !mosaic ? Visibility.Visible : Visibility.Collapsed;
        if (_strokeOptions != null) _strokeOptions.Visibility = style && !text ? Visibility.Visible : Visibility.Collapsed;
        if (_contextHint != null)
        {
            _contextHint.Text = Loc.T(_tool == Tool.Crop ? K.Editor.CropHint
                : _tool == Tool.Select ? K.Editor.SelectHint : K.Editor.BrowseHint);
            _contextHint.Visibility = style ? Visibility.Collapsed : Visibility.Visible;
        }
        if (_resetCropButton != null) _resetCropButton.Visibility = _crop.IsCropped ? Visibility.Visible : Visibility.Collapsed;
        if (_deleteSelectionButton != null) _deleteSelectionButton.Visibility = _sel != null ? Visibility.Visible : Visibility.Collapsed;
        UpdateFontStepper();
    }

    void UpdateEditorStatus()
    {
        if (_seq.Frames.Count == 0) return;
        if (_dimensionsLabel != null)
        {
            var (w, h) = CropPx(_frameW, _frameH);
            _dimensionsLabel.Text = $"{(int)Math.Round(w)} × {(int)Math.Round(h)}";
        }
        if (_zoomButton != null)
        {
            double scale = _imgRect.Width / Math.Max(1, _frameW);
            _zoomButton.Content = _zoom <= 1.001 ? Loc.T(K.Editor.FitView) : $"{scale * 100:0}%";
        }
        if (!_encoding)
        {
            _hud.Text = MultiFrame ? Loc.F(K.Editor.FramePosition, _index + 1, _seq.Frames.Count)
                : _anns.Count + _crop.Depth > 0 ? Loc.F(K.Editor.CompletedChanges, _anns.Count + _crop.Depth) : "";
            if (_seq.DroppedFrames > 0) _hud.Text += Loc.F(K.Editor.FramesDroppedByTheMemory, _seq.DroppedFrames);
            _hud.ToolTip = _hud.Text;
        }
        UpdateTimelineStatus();
        if (_finishButton != null) _finishButton.ToolTip = CommandTip("commit", ManagedOutput ? K.Editor.ContinueBehavior : K.Editor.CopyAndComplete);
    }

    void ResetCrop()
    {
        if (_encoding) return;
        if (!_crop.RestoreFull()) return;
        _order.CropPushed();
        _zoom = ZoomMath.MinZoom; _panX = _panY = 0;
        UpdateImgRect(); RedrawAnns(); RefreshUndo(); RefreshToolOptions(); Show(_index);
    }

    async void CopyCurrentFrame()
    {
        if (_encoding) return;
        try
        {
            CommitTextInput();
            var frame = await BakeFrameAsync(_index);
            bool copied = await ClipboardService.SetFrameAsync(frame, includePng: true);
            if (!_userClosed) ShowSnack(Loc.T(copied ? K.Common.Copied : K.Common.ClipboardUnavailable));
        }
        catch (Exception error) { Trace.Log($"copy frame: {error}"); if (!_userClosed) ShowSnack(Loc.T(K.Common.ClipboardUnavailable)); }
    }

    void ShowWorkspaceMenu(FrameworkElement anchor)
    {
        var menu = NewWorkspaceMenu(anchor);
        Add(K.Editor.CopyCurrentFrame, CopyCurrentFrame, "copy-frame", !_encoding);
        if (MultiFrame)
        {
            Add(K.Editor.SaveCurrentFrame, SaveFrame, "save-frame", !_encoding);
            menu.Items.Add(new Separator());
            Add(K.Editor.FilmStrip, GenerateFilmStrip, enabled: !_encoding);
            Add(K.Editor.SaveAfterimageComposite, SaveAfterimage, "save-afterimage", !_encoding);
        }
        menu.Items.Add(new Separator());
        var topmost = Add(K.Editor.AlwaysOnTop, () =>
        {
            Topmost = !Topmost;
            _settings.Mutate(s => s.EditorTopmost = Topmost);
        });
        topmost.IsChecked = Topmost;
        Add(K.Common.Settings, () => SettingsWindow.ShowOrFocus());
        Add(K.Editor.HelpAndShortcuts, () => HelpWindow.ShowOrFocus(), "help");
        menu.IsOpen = true;

        MenuItem Add(Str text, Action execute, string? command = null, bool enabled = true)
        {
            var item = new MenuItem { Header = Loc.T(text), IsEnabled = enabled };
            if (command != null && EditorCommands.Find(command) is { } entry)
                item.InputGestureText = EditorCommands.BindingsFor(_settings.Current, entry).FirstOrDefault() ?? "";
            item.Click += (_, _) => execute();
            menu.Items.Add(item);
            return item;
        }
    }

    ContextMenu NewWorkspaceMenu(FrameworkElement anchor)
    {
        if (_workspaceMenu != null) _workspaceMenu.IsOpen = false;
        return _workspaceMenu = Fluent.Ui(new ContextMenu
        {
            Resources = _workspaceMenuResources ??= Fluent.MenuResources(), PlacementTarget = anchor,
            Placement = PlacementMode.Bottom, StaysOpen = false,
        });
    }

    void ShowZoomMenu()
    {
        if (_zoomButton == null) return;
        var menu = NewWorkspaceMenu(_zoomButton);
        Add(K.Commands.ZoomFitName, ZoomFit);
        Add(K.Commands.ZoomOneName, ZoomOneToOne);
        foreach (int percent in new[] { 50, 100, 200, 400 })
        {
            int value = percent;
            var item = new MenuItem { Header = $"{value}%" };
            item.Click += (_, _) =>
            {
                double fit = _imgRect.Width / Math.Max(1, _frameW) / Math.Max(0.001, _zoom);
                SetZoom(value / 100.0 / Math.Max(0.001, fit), _viewGrid.ActualWidth / 2, _viewGrid.ActualHeight / 2);
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
        void Add(Str text, Action execute)
        {
            var item = new MenuItem { Header = Loc.T(text) };
            item.Click += (_, _) => execute();
            menu.Items.Add(item);
        }
    }

    void SetExportProgress(string label, int done = 0, int total = 0)
    {
        if (_exportLabel == null || _exportProgress == null) return;
        _exportProgress.IsIndeterminate = total == 0;
        int percent = total > 0 ? (int)Math.Clamp(done * 100L / total, 0, 100) : 0;
        _exportProgress.Value = percent;
        _exportLabel.Text = total > 0 ? Loc.F(K.Editor.ExportProgress, label, percent) : label;
    }

    void CancelExport()
    {
        _commitCts?.Cancel();
        if (_cancelExportButton != null) _cancelExportButton.IsEnabled = false;
        if (_exportLabel != null) _exportLabel.Text = Loc.T(K.Editor.CancelingExport);
    }

    void SetWorkspaceBusy(bool busy)
    {
        if (busy && _playing) TogglePlay();
        CommitTextInput();
        if (_busyPanel != null) _busyPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (_exportProgress != null) _exportProgress.IsIndeterminate = busy;
        if (_cancelExportButton != null) _cancelExportButton.IsEnabled = busy;
        if (_editingSurface != null) _editingSurface.IsEnabled = !busy;
        if (_toolPanel != null) _toolPanel.IsEnabled = !busy;
        if (_playPanel != null) _playPanel.IsEnabled = !busy;
        foreach (var (button, command, _, _) in _workspaceButtons)
            if (command != null && !command.StartsWith("zoom-", StringComparison.Ordinal) && command != "help")
                button.IsEnabled = !busy;
        if (!busy) { RefreshUndo(); UpdateEditorStatus(); }
    }
}
