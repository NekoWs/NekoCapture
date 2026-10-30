using System.Windows.Controls;
using FrameCapture.Core;

namespace FrameCapture.Overlays;

/// <summary>设置页目录与兼容入口，设置内容按需创建。</summary>
public sealed partial class SettingsWindow
{
    private sealed record SettingsPage(string Id, string? Parent, string Root, Str Title, Str Summary,
        string Glyph, int Category, Action<StackPanel> Build, Func<string>? Name = null)
    {
        /// <summary>当前语言或用户自定义的页名。</summary>
        public string Label => Name?.Invoke() ?? Loc.T(Title);
    }

    private readonly Dictionary<string, SettingsPage> _settingsPages = new();
    private readonly Dictionary<string, StackPanel> _settingsBodies = new();
    private readonly Dictionary<string, double> _settingsScroll = new();
    private readonly List<string> _settingsTrail = new();
    private string _currentSettingsPage = "";
    private string? _restoreSettingsPage;

    private static string LegacySettingsPage(int index) => index switch
    {
        1 => "behaviors", 2 => "capture", 3 => "record", 4 => "files", 5 => "editing",
        6 => "pin", 7 => "appearance", 8 => "palette", 9 => "system", 10 => "about", _ => "hotkeys",
    };

    private string ResolveSettingsPage(string? page)
    {
        if (page != null && _settingsPages.ContainsKey(page)) return page;
        return LegacySettingsPage(ParsePage(page));
    }

