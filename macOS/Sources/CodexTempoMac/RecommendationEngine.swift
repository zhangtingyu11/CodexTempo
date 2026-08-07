import Foundation

enum RecommendationEngine {
    static func recommend(snapshot: UsageSnapshot, now: Date) -> PaceAdvice {
        guard let week = snapshot.week else {
            return PaceAdvice(
                title: "等待周额度数据",
                detail: "开始一次 Codex 对话后会自动出现",
                rateLabel: "—",
                rateMultiplier: 0,
                dailyBudgetPercent: 0,
                tone: .waiting
            )
        }

        let hoursLeft = max(week.timeRemaining(at: now) / 3_600, 0.25)
        let naturalHourlyBurn = 100 / max(Double(week.windowMinutes) / 60, 1)
        let neededHourlyBurn = week.remainingPercent / hoursLeft
        var rate = neededHourlyBurn / naturalHourlyBurn

        if let shortWindow = snapshot.fiveHour {
            let shortRemaining = shortWindow.remainingPercent
            let shortHours = shortWindow.timeRemaining(at: now) / 3_600
            if shortRemaining <= 8, shortHours > 0.25 {
                rate = min(rate, 0.18)
            } else if shortRemaining <= 20, shortHours > 0.5 {
                rate = min(rate, 0.4)
            } else if shortRemaining <= 35, shortHours > 1 {
                rate = min(rate, 0.72)
            }
        }

        rate = min(max(rate, 0.15), 2.5)
        let perDay = min(week.remainingPercent, neededHourlyBurn * 24)
        let todayUsed = max(0, snapshot.todayUsedPercent ?? 0)
        let remainingToday = max(0, perDay - todayUsed)
        let detail: String
        if todayUsed <= perDay {
            detail = "今日约 \(number(todayUsed))% / 目标 \(number(perDay))% · 还可用 \(number(remainingToday))% · 本周 \(number(week.usedPercent))%"
        } else {
            detail = "今日约 \(number(todayUsed))% · 已超目标 \(number(todayUsed - perDay))% · 本周 \(number(week.usedPercent))%"
        }

        if rate < 0.5 {
            return advice("建议休息一下", detail, rate, perDay, .urgent)
        }
        if rate < 0.82 {
            return advice("今天节奏偏快", detail, rate, perDay, .caution)
        }
        if rate <= 1.22 {
            return advice("保持稳定", detail, rate, perDay, .calm)
        }
        return advice("今天表现不错", detail, rate, perDay, .encourage)
    }

    static func formatDuration(_ interval: TimeInterval) -> String {
        guard interval > 0 else { return "即将" }
        let totalMinutes = max(Int(interval / 60), 1)
        let days = totalMinutes / (24 * 60)
        let hours = (totalMinutes / 60) % 24
        let minutes = totalMinutes % 60
        if days >= 1 { return "\(days)天\(hours)小时" }
        if totalMinutes >= 60 { return "\(totalMinutes / 60)小时\(minutes)分" }
        return "\(totalMinutes)分钟"
    }

    private static func advice(
        _ title: String,
        _ detail: String,
        _ rate: Double,
        _ perDay: Double,
        _ tone: PaceTone
    ) -> PaceAdvice {
        PaceAdvice(
            title: title,
            detail: detail,
            rateLabel: "\(String(format: "%.1f", rate))× 周均速",
            rateMultiplier: rate,
            dailyBudgetPercent: perDay,
            tone: tone
        )
    }

    private static func number(_ value: Double) -> String {
        let rounded = (value * 10).rounded() / 10
        if rounded.rounded() == rounded { return String(Int(rounded)) }
        return String(format: "%.1f", rounded)
    }
}
