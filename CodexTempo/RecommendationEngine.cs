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

        rate = Math.Clamp(rate, .15, 2.5);
        var perDay = Math.Min(week.RemainingPercent, neededHourlyBurn * 24);
        var todayUsed = Math.Max(0, snapshot.TodayUsedPercent ?? 0);
        var remainingToday = Math.Max(0, perDay - todayUsed);
        var detail = todayUsed <= perDay
            ? $"今日约 {todayUsed:0.#}% / 目标 {perDay:0.#}% · 还可用 {remainingToday:0.#}% · 本周 {week.UsedPercent:0.#}%"
            : $"今日约 {todayUsed:0.#}% · 已超目标 {todayUsed - perDay:0.#}% · 本周 {week.UsedPercent:0.#}%";

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
        return Recommend(balanced, now).Tone == PaceTone.Calm
            && Recommend(balanced, now).Detail.Contains("今日约 0%")
            && Recommend(guarded, now).Tone == PaceTone.Urgent
            && Recommend(behind, now).Tone == PaceTone.Encourage
            && FormatDuration(TimeSpan.FromMinutes(90)) == "1小时30分";
    }
}
