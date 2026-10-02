namespace CodexTempo;

internal static class UsageLogic
{
    public static bool SameWindow(LimitWindow? a, LimitWindow? b) => a is not null && b is not null &&
        a.WindowMinutes == b.WindowMinutes && Math.Abs((a.ResetsAt - b.ResetsAt).TotalSeconds) <= 90;

    public static bool SameAccount(UsageSnapshot a, UsageSnapshot b) =>
        a.AccountKey is not null && a.AccountKey == b.AccountKey && a.ContextStamp == b.ContextStamp;

    public static bool Drops(UsageSnapshot current, UsageSnapshot previous) => SameAccount(current, previous) &&
        (SameWindow(current.Week, previous.Week) && current.Week!.UsedPercent < previous.Week!.UsedPercent ||
         SameWindow(current.FiveHour, previous.FiveHour) && current.FiveHour!.UsedPercent < previous.FiveHour!.UsedPercent);

    public static UsageSnapshot TrackDay(UsageSnapshot current, UsageSnapshot? previous)
    {
        var day = DateOnly.FromDateTime(current.CapturedAt.LocalDateTime);
        double? baseline = null;
        if (previous is not null && SameAccount(current, previous) && SameWindow(current.Week, previous.Week))
        {
            if (previous.UsageDay == day) baseline = previous.DayStartUsed;
            else
            {
                var midnight = new DateTimeOffset(current.CapturedAt.LocalDateTime.Date);
                // Only a recent pre-midnight observation is a defensible daily estimate.
                if (previous.CapturedAt <= midnight && midnight - previous.CapturedAt <= TimeSpan.FromMinutes(20))
                    baseline = previous.Week!.UsedPercent;
            }
        }
        if (current.Week is null || baseline > current.Week.UsedPercent) baseline = null;
        return current with { UsageDay = day, DayStartUsed = baseline,
            TodayUsedPercent = baseline is { } value ? current.Week!.UsedPercent - value : null };
    }

    public static bool RunSelfTest()
    {
        var before = new DateTimeOffset(2026, 10, 1, 23, 59, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026,10,1)));
        var old = new UsageSnapshot(null, new(30,10080,before.AddDays(3)), before, "test", AccountKey:"a", ContextStamp:"x");
        var next = old with { Week = old.Week! with { UsedPercent = 37 }, CapturedAt = before.AddMinutes(2) };
        var tracked = TrackDay(next, old);
        return tracked.TodayUsedPercent == 7 && TrackDay(tracked with { Week = tracked.Week! with { UsedPercent = 39 } }, tracked).TodayUsedPercent == 9
            && TrackDay(next with { AccountKey = "b" },old).TodayUsedPercent is null
            && TrackDay(next, old with { CapturedAt = before.AddHours(-2) }).TodayUsedPercent is null;
    }
}
