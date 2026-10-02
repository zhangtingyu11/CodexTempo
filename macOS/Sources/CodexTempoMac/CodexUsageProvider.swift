import Foundation

actor CodexUsageProvider {
    static let cachedSourceName = "Codex App Server (cached)"
    private let appServer: CodexAppServerClient
    private let store: SnapshotStore
    private var lastOfficial: UsageSnapshot?
    private var pendingLower: UsageSnapshot?
    private var persisted: UsageSnapshot?
    private var nextAttempt = Date.distantPast
    private var failures = 0

    init(appServer: CodexAppServerClient = CodexAppServerClient(), store: SnapshotStore = SnapshotStore()) {
        self.appServer = appServer
        self.store = store
        lastOfficial = store.load()
    }
    func requestRefresh() { nextAttempt = .distantPast }
    func readLatest() async -> UsageSnapshot? {
        let now = Date()
        if now < nextAttempt { return await verifiedCache(now) }
        guard let live = await appServer.readLatest() else {
            pendingLower = nil
            failures = min(failures + 1, 5)
            nextAttempt = now.addingTimeInterval(min(120, 10 * pow(2, Double(failures - 1))))
            return await verifiedCache(now)
        }
        failures = 0
        nextAttempt = now.addingTimeInterval(10)
        if let previous = lastOfficial, !UsageLogic.sameAccount(live, previous) { lastOfficial = nil }
        if let previous = lastOfficial, UsageLogic.drops(live, previous) {
            let confirmed = pendingLower.map { UsageLogic.sameAccount(live, $0)
                && Self.confirm(live.week, $0.week) && Self.confirm(live.fiveHour, $0.fiveHour) } ?? false
            pendingLower = live
            if !confirmed { return UsageLogic.active(previous, now: now) }
        }
        pendingLower = nil
        let result = UsageLogic.trackDay(live, lastOfficial)
        lastOfficial = result
        var comparable = result
        comparable.capturedAt = persisted?.capturedAt ?? result.capturedAt
        if persisted == nil || comparable != persisted || result.capturedAt.timeIntervalSince(persisted!.capturedAt) >= 60 {
            store.save(result)
            persisted = result
        }
        return result
    }
    private static func confirm(_ a: LimitWindow?, _ b: LimitWindow?) -> Bool {
        if a == nil && b == nil { return true }
        return UsageLogic.sameWindow(a,b) && a!.usedPercent >= b!.usedPercent
    }
    private func verifiedCache(_ now: Date) async -> UsageSnapshot? {
        guard let old = lastOfficial, let key = old.accountKey,
              key == (await appServer.accountKey), old.contextStamp == AccountContext.stamp() else { return nil }
        return UsageLogic.active(old, now: now)
    }
    func shutdown() async { await appServer.shutdown() }
    static func preserveAfterFailure(_ previous: UsageSnapshot?, now: Date) -> UsageSnapshot? { UsageLogic.active(previous, now: now) }
}
