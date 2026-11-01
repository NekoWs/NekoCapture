using NekoCapture.Core;

namespace NekoCapture.Overlays;

/// <summary>设置名与中英文近义词共用本地索引。</summary>
public sealed partial class SettingsWindow
{
    private void InitializeSettingsSearch()
    {
        void Index(string page, Str title, params Str[] aliases) => _settingsSearchIndex.Add(new(page, title, aliases));
        foreach (var page in _settingsPages.Values.Where(page => page.Parent != null && page.Id != "flow"))
            Index(page.Id, page.Title);
        Index("hotkeys", K.Settings.Hotkeys, K.SettingsNavigation.SearchKeys);
        Index("hotkeys", K.Hotkeys.EnableHotkeys, K.SettingsNavigation.SearchKeys);
        Index("ui-keys", K.Hotkeys.UiKeys, K.SettingsNavigation.SearchKeys);
        Index("ui-keys", K.Hotkeys.PickerNudge, K.SettingsNavigation.SearchPointer);
        Index("files", K.Settings.Output, K.SettingsNavigation.SearchFiles);
        Index("behaviors", K.Behaviors.Title, K.SettingsNavigation.SearchBehavior);
        Index("hotkeys", K.Hotkeys.Trigger, K.SettingsNavigation.SearchKeys);
        Index("behaviors", K.SettingsNavigation.Execute, K.SettingsNavigation.SearchBehavior);
        Index("selection", K.Settings.SnapToControls, K.SettingsNavigation.SearchSnap);
        Index("selection", K.Settings.SnapDwell, K.SettingsNavigation.SearchSnap, K.SettingsNavigation.SearchDelay);
        Index("selection", K.Settings.FreezeTheScreenWhileSelecting);
        Index("selection", K.Settings.WindowsPreCapturedOnFreeze);
        Index("selection", K.Settings.RightClickCancelsPicker);
        Index("selection", K.Settings.ClickAWindowToCapture);
        Index("selection", K.Settings.IncludeTheShadowInWindow);
        Index("selection", K.Settings.ShowTheLastSelection);
        Index("selection", K.Settings.SelectionMagnifier);
        Index("selection", K.Common.DelayedCapture, K.SettingsNavigation.SearchDelay);
        Index("feedback", K.Settings.AlwaysShowTheCaptureOutline);
        Index("feedback", K.Settings.ToastLifetime);
        Index("feedback", K.Settings.CaptureSound, K.SettingsNavigation.SearchSound);
        Index("editing", K.Settings.RememberTheLastToolColor);
        Index("ocr", K.Ocr.Engine, K.SettingsNavigation.SearchOcr, K.SettingsNavigation.SearchModels);
        Index("ocr", K.Ocr.Uninstall, K.SettingsNavigation.SearchModels);
        Index("ocr", K.Settings.ShowTheResultWindowAfter, K.SettingsNavigation.SearchOcr);
        Index("pin", K.Settings.PinSnapping, K.SettingsNavigation.SearchPin, K.SettingsNavigation.SearchSnap);
        Index("files", K.Settings.AutoSaveACopy, K.SettingsNavigation.SearchFiles);
        Index("files", K.SettingsNavigation.Files, K.SettingsNavigation.SearchFiles);
        Index("record-start", K.Settings.StartRecordingAfterSelecting);
        Index("record-quality", K.Settings.RecordingFrameRate, K.SettingsNavigation.SearchQuality);
        Index("record-quality", K.Settings.RecordAtFullMonitorRefresh, K.SettingsNavigation.SearchQuality);
        Index("record-quality", K.Settings.RecordWindowOnly);
        Index("record-export", K.Settings.RecordingFormat);
        Index("codecs", K.Common.VideoComponent, K.SettingsNavigation.SearchCodec);
        Index("record-feedback", K.Settings.ShowTheRegionBorderWhile);
        Index("record-feedback", K.Settings.IncludeTheMousePointer);
        Index("theme", K.Settings.AppearanceTheme, K.SettingsNavigation.SearchTheme);
        Index("theme", K.Settings.Language);
        Index("palette", K.Settings.Accent);
        foreach (var token in Theme.Tokens) Index("colors", TokenLabel(token.Key));
        Index("startup", K.Common.StartWithWindows, K.SettingsNavigation.SearchStartup);
        Index("startup", K.Common.RunAsAdministrator, K.SettingsNavigation.SearchAdmin);
        Index("welcome", K.Settings.StartupNotification);
        Index("welcome", K.Settings.ShowTheKeysInTheReady, K.SettingsNavigation.SearchKeys);
        Index("record-quality", K.Settings.CaptureTheAppItself, K.Settings.WhenOnTheEditorTheRecord);
        Index("updates", K.Updates.AutoCheck);
        Index("updates", K.Updates.Check);
    }
}
