using System.IO;
using System.Text.Json;

namespace CodexTempo;

internal static class WindowPlacementStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexTempo",
        "window.json");

    public static (double Left, double Top)? Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            var value = JsonSerializer.Deserialize<Placement>(File.ReadAllText(SettingsPath));
            return value is null || !double.IsFinite(value.Left) || !double.IsFinite(value.Top)
                ? null
                : (value.Left, value.Top);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(double left, double top)
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(directory);
            var temporary = SettingsPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Placement(left, top)));
            File.Move(temporary, SettingsPath, true);
        }
        catch
        {
            // Window placement is a convenience; failure must never stop the widget.
        }
    }

    private sealed record Placement(double Left, double Top);
}
