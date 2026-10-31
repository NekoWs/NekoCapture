using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FrameCapture.Core;
using FrameCapture.Overlays;
using FrameCapture.Services;

static partial class SelfTest
{
    private static void SettingsHierarchyChecks()
    {
        _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var hostSettings = typeof(AppHost).GetProperty("Settings")!;
        object? prior = hostSettings.GetValue(AppHost.Current);
        var settings = new SettingsService();
        hostSettings.SetValue(AppHost.Current, settings);
        Theme.Apply("dark", settings.Current.Accent); Loc.Apply(Loc.PrefZh); Fluent.ApplyGlobalStyles();
        var window = new SettingsWindow();
        object? Field(string name) => typeof(SettingsWindow).GetField(name, flags)!.GetValue(window);
        object? Invoke(string name, params object?[] args) => typeof(SettingsWindow).GetMethod(name, flags)!.Invoke(window, args);
        void Go(string id, bool animate = false) { Invoke("NavigateSettings", id, animate, true); PumpMs(animate ? Fluent.Motion.QuickMs + Fluent.Motion.EnterMs + 100 : 30); window.UpdateLayout(); }
        void Snapshot(string name)
        {
            PumpMs(Fluent.Motion.EnterMs);
            window.UpdateLayout(); var visual = (FrameworkElement)window.Content;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string directory = System.IO.Path.Combine(SettingsService.Dir, "previews"); System.IO.Directory.CreateDirectory(directory);
            using var stream = System.IO.File.Create(System.IO.Path.Combine(directory, name + ".png")); encoder.Save(stream);
        }
        try
        {
            Invoke("Build"); ShowOffscreen(window); PumpMs(150);
            var bodies = (IDictionary)Field("_settingsBodies")!;
            var navigation = (IDictionary)Field("_settingsNav")!;
            Chk("settings-hierarchy-six-root-entries", navigation.Count == 6
                && navigation.Contains("hotkeys") && navigation.Contains("capture") && navigation.Contains("record")
                && navigation.Contains("appearance") && navigation.Contains("system") && navigation.Contains("about"));
            Chk("settings-hierarchy-loads-only-current-page", bodies.Count == 1 && bodies.Contains("hotkeys"));
            Go("capture");
            var capture = (FrameworkElement)Invoke("GetSettingsBody", "capture")!;
            Chk("settings-hierarchy-category-summaries-have-no-values", !AllText(capture).Any(t => t.Text.Contains("50 ms") || t.Text.Contains("30 fps")));
            Go("capture"); Snapshot("capture");
            foreach (string id in new[] { "selection", "feedback", "editing", "ocr", "pin", "files", "record-start", "record-quality",
                "record-export", "record-feedback", "codecs", "theme", "palette", "colors", "startup", "welcome", "about", "updates", "project", "ui-keys", "presets", "behaviors" })
                Go(id);
            Chk("settings-hierarchy-all-feature-pages-build", bodies.Count >= 23);
            var search = (TextBox)Field("_settingsSearchBox")!;
            foreach (var (query, page) in new[] { ("热键", "hotkeys"), ("静音", "feedback"), ("关闭声音", "feedback"),
                ("提权", "startup"), ("磁吸", "selection"), ("保存目录", "files"), ("画质", "record-quality"), ("OCR", "ocr") })
            {
                search.Text = query; PumpMs(20);
                var matches = ((IEnumerable)Invoke("FindSettingsMatches")!).Cast<object>().ToArray();
                Chk("settings-search-synonym-" + query, matches.Any(item => (string)item.GetType().GetProperty("Page")!.GetValue(item)! == page));
            }
            search.Text = "静音"; PumpMs(50); Snapshot("search");
            var first = ((IEnumerable)Invoke("FindSettingsMatches")!).Cast<object>().First();
            Invoke("OpenSettingsSearchResult", first); PumpMs(Fluent.Motion.QuickMs + Fluent.Motion.EnterMs + 80);
            Chk("settings-search-opens-setting-and-clears-query", (string)Field("_currentSettingsPage")! == "feedback" && search.Text.Length == 0);
            search.Text = "unmatched-setting-xyz"; PumpMs(20);
            var searchBody = (FrameworkElement)bodies["search"]!;
            Chk("settings-search-empty-result-message", AllText(searchBody).Any(text => text.Text == Loc.T(K.SettingsNavigation.NoResults)));
            search.Clear(); PumpMs(100);
            var binding = settings.Current.Gestures.Bindings.First();
            Invoke("OpenSettingsBinding", binding); PumpMs(Fluent.Motion.QuickMs + 50);
            Chk("settings-shortcut-has-independent-detail", (string)Field("_currentSettingsPage")! == "binding:" + binding.Id);
            Invoke("OpenSettingsFlow", binding.EntryId); PumpMs(Fluent.Motion.QuickMs + Fluent.Motion.EnterMs + 80);
            Chk("settings-shortcut-opens-actual-shared-flow", (string)Field("_behaviorFlow")! == binding.EntryId
                && (string)Field("_currentSettingsPage")! == "flow" && (int)Field("_cat")! == 1);
            Go("capture", true); Go("hotkeys", true); Go("system", true);
            Chk("settings-navigation-only-current-page-visible", bodies.Values.Cast<StackPanel>().Count(p => p.IsVisible) == 1
                && ((StackPanel)bodies["system"]!).IsHitTestVisible);
            Go("about"); Theme.Apply("light", settings.Current.Accent); PumpMs(Fluent.Motion.ThemeMs + 50); Snapshot("about-light");
            Chk("settings-navigation-light-theme-builds", AllText((FrameworkElement)bodies["about"]!).Any(t => t.Text == Loc.T(K.About.Product)));
            SettingsPolishChecks(window, settings, Snapshot);
            SettingsOverviewChecks(window, settings);
            SettingsPaletteChecks(window, settings, Snapshot);
        }
        catch (Exception ex) { Chk("settings-hierarchy-exception", false, ex.ToString()); }
        finally { window.Close(); hostSettings.SetValue(AppHost.Current, prior); }
    }

    private static IEnumerable<TextBlock> AllText(DependencyObject parent)
    {
        if (parent is TextBlock text) yield return text;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in AllText(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
