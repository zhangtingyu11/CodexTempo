namespace CodexTempo;

public sealed record LimitWindow(double UsedPercent, int WindowMinutes, DateTimeOffset ResetsAt)
{
    public double RemainingPercent => Math.Clamp(100 - UsedPercent, 0, 100);
    public TimeSpan TimeRemaining(DateTimeOffset now) =>
        ResetsAt > now ? ResetsAt - now : TimeSpan.Zero;
}

public sealed record UsageSnapshot(
    LimitWindow? FiveHour,
    LimitWindow? Week,
    DateTimeOffset CapturedAt,
    string SourceFile,
    double? TodayUsedPercent = null);

public enum PaceTone { Calm, Encourage, Caution, Urgent, Waiting }

public sealed record PaceAdvice(
    string Title,
    string Detail,
    string RateLabel,
    double RateMultiplier,
    double DailyBudgetPercent,
    PaceTone Tone);
