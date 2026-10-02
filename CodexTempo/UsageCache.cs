using System.IO;
using System.Text.Json;

namespace CodexTempo;

internal static class UsageCache
{
    private static string CachePath => Path.Combine(CodexPathResolver.ResolveHome(), "codextempo-usage-v2.json");

    public static UsageSnapshot? Load() => Load(CachePath, DateTimeOffset.Now);

    private static UsageSnapshot? Load(string path, DateTimeOffset now)
    {
        try { return new FileInfo(path).Length > 65536 ? null : Active(JsonSerializer.Deserialize<UsageSnapshot>(File.ReadAllText(path)), now); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return null; }
    }

    public static UsageSnapshot? Active(UsageSnapshot? value, DateTimeOffset now)
    {
        if (value is null || value.CapturedAt > now.AddMinutes(5)) return null;
        var five = Valid(value.FiveHour, now) ? value.FiveHour : null;
        var week = Valid(value.Week, now) ? value.Week : null;
        if (five is null && week is null) return null;
        return value with {
            FiveHour = five, Week = week, SourceFile = CodexUsageProvider.CachedSourceName,
            TodayUsedPercent = value.CapturedAt.LocalDateTime.Date == now.LocalDateTime.Date ? value.TodayUsedPercent : null
        };
    }

    private static bool Valid(LimitWindow? window, DateTimeOffset now) => window is not null &&
        window.ResetsAt > now && double.IsFinite(window.UsedPercent) && window.UsedPercent is >= 0 and <= 100;

    public static void Save(UsageSnapshot value) => Save(value, CachePath);

    private static void Save(UsageSnapshot value, string path)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(value));
            File.Move(temporary, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        finally { try { File.Delete(temporary); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
    }

    public static bool RunSelfTest()
    {
        var now = DateTimeOffset.Now;
        var path = Path.Combine(Path.GetTempPath(), "codextempo-cache-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var snapshot = new UsageSnapshot(new LimitWindow(20, 300, now.AddHours(1)),
                new LimitWindow(38, 10080, now.AddDays(3)), now, CodexAppServerClient.SourceName, 7);
            Save(snapshot, path);
            var restored = Load(path, now);
            var later = Load(path, now.AddDays(1));
            var valid = restored?.Week?.UsedPercent == 38 && restored.FiveHour?.UsedPercent == 20
                && restored.TodayUsedPercent == 7 && restored.SourceFile == CodexUsageProvider.CachedSourceName
                && restored.CapturedAt == now && later?.FiveHour is null && later?.Week?.UsedPercent == 38
                && later.TodayUsedPercent is null && Load(path, now.AddDays(4)) is null;
            File.WriteAllText(path, "{broken");
            return valid && Load(path, now) is null;
        }
        finally { File.Delete(path); }
    }
}
