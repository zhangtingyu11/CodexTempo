using System.IO;
using System.Text;
using System.Text.Json;

namespace CodexTempo;

public sealed class CodexUsageReader : IDisposable
{
    private readonly string _sessionsRoot;
    private readonly FileSystemWatcher? _watcher;
    private readonly object _gate = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private string? _latestCandidate;
    private DateOnly? _baselineDate;
    private long _baselineReset;
    private double _baselineWeekUsed;
    private sealed record CacheEntry(long FileLength, UsageSnapshot Snapshot);

    public CodexUsageReader()
    {
        var codexHome = CodexPathResolver.ResolveHome();
        _sessionsRoot = Path.Combine(codexHome, "sessions");

        if (Directory.Exists(_sessionsRoot))
        {
            _watcher = new FileSystemWatcher(_sessionsRoot, "*.jsonl")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
            _watcher.Created += Track;
            _watcher.Changed += Track;
            _watcher.Renamed += (_, e) => SetCandidate(e.FullPath);
        }
    }

    private void Track(object sender, FileSystemEventArgs e) => SetCandidate(e.FullPath);

    private void SetCandidate(string path)
    {
        lock (_gate) _latestCandidate = path;
    }

    public async Task<UsageSnapshot?> ReadLatestAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_sessionsRoot)) return null;

        string? candidate;
        lock (_gate) candidate = _latestCandidate;

        var files = new List<FileInfo>();
        if (candidate is not null && File.Exists(candidate))
            files.Add(new FileInfo(candidate));

        // Only enumerate the current and previous date folders. FileSystemWatcher
        // catches newly-created sessions between refreshes.
        foreach (var day in new[] { DateTime.Today, DateTime.Today.AddDays(-1) })
        {
            var dir = Path.Combine(_sessionsRoot, day.ToString("yyyy"), day.ToString("MM"), day.ToString("dd"));
            if (!Directory.Exists(dir)) continue;
            files.AddRange(new DirectoryInfo(dir).EnumerateFiles("*.jsonl"));
        }

        UsageSnapshot? newest = null;
        var now = DateTimeOffset.Now;
        LimitWindow? five = null;
        DateTimeOffset fiveAt = DateTimeOffset.MinValue;
        LimitWindow? week = null;
        DateTimeOffset weekAt = DateTimeOffset.MinValue;

        foreach (var file in files.DistinctBy(f => f.FullName).OrderByDescending(f => f.LastWriteTimeUtc).Take(8))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await ReadFileAsync(file.FullName, cancellationToken);
            if (snapshot is null) continue;
            if (newest is null || snapshot.CapturedAt > newest.CapturedAt) newest = snapshot;
            if (snapshot.FiveHour is { } shortWindow && shortWindow.ResetsAt > now && snapshot.CapturedAt > fiveAt)
                (five, fiveAt) = (snapshot.FiveHour, snapshot.CapturedAt);
            if (snapshot.Week is { } weekWindow && weekWindow.ResetsAt > now && snapshot.CapturedAt > weekAt)
                (week, weekAt) = (snapshot.Week, snapshot.CapturedAt);
        }

        if (newest is null) return null;
        SetCandidate(newest.SourceFile);
        var todayUsed = await EstimateTodayUsedAsync(week, cancellationToken);
        return newest with { FiveHour = five, Week = week, TodayUsedPercent = todayUsed };
    }

    private async Task<double?> EstimateTodayUsedAsync(LimitWindow? currentWeek, CancellationToken ct)
    {
        if (currentWeek is null) return null;
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (_baselineDate == today && _baselineReset == currentWeek.ResetsAt.ToUnixTimeSeconds())
            return Math.Max(0, currentWeek.UsedPercent - _baselineWeekUsed);

        UsageSnapshot? baseline = null;
        var previousDay = DateTime.Today.AddDays(-1);
        var previousDir = Path.Combine(_sessionsRoot, previousDay.ToString("yyyy"),
            previousDay.ToString("MM"), previousDay.ToString("dd"));
        if (Directory.Exists(previousDir))
        {
            foreach (var file in new DirectoryInfo(previousDir).EnumerateFiles("*.jsonl")
                         .OrderByDescending(f => f.LastWriteTimeUtc).Take(6))
            {
                var snapshot = await ReadFileAsync(file.FullName, ct);
                if (snapshot?.Week?.ResetsAt == currentWeek.ResetsAt &&
                    (baseline is null || snapshot.CapturedAt > baseline.CapturedAt))
                    baseline = snapshot;
            }
        }

        if (baseline is null)
        {
            var currentDir = Path.Combine(_sessionsRoot, DateTime.Today.ToString("yyyy"),
                DateTime.Today.ToString("MM"), DateTime.Today.ToString("dd"));
            if (Directory.Exists(currentDir))
            {
                foreach (var file in new DirectoryInfo(currentDir).EnumerateFiles("*.jsonl")
                             .OrderBy(f => f.CreationTimeUtc).Take(16))
                {
                    var snapshot = await ReadOldestFileSnapshotAsync(file.FullName, ct);
                    if (snapshot?.Week?.ResetsAt == currentWeek.ResetsAt &&
                        (baseline is null || snapshot.CapturedAt < baseline.CapturedAt))
                        baseline = snapshot;
                }
            }
        }

        _baselineDate = today;
        _baselineReset = currentWeek.ResetsAt.ToUnixTimeSeconds();
        _baselineWeekUsed = baseline?.Week?.UsedPercent ?? currentWeek.UsedPercent;
        return currentWeek.UsedPercent >= _baselineWeekUsed
            ? currentWeek.UsedPercent - _baselineWeekUsed
            : currentWeek.UsedPercent;
    }

    private static async Task<UsageSnapshot?> ReadOldestFileSnapshotAsync(string path, CancellationToken ct)
    {
        const int probeSize = 512 * 1024;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 8192, FileOptions.Asynchronous);
            var head = await ReadRangeAsync(stream, 0, Math.Min(stream.Length, probeSize), ct);
            return ParseOldest(head, path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    private async Task<UsageSnapshot?> ReadFileAsync(string path, CancellationToken ct)
    {
        const int probeSize = 512 * 1024;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);

            CacheEntry? cached;
            lock (_gate) _cache.TryGetValue(path, out cached);
            if (cached is not null && cached.FileLength == stream.Length)
                return cached.Snapshot;

            if (cached is not null && cached.FileLength <= stream.Length)
            {
                // Read only data appended since the last pass, with a small overlap
                // in case the previous pass ended while a JSONL line was mid-write.
                var start = Math.Max(0, cached.FileLength - 8192);
                var appended = await ReadRangeAsync(stream, start, stream.Length, ct);
                var newer = ParseNewest(appended, path);
                var result = newer is not null && newer.CapturedAt >= cached.Snapshot.CapturedAt
                    ? newer : cached.Snapshot;
                lock (_gate) _cache[path] = new(stream.Length, result);
                return result;
            }

            // First encounter: inspect a bounded tail, then a bounded head. Rate
            // snapshots normally appear in one of these regions. Never walk an
            // entire long transcript during widget startup.
            var tailStart = Math.Max(0, stream.Length - probeSize);
            var tail = await ReadRangeAsync(stream, tailStart, stream.Length, ct);
            var snapshot = ParseNewest(tail, path);
            if (snapshot is null && tailStart > 0)
            {
                var headEnd = Math.Min(stream.Length, probeSize);
                var head = await ReadRangeAsync(stream, 0, headEnd, ct);
                snapshot = ParseNewest(head, path);
            }
            if (snapshot is not null)
            {
                lock (_gate) _cache[path] = new(stream.Length, snapshot);
                return snapshot;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    private static async Task<string> ReadRangeAsync(FileStream stream, long start, long end, CancellationToken ct)
    {
        stream.Seek(start, SeekOrigin.Begin);
        var length = checked((int)(end - start));
        var buffer = new byte[length];
        var read = 0;
        while (read < length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read, length - read), ct);
            if (count == 0) break;
            read += count;
        }
        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    private static UsageSnapshot? ParseNewest(string text, string path)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i];
            var jsonStart = line.IndexOf('{');
            if (jsonStart < 0 || !line.AsSpan(jsonStart).Contains("\"rate_limits\"", StringComparison.Ordinal))
                continue;

            var snapshot = ParseLine(line[jsonStart..], path);
            if (snapshot is not null) return snapshot;
        }
        return null;
    }

    private static UsageSnapshot? ParseOldest(string text, string path)
    {
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var jsonStart = line.IndexOf('{');
            if (jsonStart < 0 || !line.AsSpan(jsonStart).Contains("\"rate_limits\"", StringComparison.Ordinal))
                continue;
            var snapshot = ParseLine(line[jsonStart..], path);
            if (snapshot is not null) return snapshot;
        }
        return null;
    }

    private static UsageSnapshot? ParseLine(string json, string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("payload", out var payload)) return null;
            JsonElement limits;
            if (payload.TryGetProperty("rate_limits", out var directLimits))
                limits = directLimits;
            else if (payload.TryGetProperty("info", out var info) &&
                     info.TryGetProperty("rate_limits", out var nestedLimits))
                limits = nestedLimits;
            else
                return null;
            if (limits.ValueKind != JsonValueKind.Object) return null;

            // Codex can emit separate metered buckets for specific models.
            // Only the canonical "codex" bucket represents the general
            // 5-hour/weekly allowance shown by this widget.
            if (limits.TryGetProperty("limit_id", out var limitIdElement))
            {
                var limitId = limitIdElement.GetString();
                if (!string.IsNullOrWhiteSpace(limitId) &&
                    !limitId.Equals("codex", StringComparison.OrdinalIgnoreCase))
                    return null;
            }

            LimitWindow? five = null;
            LimitWindow? week = null;
            foreach (var name in new[] { "primary", "secondary" })
            {
                if (!limits.TryGetProperty(name, out var item) || item.ValueKind != JsonValueKind.Object)
                    continue;
                if (!item.TryGetProperty("used_percent", out var used) ||
                    !item.TryGetProperty("window_minutes", out var window) ||
                    !item.TryGetProperty("resets_at", out var reset)) continue;

                var value = new LimitWindow(used.GetDouble(), window.GetInt32(),
                    DateTimeOffset.FromUnixTimeSeconds(reset.GetInt64()));
                if (value.WindowMinutes is >= 270 and <= 330) five = value;
                else if (value.WindowMinutes is >= 9000 and <= 11000) week = value;
            }

            if (five is null && week is null) return null;
            var captured = root.TryGetProperty("timestamp", out var stamp) &&
                           DateTimeOffset.TryParse(stamp.GetString(), out var parsed)
                ? parsed : new FileInfo(path).LastWriteTimeUtc;
            return new UsageSnapshot(five, week, captured, path);
        }
        catch (JsonException) { return null; }
    }

    public static bool RunSelfTest()
    {
        const string canonical = """
            {"timestamp":"2026-07-30T14:40:28Z","payload":{"rate_limits":{
              "limit_id":"codex","primary":{"used_percent":31,"window_minutes":10080,"resets_at":1785903281}
            }}}
            """;
        const string modelSpecific = """
            {"timestamp":"2026-07-30T14:40:38Z","payload":{"rate_limits":{
              "limit_id":"codex_bengalfox","limit_name":"GPT-5.3-Codex-Spark",
              "primary":{"used_percent":0,"window_minutes":10080,"resets_at":1786027234}
            }}}
            """;

        var accepted = ParseLine(canonical, "canonical.jsonl");
        var rejected = ParseLine(modelSpecific, "model-specific.jsonl");
        return accepted?.Week?.UsedPercent == 31 && rejected is null;
    }

    public void Dispose() => _watcher?.Dispose();
}
