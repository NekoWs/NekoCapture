using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FrameCapture.Runtime;
using GestureCore;

namespace FrameCapture.Resident;

/// <summary>原生消息线程只处理输入；独立协调线程负责文件、进程和界面命令。</summary>
internal sealed partial class ResidentHost : IDisposable, IChainInvoker
{
    private readonly GestureEngine _engine;
    private readonly GestureEngine _syntheticEngine;
    private readonly HashSet<int> _knownKeys = [];
    private readonly ConcurrentQueue<string[]> _work = new();
    private readonly ConcurrentQueue<ResidentManifest> _manifests = new();
    private readonly Queue<string[]> _pending = new();
    private readonly AutoResetEvent _workReady = new(false);
    private readonly Thread _coordinator;
    private readonly WindowProcedure _windowProcedure;
    private readonly HookProcedure _keyProcedure, _mouseProcedure;
    private readonly string _uiPath;
    private IntPtr _window, _keyboard, _mouse, _icon, _change;
    private Process? _worker;
    private volatile bool _stopping, _paused;
    private bool _workerReady, _syncRequested, _tickActive;
    private long _manifestTicks = -1;
    private long _workerStarted;
    private ResidentManifest _manifest = new();
    private ResidentManifest? _inputSnapshot;
    private GestureBinding[] _runtimeBindings = [];
    private readonly Mutex _mutex;
    private Timer? _retry;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");

    private static void Main()
    {
        Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
        try { using var host = new ResidentHost(); host.Run(); }
        catch (Exception error) { Log(error); }
    }

