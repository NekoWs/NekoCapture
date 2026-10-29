using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;
using Microsoft.Win32;

namespace FrameCapture.Services;

/// <summary>用户登录入口的可替换存储边界。</summary>
internal interface IStartupRegistration
{
    /// <summary>当前用户的 Run 项；null 表示未登记。</summary>
    string? RunValue { get; set; }
    /// <summary>读取任务 XML，缺失时返回 null。</summary>
    /// <param name="name">任务名称。</param>
    /// <returns>完整任务定义。</returns>
    string? ReadTask(string name);
    /// <summary>登记使用交互式令牌的任务。</summary>
    /// <param name="name">任务名称。</param>
    /// <param name="xml">任务定义。</param>
    /// <param name="sid">任务所属用户。</param>
    void WriteTask(string name, string xml, string sid);
    /// <summary>删除任务，缺失时忽略。</summary>
    /// <param name="name">任务名称。</param>
    void DeleteTask(string name);
}

/// <summary>以用户 SID 为范围登记登录任务，兼容同一用户的全局命名入口。</summary>
internal sealed class StartupPlan
{
    private readonly IStartupRegistration _storage;
    private readonly string _exe, _sid, _name;
    private readonly bool _enable, _elevated;
    private readonly string? _run, _task, _legacy;
    private bool _taskTouched, _legacyTouched, _runTouched;
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// <summary>读取入口快照，不修改系统状态。</summary>
    /// <param name="storage">登录入口存储。</param>
    /// <param name="exe">本程序绝对路径。</param>
    /// <param name="sid">当前用户 SID。</param>
    /// <param name="enable">是否启用自启。</param>
    /// <param name="elevated">是否要求最高权限。</param>
    internal StartupPlan(IStartupRegistration storage, string exe, string sid, bool enable, bool elevated)
    {
        _storage = storage; _exe = exe; _sid = sid; _enable = enable; _elevated = elevated;
        _name = NameFor(sid);
        _run = storage.RunValue;
        _task = storage.ReadTask(_name);
        if (_task != null && !Owns(_task, exe, sid)) throw new InvalidOperationException("Startup task belongs to another application");
        string? legacy = storage.ReadTask("FrameCapture");
        _legacy = legacy != null && Owns(legacy, exe, sid) ? legacy : null;
    }

    /// <summary>本次修改是否需要管理员令牌；纯 Run 项操作不需要。</summary>
    internal bool NeedsElevation => _legacy != null || (_enable && _elevated
        ? _task == null || !Matches(_task, _exe, _sid) : _task != null);

    /// <summary>应用入口事务，失败时恢复快照。</summary>
    /// <returns>成功登记的入口种类。</returns>
    internal StartupResult Apply()
    {
        try
        {
            if (_enable && _elevated)
            {
                if (_task == null || !Matches(_task, _exe, _sid))
                { _taskTouched = true; _storage.WriteTask(_name, BuildXml(_exe, _sid), _sid); }
            }
            else if (_task != null) { _taskTouched = true; _storage.DeleteTask(_name); }
            if (_legacy != null) { _legacyTouched = true; _storage.DeleteTask("FrameCapture"); }
            string? run = _enable && !_elevated ? $"\"{_exe}\" --startup" : null;
            if (_run != run) { _runTouched = true; _storage.RunValue = run; }
            return !_enable ? StartupResult.Off : _elevated ? StartupResult.Task : StartupResult.RunKey;
        }
        catch { Restore(); throw; }
    }

    /// <summary>恢复本次操作触及的入口；恢复失败保留异常信息给调用方。</summary>
    internal void Restore()
    {
        var failures = new List<Exception>();
        void RestoreOne(Action action) { try { action(); } catch (Exception ex) { failures.Add(ex); } }
        if (_runTouched) RestoreOne(() => _storage.RunValue = _run);
        if (_legacyTouched) RestoreOne(() => _storage.WriteTask("FrameCapture", _legacy!, _sid));
        if (_taskTouched) RestoreOne(() => { if (_task == null) _storage.DeleteTask(_name); else _storage.WriteTask(_name, _task, _sid); });
        if (failures.Count > 0) throw new AggregateException("Startup rollback failed", failures);
    }

