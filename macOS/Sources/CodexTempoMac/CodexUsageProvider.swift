import Foundation

actor CodexUsageProvider {
    static let cachedSourceName = "Codex App Server (cached)"

    private let appServer: CodexAppServerClient
    private let sessionReader: CodexUsageReader
    private var lastOfficial: UsageSnapshot?

    init(
        appServer: CodexAppServerClient = CodexAppServerClient(),
        sessionReader: CodexUsageReader = CodexUsageReader()
    ) {
        self.appServer = appServer
        self.sessionReader = sessionReader
    }

    func readLatest() async -> UsageSnapshot? {
        if let official = await appServer.readLatest() {
            let now = Date()
            let stabilized = Self.stabilize(official, previous: lastOfficial, now: now)
            var todayUsed = await stabilized.week.asyncFlatMap { await sessionReader.estimateTodayUsed(for: $0) }
            if let previous = lastOfficial,
               Calendar.current.isDate(previous.capturedAt, inSameDayAs: now),
               Self.sameWindow(stabilized.week, previous.week) {
                todayUsed = max(todayUsed ?? 0, previous.todayUsedPercent ?? 0)
            }
            let result = UsageSnapshot(
                fiveHour: stabilized.fiveHour,
                week: stabilized.week,
                capturedAt: stabilized.capturedAt,
                source: stabilized.source,
                todayUsedPercent: todayUsed
            )
            lastOfficial = result
            return result
        }

        if let preserved = Self.preserveAfterFailure(lastOfficial, now: Date()) {
            return preserved
        }
        return await sessionReader.readLatest()
    }

    func shutdown() async {
        await appServer.shutdown()
    }

    static func stabilize(_ current: UsageSnapshot, previous: UsageSnapshot?, now: Date) -> UsageSnapshot {
        guard let previous else { return current }
        return UsageSnapshot(
            fiveHour: stabilizeWindow(current.fiveHour, previous: previous.fiveHour, now: now),
            week: stabilizeWindow(current.week, previous: previous.week, now: now),
            capturedAt: current.capturedAt,
            source: current.source,
            todayUsedPercent: current.todayUsedPercent
        )
    }

    static func preserveAfterFailure(_ previous: UsageSnapshot?, now: Date) -> UsageSnapshot? {
        guard let previous,
              previous.fiveHour?.resetsAt ?? .distantPast > now ||
                previous.week?.resetsAt ?? .distantPast > now else { return nil }
        return UsageSnapshot(
            fiveHour: previous.fiveHour,
            week: previous.week,
            capturedAt: previous.capturedAt,
            source: cachedSourceName,
            todayUsedPercent: previous.todayUsedPercent
        )
    }

    private static func stabilizeWindow(_ current: LimitWindow?, previous: LimitWindow?, now: Date) -> LimitWindow? {
        guard let previous else { return current }
        guard let current else { return previous.resetsAt > now ? previous : nil }
        guard sameWindow(current, previous) else { return current }
        return current.usedPercent < previous.usedPercent ? previous : current
    }

    private static func sameWindow(_ left: LimitWindow?, _ right: LimitWindow?) -> Bool {
        guard let left, let right,
              left.windowMinutes == right.windowMinutes else { return false }
        return abs(left.resetsAt.timeIntervalSince(right.resetsAt)) <= 90
    }
}

private extension Optional {
    func asyncFlatMap<T>(_ transform: (Wrapped) async -> T?) async -> T? {
        guard let value = self else { return nil }
        return await transform(value)
    }
}
