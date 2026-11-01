using System.IO;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using NekoCapture.Core;
using NekoCapture.Services;

static partial class SelfTest
{
    static void StartupChecks()
    {
        const string sid = "S-1-5-21-100-200-300-1001";
        const string otherSid = "S-1-5-21-100-200-300-1002";
        const string exe = @"D:\Apps & Tools\NekoCapture\NekoCapture.exe";
        string name = StartupPlan.NameFor(sid);
        string xml = StartupPlan.BuildXml(exe, sid);
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var parsed = XDocument.Parse(xml);
        Chk("startup-task-highest-interactive-current-user", parsed.Descendants(ns + "UserId").All(x => x.Value == sid)
            && parsed.Descendants(ns + "UserId").Count() == 2 && xml.Contains("HighestAvailable") && xml.Contains("InteractiveToken"));
        Chk("startup-task-command-xml-escaping", parsed.Descendants(ns + "Command").Single().Value == exe
            && parsed.Descendants(ns + "Arguments").Single().Value == "--startup"
            && parsed.Descendants(ns + "WorkingDirectory").Single().Value == Path.GetDirectoryName(exe));
        Chk("startup-task-keeps-running-on-battery", xml.Contains("<ExecutionTimeLimit>PT0S")
            && xml.Contains("<DisallowStartIfOnBatteries>false") && xml.Contains("<StopIfGoingOnBatteries>false") && xml.Contains("IgnoreNew"));
        parsed.Descendants(ns + "RegistrationInfo").Single().Add(new XElement(ns + "Date", "2026-01-01T00:00:00"));
        Chk("startup-task-matching-ignores-scheduler-metadata", StartupPlan.Matches(parsed.ToString(), exe, sid));
        var storage = new StartupMemory { RunValue = "ordinary-entry" };
        var plan = new StartupPlan(storage, exe, sid, true, true);
        Chk("startup-admin-and-autostart-requires-one-authorization", plan.NeedsElevation);
        Chk("startup-upgrade-registers-task-before-clearing-run", plan.Apply() == StartupResult.Task && storage.RunValue == null
            && storage.Tasks.ContainsKey(name) && storage.Operations.IndexOf("write-task") < storage.Operations.LastIndexOf("write-run"));
        storage.Operations.Clear();
        plan = new StartupPlan(storage, exe, sid, true, true);
        Chk("startup-valid-task-needs-no-new-authorization", !plan.NeedsElevation && plan.Apply() == StartupResult.Task
            && storage.Operations.Count == 0);
        string moved = @"D:\Moved\NekoCapture.exe";
        plan = new StartupPlan(storage, moved, sid, true, true);
        Chk("startup-moved-executable-updates-task", plan.NeedsElevation && plan.Apply() == StartupResult.Task
            && StartupPlan.Matches(storage.Tasks[name], moved, sid));
        plan = new StartupPlan(storage, moved, sid, true, false);
        Chk("startup-disable-admin-keeps-ordinary-autostart", plan.NeedsElevation && plan.Apply() == StartupResult.RunKey
            && storage.Tasks.Count == 0 && storage.RunValue == $"\"{moved}\" --startup");
        plan = new StartupPlan(storage, moved, sid, false, false);
        Chk("startup-disable-ordinary-needs-no-authorization", !plan.NeedsElevation && plan.Apply() == StartupResult.Off
            && storage.RunValue == null);
        storage.Tasks["NekoCapture"] = xml;
        plan = new StartupPlan(storage, exe, sid, true, true);
        Chk("startup-migrates-owned-global-task", plan.Apply() == StartupResult.Task && !storage.Tasks.ContainsKey("NekoCapture"));
        storage = new StartupMemory(); storage.Tasks["NekoCapture"] = StartupPlan.BuildXml(exe, otherSid);
        plan = new StartupPlan(storage, exe, sid, true, false);
        Chk("startup-leaves-other-users-task-untouched", !plan.NeedsElevation && plan.Apply() == StartupResult.RunKey
            && storage.Tasks.ContainsKey("NekoCapture") && name != StartupPlan.NameFor(otherSid));
        storage = new StartupMemory { RunValue = "previous-run", FailOperation = "write-task" };
        bool failed = false;
        try { new StartupPlan(storage, exe, sid, true, true).Apply(); } catch (IOException) { failed = true; }
        Chk("startup-create-failure-keeps-run-and-task-snapshot", failed && storage.RunValue == "previous-run" && storage.Tasks.Count == 0);
        storage = new StartupMemory { RunValue = "previous-run", FailOperation = "write-run" };
        failed = false;
        try { new StartupPlan(storage, exe, sid, true, true).Apply(); } catch (IOException) { failed = true; }
        Chk("startup-registry-failure-removes-new-task", failed && storage.RunValue == "previous-run" && storage.Tasks.Count == 0);
        storage = new StartupMemory { RunValue = "previous-run", FailOperation = "delete-task" }; storage.Tasks[name] = xml;
        failed = false;
        try { new StartupPlan(storage, exe, sid, false, false).Apply(); } catch (IOException) { failed = true; }
        Chk("startup-delete-failure-preserves-enabled-entry", failed && storage.RunValue == "previous-run" && storage.Tasks[name] == xml);
        Chk("startup-uninstall-targets-exact-install-directory", WindowsStartupRegistration.TargetsExecutable(xml, exe)
            && !WindowsStartupRegistration.TargetsExecutable(xml, moved));
        Chk("startup-login-never-requests-uac", !StartupService.ShouldElevate(new[] { "--startup" }, false, false, true));
        Chk("startup-manual-launch-may-request-uac", StartupService.ShouldElevate(Array.Empty<string>(), false, false, true));
        Chk("startup-elevation-attempt-does-not-loop", !StartupService.ShouldElevate(new[] { "--elevated-attempt" }, false, false, true));
        Chk("startup-elevated-and-scripted-launches-are-quiet", !StartupService.ShouldElevate(Array.Empty<string>(), true, false, true)
            && !StartupService.ShouldElevate(Array.Empty<string>(), false, true, true));
        var values = new[] { "", @"C:\目录 & tools\", "a\"b", "--settings", "--page", "system" };
        IntPtr argv = ParseStartupArguments("NekoCapture.exe " + StartupService.QuoteArguments(values), out int count);
        try
        {
            var decoded = Enumerable.Range(1, count - 1).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))).ToArray();
            Chk("startup-handoff-arguments-roundtrip", decoded.SequenceEqual(values));
        }
        finally { FreeStartupArguments(argv); }
        Chk("startup-handoff-invalid-parent-is-rejected", !StartupService.WaitForParent(new[] { "--wait-parent", "invalid", "1" })
            && !StartupService.WaitForParent(new[] { "--wait-parent", Environment.ProcessId.ToString(), "1" }));
        using (var native = new WindowsStartupRegistration())
            Chk("startup-native-missing-task-is-unconfigured", native.ReadTask("NekoCapture-Diagnostic-" + Guid.NewGuid().ToString("N")) == null);
        StartupSettingsChecks();
        StartupStandardChecks();
    }

    static void StartupSettingsChecks()
    {
        string? previousDir = Environment.GetEnvironmentVariable("NC_SETTINGS_DIR");
        var shared = SettingsService.Shared;
        string root = Path.Combine(Path.GetTempPath(), "startup-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("NC_SETTINGS_DIR", root);
            var settings = new SettingsService();
            Chk("startup-preferences-write-both-flags-together", settings.TrySaveStartup(true, true)
                && settings.Current.RunAtStartup && settings.Current.RunAsAdmin
                && File.ReadAllText(SettingsService.FilePath).Contains("\"RunAsAdmin\": true"));
            const string sid = "S-1-5-21-100-200-300-1001", exe = @"D:\Apps\NekoCapture.exe";
            string name = StartupPlan.NameFor(sid), xml = StartupPlan.BuildXml(exe, sid);
            var storage = new StartupMemory(); storage.Tasks[name] = xml;
            bool failed = false;
            try
            {
                StartupService.ApplyLocal(new StartupPlan(storage, exe, sid, true, false), true, false, settings,
                    () => throw new IOException("Standard instance could not start"));
            }
            catch (AggregateException) { failed = true; }
            Chk("startup-downgrade-launch-failure-restores-entries", failed && storage.RunValue == null && storage.Tasks[name] == xml);
            Chk("startup-downgrade-launch-failure-restores-preferences", settings.Current.RunAtStartup && settings.Current.RunAsAdmin
                && File.ReadAllText(SettingsService.FilePath).Contains("\"RunAsAdmin\": true"));
            bool launched = false;
            var change = StartupService.ApplyLocal(new StartupPlan(storage, exe, sid, false, false), false, false, settings, () => launched = true);
            Chk("startup-downgrade-commits-before-handoff", launched && change.Restarted && change.Result == StartupResult.Off
                && storage.Tasks.Count == 0 && storage.RunValue == null && !settings.Current.RunAtStartup && !settings.Current.RunAsAdmin);
            settings.TrySaveStartup(true, true);
            File.Delete(SettingsService.FilePath); Directory.CreateDirectory(SettingsService.FilePath);
            Chk("startup-preferences-failed-write-keeps-memory-state", !settings.TrySaveStartup(false, false)
                && settings.Current.RunAtStartup && settings.Current.RunAsAdmin);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NC_SETTINGS_DIR", previousDir);
            typeof(SettingsService).GetProperty("Shared")!.SetValue(null, shared);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [DllImport("shell32.dll", EntryPoint = "CommandLineToArgvW", CharSet = CharSet.Unicode)]
    static extern IntPtr ParseStartupArguments(string commandLine, out int count);
    [DllImport("kernel32.dll", EntryPoint = "LocalFree")]
    static extern IntPtr FreeStartupArguments(IntPtr pointer);

    sealed class StartupMemory : IStartupRegistration
    {
        internal readonly Dictionary<string, string> Tasks = new();
        internal readonly List<string> Operations = new();
        internal string? FailOperation;
        private string? _run;
        public string? RunValue { get => _run; set { Check("write-run"); _run = value; } }
        public string? ReadTask(string name) => Tasks.GetValueOrDefault(name);
        public void WriteTask(string name, string xml, string sid) { Check("write-task"); Tasks[name] = xml; }
        public void DeleteTask(string name) { Check("delete-task"); Tasks.Remove(name); }
        private void Check(string operation)
        {
            Operations.Add(operation);
            if (FailOperation != operation) return;
            FailOperation = null;
            throw new IOException("Simulated startup registration failure");
        }
    }
}
