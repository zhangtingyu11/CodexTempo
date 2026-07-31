namespace CodexTempo;

public sealed class CodexUsageProvider : IDisposable
{
    private readonly CodexAppServerClient _appServer = new();
    private readonly CodexUsageReader _sessionReader = new();

    public async Task<UsageSnapshot?> ReadLatestAsync(CancellationToken cancellationToken = default)
    {
        var live = await _appServer.ReadLatestAsync(cancellationToken);
        if (live is not null)
        {
            var todayUsed = live.Week is null
                ? null
                : await _sessionReader.EstimateTodayUsedForAsync(live.Week, cancellationToken);
            return live with { TodayUsedPercent = todayUsed };
        }

        return await _sessionReader.ReadLatestAsync(cancellationToken);
    }

    public void Dispose()
    {
        _appServer.Dispose();
        _sessionReader.Dispose();
    }
}
