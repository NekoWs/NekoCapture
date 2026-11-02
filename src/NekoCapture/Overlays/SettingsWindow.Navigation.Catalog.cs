using System.Windows.Controls;
using NekoCapture.Core;
using NekoCapture.Encoding;
using NekoCapture.Services;

namespace NekoCapture.Overlays;

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

    /// <summary>左侧导航的七个根：图标区只放这一层，子页走页内分页条。</summary>
    private static readonly string[] SettingsRoots =
        { "hotkeys", "tray", "capture", "record", "appearance", "system", "about" };

    /// <summary>根 → 点进来落在哪一页；子页不单独出现在导航里。</summary>
    private static readonly Dictionary<string, string> SettingsLanding = new()
    {
        ["hotkeys"] = "hotkeys", ["tray"] = "tray", ["capture"] = "selection", ["record"] = "record-start",
        ["appearance"] = "theme", ["system"] = "startup", ["about"] = "about",
    };

    /// <summary>根 → 页内分页条上的页，按声明顺序排列。</summary>
    private readonly Dictionary<string, List<string>> _settingsTabsOf = new();

    /// <summary>页标识的别名：搜索、外部跳转与脚本参数都还能落在对的地方。</summary>
    private static readonly Dictionary<string, string> SettingsAliases = new()
    {
        ["system"] = "startup", ["codecs"] = "record-export",
    };

    private static string LegacySettingsPage(int index) => index switch
    {
        1 => "behaviors", 2 => "capture", 3 => "record", 4 => "files", 5 => "editing",
        6 => "pin", 7 => "appearance", 8 => "palette", 9 => "system", 10 => "about", _ => "hotkeys",
    };

    private string ResolveSettingsPage(string? page)
    {
        if (page != null && _settingsPages.ContainsKey(page)) return page;
        if (page != null && SettingsAliases.TryGetValue(page, out var alias)) return alias;
        return LegacySettingsPage(ParsePage(page));
    }

    private void InitializeSettingsCatalog()
    {
        void Page(string id, string? parent, string root, Str title, Str summary, string glyph, int category, Action<StackPanel> build)
        {
            _settingsPages[id] = new(id, parent, root, title, summary, glyph, category, build);
            if (!_settingsTabsOf.TryGetValue(root, out var list)) _settingsTabsOf[root] = list = new();
            if (id != "flow" && !list.Contains(id)) list.Add(id);   // 行为详情不是分页条上的兄弟页
        }
        Page("hotkeys", null, "hotkeys", K.Settings.Hotkeys, Str.Empty, "\uE765", 0, BuildHotkeys);
        Page("presets", "hotkeys", "hotkeys", K.SettingsNavigation.Presets, K.SettingsNavigation.PresetsHint, "\uE8A5", 0,
            p => p.Children.Add(BuildBehaviorPresets()));
        Page("ui-keys", "hotkeys", "hotkeys", K.SettingsNavigation.UiKeys, K.SettingsNavigation.UiKeysHint, "\uE765", 0, BuildInterfaceKeys);
        Page("behaviors", "hotkeys", "hotkeys", K.SettingsNavigation.Execute, K.SettingsNavigation.SharedHint, "\uE7A8", 1, BuildSharedBehaviorList);
        Page("flow", "behaviors", "hotkeys", K.SettingsNavigation.Execute, K.SettingsNavigation.ExecuteHint, "\uE7A8", 1, p =>
        {
            BuildBehaviors(p);
            if (_behaviorHeader != null) _behaviorHeader.Visibility = System.Windows.Visibility.Collapsed;
        });
        Page("tray", null, "tray", K.Tray.Title, K.Triggers.SearchTray, "\uE7B8", 11, BuildTrayPage);
        Page("selection", null, "capture", K.SettingsNavigation.Selection, K.SettingsNavigation.SelectionHint, "\uE7A8", 2, BuildSelectionSettings);
        Page("feedback", "selection", "capture", K.SettingsNavigation.Feedback, K.SettingsNavigation.FeedbackHint, "\uE946", 2, BuildCaptureFeedback);
        Page("editing", "selection", "capture", K.SettingsNavigation.Editing, K.SettingsNavigation.EditingHint, "\uE70F", 5, p =>
        {
            p.Children.Add(ToggleRow("\uEDD7", Loc.T(K.Settings.RememberTheLastToolColor), Loc.T(K.Settings.ClosingTheEditorRemembers),
                _settings.Current.RememberEditorTools, v => _settings.Mutate(s => s.RememberEditorTools = v)));
            Cardify(p);
        });
        Page("ocr", "selection", "capture", K.SettingsNavigation.Recognition, K.SettingsNavigation.OcrHint, "\uE8C1", 5, p =>
        {
            BuildOcrSettings(p);
            p.Children.Add(ToggleRow("\uE8C1", Loc.T(K.Settings.ShowTheResultWindowAfter), Loc.T(K.Settings.AfterASuccessfulRecognition),
                _settings.Current.OcrShowResult, v => _settings.Mutate(s => s.OcrShowResult = v)));
            Cardify(p);
        });
        Page("pin", "selection", "capture", K.Common.Pin, K.SettingsNavigation.PinHint, "\uE718", 6, p =>
        {
            p.Children.Add(ToggleRow("\uE718", Loc.T(K.Settings.PinSnapping), Loc.T(K.Settings.DraggingAPinSnapsIt),
                _settings.Current.PinSnapEdges, v => _settings.Mutate(s => s.PinSnapEdges = v)));
            Cardify(p);
        });
        Page("files", "selection", "capture", K.SettingsNavigation.Files, K.SettingsNavigation.FilesHint, "\uE74E", 4, p =>
        {
            p.Children.Add(UiNote(Loc.T(K.SettingsNavigation.SharedFiles)));
            p.Children.Add(ToggleRow("\uE74E", Loc.T(K.Settings.AutoSaveACopy), Loc.T(K.Settings.AfterACaptureOnTop),
                _settings.Current.AutoSaveCopy, v => _settings.Mutate(s => s.AutoSaveCopy = v)));
            p.Children.Add(PathRow()); Cardify(p);
        });
        Page("record-start", null, "record", K.Common.RecordStartLabel, K.SettingsNavigation.RecordStartHint, "\uE768", 3, p =>
        {
            p.Children.Add(ToggleRow("\uE768", Loc.T(K.Settings.StartRecordingAfterSelecting), Loc.T(K.Settings.StartRecordingAfterSelectingTip),
                _settings.Current.RecordNeedsStartClick, v => _settings.Mutate(s => s.RecordNeedsStartClick = v)));
            Cardify(p);
        });
        Page("record-quality", "record-start", "record", K.SettingsNavigation.Quality, K.SettingsNavigation.QualityHint, "\uEC4A", 3, BuildRecordingQuality);
        Page("record-export", "record-start", "record", K.SettingsNavigation.Export, K.SettingsNavigation.ExportHint, "\uE74E", 3, p =>
        {
            p.Children.Add(RecordFormatRow()); Cardify(p);
            p.Children.Add(SectionHeader(Loc.T(K.Common.VideoComponent)));
            p.Children.Add(UiNote(Loc.T(K.Common.VideoComponentDescription)));
            foreach (var format in new[] { VideoFormat.Mp4, VideoFormat.WebM })
                p.Children.Add(VideoComponentRow(VideoComponentInstall.For(format)));
            Cardify(p);
            p.Children.Add(SettingsPageEntry("files"));
        });
        Page("record-feedback", "record-start", "record", K.SettingsNavigation.RecordFeedback, K.SettingsNavigation.RecordFeedbackHint, "\uE714", 3, BuildRecordingFeedback);
        Page("theme", null, "appearance", K.SettingsNavigation.Theme, K.SettingsNavigation.ThemeHint, "\uE793", 7, p =>
        { p.Children.Add(ThemeRow()); p.Children.Add(LanguageRow()); Cardify(p); });
        Page("palette", "theme", "appearance", K.Common.Palette, K.SettingsNavigation.PaletteHint, "\uE790", 8, BuildPalette);
        Page("colors", "theme", "appearance", K.SettingsNavigation.Colors, K.SettingsNavigation.ColorsHint, "\uE790", 8, BuildCustomColors);
        Page("startup", null, "system", K.SettingsNavigation.Startup, K.SettingsNavigation.StartupHint, "\uE713", 9, p =>
        {
            p.Children.Add(StartupOptions());
            Cardify(p);
        });
        Page("welcome", "startup", "system", K.Settings.StartupNotification, K.SettingsNavigation.WelcomeHint, "\uE946", 9, p =>
        {
            p.Children.Add(ToggleRow("\uE946", Loc.T(K.Settings.StartupNotification), Loc.T(K.Settings.AfterStartupANoticeAppears),
                _settings.Current.StartupToast, v => _settings.Mutate(s => s.StartupToast = v)));
            p.Children.Add(ToggleRow("\uE765", Loc.T(K.Settings.ShowTheKeysInTheReady), Loc.T(K.Settings.TheReadyCardLists),
                _settings.Current.WelcomeShowsHotkeys, v => _settings.Mutate(s => s.WelcomeShowsHotkeys = v)));
            p.Children.Add(WelcomeDurationRow());
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