    private ResidentHost()
    {
        _mutex = new Mutex(true, @"Local\FrameCapture.Resident" + ResidentProtocol.Suffix, out bool owner);
        if (!owner) throw new InvalidOperationException("Resident already running");
        _uiPath = Environment.GetEnvironmentVariable("FC_TEST_NO_FG") == "1"
            && Environment.GetEnvironmentVariable("FC_RESIDENT_UI_PATH") is { Length: > 0 } path
                ? Path.GetFullPath(path) : Path.Combine(AppContext.BaseDirectory, "FrameCapture.exe");
        _engine = new GestureEngine(this);
        _syntheticEngine = new GestureEngine(this);
        _windowProcedure = WindowProc;
        _keyProcedure = KeyProc; _mouseProcedure = MouseProc;
        var windowClass = new WindowClass { Procedure = Marshal.GetFunctionPointerForDelegate(_windowProcedure),
            Instance = GetModuleHandle(null), Name = "FrameCapture.Resident.Window" };
        if (RegisterClass(ref windowClass) == 0) throw new System.ComponentModel.Win32Exception();
        _window = CreateWindowEx(0, windowClass.Name, ResidentProtocol.WindowName, 0, 0, 0, 0, 0, IntPtr.Zero,
            IntPtr.Zero, windowClass.Instance, IntPtr.Zero);
        if (_window == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        ChangeWindowMessageFilterEx(_window, 0x004A, 1, IntPtr.Zero);
        ExtractIconEx(_uiPath, 0, out IntPtr large, out _icon, 1);
        if (large != IntPtr.Zero) DestroyIcon(large);
        Directory.CreateDirectory(ResidentProtocol.DirectoryPath);
        _change = FindFirstChangeNotification(ResidentProtocol.DirectoryPath, false, 17);
        if (_change == new IntPtr(-1)) throw new System.ComponentModel.Win32Exception();
        _coordinator = new Thread(Coordinate) { IsBackground = true, Name = "Resident work" };
        _coordinator.Start();
        QueueWork(["--reload-inputs"]);
    }

    private void Run()
    {
        while (!_stopping)
        {
            uint reason = MsgWaitForMultipleObjectsEx(1, ref _change, uint.MaxValue, 0x04FF, 4);
            if (reason == 0) { FindNextChangeNotification(_change); QueueWork(["--reload-inputs"]); }
            while (PeekMessage(out var message, IntPtr.Zero, 0, 0, 1))
            {
                if (message.Id == 0x0012) { _stopping = true; break; }
                TranslateMessage(ref message); DispatchMessage(ref message);
            }
        }
    }

    private IntPtr WindowProc(IntPtr window, uint message, IntPtr w, IntPtr l)
    {
        try
        {
            if (message == 0x004A && ResidentProtocol.Receive(w, l, _uiPath) is { } args)
            {
                if (args.FirstOrDefault() == "--resident-pause")
                {
                    bool paused = args.ElementAtOrDefault(1) == "1";
                    if (paused != _paused) { _paused = paused; _engine.SetBindings(_runtimeBindings); _syntheticEngine.SetBindings(_runtimeBindings); UpdateTick(); }
                    QueueWork(["--update-icon"]);
                }
                else if (args.FirstOrDefault() == "--resident-test-key" && Environment.GetEnvironmentVariable("FC_TEST_NO_FG") == "1"
                    && args.Length == 4 && int.TryParse(args[1], out int key) && int.TryParse(args[2], out int mods))
                {
                    if (!_paused && args[3] == "down") _syntheticEngine.OnKeyDown(key, (ModifierKeys)mods, Environment.TickCount64);
                    else if (!_paused && args[3] == "up") _syntheticEngine.OnKeyUp(key, (ModifierKeys)mods, Environment.TickCount64);
                    UpdateTick();
                }
                else if (args.FirstOrDefault() == "--resident-retire") { _stopping = true; _workReady.Set(); }
                else QueueWork(args);
                return 1;
            }
            if (message == 0x8001)
            {
                if ((uint)l.ToInt64() == 0x0202) QueueWork(["--picker"]);
                else if ((uint)l.ToInt64() is 0x0205 or 0x007B) QueueWork(["--tray-menu"]);
                return 0;
            }
            if (message == 0x8002) { ApplyInputs(); return 0; }
            if (message == 0x0113)
            {
                if (!_paused) { long now = Environment.TickCount64; _engine.Tick(now); _syntheticEngine.Tick(now); }
                UpdateTick(); return 0;
            }
            if (message == 0x0010) { _stopping = true; return 0; }
            if (message == _taskbarCreated) QueueWork(["--update-icon"]);
        }
        catch (Exception error) { QueueWork(["--resident-error", error.ToString()]); }
        return DefWindowProc(window, message, w, l);
    }

    private void QueueWork(string[] arguments)
    {
        if (_stopping) return;
        _work.Enqueue(arguments);
        try { _workReady.Set(); } catch (ObjectDisposedException) { }
    }

    private void Coordinate()
    {
        while (!_stopping)
        {
            _workReady.WaitOne();
            while (!_stopping && _work.TryDequeue(out var args))
            {
                try
                {
                    string command = args.FirstOrDefault() ?? "";
                    switch (command)
                    {
                        case "--reload-inputs": ReloadInputs(); break;
                        case "--update-icon": UpdateIcon(); break;
                        case "--retry-ui": StartWorker(); FlushPending(); break;
                        case "--resident-error": Log(new InvalidOperationException(args.ElementAtOrDefault(1))); break;
                        case "--resident-retire": PostMessage(_window, 0x0010, IntPtr.Zero, IntPtr.Zero); break;
                        case "--resident-ready":
                            if (_worker != null && args.ElementAtOrDefault(1) == _worker.Id.ToString()) { _workerReady = true; FlushPending(); }
                            break;
                        case "--resident-worker-exited":
                            if (_worker == null || args.ElementAtOrDefault(1) != _worker.Id.ToString()) break;
                            _worker?.Dispose(); _worker = null; _workerReady = false;
                            if (Environment.TickCount64 - _workerStarted >= 3000) StartWorker();
                            else
                            {
                                _retry ??= new Timer(_ => QueueWork(["--retry-ui"]));
                                _retry.Change(1000, Timeout.Infinite);
                            }
                            break;
                        case "--refresh-worker-hook":
                            if (_workerReady) { _pending.Enqueue(["--resident-refresh-hook"]); FlushPending(); }
                            break;
                        default:
                            if (args.Length == 0 || command == "--startup") break;
                            if (_pending.Count < 64) _pending.Enqueue(args);
                            StartWorker(); FlushPending(); break;
                    }
                }
                catch (Exception error) { Log(error); }
            }
        }
    }

    private void ReloadInputs()
    {
        long stamp = File.Exists(ResidentProtocol.ManifestPath) ? File.GetLastWriteTimeUtc(ResidentProtocol.ManifestPath).Ticks : 0;
        string settings = Path.Combine(ResidentProtocol.DirectoryPath, "settings.json");
        if (stamp != _manifestTicks)
        {
            if (stamp != 0)
            {
                var manifest = ResidentProtocol.ReadManifest();
                if (manifest != null) { _manifest = manifest; ReloadIcons(); _manifests.Enqueue(manifest); PostMessage(_window, 0x8002, IntPtr.Zero, IntPtr.Zero); }
            }
            _manifestTicks = stamp; UpdateIcon();
        }
        bool stale = stamp == 0 || (File.Exists(settings) && File.GetLastWriteTimeUtc(settings).Ticks != _manifest.SourceTicks);
        if (stale && !_syncRequested) { _syncRequested = true; _pending.Enqueue(["--resident-sync"]); StartWorker(); FlushPending(); }
        if (!stale) _syncRequested = false;
        StartWorker();
    }

    private void StartWorker()
    {
        if (_worker is { HasExited: false }) return;
        var start = new ProcessStartInfo(_uiPath) { UseShellExecute = false, CreateNoWindow = true };
        start.Environment["FC_RESIDENT_PATH"] = Environment.ProcessPath!;
        start.ArgumentList.Add(ResidentProtocol.WorkerFlag);
        start.ArgumentList.Add("--elevated-attempt");
        start.ArgumentList.Add("--resident-paused"); start.ArgumentList.Add(_paused ? "1" : "0");
        _worker?.Dispose();
        _workerReady = false; _workerStarted = Environment.TickCount64;
        _worker = Process.Start(start) ?? throw new InvalidOperationException("UI worker did not start");
        int workerId = _worker.Id;
        _worker.EnableRaisingEvents = true;
        _worker.Exited += (_, _) => QueueWork(["--resident-worker-exited", workerId.ToString()]);
    }

    private void FlushPending()
    {
        if (!_workerReady || _worker == null || _worker.HasExited) return;
        IntPtr after = IntPtr.Zero, sink = IntPtr.Zero;
        while ((after = FindWindowEx(new IntPtr(-3), after, null, "FrameCapture.MessageSink")) != IntPtr.Zero)
        {
            GetWindowThreadProcessId(after, out uint pid);
            if (pid == _worker.Id) { sink = after; break; }
        }
        while (sink != IntPtr.Zero && _pending.TryPeek(out var args))
        {
            if (!ResidentProtocol.Send(sink, args))
            {
                _retry ??= new Timer(_ => QueueWork(["--retry-ui"]));
                _retry.Change(100, Timeout.Infinite); break;
            }
            _pending.Dequeue();
        }
    }

    private void ApplyInputs()
    {
        ResidentManifest? latest = null;
        while (_manifests.TryDequeue(out var next)) latest = next;
        if (latest == null) return;
        if (_inputSnapshot != null && latest.Enabled == _inputSnapshot.Enabled && latest.Signature == _inputSnapshot.Signature
            && latest.Inputs.SequenceEqual(_inputSnapshot.Inputs)) return;
        _inputSnapshot = latest;
        _runtimeBindings = latest.Enabled ? latest.Inputs.Select(input => new GestureBinding
        {
            Id = input.Id, Chord = new KeyChord((ModifierKeys)input.Mods, input.Key), Trigger = (TriggerKind)input.Trigger,
            DoubleClickMs = input.DoubleClickMs, LongPressMs = input.LongPressMs, Priority = input.Priority, Actions = ["invoke"]
        }).ToArray() : [];
        _engine.SetBindings(_runtimeBindings);
        _syntheticEngine.SetBindings(_runtimeBindings);
        _knownKeys.Clear(); foreach (var binding in _runtimeBindings) _knownKeys.Add(binding.Chord.Key);
        bool keyboard = latest.Enabled && latest.Inputs.Length > 0;
        bool mouse = latest.Enabled && latest.Inputs.Any(input => (TriggerKind)input.Trigger is TriggerKind.Click or TriggerKind.DoubleClick or TriggerKind.TripleClick or TriggerKind.LongPress);
        if (!keyboard && _keyboard != IntPtr.Zero) { UnhookWindowsHookEx(_keyboard); _keyboard = IntPtr.Zero; }
        if (!mouse && _mouse != IntPtr.Zero) { UnhookWindowsHookEx(_mouse); _mouse = IntPtr.Zero; }
        if (keyboard && _keyboard == IntPtr.Zero)
        {
            _keyboard = SetWindowsHookEx(13, _keyProcedure, GetModuleHandle(null), 0);
            QueueWork(["--refresh-worker-hook"]);
        }
        if (mouse && _mouse == IntPtr.Zero)
            _mouse = SetWindowsHookEx(14, _mouseProcedure, GetModuleHandle(null), 0);
        UpdateTick();
    }

    private IntPtr KeyProc(int code, IntPtr w, IntPtr l)
    {
        if (code >= 0 && !_paused)
        {
            try
            {
                var info = Marshal.PtrToStructure<KeyboardData>(l);
                if (!_knownKeys.Contains((int)info.Key) && info.Key is not (>= 0x10 and <= 0x12) and not (>= 0xA0 and <= 0xA5) and not (0x5B or 0x5C))
                    return CallNextHookEx(IntPtr.Zero, code, w, l);
                bool down = (info.Flags & 0x80) == 0;
                var mods = FrameCapture.Core.GestureModifiers.AfterKeyboardEvent(ReadMods(), (int)info.Key, down, KeyIsDown);
                long time = FrameCapture.Core.GestureClock.Expand(info.Time, Environment.TickCount64);
                bool swallow = down ? _engine.OnKeyDown((int)info.Key, mods, time) : _engine.OnKeyUp((int)info.Key, mods, time);
                UpdateTick(); if (swallow) return 1;
            }
            catch { }
        }
        return CallNextHookEx(IntPtr.Zero, code, w, l);
    }

    private IntPtr MouseProc(int code, IntPtr w, IntPtr l)
    {
        if (code >= 0 && !_paused)
        {
            try
            {
                int message = w.ToInt32();
                if (message is not (0x0201 or 0x0204 or 0x0207 or 0x0202 or 0x0205 or 0x0208)) return CallNextHookEx(IntPtr.Zero, code, w, l);
                var info = Marshal.PtrToStructure<MouseData>(l);
                long time = FrameCapture.Core.GestureClock.Expand(info.Time, Environment.TickCount64);
                if (message is 0x0201 or 0x0204 or 0x0207) _engine.OnMouseDown(message, ReadMods(), time);
                else if (message is 0x0202 or 0x0205 or 0x0208) _engine.OnMouseUp(message, ReadMods(), time);
                UpdateTick();
            }
            catch { }
        }
        return CallNextHookEx(IntPtr.Zero, code, w, l);
    }

    private static ModifierKeys ReadMods() => (GetAsyncKeyState(0x11) < 0 ? ModifierKeys.Control : 0)
        | (GetAsyncKeyState(0x12) < 0 ? ModifierKeys.Alt : 0) | (GetAsyncKeyState(0x10) < 0 ? ModifierKeys.Shift : 0)
        | (GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0 ? ModifierKeys.Win : 0);

    private static bool KeyIsDown(int key) => GetAsyncKeyState(key) < 0;

    private void UpdateTick()
    {
        bool needs = !_paused && (_engine.NeedsTick || _syntheticEngine.NeedsTick);
        if (needs == _tickActive) return;
        _tickActive = needs;
        if (needs) SetTimer(_window, 1, 20, IntPtr.Zero); else KillTimer(_window, 1);
    }

    /// <inheritdoc/>
    public void BeginBinding(GestureBinding binding) { }
    /// <inheritdoc/>
    public bool Invoke(string actionName) => false;
    /// <inheritdoc/>
    public bool InvokeChain(GestureBinding binding) { QueueWork(["--run-binding", binding.Id]); return true; }

    private static void Log(Exception error)
    {
        try { Directory.CreateDirectory(ResidentProtocol.DirectoryPath); File.AppendAllText(Path.Combine(ResidentProtocol.DirectoryPath, "resident.log"), $"[{DateTimeOffset.Now:O}] {error}\n"); }
        catch { }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _stopping = true; _workReady.Set(); bool joined = _coordinator.Join(1000);
        if (_keyboard != IntPtr.Zero) UnhookWindowsHookEx(_keyboard);
        if (_mouse != IntPtr.Zero) UnhookWindowsHookEx(_mouse);
        RemoveIcon();
        if (_icon != IntPtr.Zero) DestroyIcon(_icon);
        if (_pausedIcon != IntPtr.Zero) DestroyIcon(_pausedIcon);
        if (_change != IntPtr.Zero && _change != new IntPtr(-1)) FindCloseChangeNotification(_change);
        if (_window != IntPtr.Zero) DestroyWindow(_window);
        _worker?.Dispose(); if (joined) _workReady.Dispose();
        _retry?.Dispose();
        _mutex.ReleaseMutex(); _mutex.Dispose();
    }
}
