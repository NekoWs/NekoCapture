using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using FrameCapture.Services;

static partial class SelfTest
{
    static int StandardHandoffProbe(string[] arguments)
    {
        if (!StartupService.WaitForStandardHandoff(arguments)) return 2;
        int index = Array.IndexOf(arguments, "--startup-handoff-probe");
        if (index < 0 || index + 2 >= arguments.Length) return 3;
        using var identity = WindowsIdentity.GetCurrent();
        File.WriteAllText(arguments[index + 1], JsonSerializer.Serialize(new
        {
            Elevated = StartupService.IsElevated, User = identity.User!.Value, Argument = arguments[index + 2],
            Settings = Environment.GetEnvironmentVariable("FC_SETTINGS_DIR"), Temp = Path.GetTempPath()
        }));
        return 0;
    }

    static void StartupStandardChecks()
    {
        Chk("startup-standard-rejects-invalid-handoff-without-launch", !StartupService.WaitForStandardHandoff(new[] { "--standard-handoff", "invalid" }));
        string output = Path.Combine(Path.GetTempPath(), "standard-handoff-" + Guid.NewGuid().ToString("N") + ".json");
        Process? child = null;
        try
        {
            string argument = "目录 & tools \\\"quote\\\"";
            child = StartupService.BeginStandardHandoff(Environment.ProcessPath!,
                StartupService.QuoteArguments(new[] { "--startup-handoff-probe", output, argument }), 0);
            Thread.Sleep(150);
            Chk("startup-standard-desktop-child-waits-for-lock-release", !child.HasExited && !File.Exists(output));
            StartupService.ReleaseStandardHandoff();
            bool finished = child.WaitForExit(8000);
            using var proof = JsonDocument.Parse(File.ReadAllText(output));
            using var identity = WindowsIdentity.GetCurrent();
            var root = proof.RootElement;
            Chk("startup-standard-desktop-launch-verifies-actual-user-and-token", finished && child.ExitCode == 0
                && !root.GetProperty("Elevated").GetBoolean() && root.GetProperty("User").GetString() == identity.User!.Value);
            Chk("startup-standard-desktop-launch-preserves-isolation-and-arguments", root.GetProperty("Argument").GetString() == argument
                && root.GetProperty("Settings").GetString() == Environment.GetEnvironmentVariable("FC_SETTINGS_DIR")
                && root.GetProperty("Temp").GetString() == Path.GetTempPath());
        }
        catch (Exception ex) { Chk("startup-standard-native-handoff", false, ex.ToString()); }
        finally
        {
            StartupService.ReleaseStandardHandoff();
            if (child != null) { if (!child.HasExited) { child.Kill(); child.WaitForExit(2000); } child.Dispose(); }
            if (File.Exists(output)) File.Delete(output);
        }
    }
}