    private void InitializeSettingsCatalog()
    {
        void Page(string id, string? parent, string root, Str title, Str summary, string glyph, int category, Action<StackPanel> build)
            => _settingsPages[id] = new(id, parent, root, title, summary, glyph, category, build);
        void Group(string id, Str title, string glyph, int category)
            => Page(id, null, id, title, Str.Empty, glyph, category, p => BuildSettingsGroup(p, id));
        Group("capture", K.Common.Capture, "\uE7A8", 2);
        Group("record", K.Common.Recording, "\uE714", 3);
        Group("appearance", K.Common.Appearance, "\uE790", 7);
        Group("system", K.Settings.System, "\uE713", 9);
        Page("hotkeys", null, "hotkeys", K.Settings.Hotkeys, Str.Empty, "\uE765", 0, BuildHotkeys);
        Page("presets", "hotkeys", "hotkeys", K.SettingsNavigation.Presets, K.SettingsNavigation.PresetsHint, "\uE8A5", 0,
            p => p.Children.Add(BuildBehaviorPresets()));
        Page("ui-keys", "hotkeys", "hotkeys", K.SettingsNavigation.UiKeys, K.SettingsNavigation.UiKeysHint, "\uE765", 0, BuildInterfaceKeys);
        Page("behaviors", "hotkeys", "hotkeys", K.SettingsNavigation.Shared, K.SettingsNavigation.SharedHint, "\uE7A8", 1, BuildSharedBehaviorList);
        Page("flow", "behaviors", "hotkeys", K.SettingsNavigation.Execute, K.SettingsNavigation.ExecuteHint, "\uE7A8", 1, p =>
        {
            BuildBehaviors(p);
            if (_behaviorHeader != null) _behaviorHeader.Visibility = System.Windows.Visibility.Collapsed;
        });
        Page("selection", "capture", "capture", K.SettingsNavigation.Selection, K.SettingsNavigation.SelectionHint, "\uE7A8", 2, BuildSelectionSettings);
        Page("feedback", "capture", "capture", K.SettingsNavigation.Feedback, K.SettingsNavigation.FeedbackHint, "\uE946", 2, BuildCaptureFeedback);
        Page("editing", "capture", "capture", K.SettingsNavigation.Editing, K.SettingsNavigation.EditingHint, "\uE70F", 5, p =>
        {
            p.Children.Add(ToggleRow("\uEDD7", Loc.T(K.Settings.RememberTheLastToolColor), Loc.T(K.Settings.ClosingTheEditorRemembers),
                _settings.Current.RememberEditorTools, v => _settings.Mutate(s => s.RememberEditorTools = v)));
            Cardify(p);
        });
        Page("ocr", "capture", "capture", K.SettingsNavigation.Recognition, K.SettingsNavigation.OcrHint, "\uE8C1", 5, p =>
        {
            BuildOcrSettings(p);
            p.Children.Add(ToggleRow("\uE8C1", Loc.T(K.Settings.ShowTheResultWindowAfter), Loc.T(K.Settings.AfterASuccessfulRecognition),
                _settings.Current.OcrShowResult, v => _settings.Mutate(s => s.OcrShowResult = v)));
            Cardify(p);
        });
        Page("pin", "capture", "capture", K.Common.Pin, K.SettingsNavigation.PinHint, "\uE718", 6, p =>
        {
            p.Children.Add(ToggleRow("\uE718", Loc.T(K.Settings.PinSnapping), Loc.T(K.Settings.DraggingAPinSnapsIt),
                _settings.Current.PinSnapEdges, v => _settings.Mutate(s => s.PinSnapEdges = v)));
            Cardify(p);
        });
        Page("files", "capture", "capture", K.SettingsNavigation.Files, K.SettingsNavigation.FilesHint, "\uE74E", 4, p =>
        {
            p.Children.Add(UiNote(Loc.T(K.SettingsNavigation.SharedFiles)));
            p.Children.Add(ToggleRow("\uE74E", Loc.T(K.Settings.AutoSaveACopy), Loc.T(K.Settings.AfterACaptureOnTop),
                _settings.Current.AutoSaveCopy, v => _settings.Mutate(s => s.AutoSaveCopy = v)));
            p.Children.Add(PathRow()); Cardify(p);
        });
        Page("record-start", "record", "record", K.Common.RecordStartLabel, K.SettingsNavigation.RecordStartHint, "\uE768", 3, p =>
        {
            p.Children.Add(ToggleRow("\uE768", Loc.T(K.Settings.StartRecordingAfterSelecting), Loc.T(K.Settings.StartRecordingAfterSelectingTip),
                _settings.Current.RecordNeedsStartClick, v => _settings.Mutate(s => s.RecordNeedsStartClick = v)));
            Cardify(p);
            p.Children.Add(SettingsEntry(Loc.T(K.SettingsNavigation.Execute), Loc.T(K.SettingsNavigation.ExecuteHint), "\uE7A8",
                () => NavigateSettings("behaviors", true)));
        });
        Page("record-quality", "record", "record", K.SettingsNavigation.Quality, K.SettingsNavigation.QualityHint, "\uEC4A", 3, BuildRecordingQuality);
        Page("record-export", "record", "record", K.SettingsNavigation.Export, K.SettingsNavigation.ExportHint, "\uE74E", 3, p =>
        {
            p.Children.Add(RecordFormatRow()); Cardify(p);
            p.Children.Add(SettingsPageEntry("codecs")); p.Children.Add(SettingsPageEntry("files"));
        });
        Page("codecs", "record-export", "record", K.Common.VideoComponent, K.Common.VideoComponentDescription, "\uE714", 3, p =>
        { p.Children.Add(VideoComponentRow()); Cardify(p); });
        Page("record-feedback", "record", "record", K.SettingsNavigation.RecordFeedback, K.SettingsNavigation.RecordFeedbackHint, "\uE714", 3, BuildRecordingFeedback);
        Page("theme", "appearance", "appearance", K.SettingsNavigation.Theme, K.SettingsNavigation.ThemeHint, "\uE793", 7, p =>
        { p.Children.Add(ThemeRow()); p.Children.Add(LanguageRow()); Cardify(p); });
        Page("palette", "appearance", "appearance", K.Common.Palette, K.SettingsNavigation.PaletteHint, "\uE790", 8, BuildPalette);
        Page("colors", "palette", "appearance", K.SettingsNavigation.Colors, K.SettingsNavigation.ColorsHint, "\uE790", 8, BuildCustomColors);
        Page("startup", "system", "system", K.SettingsNavigation.Startup, K.SettingsNavigation.StartupHint, "\uE713", 9, p =>
        {
            p.Children.Add(StartupOptions());
            p.Children.Add(ToggleRow("\uE8B9", Loc.T(K.Settings.CaptureTheAppItself), Loc.T(K.Settings.WhenOnTheEditorTheRecord),
                _settings.Current.CaptureSelf, v => { _settings.Mutate(s => s.CaptureSelf = v); foreach (System.Windows.Window w in System.Windows.Application.Current.Windows) ApplyCaptureAffinity(w); }));
            Cardify(p);
        });
        Page("welcome", "system", "system", K.Settings.StartupNotification, K.SettingsNavigation.WelcomeHint, "\uE946", 9, p =>
        {
            p.Children.Add(ToggleRow("\uE946", Loc.T(K.Settings.StartupNotification), Loc.T(K.Settings.AfterStartupANoticeAppears),
                _settings.Current.StartupToast, v => _settings.Mutate(s => s.StartupToast = v)));
            p.Children.Add(ToggleRow("\uE765", Loc.T(K.Settings.ShowTheKeysInTheReady), Loc.T(K.Settings.TheReadyCardLists),
                _settings.Current.WelcomeShowsHotkeys, v => _settings.Mutate(s => s.WelcomeShowsHotkeys = v)));
            Cardify(p);
        });
        Page("about", null, "about", K.About.Title, Str.Empty, "\uE946", 10, BuildAbout);
        Page("updates", "about", "about", K.Updates.Software, K.SettingsNavigation.UpdatesHint, "\uE946", 10, p =>
        {
            p.Children.Add(AboutCard(AboutUpdateOptions()));
            p.Children.Add(AboutCard(AutoUpdateOption()));
        });
        Page("project", "about", "about", K.SettingsNavigation.Project, K.SettingsNavigation.ProjectHint, "\uE8A5", 10, BuildAboutProject);
        InitializeSettingsSearch();
    }
}
