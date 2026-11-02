using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using NekoCapture.Core;
using NekoCapture.Native;
using NekoCapture.Services;

static partial class SelfTest
{
    private static void TrayMenuChecks()
    {
        _ = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
        var settingsProperty = typeof(AppHost).GetProperty("Settings")!;
        var previousSettings = settingsProperty.GetValue(AppHost.Current);
        bool english = Loc.IsEnglish, dark = Theme.IsDark; string accent = Theme.Accent.ToString();
        var saving = typeof(ResidentBridge).GetField("_saving", statics)!;
        bool previousSaving = (bool)saving.GetValue(null)!;
        var deferred = (Queue<Action>)typeof(ResidentBridge).GetField("Deferred", statics)!.GetValue(null)!;
        var previousRequests = deferred.ToArray(); deferred.Clear();
        TrayService? tray = null;
        object? Field(string name) => typeof(TrayService).GetField(name, instance)!.GetValue(tray);
        try
        {
            settingsProperty.SetValue(AppHost.Current, new SettingsService());
            Theme.Apply("dark", "#4CC2FF"); Loc.Apply(Loc.PrefZh); Fluent.ApplyGlobalStyles();
            tray = new TrayService(AppHost.Current);
            var foreground = Win32.GetForegroundWindow();
            using var process = Process.GetCurrentProcess(); process.Refresh(); long before = process.PrivateMemorySize64;
            var watch = Stopwatch.StartNew(); tray.PrepareMenu(); double firstMs = watch.Elapsed.TotalMilliseconds;
            process.Refresh(); long delta = process.PrivateMemorySize64 - before;
            var menu = (ContextMenu)Field("_menu")!; var anchor = (Window)Field("_menuAnchor")!;
            Chk("tray-menu-prepares-without-showing-or-activating", !menu.IsOpen && !anchor.IsVisible
                && new WindowInteropHelper(anchor).Handle == IntPtr.Zero && Win32.GetForegroundWindow() == foreground);
            Chk("tray-menu-keeps-six-actions-and-separator", menu.Items.OfType<MenuItem>().Count() == 6
                && menu.Items.OfType<Separator>().Count() == 1);
            Chk("tray-menu-disables-overlapping-system-popup-animation",
                Equals(menu.Resources[SystemParameters.MenuPopupAnimationKey], PopupAnimation.None)
                && Equals(anchor.Resources[SystemParameters.MenuPopupAnimationKey], PopupAnimation.None));
            long allocated = GC.GetAllocatedBytesForCurrentThread(); watch.Restart();
            for (int i = 0; i < 100; i++) tray.PrepareMenu();
            double warmMs = watch.Elapsed.TotalMilliseconds / 100;
            long warmAllocation = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Chk("tray-menu-warm-preparation-reuses-layout-and-window", ReferenceEquals(menu, Field("_menu"))
                && ReferenceEquals(anchor, Field("_menuAnchor")) && warmAllocation < 1024);
            Console.WriteLine($"TRAY-MENU prepare_first_ms={firstMs:F2} prepare_warm_ms={warmMs:F4} private_delta={delta}");
            Loc.Apply(Loc.PrefEn); tray.PrepareMenu();
            var translated = (ContextMenu)Field("_menu")!;
            Chk("tray-menu-language-change-refreshes-cached-items", !ReferenceEquals(menu, translated)
                && translated.Items.OfType<MenuItem>().Any(item => Equals(item.Header, Loc.T(K.Common.Settings))));
            var resources = translated.Resources;
            Theme.Apply("light", "#4CC2FF"); tray.PrepareMenu();
            Chk("tray-menu-theme-change-refreshes-cached-style", !ReferenceEquals(resources, ((ContextMenu)Field("_menu")!).Resources));

            var settings = AppHost.Current.Settings;
            var custom = settings.Current.Gestures.Bindings.First(b => b.Source == TriggerSources.TrayMenu);
            settings.Mutate(_ => custom.Name = "custom menu"); tray.PrepareMenu();
            Chk("tray-menu-renaming-refreshes-visible-label", ((ContextMenu)Field("_menu")!).Items.OfType<MenuItem>()
                .Any(item => Equals(item.Header, "custom menu")));
            settings.Mutate(_ => custom.Enabled = false); tray.PrepareMenu();
            Chk("tray-menu-disabled-trigger-remains-disabled", ((ContextMenu)Field("_menu")!).Items.OfType<MenuItem>()
                .Single(item => Equals(item.Header, "custom menu")).IsEnabled == false);
            settings.Mutate(s => s.Gestures.Bindings.Remove(custom)); tray.PrepareMenu();
            Chk("tray-menu-deleted-trigger-is-removed", ((ContextMenu)Field("_menu")!).Items.OfType<MenuItem>().Count() == 5
                && !((ContextMenu)Field("_menu")!).Items.OfType<MenuItem>().Any(item => Equals(item.Header, "custom menu")));
            settings.Mutate(s => s.Gestures.Bindings.Add(new GestureBindingDto { Id = "test.menu", Name = "added menu",
                Source = TriggerSources.TrayMenu, EntryId = TrayDefaults.Clipboard })); tray.PrepareMenu();
            Chk("tray-menu-user-trigger-is-added", ((ContextMenu)Field("_menu")!).Items.OfType<MenuItem>().Any(item => Equals(item.Header, "added menu")));
            var created = settings.Current.Gestures.AddTrayMenu("independent menu");
            settings.Mutate(_ => { }); tray.PrepareMenu();
            MenuItem CreatedItem() => ((ContextMenu)Field("_menu")!).Items.OfType<MenuItem>().Single(item => Equals(item.Header, created.Name));
            Chk("tray-menu-empty-custom-flow-is-visible-and-disabled", !CreatedItem().IsEnabled);
            settings.Mutate(s => s.Gestures.Graph.Flow(created.EntryId)!.Steps.Add(s.Gestures.Graph.Add(GestureActions.ReadClipboardImage).Id));
            tray.PrepareMenu();
            Chk("tray-menu-editing-own-flow-enables-item", CreatedItem().IsEnabled && ((TextBlock)CreatedItem().Icon).Text == "\uE718");
            settings.Mutate(_ => created.MenuIcon = "record"); tray.PrepareMenu();
            Chk("tray-menu-icon-change-invalidates-cache", ((TextBlock)CreatedItem().Icon).Text == "\uE714");
            settings.Mutate(_ => created.MenuIcon = ""); tray.PrepareMenu();
            Chk("tray-menu-auto-icon-can-be-restored", ((TextBlock)CreatedItem().Icon).Text == "\uE718");
            settings.Mutate(s => s.Gestures.Graph.Flows.RemoveAll(flow => flow.Id == created.EntryId)); tray.PrepareMenu();
            Chk("tray-menu-unlinked-item-stays-visible-and-disabled", !CreatedItem().IsEnabled);

            saving.SetValue(null, true); var order = new List<int>();
            ResidentBridge.RunTrayAction(() => order.Add(1)); ResidentBridge.RunTrayAction(() => order.Add(2));
            Chk("tray-menu-actions-wait-for-session-save", order.Count == 0 && deferred.Count == 2);
            saving.SetValue(null, false); while (deferred.TryDequeue(out var request)) request();
            Chk("tray-menu-deferred-actions-retain-order", order.SequenceEqual(new[] { 1, 2 }));
            tray.Dispose();
            Chk("tray-menu-dispose-releases-cached-window", Field("_menu") == null && Field("_menuAnchor") == null);
        }
        catch (Exception error) { Chk("tray-menu-exception", false, error.ToString()); }
        finally
        {
            tray?.Dispose(); saving.SetValue(null, previousSaving);
            deferred.Clear(); foreach (var request in previousRequests) deferred.Enqueue(request);
            settingsProperty.SetValue(AppHost.Current, previousSettings);
            Loc.Apply(english ? Loc.PrefEn : Loc.PrefZh); Theme.Apply(dark ? "dark" : "light", accent);
        }
    }
}
