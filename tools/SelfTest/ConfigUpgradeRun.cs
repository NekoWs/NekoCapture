using System.Diagnostics;
using System.IO;
using System.Text.Json;
using NekoCapture.Core;

static partial class SelfTest
{
    /// <summary>配置结构升级与配色迁移在安装器的独立升级进程里：跑一次真正的进程，验证备份、版本号与逐条迁移。</summary>
    private static void ConfigUpgradeChecks()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository != null && !File.Exists(Path.Combine(repository.FullName, "NekoCapture.slnx"))) repository = repository.Parent;
        string? exe = null;
        foreach (string configuration in new[] { "x64\\Debug", "Debug" })
        {
            string candidate = Path.Combine(repository!.FullName, "src", "ConfigUpgrade", "bin", configuration,
                "net8.0-windows10.0.19041.0", "NekoCapture.ConfigUpgrade.exe");
            if (File.Exists(candidate)) { exe = candidate; break; }
        }
        if (exe == null) { Chk("config-upgrade-binary", false, "找不到配置升级进程的产物"); return; }

        string scratch = Path.Combine(SettingsService.Dir, "config-upgrade");
        Directory.CreateDirectory(scratch);
        bool Run(string json, out string file, out int code)
        {
            file = Path.Combine(scratch, "settings-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(file, json);
            var info = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            info.ArgumentList.Add(file);
            using var process = Process.Start(info)!;
            string error = process.StandardError.ReadToEnd();
            process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            code = process.ExitCode;
            if (code != 0) Chk("config-upgrade-run", false, error.Trim());
            return code == 0;
        }

        // 预设配色副本落后一个版本：读错位的那条按表换掉，手改的那枚留在原位，原文件落一份备份
        const string legacy = "{\"ColorPreset\":\"midnight\",\"PresetRevision\":0,"
            + "\"Colors\":{\"dark.Hover\":\"#33FFFF\",\"dark.RowBg\":\"#123456\"}}";
        if (Run(legacy, out string migrated, out _))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(migrated));
            var root = document.RootElement;
            int version = root.TryGetProperty(nameof(AppSettings.ConfigVersion), out var v) ? v.GetInt32() : -1;
            int revision = root.TryGetProperty(nameof(AppSettings.PresetRevision), out var r) ? r.GetInt32() : -1;
            var colors = root.GetProperty(nameof(AppSettings.Colors));
            string hover = colors.TryGetProperty("dark.Hover", out var h) ? h.GetString() ?? "" : "";
            string row = colors.TryGetProperty("dark.RowBg", out var b) ? b.GetString() ?? "" : "";
            string expected = ColorPresets.Find("midnight").Colors.TryGetValue("dark.Hover", out var table) ? table : "";
            // 读错位的那条被换成表里的值；换完之后若与出厂同值，归一里会被省掉，两种都算对
            bool hoverOk = hover.Length == 0 || hover == expected;
            Chk("config-upgrade-migrates-preset-colors",
                version == AppSettings.CurrentConfigVersion && revision == ColorPresets.Revision
                    && hoverOk && row == "#123456",
                $"版本 {version}，预设版本 {revision}，暗色悬停 {hover}（表里 {expected}），手改行底 {row}");
            string backup = Directory.GetFiles(scratch, Path.GetFileName(migrated) + "*.bak").FirstOrDefault() ?? "";
            Chk("config-upgrade-writes-backup",
                backup.Length > 0 && File.ReadAllText(backup) == legacy,
                backup.Length > 0 ? "备份与原文件一致" : "没有生成备份");
        }

        // 出厂预设的覆盖表是空的：这种设置下的自定义颜色一枚都不许被迁移抹掉
        const string own = "{\"ColorPreset\":\"default\",\"PresetRevision\":0,"
            + "\"Colors\":{\"dark.CardBg\":\"#123456\"}}";
        if (Run(own, out string keptFile, out _))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(keptFile));
            var colors = document.RootElement.GetProperty(nameof(AppSettings.Colors));
            string card = colors.TryGetProperty("dark.CardBg", out var c) ? c.GetString() ?? "" : "";
            int revision = document.RootElement.TryGetProperty(nameof(AppSettings.PresetRevision), out var r) ? r.GetInt32() : -1;
            Chk("config-upgrade-keeps-customs-on-stock-preset", card == "#123456" && revision == ColorPresets.Revision,
                $"出厂预设下手改的卡面色 {card}，预设版本 {revision}");
        }
    }
}
