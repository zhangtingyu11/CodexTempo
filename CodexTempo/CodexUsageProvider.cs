namespace CodexTempo;

public sealed class CodexUsageProvider : IDisposable
{
    public const string CachedSourceName = "Codex App Server (cached)";

    private readonly CodexAppServerClient _appServer = new();
    private readonly CodexUsageReader _sessionReader = new();
    private UsageSnapshot? _lastOfficial;

    public async Task<UsageSnapshot?> ReadLatestAsync(CancellationToken cancellationToken = default)
    {
        var live = await _appServer.ReadLatestAsync(cancellationToken);
        if (live is not null)
        {
            live = StabilizeOfficial(live, _lastOfficial, DateTimeOffset.Now);
            var todayUsed = live.Week is null
                ? null
                : await _sessionReader.EstimateTodayUsedForAsync(live.Week, cancellationToken);
            if (_lastOfficial is not null &&
                DateOnly.FromDateTime(_lastOfficial.CapturedAt.LocalDateTime) == DateOnly.FromDateTime(DateTime.Now) &&
                SameWindow(live.Week, _lastOfficial.Week))
                todayUsed = Math.Max(todayUsed ?? 0, _lastOfficial.TodayUsedPercent ?? 0);

            _lastOfficial = live with { TodayUsedPercent = todayUsed };
            return _lastOfficial;
        }

        var now = DateTimeOffset.Now;
        var preserved = PreserveAfterFailure(_lastOfficial, now);
        if (preserved is not null) return preserved;

        return await _sessionReader.ReadLatestAsync(cancellationToken);
    }

    private static UsageSnapshot StabilizeOfficial(
        UsageSnapshot current,
        UsageSnapshot? previous,
        DateTimeOffset now)
    {
        if (previous is null) return current;
        return current with
        {
            FiveHour = StabilizeWindow(current.FiveHour, previous.FiveHour, now),
            Week = StabilizeWindow(current.Week, previous.Week, now)
        };
    }

    private static LimitWindow? StabilizeWindow(
        LimitWindow? current,
        LimitWindow? previous,
        DateTimeOffset now)
    {
        if (previous is null) return current;
        if (current is null)
            return previous.ResetsAt > now ? previous : null;
        if (!SameWindow(current, previous)) return current;

        // Used percentage is monotonic inside one quota window. A lower value
        // is a stale replica or fallback artifact, so retain the highest
        // confirmed value until the reset timestamp actually changes.
        return current.UsedPercent < previous.UsedPercent ? previous : current;
    }

    private static bool SameWindow(LimitWindow? left, LimitWindow? right) =>
        left is not null &&
        right is not null &&
        left.WindowMinutes == right.WindowMinutes &&
        Math.Abs((left.ResetsAt - right.ResetsAt).TotalSeconds) <= 90;

    private static bool HasActiveWindow(UsageSnapshot snapshot, DateTimeOffset now) =>
        snapshot.FiveHour?.ResetsAt > now || snapshot.Week?.ResetsAt > now;

    private static UsageSnapshot? PreserveAfterFailure(
        UsageSnapshot? previous,
        DateTimeOffset now) =>
        previous is not null && HasActiveWindow(previous, now)
            ? previous with { SourceFile = CachedSourceName }
            : null;

    public static bool RunSelfTest()
    {
        var now = DateTimeOffset.Parse("2026-08-03T12:00:00+08:00");
        var reset = now.AddDays(2);
        var previous = new UsageSnapshot(
            null,
            new LimitWindow(38, 10080, reset),
            now.AddSeconds(-10),
            CodexAppServerClient.SourceName,
            4);
        var staleReplica = previous with
        {
            Week = new LimitWindow(33, 10080, reset.AddSeconds(1)),
            CapturedAt = now
        };
        var advanced = staleReplica with { Week = new LimitWindow(40, 10080, reset) };
        var resetWindow = staleReplica with
        {
            Week = new LimitWindow(1, 10080, reset.AddDays(7))
        };
        var preserved = PreserveAfterFailure(previous, now);

        return StabilizeOfficial(staleReplica, previous, now).Week?.UsedPercent == 38
               && StabilizeOfficial(advanced, previous, now).Week?.UsedPercent == 40
               && StabilizeOfficial(resetWindow, previous, now).Week?.UsedPercent == 1
               && HasActiveWindow(previous, now)
               && preserved?.SourceFile == CachedSourceName
               && preserved.Week?.RemainingPercent == 62
               && PreserveAfterFailure(previous, reset.AddSeconds(1)) is null;
    }

    public void Dispose()
    {
        _appServer.Dispose();
        _sessionReader.Dispose();
    }
}
