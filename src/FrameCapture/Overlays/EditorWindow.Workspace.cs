using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FrameCapture.Core;
using FrameCapture.Encoding;

namespace FrameCapture.Overlays;

/// <summary>编辑器工作区：输出栏、上下文选项、工具栏、画布与状态。</summary>
public sealed partial class EditorWindow
{
    TextBlock? _titleLabel, _activeToolLabel, _contextHint, _dimensionsLabel;
    TextBlock? _exportLabel;
    Button? _browseButton, _finishButton, _saveButton, _resetCropButton, _zoomButton, _cancelExportButton, _deleteSelectionButton;
    Border? _busyPanel;
    ProgressBar? _exportProgress;
    ContextMenu? _workspaceMenu;
    FrameworkElement? _editingSurface;
    bool MultiFrame => _seq.Frames.Count > 1;

    void BuildLayout()
    {
        var root = new Grid();
        for (int i = 0; i < 6; i++) root.RowDefinitions.Add(new RowDefinition
        { Height = i == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        _root = root;
        Content = root;
        root.Children.Add(BuildHeader());
        _toolsBar = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        _activeToolLabel = WorkspaceLabel(Loc.T(K.Editor.Browse), 13);
        _activeToolLabel.FontWeight = FontWeights.SemiBold;
        _activeToolLabel.Foreground = Theme.TextBrush;
        _activeToolLabel.MinWidth = 62;
        _activeToolLabel.Margin = new Thickness(4, 0, 12, 0);
        _toolsBar.Children.Add(_activeToolLabel);
        _contextHint = WorkspaceLabel(Loc.T(K.Editor.BrowseHint));
        _contextHint.Margin = new Thickness(4, 4, 8, 4);
        _toolsBar.Children.Add(_contextHint);
        BuildStyleOptions(_toolsBar);
        _resetCropButton = TextButton(K.Editor.ResetCrop, ResetCrop);
        _resetCropButton.Visibility = Visibility.Collapsed;
        _toolsBar.Children.Add(_resetCropButton);
        _deleteSelectionButton = TextButton(K.Commands.DeleteObjectName, DeleteSelected, "delete-object");
        _toolsBar.Children.Add(_deleteSelectionButton);
        _toolPanel = Panel(_toolsBar);
        _toolPanel.MinHeight = 48;
        Grid.SetRow(_toolPanel, 1);
        root.Children.Add(_toolPanel);

        var workspace = new Grid();
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rail = WorkspacePanel(BuildToolRail(), new Thickness(5, 8, 5, 8));
        rail.BorderThickness = new Thickness(0, 0, 1, 0);
        workspace.Children.Add(rail);
        _viewGrid.ClipToBounds = true;
        _viewGrid.Children.Add(_view);
        _viewGrid.Children.Add(_canvas);
        BuildSnack();
        var viewHost = new Border
        {
            Background = Theme.WindowBgSolidBrush,
            Child = _viewGrid, ClipToBounds = true,
        };
        Grid.SetColumn(viewHost, 1);
        workspace.Children.Add(viewHost);
        Grid.SetRow(workspace, 2);
        root.Children.Add(workspace);
        _editingSurface = workspace;
        if (MultiFrame)
        {
            _playPanel = Panel(BuildTimelinePanel());
            Grid.SetRow(_playPanel, 3);
            root.Children.Add(_playPanel);
        }
        _busyPanel = Panel(BuildExportPanel());
        _busyPanel.Visibility = Visibility.Collapsed;
        Grid.SetRow(_busyPanel, 4);
        root.Children.Add(_busyPanel);
        var status = Panel(BuildStatusBar());
        status.BorderThickness = new Thickness(0);
        Grid.SetRow(status, 5);
        root.Children.Add(status);
        RefreshToolOptions();
    }

    Border BuildHeader()
    {
        var grid = new Grid { MinHeight = 42 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var identity = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _titleLabel = WorkspaceLabel(Loc.T(MultiFrame ? K.Editor.VideoWorkspace : K.Editor.StillWorkspace), 15);
        _titleLabel.Foreground = Theme.TextBrush;
        _titleLabel.FontWeight = FontWeights.SemiBold;
        _titleLabel.Margin = new Thickness(6, 0, 16, 0);
        identity.Children.Add(_titleLabel);
        _undoBtn = CommandIcon("\uE7A7", K.Editor.Undo, "undo", UndoAnn);
        _redoBtn = CommandIcon("\uE7A6", K.Editor.Redo, "redo", RedoAnn);
        identity.Children.Add(_undoBtn);
        identity.Children.Add(_redoBtn);
        grid.Children.Add(identity);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var ocr = CommandIcon("\uE8C1", K.Common.RecognizeText, "ocr", OcrCurrentFrame);
        var pin = CommandIcon("\uE840", K.Editor.PinToScreen, "pin-frame", PinCurrentFrame);
        actions.Children.Add(ocr);
        actions.Children.Add(pin);
        _saveButton = TextButton(K.Editor.SaveOutput, () => { if (!_encoding) { if (MultiFrame) SaveRecording(); else SaveFrame(); } },
            MultiFrame ? "save-recording" : "save-frame");
        actions.Children.Add(_saveButton);
        _finishButton = TextButton(K.Common.Done, Commit, "commit", primary: true);
        _finishButton.MinWidth = 72;
        _okBtns.Add(_finishButton);
        actions.Children.Add(_finishButton);
        var more = IconBtn("\uE712", Loc.T(K.Editor.More));
        more.Click += (_, _) => ShowWorkspaceMenu(more);
        actions.Children.Add(more);
        var close = IconBtn("\uE711", Loc.T(K.Common.Close));
        close.Click += (_, _) => Fluent.CloseWithFade(this);
        actions.Children.Add(close);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        var header = Panel(grid);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else try { DragMove(); } catch { }
            e.Handled = true;
        };
        RefreshUndo();
        return header;
    }

    FrameworkElement BuildToolRail()
    {
        var tools = new StackPanel();
        _browseButton = CommandIcon("\uE7C9", K.Editor.Browse, "tool.X", () => SetTool(Tool.Browse));
        _browseButton.Width = 38;
        _browseButton.Height = 32;
        _browseButton.Margin = new Thickness(4, 1, 4, 1);
        tools.Children.Add(_browseButton);
        for (int i = 0; i < ToolOrder.Length; i++)
        {
            var info = ToolCatalog.Tools[i];
            var tool = ToolOrder[i];
            if (i is 1 or 9) tools.Children.Add(new Border { Height = 8 });
            var button = CommandIcon(info.Glyph, info.Tip, "tool." + info.Key, () => SetTool(tool));
            button.Width = 38;
            button.Height = 32;
            button.Margin = new Thickness(4, 1, 4, 1);
            _toolBtns.Add(button);
            tools.Children.Add(button);
        }
        RefreshToolHighlight();
        return new ScrollViewer
        {
            Content = tools, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly,
        };
    }

    FrameworkElement BuildStatusBar()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _dimensionsLabel = WorkspaceLabel("");
        _dimensionsLabel.Margin = new Thickness(4, 0, 14, 0);
        grid.Children.Add(_dimensionsLabel);
        _hud.TextTrimming = TextTrimming.CharacterEllipsis;
        _hud.Margin = new Thickness(0);
        Grid.SetColumn(_hud, 1);
        grid.Children.Add(_hud);
        var zoom = new StackPanel { Orientation = Orientation.Horizontal };
        var zoomOut = TextButton(K.Commands.ZoomOutName, () => ZoomStep(1 / 1.25), "zoom-out");
        zoomOut.Content = "−"; zoomOut.Padding = new Thickness(5, 0, 5, 0);
        _zoomButton = TextButton(K.Editor.FitView, ShowZoomMenu, "zoom-fit");
        _zoomButton.MinWidth = 66;
        var zoomIn = TextButton(K.Commands.ZoomInName, () => ZoomStep(1.25), "zoom-in");
        zoomIn.Content = "+"; zoomIn.Padding = new Thickness(5, 0, 5, 0);
        foreach (var button in new[] { zoomOut, _zoomButton, zoomIn })
        { button.Height = 26; button.BorderThickness = new Thickness(0); zoom.Children.Add(button); }
        Grid.SetColumn(zoom, 2);
        grid.Children.Add(zoom);
        return grid;
    }

    FrameworkElement BuildExportPanel()
    {
        var grid = new Grid { Margin = new Thickness(4, 2, 4, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var status = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        _exportLabel = WorkspaceLabel(Loc.T(K.Editor.BuildingFilmStrip));
        _exportLabel.Margin = new Thickness(0, 0, 0, 6);
        _exportProgress = Fluent.Ui(new ProgressBar { Height = 3, Minimum = 0, Maximum = 100, Foreground = new SolidColorBrush(Theme.Accent) });
        status.Children.Add(_exportLabel);
        status.Children.Add(_exportProgress);
        grid.Children.Add(status);
        _cancelExportButton = TextButton(K.Editor.CancelExport, CancelExport);
        Grid.SetColumn(_cancelExportButton, 1);
        grid.Children.Add(_cancelExportButton);
        return grid;
    }

    void BuildSnack()
    {
        _snackText = new TextBlock { Foreground = Brushes.White, FontSize = 13, TextAlignment = TextAlignment.Center };
        _snack = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE8, 0x20, 0x22, 0x26)),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 8, 14, 8),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(20), Opacity = 0, Visibility = Visibility.Collapsed,
            IsHitTestVisible = false, Child = _snackText,
        };
        _viewGrid.Children.Add(_snack);
    }

    void FitMinWidthToToolbar()
    {
        MinWidth = Math.Min(720, SystemParameters.WorkArea.Width);
        MinHeight = Math.Min(520, SystemParameters.WorkArea.Height);
    }
}
