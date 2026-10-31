using System.Windows;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>录制面板独立停止、避让并标识并行会话，准备键交给最近的待确认面板。</summary>
public sealed partial class RecordPanelWindow
{
    private static readonly List<RecordPanelWindow> Panels = new();
    private static int _nextSessionId;
    private readonly int _sessionId = ++_nextSessionId;

    private static void RegisterPanel(RecordPanelWindow window)
    {
        Panels.Add(window); _current = window; RefreshSessionLabels();
    }

    private static void UnregisterPanel(RecordPanelWindow window)
    {
        Panels.Remove(window); _current = Panels.LastOrDefault();
        RefreshReadyKeys(); RefreshSessionLabels();
    }

    private static void RefreshReadyKeys()
    {
        var ready = Panels.LastOrDefault(panel => panel._armed && !panel._finished);
        Services.HotkeyManager.ArmedRecordKey = ready == null ? null : ready.HandleReadyKey;
    }

    private static void RefreshSessionLabels()
    {
        foreach (var panel in Panels)
            panel._recordStatus.Text = Panels.Count > 1 ? Loc.F(K.Capture.RecordingSession, panel._sessionId) : Loc.T(K.Capture.RecordActive);
    }

    private Rect SeparatePanel(Rect preferred)
    {
        bool Free(Rect candidate) => Panels.All(other => ReferenceEquals(other, this) || !other.IsVisible
            || !candidate.IntersectsWith(new Rect(other.Left, other.Top, other.Width, other.Height)));
        if (Free(preferred)) return preferred;
        for (int step = 1; step <= Panels.Count; step++)
            foreach (int sign in new[] { 1, -1 })
            {
                var candidate = preferred; candidate.Y += sign * step * (preferred.Height + 8);
                if (_workDip.Contains(candidate) && Free(candidate)) return candidate;
            }
        return preferred;
    }
}
