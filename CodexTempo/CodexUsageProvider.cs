namespace CodexTempo;

public sealed class CodexUsageProvider : IDisposable
{
    public const string CachedSourceName = "Codex App Server (cached)";
    private readonly CodexAppServerClient _appServer = new();
    private UsageSnapshot? _lastOfficial = UsageCache.Load();
    private UsageSnapshot? _pendingLower;
    private UsageSnapshot? _persisted;
    private DateTimeOffset _nextAttempt;
    private int _failures;
    private bool _disposed;
    // Verify account/read before showing a persisted value, including at startup.
    public UsageSnapshot? StartupSnapshot => null;
    public void RequestRefresh() => _nextAttempt = DateTimeOffset.MinValue;

    public async Task<UsageSnapshot?> ReadLatestAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return null;
        var now = DateTimeOffset.Now;
        if (now < _nextAttempt) return VerifiedCache(now);
        var live = await _appServer.ReadLatestAsync(cancellationToken);
        if (_disposed) return null;
        if (live is null)
        {
            _pendingLower = null;
            _failures = Math.Min(_failures + 1, 5);
            _nextAttempt = now.AddSeconds(Math.Min(120, 10 * Math.Pow(2, _failures - 1)));
            return VerifiedCache(now);
        }
        _failures = 0;
        _nextAttempt = now.AddSeconds(10);
        if (_lastOfficial is not null && !UsageLogic.SameAccount(live, _lastOfficial)) _lastOfficial = null;
        if (_lastOfficial is not null && UsageLogic.Drops(live, _lastOfficial))
        {
            // Hold only one poll, without assigning a fresh timestamp to old values.
            // A second non-decreasing lower reading confirms an official correction.
            var confirmed = _pendingLower is not null && UsageLogic.SameAccount(live, _pendingLower)
                && ConfirmWindow(live.Week, _pendingLower.Week) && ConfirmWindow(live.FiveHour, _pendingLower.FiveHour);
            _pendingLower = live;
            if (!confirmed) return UsageCache.Active(_lastOfficial, now);
        }
        _pendingLower = null;
        _lastOfficial = UsageLogic.TrackDay(live, _lastOfficial);
        if (_persisted is null || _lastOfficial.CapturedAt - _persisted.CapturedAt >= TimeSpan.FromMinutes(1) ||
            _lastOfficial with { CapturedAt = _persisted.CapturedAt } != _persisted)
        { UsageCache.Save(_lastOfficial); _persisted = _lastOfficial; }
        return _lastOfficial;
    }

    private static bool ConfirmWindow(LimitWindow? a, LimitWindow? b) =>
        a is null && b is null || UsageLogic.SameWindow(a,b) && a!.UsedPercent >= b!.UsedPercent;

    private UsageSnapshot? VerifiedCache(DateTimeOffset now) => _lastOfficial is { } old &&
        old.AccountKey is not null && old.AccountKey == _appServer.AccountKey &&
        old.ContextStamp == AccountContext.Stamp() ? UsageCache.Active(old, now) : null;

    public static bool RunSelfTest() => UsageLogic.RunSelfTest() &&
        ConfirmWindow(new(40,10080,DateTimeOffset.UnixEpoch),new(39,10080,DateTimeOffset.UnixEpoch)) &&
        !ConfirmWindow(new(38,10080,DateTimeOffset.UnixEpoch),new(39,10080,DateTimeOffset.UnixEpoch));

    public void Dispose() { _disposed = true; _appServer.Dispose(); }
}
