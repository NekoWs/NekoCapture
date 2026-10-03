using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;

class Prog
{
    [STAThread]
    static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var win = new Window
        {
            Title = "UiProbe Win", Width = 320, Height = 240,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = 150, Top = 150,
            Content = new System.Windows.Controls.TextBox { Text = "snap me", Width = 140, Height = 26, Margin = new Thickness(20) },
        };
        win.Show();
        var pump = new DispatcherFrame();
        win.Dispatcher.BeginInvoke(() => { pump.Continue = false; }, DispatcherPriority.Loaded);
        Dispatcher.PushFrame(pump);

        var tb = (System.Windows.Controls.TextBox)win.Content;
        var tl = tb.PointToScreen(new Point(0, 0));
        var br = tb.PointToScreen(new Point(tb.ActualWidth, tb.ActualHeight));
        int cx = (int)((tl.X + br.X) / 2), cy = (int)((tl.Y + br.Y) / 2);
        Console.WriteLine($"tb screen {tl.X},{tl.Y}..{br.X},{br.Y} point {cx},{cy}");
        foreach (var t in FrameCapture.Native.WindowEnum.ListTopmostCandidates(IntPtr.Zero).Take(6))
            Console.WriteLine($"cand {t.Hwnd} bounds={t.Bounds}");

        IntPtr hw = new System.Windows.Interop.WindowInteropHelper(win).Handle;
        var root = AutomationElement.FromHandle(hw);
        Console.WriteLine($"root rect={root.Current.BoundingRectangle} name={root.Current.Name}");
        var w = TreeWalker.ControlViewWalker;
        var child = w.GetFirstChild(root);
        int n = 0;
        while (child != null && n < 8)
        {
            Console.WriteLine($"  child[{n}] ct={child.Current.ControlType.LocalizedControlType} rect={child.Current.BoundingRectangle}");
            var gc = w.GetFirstChild(child);
            int m = 0;
            while (gc != null && m < 6)
            {
                Console.WriteLine($"    gchild[{m}] ct={gc.Current.ControlType.LocalizedControlType} rect={gc.Current.BoundingRectangle}");
                gc = w.GetNextSibling(gc); m++;
            }
            child = w.GetNextSibling(child); n++;
        }
        var hit = FrameCapture.Core.ElementSnap.At(cx, cy, IntPtr.Zero);
        Console.WriteLine($"ElementSnap.At -> {(hit == null ? "null" : hit.Rect.ToString())}");
        win.Close();
    }
}
