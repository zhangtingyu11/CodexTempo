namespace CodexTempo;

public static class RecommendationEngine
{
    public static PaceAdvice Recommend(UsageSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.Week is null)
            return new("等待周额度数据", "开始一次 Codex 对话后会自动出现", "—", 0, 0, PaceTone.Waiting);

        var week = snapshot.Week;
        var hoursLeft = Math.Max(week.TimeRemaining(now).TotalHours, 0.25);
        var naturalHourlyBurn = 100d / Math.Max(week.WindowMinutes / 60d, 1);
        var neededHourlyBurn = week.RemainingPercent / hoursLeft;
        var rate = neededHourlyBurn / naturalHourlyBurn;

        // The short window is a safety governor. It keeps the weekly plan from
        // recommending a burst that immediately exhausts the current 5h window.
        if (snapshot.FiveHour is { } shortWindow)
        {
            var shortRemaining = shortWindow.RemainingPercent;
            var shortHours = shortWindow.TimeRemaining(now).TotalHours;
            if (shortRemaining <= 8 && shortHours > .25) rate = Math.Min(rate, .18);
            else if (shortRemaining <= 20 && shortHours > .5) rate = Math.Min(rate, .4);
            else if (shortRemaining <= 35 && shortHours > 1) rate = Math.Min(rate, .72);
        }

        rate = Math.Clamp(rate, 0, 2.5);
        var midnight = new DateTimeOffset(now.LocalDateTime.Date.AddDays(1));
        var remainingToday = Math.Min(week.RemainingPercent,
            neededHourlyBurn * Math.Max(0, Math.Min(hoursLeft, (midnight - now).TotalHours)));
        var perDay = (snapshot.TodayUsedPercent ?? 0) + remainingToday;
        var detail = snapshot.TodayUsedPercent is { } todayUsed
            ? $"今日约 {todayUsed:0.#}% / 目标 {perDay:0.#}% · 还可安排 {remainingToday:0.#}%"
            : $"今日已用暂无法估算 · 今日还可安排约 {remainingToday:0.#}%";
        if (snapshot.FiveHour?.RemainingPercent <= 8) detail += " · 5h 紧张，先休息";

        if (rate < .5)
            return new("建议休息一下", detail,
                $"{rate:0.0}× 周均速", rate, perDay, PaceTone.Urgent);
        if (rate < .82)
            return new("今天节奏偏快", detail,
                $"{rate:0.0}× 周均速", rate, perDay, PaceTone.Caution);
        if (rate <= 1.22)
            return new("保持稳定", detail,
                $"{rate:0.0}× 周均速", rate, perDay, PaceTone.Calm);

        return new("今天表现不错", detail,
            $"{rate:0.0}× 周均速", rate, perDay, PaceTone.Encourage);
    }

    public static string FormatDuration(TimeSpan span)
    {
        if (span <= TimeSpan.Zero) return "即将";
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays}天{span.Hours}小时";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}小时{span.Minutes}分";
        return $"{Math.Max(1, span.Minutes)}分钟";
    }

    public static bool RunSelfTest()
    {
        var now = DateTimeOffset.Parse("2026-07-30T12:00:00+08:00");
        var balanced = new UsageSnapshot(
            new(30, 300, now.AddHours(3)),
            new(50, 10080, now.AddHours(84)), now, "");
        var guarded = balanced with { FiveHour = new(94, 300, now.AddHours(2)) };
        var behind = balanced with { Week = new(10, 10080, now.AddHours(48)) };
        var late = new DateTimeOffset(DateTime.Today.AddHours(23));
        var lateSnapshot = new UsageSnapshot(null,new(30,10080,late.AddDays(7)),late,"test");
        return Recommend(balanced, now).Tone == PaceTone.Calm
            && Recommend(balanced, now).Detail.Contains("暂无法估算")
            && Recommend(guarded, now).Tone == PaceTone.Urgent
            && Recommend(behind, now).Tone == PaceTone.Encourage
            && Math.Abs(Recommend(lateSnapshot,late).DailyBudgetPercent - 70d / 168) < .001
            && Math.Abs(Recommend(lateSnapshot with { TodayUsedPercent = 7 },late).DailyBudgetPercent - (7 + 70d / 168)) < .001
            && FormatDuration(TimeSpan.FromMinutes(90)) == "1小时30分";
    }
}