    /// <summary>生成每用户独立的任务名称。</summary>
    /// <param name="sid">有效的 Windows 用户 SID。</param>
    /// <returns>当前用户的 FrameCapture 任务名称。</returns>
    internal static string NameFor(string sid) => "FrameCapture-" + new SecurityIdentifier(sid).Value;

    /// <summary>构造无需密码的交互式高权限登录任务。</summary>
    /// <param name="exe">本程序绝对路径。</param>
    /// <param name="sid">当前用户 SID。</param>
    /// <returns>任务计划程序 XML。</returns>
    internal static string BuildXml(string exe, string sid)
    {
        XElement E(string name, object value) => new(Ns + name, value);
        return new XDocument(new XElement(Ns + "Task", new XAttribute("version", "1.2"),
            E("RegistrationInfo", E("Author", "FrameCapture")),
            E("Triggers", E("LogonTrigger", new[] { E("Enabled", true), E("UserId", sid) })),
            E("Principals", new XElement(Ns + "Principal", new XAttribute("id", "User"),
                E("UserId", sid), E("LogonType", "InteractiveToken"), E("RunLevel", "HighestAvailable"))),
            E("Settings", new[] { E("MultipleInstancesPolicy", "IgnoreNew"), E("DisallowStartIfOnBatteries", false),
                E("StopIfGoingOnBatteries", false), E("StartWhenAvailable", true), E("ExecutionTimeLimit", "PT0S"), E("Enabled", true) }),
            new XElement(Ns + "Actions", new XAttribute("Context", "User"),
                E("Exec", new[] { E("Command", exe), E("Arguments", "--startup"), E("WorkingDirectory", Path.GetDirectoryName(exe)!) }))))
            .ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>检查入口的用户、路径及交互式登录契约。</summary>
    /// <param name="xml">任务定义。</param>
    /// <param name="exe">目标可执行文件。</param>
    /// <param name="sid">目标用户 SID。</param>
    /// <returns>无需更新时为 true。</returns>
    internal static bool Matches(string xml, string exe, string sid)
    {
        try
        {
            var root = XDocument.Parse(xml);
            var principal = root.Descendants(Ns + "Principal").Single();
            var trigger = root.Descendants(Ns + "LogonTrigger").Single();
            var action = root.Descendants(Ns + "Exec").Single();
            var settings = root.Descendants(Ns + "Settings").Single();
            string? Value(XElement element, string name) => element.Element(Ns + name)?.Value;
            return Owns(xml, exe, sid) && Value(principal, "LogonType") == "InteractiveToken"
                && Value(principal, "RunLevel") == "HighestAvailable" && Value(trigger, "UserId") == sid
                && Value(trigger, "Enabled") != "false" && root.Descendants(Ns + "Triggers").Elements().Count() == 1
                && root.Descendants(Ns + "Actions").Elements().Count() == 1
                && string.Equals(Value(action, "Command")?.Trim('"'), exe, StringComparison.OrdinalIgnoreCase)
                && Value(action, "Arguments") == "--startup"
                && string.Equals(Value(action, "WorkingDirectory"), Path.GetDirectoryName(exe), StringComparison.OrdinalIgnoreCase)
                && Value(settings, "Enabled") != "false" && Value(settings, "DisallowStartIfOnBatteries") == "false"
                && Value(settings, "StopIfGoingOnBatteries") == "false" && Value(settings, "ExecutionTimeLimit") == "PT0S"
                && Value(settings, "MultipleInstancesPolicy") == "IgnoreNew";
        }
        catch { return false; }
    }

    /// <summary>检查任务是否属于同一用户的本程序。</summary>
    /// <param name="xml">任务定义。</param>
    /// <param name="exe">本程序路径。</param>
    /// <param name="sid">当前用户 SID。</param>
    /// <returns>仅用户名及可执行文件名均匹配时为 true。</returns>
    internal static bool Owns(string xml, string exe, string sid)
    {
        try
        {
            var root = XDocument.Parse(xml);
            string owner = root.Descendants(Ns + "Principal").Elements(Ns + "UserId").Single().Value;
            if (!owner.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase))
                owner = ((SecurityIdentifier)new NTAccount(owner).Translate(typeof(SecurityIdentifier))).Value;
            string command = root.Descendants(Ns + "Exec").Elements(Ns + "Command").Single().Value.Trim('"');
            return owner == sid && string.Equals(Path.GetFileName(command), Path.GetFileName(exe), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}

/// <summary>HKCU 与本地任务计划程序的存储实现。</summary>
internal sealed class WindowsStartupRegistration : IStartupRegistration, IDisposable
{
    private dynamic? _service, _folder;
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <inheritdoc/>
    public string? RunValue
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue("FrameCapture") as string; }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value == null) key.DeleteValue("FrameCapture", false); else key.SetValue("FrameCapture", value);
        }
    }

    private dynamic Folder
    {
        get
        {
            if (_folder != null) return _folder;
            _service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!);
            _service!.Connect();
            return _folder = _service.GetFolder(@"\");
        }
    }

    /// <inheritdoc/>
    public string? ReadTask(string name)
    {
        dynamic? task = null;
        try { task = Folder.GetTask(name); return (string)task.Xml; }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80070002)) { return null; }
        finally { if (task != null) Marshal.FinalReleaseComObject(task); }
    }

    /// <inheritdoc/>
    public void WriteTask(string name, string xml, string sid)
    {
        dynamic task = Folder.RegisterTask(name, xml, 6, sid, null, 3, null);
        Marshal.FinalReleaseComObject(task);
    }

    /// <inheritdoc/>
    public void DeleteTask(string name)
    {
        try { Folder.DeleteTask(name, 0); }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80070002)) { }
    }

    /// <summary>卸载时移除指向指定程序的各用户登录任务。</summary>
    /// <param name="exe">正在卸载的程序绝对路径。</param>
    internal void RemoveForExecutable(string exe)
    {
        dynamic tasks = Folder.GetTasks(1);
        try
        {
            var names = new List<string>();
            for (int index = 1; index <= (int)tasks.Count; index++)
            {
                dynamic task = tasks[index];
                try
                {
                    string name = task.Name;
                    if ((name == "FrameCapture" || name.StartsWith("FrameCapture-S-1-", StringComparison.Ordinal)) &&
                        TargetsExecutable((string)task.Xml, exe)) names.Add(name);
                }
                finally { Marshal.FinalReleaseComObject(task); }
            }
            foreach (string name in names) DeleteTask(name);
        }
        finally { Marshal.FinalReleaseComObject(tasks); }
    }

    /// <summary>核对任务唯一执行动作的完整路径，防止卸载影响其他安装目录。</summary>
    /// <param name="xml">任务定义。</param>
    /// <param name="exe">正在卸载的可执行文件。</param>
    /// <returns>唯一执行动作指向该程序时为 true。</returns>
    internal static bool TargetsExecutable(string xml, string exe)
    {
        try
        {
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            var actions = XDocument.Parse(xml).Descendants(ns + "Actions").Elements().ToArray();
            return actions.Length == 1 && actions[0].Name == ns + "Exec" && string.Equals(
                actions[0].Element(ns + "Command")?.Value.Trim('"'), exe, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_folder != null) Marshal.FinalReleaseComObject(_folder);
        if (_service != null) Marshal.FinalReleaseComObject(_service);
        _folder = _service = null;
    }
}
