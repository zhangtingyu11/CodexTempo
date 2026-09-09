using Microsoft.Win32;

namespace CodexTempo;

internal static class StartupSettings
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("CodexTempo") is string command && !string.IsNullOrWhiteSpace(command);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定程序路径。");
            key.SetValue("CodexTempo", $"\"{executable}\"", RegistryValueKind.String);
        }
        else key.DeleteValue("CodexTempo", false);
    }
}
