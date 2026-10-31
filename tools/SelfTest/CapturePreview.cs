using System.Windows;
using System.Windows.Controls;
using FrameCapture.Capture;
using FrameCapture.Core;
using FrameCapture.Overlays;

static partial class SelfTest
{
    static void CapturePreviewChecks()
    {
        var graph = BuiltInBehaviors.CreateCapture();
        Chk("immediate-capture-valid-hidden-entry", graph.IsValid(BuiltInBehaviors.Capture)
            && graph.Flows.Count == 1 && !BehaviorPresets.All.Contains(BuiltInBehaviors.Capture));
        var select = graph.Node(graph.Flows.Single().Steps.Single())!;
        Chk("immediate-capture-selects-with-mode-switch", select is
            { Action: GestureActions.Select, ShowModeSwitch: true, RecordMode: false });
        var calls = new List<string>();
        Task<bool> Run(string mode, string captureResult = BehaviorPorts.Done) =>
            BehaviorRunner.RunAsync(graph, BuiltInBehaviors.Capture, node =>
            {
                calls.Add(node.Action);
                return Task.FromResult(node.Action switch
                {
                    GestureActions.Select => mode,
                    GestureActions.GrabStill or GestureActions.RecordStart => captureResult,
                    GestureActions.EditLastVideo => BehaviorPorts.Saved,
                    _ => BehaviorPorts.Done,
                });
            });
        Chk("immediate-capture-still-notice-order", Run(BehaviorPorts.Still).GetAwaiter().GetResult()
            && calls.SequenceEqual(new[] { GestureActions.Select, GestureActions.GrabStill, GestureActions.NotifyToast }));
        calls.Clear();
        Chk("immediate-capture-video-edit-copy-order", Run(BehaviorPorts.Record).GetAwaiter().GetResult()
            && calls.SequenceEqual(new[] { GestureActions.Select, GestureActions.RecordStart,
                GestureActions.EditLastVideo, GestureActions.CopyLast }));
        calls.Clear();
        Chk("immediate-capture-cancel-stops", !Run(BehaviorPorts.Cancel).GetAwaiter().GetResult()
            && calls.SequenceEqual(new[] { GestureActions.Select }));
        calls.Clear();
        Chk("immediate-capture-failure-does-not-notify", !Run(BehaviorPorts.Still, BehaviorPorts.Failed).GetAwaiter().GetResult()
            && calls.SequenceEqual(new[] { GestureActions.Select, GestureActions.GrabStill }));
        calls.Clear();
        var stopped = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = BehaviorRunner.RunAsync(graph, BuiltInBehaviors.Capture, node =>
        {
            calls.Add(node.Action);
            return node.Action == GestureActions.RecordStart ? stopped.Task
                : Task.FromResult(node.Action switch
                {
                    GestureActions.Select => BehaviorPorts.Record,
                    GestureActions.EditLastVideo => BehaviorPorts.Saved,
                    _ => BehaviorPorts.Done,
                });
        });
        Chk("immediate-capture-edit-waits-for-recording", !pending.IsCompleted
            && calls.SequenceEqual(new[] { GestureActions.Select, GestureActions.RecordStart }));
        stopped.SetResult(BehaviorPorts.Done);
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!pending.IsCompleted && timeout.ElapsedMilliseconds < 3000) PumpMs(10);
        Chk("immediate-capture-edits-after-recording", pending.IsCompletedSuccessfully && pending.Result
            && calls.Contains(GestureActions.EditLastVideo));
        select.ShowModeSwitch = false;
        Chk("immediate-capture-runs-have-independent-graphs", BuiltInBehaviors.CreateCapture().Nodes[0].ShowModeSwitch);

        const int w = 600, h = 6000;
        var pixels = new byte[w * h * 4];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            int at = (y * w + x) * 4;
            pixels[at] = (byte)x; pixels[at + 1] = (byte)y; pixels[at + 2] = (byte)(y >> 8); pixels[at + 3] = 255;
        }
        var shortView = ScrollPreviewWindow.Viewport(pixels, w, 300, 200, 600);
        var growingView = ScrollPreviewWindow.Viewport(pixels, w, 1200, 200, 600);
        var longView = ScrollPreviewWindow.Viewport(pixels, w, h, 200, 600);
        Chk("scroll-preview-grows-with-constant-width", shortView is { W: 200, H: 100 }
            && growingView is { W: 200, H: 400 } && longView is { W: 200, H: 600 });
        int Row(int row) => longView.Bgra[row * longView.W * 4 + 1] | longView.Bgra[row * longView.W * 4 + 2] << 8;
        Chk("scroll-preview-shows-newest-tail-without-shrinking", Row(0) == 4200 && Row(1) - Row(0) == 3
            && Row(longView.H - 1) == h - 1);
        Chk("scroll-preview-pixel-budget", longView.Bgra.Length == 200 * 600 * 4
            && longView.Bgra[^4] == (byte)((w - 1) & 255));
        var small = pixels[..(w * 4)];
        Chk("scroll-preview-does-not-enlarge", ScrollPreviewWindow.Viewport(small, w, 1, 1000, 1000)
            is { W: w, H: 1 } actual && actual.Bgra.SequenceEqual(small));
        Chk("scroll-preview-invalid-buffer-is-empty", ScrollPreviewWindow.Viewport(small, w, h, 200, 600).Bgra.Length == 0
            && ScrollPreviewWindow.Viewport(pixels, w, h, 0, 600).Bgra.Length == 0);

        _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var previousContext = System.Threading.SynchronizationContext.Current;
        System.Threading.SynchronizationContext.SetSynchronizationContext(
            new System.Windows.Threading.DispatcherSynchronizationContext());
        var preview = new ScrollPreviewWindow(new RectI(0, 0, w, 300), "");
        try
        {
            var image = ((Grid)preview.Content).Children.OfType<Image>().Single();
            Chk("scroll-preview-image-only-frameless-window", preview.Content is Grid { Effect: null } root
                && root.Children.Count == 1 && preview.WindowStyle == WindowStyle.None && !preview.ShowActivated);
            preview.SetImage(pixels, w, 300);
            timeout.Restart();
            while (image.Source == null && timeout.ElapsedMilliseconds < 3000) PumpMs(10);
            double initialHeight = preview.Height, initialWidth = preview.Width;
            var firstSource = image.Source;
            preview.SetImage(pixels, w, h);
            timeout.Restart();
            while (ReferenceEquals(firstSource, image.Source) && timeout.ElapsedMilliseconds < 3000) PumpMs(10);
            Chk("scroll-preview-window-follows-content-height", image.Source != null && preview.Height > initialHeight
                && preview.Width == initialWidth && image.Height == preview.Height);
        }
        finally
        {
            preview.Close();
            System.Threading.SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }
}
