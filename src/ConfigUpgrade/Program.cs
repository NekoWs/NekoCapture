using System.IO;
using System.Text.Json;
using NekoCapture.Core;

namespace NekoCapture.ConfigUpgrade;

/// <summary>安装器调用的独立配置迁移进程；全部文件准备成功后才提交。</summary>
internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [STAThread]
    private static int Main(string[] paths)
    {
        var pending = new List<(string Path, string Temp, string Backup, byte[] Original)>();
        var committed = new List<(string Path, byte[] Original)>();
        try
        {
            foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(path)) continue;
                byte[] original = File.ReadAllBytes(path);
                using var text = new StreamReader(new MemoryStream(original), System.Text.Encoding.UTF8, true);
                string json = text.ReadToEnd();
                using var doc = JsonDocument.Parse(json);
                int version = doc.RootElement.TryGetProperty(nameof(AppSettings.ConfigVersion), out var value) ? value.GetInt32() : 0;
                if (version < 0) throw new InvalidDataException("Invalid configuration version: " + path);
                if (version > AppSettings.CurrentConfigVersion) throw new InvalidDataException("Configuration requires a newer installer: " + path);
                if (version == AppSettings.CurrentConfigVersion) continue;
                var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? throw new InvalidDataException("Invalid configuration: " + path);
                if (!doc.RootElement.TryGetProperty(nameof(AppSettings.PresetRevision), out _)) settings.PresetRevision = 0;
                while (version < AppSettings.CurrentConfigVersion)
                {
                    switch (version)
                    {
                        case 0: UpgradeFromZero(settings); version = 1; break;
                        case 1: settings.Gestures.Migrate(); version = 2; break;
                        default: throw new InvalidDataException("Missing configuration migration step: " + version);
                    }
                }
                settings.ConfigVersion = version;
                settings.Normalize();
                string temp = path + ".upgrade-" + Guid.NewGuid().ToString("N") + ".tmp";
                string backup = path + ".before-v" + AppSettings.CurrentConfigVersion + "-" + Guid.NewGuid().ToString("N") + ".bak";
                pending.Add((path, temp, backup, original));
                File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
            }
            foreach (var item in pending)
            {
                File.WriteAllBytes(item.Backup, item.Original);
                File.Move(item.Temp, item.Path, true);
                committed.Add((item.Path, item.Original));
            }
            return 0;
        }
        catch (Exception ex)
        {
            foreach (var item in committed.AsEnumerable().Reverse())
                try { File.WriteAllBytes(item.Path, item.Original); } catch (Exception error) { Console.Error.WriteLine(error); }
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally { foreach (var item in pending) try { File.Delete(item.Temp); } catch { } }
    }

    private static void UpgradeFromZero(AppSettings settings)
    {
        settings.Colors ??= new();
        if (settings.PresetRevision < ColorPresets.Revision)
        {
            foreach (var pair in ColorPresets.Find(settings.ColorPreset).Colors)
            {
                if (!settings.Colors.TryGetValue(pair.Key, out string? stored)) continue;
                var before = HexColor.Parse(stored); var current = HexColor.Parse(pair.Value);
                if (before?.A == 255 && current?.A < 255) settings.Colors[pair.Key] = pair.Value;
            }
            settings.PresetRevision = ColorPresets.Revision;
        }
        settings.Gestures ??= GestureConfig.CreateCurrentDefaults();
        settings.Gestures.Bindings ??= new();
        settings.Gestures.SeedIfNeeded(GestureConfig.DefaultCaptureKey, GestureConfig.DefaultRecordKey);
        settings.Gestures.UpgradeStockDefaults();
        settings.Gestures.Migrate();
    }
}
