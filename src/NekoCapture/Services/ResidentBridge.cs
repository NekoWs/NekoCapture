using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using NekoCapture.Core;
using NekoCapture.Overlays;
using NekoCapture.Runtime;
using Trace = NekoCapture.Core.Trace;

namespace NekoCapture.Services;

/// <summary>预热界面服务、同步输入快照并整理空闲缓存；常驻进程独立维护托盘和全局手势。</summary>
internal static class ResidentBridge
{
    private static readonly string[] Arguments = Environment.GetCommandLineArgs();
    /// <summary>当前进程由轻量宿主按需启动。</summary>
    internal static bool IsWorker { get; } = Arguments.Contains(ResidentProtocol.WorkerFlag);
    /// <summary>本配置的界面互斥名称。</summary>
    internal static string UiMutexName => @"Local\NekoCapture.SingleInstance" + (IsWorker ? ".Worker" + ResidentProtocol.Suffix : "");
    private static string ResidentPath => Environment.GetEnvironmentVariable("NC_TEST_NO_FG") == "1"
        && Environment.GetEnvironmentVariable("NC_RESIDENT_PATH") is { Length: > 0 } path ? path
            : Path.Combine(AppContext.BaseDirectory, "NekoCapture.Resident.exe");
    private static readonly Queue<Action> Deferred = new();
    private static DispatcherTimer? _idle;
    private static bool _saving, _retiring, _idlePrepared;
    private static long _nextMemoryCheck;
    private const long WarmWorkerBudget = 88L << 20;
    private static long _activity, _quietSince;
    private static SettingsService? _settings;

    /// <summary>将常规启动交给原生宿主；无原生组件或隔离测试未显式启用时保留单进程模式。</summary>
    /// <param name="arguments">应用参数。</param>
    /// <returns>已交给常驻宿主或提权交接时为 true。</returns>
    internal static bool TryBootstrap(string[] arguments)
    {
        bool forced = Environment.GetEnvironmentVariable("NC_RESIDENT_ENABLED") == "1";
        bool scripted = Environment.GetEnvironmentVariable("NC_TEST_NO_FG") == "1"
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NC_SETTINGS_DIR"));
        if (IsWorker || Environment.GetEnvironmentVariable("NC_RESIDENT_DISABLED") == "1" || (scripted && !forced) || !File.Exists(ResidentPath)) return false;
        string[] request = StartupRequest(arguments);
        if (ResidentProtocol.Send(ResidentProtocol.FindHost(), request)) return true;
        try
        {
            using var bootstrap = new Mutex(false, @"Local\NekoCapture.Bootstrap" + ResidentProtocol.Suffix);
            try { if (!bootstrap.WaitOne(5000)) return false; }
            catch (AbandonedMutexException) { }
            try
            {
                if (ResidentProtocol.Send(ResidentProtocol.FindHost(), request)) return true;
                var settings = new SettingsService();
                settings.Current.ApplyPalette(); Loc.Apply(settings.Current.UiLanguage);
                if (StartupService.ShouldElevate(arguments, scripted || Environment.GetEnvironmentVariable("NC_NO_ELEVATE") == "1",
                    StartupService.IsElevated, settings.Current.RunAsAdmin))
                {
                    var change = StartupService.Configure(settings.Current.RunAtStartup, true, true, StartupService.QuoteArguments(arguments), settings);
                    if (change.Result is not (StartupResult.Cancelled or StartupResult.Failed)) return true;
                }
                if (!scripted && StartupService.IsElevated && settings.Current is { RunAtStartup: true, RunAsAdmin: true })
                    StartupService.Apply(true, true);
                WriteManifest(settings);
                using var resident = Process.Start(new ProcessStartInfo(ResidentPath) { UseShellExecute = false, CreateNoWindow = true });
                var wait = Stopwatch.StartNew();
                while (ResidentProtocol.FindHost() == IntPtr.Zero && wait.ElapsedMilliseconds < 3000) Thread.Sleep(20);
                if (!ResidentProtocol.Send(ResidentProtocol.FindHost(), request)) return false;
                if (request.Length == 0 && settings.Current.StartupToast) ResidentProtocol.Send(ResidentProtocol.FindHost(), ["--resident-welcome"]);
                return true;
            }
            finally { bootstrap.ReleaseMutex(); }
        }
        catch (Exception error) { Trace.Log("resident bootstrap: " + error); return false; }
    }

