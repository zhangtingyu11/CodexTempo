import Foundation

struct LimitWindow: Codable, Equatable, Sendable {
    let usedPercent: Double
    let windowMinutes: Int
    let resetsAt: Date

    var remainingPercent: Double {
        min(max(100 - usedPercent, 0), 100)
    }

    func timeRemaining(at now: Date) -> TimeInterval {
        max(resetsAt.timeIntervalSince(now), 0)
    }
}

struct UsageSnapshot: Codable, Equatable, Sendable {
    let fiveHour: LimitWindow?
    let week: LimitWindow?
    let capturedAt: Date
    let source: String
    let todayUsedPercent: Double?

    init(
        fiveHour: LimitWindow?,
        week: LimitWindow?,
        capturedAt: Date,
        source: String,
        todayUsedPercent: Double? = nil
    ) {
        self.fiveHour = fiveHour
        self.week = week
        self.capturedAt = capturedAt
        self.source = source
        self.todayUsedPercent = todayUsedPercent
    }

    func replacing(
        fiveHour: LimitWindow? = nil,
        week: LimitWindow? = nil,
        source: String? = nil,
        todayUsedPercent: Double? = nil,
        keepNilWindows: Bool = true
    ) -> UsageSnapshot {
        UsageSnapshot(
            fiveHour: keepNilWindows ? (fiveHour ?? self.fiveHour) : fiveHour,
            week: keepNilWindows ? (week ?? self.week) : week,
            capturedAt: capturedAt,
            source: source ?? self.source,
            todayUsedPercent: todayUsedPercent ?? self.todayUsedPercent
        )
    }
}

enum PaceTone: Equatable, Sendable {
    case calm
    case encourage
    case caution
    case urgent
    case waiting
}

struct PaceAdvice: Equatable, Sendable {
    let title: String
    let detail: String
    let rateLabel: String
    let rateMultiplier: Double
    let dailyBudgetPercent: Double
    let tone: PaceTone
}
