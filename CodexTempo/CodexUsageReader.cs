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
    private double? _baselineWeekUsed;
    private sealed record CacheEntry(long FileLength, DateTime WriteTime, UsageSnapshot? Snapshot);

    public CodexUsageReader() : this(Path.Combine(CodexPathResolver.ResolveHome(), "sessions"))
    {
    }

    internal CodexUsageReader(string sessionsRoot)
    {
        _sessionsRoot = sessionsRoot;

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

        // Several Codex tasks can write session files at the same time. A small
        // fixed limit here used to let model-specific sessions crowd the
        // canonical account snapshot out of the scan, leaving the widget stuck
        // on an old value. 128 cached metadata probes remain cheap while covering
        // busy desktop sessions reliably.
        foreach (var file in files.DistinctBy(f => f.FullName)
                     .OrderByDescending(f => f.LastWriteTimeUtc)
                     .Take(128))
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
            return _baselineWeekUsed is { } known ? Math.Max(0, currentWeek.UsedPercent - known) : null;

        UsageSnapshot? baseline = null;
        var previousDay = DateTime.Today.AddDays(-1);
        var previousDir = Path.Combine(_sessionsRoot, previousDay.ToString("yyyy"),
            previousDay.ToString("MM"), previousDay.ToString("dd"));
        if (Directory.Exists(previousDir))
        {
            foreach (var file in new DirectoryInfo(previousDir).EnumerateFiles("*.jsonl")
                         .OrderByDescending(f => f.LastWriteTimeUtc).Take(6))
            {
                var snapshot = await ReadBeforeMidnightAsync(file.FullName, ct);
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
                    var snapshot = await ReadBeforeMidnightAsync(file.FullName, ct);
                    if (snapshot?.Week?.ResetsAt == currentWeek.ResetsAt &&
                        (baseline is null || snapshot.CapturedAt < baseline.CapturedAt))
                        baseline = snapshot;
                }
            }
        }

        _baselineDate = today;
        _baselineReset = currentWeek.ResetsAt.ToUnixTimeSeconds();
        _baselineWeekUsed = baseline?.Week?.UsedPercent;
        return _baselineWeekUsed is { } start && currentWeek.UsedPercent >= start
            ? currentWeek.UsedPercent - start : null;
    }

    private static async Task<UsageSnapshot?> ReadBeforeMidnightAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite | FileShare.Delete,8192,FileOptions.Asynchronous);
            const int limit = 512 * 1024;
            var end = stream.Length;
            var tail = await ReadRangeAsync(stream,Math.Max(0,end-limit),end,ct);
            var head = end > limit ? await ReadRangeAsync(stream,0,Math.Min(end,limit),ct) : "";
            var cutoff = new DateTimeOffset(DateTime.Today);
            return (head + "\n" + tail).Split('\n',StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains("\"rate_limits\"",StringComparison.Ordinal))
                .Select(line => ParseLine(line,path))
                .Where(s => s is not null && s.CapturedAt <= cutoff && s.CapturedAt >= cutoff.AddDays(-1))
                .MaxBy(s => s!.CapturedAt);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    internal Task<double?> EstimateTodayUsedForAsync(LimitWindow currentWeek, CancellationToken ct) =>
        EstimateTodayUsedAsync(currentWeek, ct);

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
            var end = stream.Length;
            var writeTime = File.GetLastWriteTimeUtc(path);
            lock (_gate) _cache.TryGetValue(path, out cached);
            if (cached is not null && cached.FileLength == end && cached.WriteTime == writeTime)
                return cached.Snapshot;

            if (cached?.Snapshot is not null && cached.FileLength < end)
            {
                // Read only data appended since the last pass, with a small overlap
                // in case the previous pass ended while a JSONL line was mid-write.
                var start = Math.Max(Math.Max(0, cached.FileLength - 8192), end - probeSize);
                var appended = await ReadRangeAsync(stream, start, end, ct);
                var newer = ParseNewest(appended, path);
                var result = newer is not null && newer.CapturedAt >= cached.Snapshot.CapturedAt
                    ? newer : cached.Snapshot;
                Store(path, new(end,writeTime,result));
                return result;
            }

            // First encounter: inspect a bounded tail, then a bounded head. Rate
            // snapshots normally appear in one of these regions. Never walk an
            // entire long transcript during widget startup.
            var tailStart = Math.Max(0, end - probeSize);
            var tail = await ReadRangeAsync(stream, tailStart, end, ct);
            var snapshot = ParseNewest(tail, path);
            if (snapshot is null && tailStart > 0)
            {
                var headEnd = Math.Min(stream.Length, probeSize);
                var head = await ReadRangeAsync(stream, 0, headEnd, ct);
                snapshot = ParseNewest(head, path);
            }
            Store(path, new(end,writeTime,snapshot));
            return snapshot;
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

    private void Store(string path, CacheEntry entry)
    {
        lock (_gate)
        {
            if (_cache.Count >= 256 && !_cache.ContainsKey(path)) _cache.Remove(_cache.Keys.First());
            _cache[path] = entry;
        }
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

                if (used.ValueKind != JsonValueKind.Number || !used.TryGetDouble(out var percent) ||
                    window.ValueKind != JsonValueKind.Number || !window.TryGetInt32(out var minutes) ||
                    reset.ValueKind != JsonValueKind.Number || !reset.TryGetInt64(out var seconds) ||
                    seconds < 0 || seconds > 253402300799) continue;
                var value = new LimitWindow(percent, minutes, DateTimeOffset.FromUnixTimeSeconds(seconds));
                if (!double.IsFinite(value.UsedPercent) || value.UsedPercent < 0 || value.UsedPercent > 100) continue;
                if (value.WindowMinutes is >= 270 and <= 330) five = value;
                else if (value.WindowMinutes is >= 9000 and <= 11000) week = value;
            }

            if (five is null && week is null) return null;
            var captured = root.TryGetProperty("timestamp", out var stamp) &&
                           DateTimeOffset.TryParse(stamp.GetString(), out var parsed)
                ? parsed : new FileInfo(path).LastWriteTimeUtc;
            return new UsageSnapshot(five, week, captured, path);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentOutOfRangeException or OverflowException) { return null; }
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
        return accepted?.Week?.UsedPercent == 31
               && rejected is null
               && ParseLine("{\"payload\":{\"rate_limits\":{\"primary\":{\"used_percent\":null,\"window_minutes\":10080,\"resets_at\":1800000000}}}}", "bad.jsonl") is null
               && RunRefreshSelfTestCode() == 0;
    }

    internal static int RunRefreshSelfTestCode() =>
        Task.Run(RunRefreshSelfTestAsync).GetAwaiter().GetResult();

    private static async Task<int> RunRefreshSelfTestAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"codex-tempo-test-{Guid.NewGuid():N}");
        var day = DateTime.Today;
        var folder = Path.Combine(root, day.ToString("yyyy"), day.ToString("MM"), day.ToString("dd"));
        Directory.CreateDirectory(folder);
        var reset = DateTimeOffset.Now.AddDays(3).ToUnixTimeSeconds();
        var canonical = Path.Combine(folder, "canonical.jsonl");

        try
        {
            await File.WriteAllTextAsync(canonical,
                BuildTestLine(20, reset, "2026-07-30T14:40:28Z"));

            // More than the old eight-file scan limit, all newer than the
            // canonical file and all intentionally irrelevant.
            for (var i = 0; i < 12; i++)
            {
                var decoy = Path.Combine(folder, $"model-{i:00}.jsonl");
                await File.WriteAllTextAsync(decoy,
                    BuildTestLine(0, reset, "2026-07-30T14:40:38Z", "codex_bengalfox"));
                File.SetLastWriteTimeUtc(decoy, DateTime.UtcNow.AddSeconds(i + 1));
            }

            using var reader = new CodexUsageReader(root);
            var first = await reader.ReadLatestAsync();
            if (first?.Week?.UsedPercent != 20) return 1;

            await File.AppendAllTextAsync(canonical,
                Environment.NewLine + BuildTestLine(24, reset, "2026-07-30T14:41:28Z"));
            var second = await reader.ReadLatestAsync();
            if (second?.Week?.UsedPercent != 24) return 2;
            var crossed = Path.Combine(folder, "cross-midnight.jsonl");
            var midnight = new DateTimeOffset(DateTime.Today);
            await File.WriteAllTextAsync(crossed,
                BuildTestLine(30, reset, midnight.AddMinutes(-1).ToString("o")) + "\n" +
                BuildTestLine(37, reset, midnight.AddMinutes(1).ToString("o")));
            return (await ReadBeforeMidnightAsync(crossed, CancellationToken.None))?.Week?.UsedPercent == 30 ? 0 : 3;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string BuildTestLine(
        double usedPercent,
        long reset,
        string timestamp,
        string limitId = "codex") =>
        JsonSerializer.Serialize(new
        {
            timestamp,
            payload = new
            {
                rate_limits = new
                {
                    limit_id = limitId,
                    primary = new
                    {
                        used_percent = usedPercent,
                        window_minutes = 10080,
                        resets_at = reset
                    }
                }
            }
        });

    public void Dispose() => _watcher?.Dispose();
}