    private static string[] StartupRequest(string[] arguments)
    {
        string[] flags = ["--settings", "--help", "--edit-last", "--scroll", "--delay", "--pin-clip", "--pause-toggle",
            "--ocr", "--welcome", "--picker", "--tray-menu", "--editor", "--pin"];
        int index = Array.FindIndex(arguments, argument => flags.Contains(argument));
        if (Environment.GetEnvironmentVariable("NC_TEST_NO_FG") == "1")
        {
            int test = Array.FindIndex(arguments, argument => argument is "--run-binding" or "--resident-test-key" or "--editorkey" or "--resident-test-stall");
            if (test >= 0) index = test;
        }
        return index < 0 ? [] : arguments[index..];
    }

    /// <summary>连接运行中的界面，恢复惰性产物并发布真实可运行绑定。</summary>
    internal static void InitializeWorker()
    {
        if (!IsWorker) return;
        _settings = AppHost.Current.Settings;
        AppHost.Current.Controller.RestoreResidentSession();
        InputsChanged();
        _settings.Changed += InputsChanged;
        int paused = Array.IndexOf(Arguments, "--resident-paused");
        HotkeyManager.Paused = paused >= 0 && Arguments.ElementAtOrDefault(paused + 1) == "1";
        _quietSince = Environment.TickCount64;
        _idle = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(500) };
        _idle.Tick += IdleTick; _idle.Start();
        ResidentProtocol.Send(ResidentProtocol.FindHost(), ["--resident-ready", Environment.ProcessId.ToString()]);
    }

    private static void InputsChanged()
    {
        try { if (_settings != null) WriteManifest(_settings); }
        catch (Exception error) { Trace.Log("resident inputs: " + error); }
    }

    /// <summary>配色变化时同步常驻托盘图标与加载身份。</summary>
    internal static void RefreshTray() => InputsChanged();

    private static void WriteManifest(SettingsService settings)
    {
        var value = settings.Current;
        Directory.CreateDirectory(SettingsService.Dir);
        TrayService.SaveResidentIcons();
        var clicks = TriggerExecution.TrayClicks(value.Gestures);
        var manifest = new ResidentManifest
        {
            SourceTicks = File.Exists(SettingsService.FilePath) ? File.GetLastWriteTimeUtc(SettingsService.FilePath).Ticks : 0,
            Enabled = value.Gestures.Enabled,
            TraySingle = clicks.Single, TrayDouble = clicks.Double, TrayEquivalent = clicks.Equivalent,
            Inputs = value.Gestures.ToBindings().Select(binding => new ResidentInput
            {
                Id = binding.Id, Key = binding.Chord.Key, Mods = (int)binding.Chord.Mods, Trigger = (int)binding.Trigger,
                Priority = binding.Priority, DoubleClickMs = binding.DoubleClickMs, LongPressMs = binding.LongPressMs
            }).ToArray(),
            Tooltip = TrayService.ActiveTip(value),
            IconRevision = TrayService.ResidentIconRevision,
            PausedTooltip = Loc.T(K.Tray.NekoCapturePausedHotkeysReleased)
        };
        Directory.CreateDirectory(SettingsService.Dir);
        manifest.Signature = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value.Gestures)));
        string json = JsonSerializer.Serialize(manifest, ResidentJson.Default.ResidentManifest);
        if (File.Exists(ResidentProtocol.ManifestPath))
        {
            using var file = new FileStream(ResidentProtocol.ManifestPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(file);
            if (reader.ReadToEnd() == json) return;
        }
        string temporary = ResidentProtocol.ManifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, json);
            File.Move(temporary, ResidentProtocol.ManifestPath, overwrite: true);
        }
        finally { try { File.Delete(temporary); } catch { } }
    }

    /// <summary>受理常驻命令；菜单立即显示，操作产物的请求等待快照保存完成。</summary>
    /// <param name="sender">发送方窗口。</param>
    /// <param name="payload">WM_COPYDATA 参数。</param>
    /// <returns>合法命令已受理时为 true。</returns>
    internal static bool Receive(IntPtr sender, IntPtr payload)
    {
        if (!IsWorker || _retiring || ResidentProtocol.Receive(sender, payload, ResidentPath) is not { } arguments) return false;
        _activity++; _quietSince = Environment.TickCount64;
        _idlePrepared = false;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (arguments.FirstOrDefault() == "--tray-menu") Execute(arguments);
            else if (_saving) Deferred.Enqueue(() => Execute(arguments));
            else Execute(arguments);
        }, arguments.FirstOrDefault() == "--tray-menu" ? DispatcherPriority.Send : DispatcherPriority.Normal);
        return true;
    }

    /// <summary>托盘动作在 UI 线程执行；保存快照期间与其他请求共同按到达顺序等待。</summary>
    /// <param name="action">菜单选择对应的实际动作。</param>
    internal static void RunTrayAction(Action action)
    {
        _activity++; _quietSince = Environment.TickCount64; _idlePrepared = false;
        if (_saving) Deferred.Enqueue(action); else action();
    }

    private static void Execute(string[] arguments)
    {
        var host = AppHost.Current;
        string command = arguments.FirstOrDefault() ?? "";
        switch (command)
        {
            case "--run-binding":
                var dto = host.Settings.Current.Gestures.Bindings.FirstOrDefault(binding => binding.Id == arguments.ElementAtOrDefault(1) && binding.Enabled);
                if (dto?.ToBinding() is { } binding) new GestureInvoker(host).InvokeChain(binding);
                break;
            case "--settings": SettingsWindow.ShowOrFocus(Page(arguments)); break;
            case "--help": HelpWindow.ShowOrFocus(); break;
            case "--edit-last": host.Controller.EditLastStill(); break;
            case "--scroll": host.Controller.BeginScrollCapture(); break;
            case "--delay": host.Controller.BeginDelayedCapture(); break;
            case "--pin-clip": _ = host.Controller.PinFromClipboardAsync(); break;
            case "--pause-toggle": host.Tray.TogglePause(); break;
            case "--ocr": host.Controller.OcrStoredStill(); break;
            case "--resident-welcome":
                bool firstRun = !host.Settings.Current.FirstRunHintShown;
                if (firstRun) host.Settings.Mutate(settings => settings.FirstRunHintShown = true);
                WelcomeToast.Show(host.Settings.Current.Gestures.CaptureKeyText(), host.Settings.Current.Gestures.RecordKeyText());
                break;
            case "--welcome":
                var gestures = host.Settings.Current.Gestures;
                WelcomeToast.Show(gestures.CaptureKeyText(), gestures.RecordKeyText());
                break;
            case "--run-trigger": TriggerExecution.Run(host, arguments.ElementAtOrDefault(1) ?? ""); break;
            case "--picker": host.Controller.BeginScopeSelection(); break;
            case "--tray-menu":
                host.Tray.ShowMenu(long.TryParse(arguments.ElementAtOrDefault(1), out long requestedAt) ? requestedAt : Environment.TickCount64);
                break;
            case "--editor": if (arguments.ElementAtOrDefault(1) is { } png) host.Controller.OpenEditorWithPng(png); break;
            case "--pin": if (arguments.ElementAtOrDefault(1) is { } image) host.Controller.PinWithPng(image); break;
            case "--resident-sync": InputsChanged(); break;
            case "--resident-refresh-hook": host.Hotkeys.Install(); break;
            case "--editorkey":
                if (Environment.GetEnvironmentVariable("NC_TEST_NO_FG") == "1" && int.TryParse(arguments.ElementAtOrDefault(1), out int key))
                    host.Controller.FeedEditorKey(System.Windows.Input.ModifierKeys.None, System.Windows.Input.KeyInterop.KeyFromVirtualKey(key));
                break;
            case "--resident-test-stall":
                if (Environment.GetEnvironmentVariable("NC_TEST_NO_FG") == "1" && int.TryParse(arguments.ElementAtOrDefault(1), out int delay)) Thread.Sleep(Math.Clamp(delay, 0, 1500));
                break;
            case "--resident-exit": Application.Current.Shutdown(); break;
        }
    }

    private static string? Page(string[] arguments)
    {
        int index = Array.IndexOf(arguments, "--page");
        return index >= 0 ? arguments.ElementAtOrDefault(index + 1) : null;
    }

    private static async void IdleTick(object? sender, EventArgs args)
    {
        if (_saving) return;
        bool visible = Application.Current.Windows.Cast<Window>().Any(window => window.IsVisible);
        if (visible || Busy) { _quietSince = Environment.TickCount64; _idlePrepared = false; return; }
        if (Environment.TickCount64 - _quietSince < 3000) return;
        if (_idlePrepared)
        {
            if (Environment.TickCount64 < _nextMemoryCheck) return;
            _nextMemoryCheck = Environment.TickCount64 + 15000;
            using var process = Process.GetCurrentProcess();
            if (process.PrivateMemorySize64 > WarmWorkerBudget)
            { _idle?.Stop(); _retiring = true; Application.Current.Shutdown(); }
            return;
        }
        _saving = true; long generation = _activity;
        try
        {
            await AppHost.Current.Controller.SaveResidentSessionAsync();
            if (generation == _activity && Deferred.Count == 0 && !Busy)
            {
                AppHost.Current.Controller.ReleaseResidentBuffers();
                if (ElementProbe.Stalled)
                { _idle?.Stop(); _retiring = true; Application.Current.Shutdown(); return; }
                Memory.Reclaim(); _idlePrepared = true;
                _nextMemoryCheck = Environment.TickCount64 + 15000;
                Trace.Log("resident idle-ready");
                return;
            }
        }
        catch (Exception error)
        {
            Trace.Log("resident suspend: " + error);
            _quietSince = Environment.TickCount64 + 30000;
        }
        finally { _saving = false; }
        while (Deferred.TryDequeue(out var request)) request();
    }

    private static bool Busy => !AppHost.Current.Controller.CanSuspend || BackgroundWork.Busy || VideoComponentInstall.AnyBusy;

    /// <summary>同步运行时暂停状态；仅传递布尔状态，不传递按键。</summary>
    /// <param name="paused">是否暂停全局手势。</param>
    internal static void PublishPause(bool paused)
    {
        if (IsWorker) ResidentProtocol.Send(ResidentProtocol.FindHost(), ["--resident-pause", paused ? "1" : "0"]);
    }

    /// <summary>权限重启或主动退出前收起原生宿主，下一实例按目标权限重建。</summary>
    internal static void RetireHost()
    {
        if (!IsWorker) return;
        ResidentProtocol.Send(ResidentProtocol.FindHost(), ["--resident-retire"]);
        var wait = Stopwatch.StartNew();
        while (ResidentProtocol.FindHost() != IntPtr.Zero && wait.ElapsedMilliseconds < 1000) Thread.Sleep(10);
    }

    /// <summary>解除当前界面的配置订阅与空闲检查。</summary>
    internal static void StopWorker()
    {
        _idle?.Stop();
        if (_settings != null) _settings.Changed -= InputsChanged;
        _settings = null;
    }
}
