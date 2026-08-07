import Foundation

enum SelfTests {
    static func run() async -> Bool {
        var failures: [String] = []
        checkRecommendation(&failures)
        checkSnapshotStore(&failures)
        checkAppServerParser(&failures)
        checkStabilization(&failures)
        await checkSessionReader(&failures)

        if failures.isEmpty {
            print("Codex Tempo self-test: PASS")
            return true
        }
        failures.forEach { print("FAIL: \($0)") }
        return false
    }

    private static func checkRecommendation(_ failures: inout [String]) {
        let now = Date(timeIntervalSince1970: 1_775_000_000)
        let balanced = UsageSnapshot(
            fiveHour: LimitWindow(usedPercent: 30, windowMinutes: 300, resetsAt: now.addingTimeInterval(3 * 3_600)),
            week: LimitWindow(usedPercent: 50, windowMinutes: 10_080, resetsAt: now.addingTimeInterval(84 * 3_600)),
            capturedAt: now,
            source: "test"
        )
        let balancedAdvice = RecommendationEngine.recommend(snapshot: balanced, now: now)
        require(balancedAdvice.tone == .calm,
                "balanced recommendation should be calm", into: &failures)
        require(balancedAdvice.title == "保持稳定",
                "calm recommendation should use positive copy", into: &failures)
        let guarded = UsageSnapshot(
            fiveHour: LimitWindow(usedPercent: 94, windowMinutes: 300, resetsAt: now.addingTimeInterval(2 * 3_600)),
            week: balanced.week,
            capturedAt: now,
            source: "test"
        )
        let guardedAdvice = RecommendationEngine.recommend(snapshot: guarded, now: now)
        require(guardedAdvice.tone == .urgent,
                "short-window governor should be urgent", into: &failures)
        require(guardedAdvice.title == "建议休息一下",
                "urgent recommendation should use supportive copy", into: &failures)
        let caution = UsageSnapshot(
            fiveHour: nil,
            week: LimitWindow(usedPercent: 50, windowMinutes: 10_080, resetsAt: now.addingTimeInterval(120 * 3_600)),
            capturedAt: now,
            source: "test"
        )
        require(RecommendationEngine.recommend(snapshot: caution, now: now).title == "今天节奏偏快",
                "caution recommendation should describe the situation", into: &failures)
        let encourage = UsageSnapshot(
            fiveHour: nil,
            week: LimitWindow(usedPercent: 10, windowMinutes: 10_080, resetsAt: now.addingTimeInterval(48 * 3_600)),
            capturedAt: now,
            source: "test"
        )
        require(RecommendationEngine.recommend(snapshot: encourage, now: now).title == "今天表现不错",
                "encouraging recommendation should stay positive", into: &failures)
        require(RecommendationEngine.formatDuration(90 * 60) == "1小时30分",
                "duration formatter should preserve hours and minutes", into: &failures)
    }

    private static func checkAppServerParser(_ failures: inout [String]) {
        let response: [String: Any] = [
            "result": [
                "rateLimitsByLimitId": [
                    "codex_bengalfox": [
                        "primary": ["usedPercent": 0, "windowDurationMins": 10_080, "resetsAt": 1_800_000_000]
                    ],
                    "codex": [
                        "primary": ["usedPercent": 38, "windowDurationMins": 10_080, "resetsAt": 1_800_000_000]
                    ]
                ]
            ]
        ]
        let parsed = CodexAppServerClient.parseResponse(response, capturedAt: .distantPast)
        require(parsed?.week?.usedPercent == 38,
                "App Server parser should select the canonical codex bucket", into: &failures)
    }

    private static func checkSnapshotStore(_ failures: inout [String]) {
        let suiteName = "CodexTempoSelfTests.\(UUID().uuidString)"
        guard let defaults = UserDefaults(suiteName: suiteName) else {
            failures.append("could not create isolated defaults suite")
            return
        }
        defer { defaults.removePersistentDomain(forName: suiteName) }
        let now = Date(timeIntervalSince1970: 1_775_000_000)
        let expected = UsageSnapshot(
            fiveHour: LimitWindow(usedPercent: 20, windowMinutes: 300, resetsAt: now.addingTimeInterval(7_200)),
            week: LimitWindow(usedPercent: 40, windowMinutes: 10_080, resetsAt: now.addingTimeInterval(400_000)),
            capturedAt: now,
            source: "cache-test",
            todayUsedPercent: 3
        )
        let store = SnapshotStore(defaults: defaults, now: { now })
        store.save(expected)
        require(store.load() == expected, "snapshot cache should round-trip exactly", into: &failures)
        let expiredStore = SnapshotStore(defaults: defaults, now: { now.addingTimeInterval(500_000) })
        require(expiredStore.load() == nil, "snapshot cache should reject expired windows", into: &failures)
    }

    private static func checkStabilization(_ failures: inout [String]) {
        let now = Date(timeIntervalSince1970: 1_775_000_000)
        let reset = now.addingTimeInterval(2 * 86_400)
        let previous = snapshot(used: 38, reset: reset, capturedAt: now.addingTimeInterval(-10))
        let stale = snapshot(used: 33, reset: reset.addingTimeInterval(1), capturedAt: now)
        let newWindow = snapshot(used: 1, reset: reset.addingTimeInterval(7 * 86_400), capturedAt: now)
        require(CodexUsageProvider.stabilize(stale, previous: previous, now: now).week?.usedPercent == 38,
                "stabilizer should reject a lower replica", into: &failures)
        require(CodexUsageProvider.stabilize(newWindow, previous: previous, now: now).week?.usedPercent == 1,
                "stabilizer should accept a genuine reset", into: &failures)
        require(CodexUsageProvider.preserveAfterFailure(previous, now: now)?.source == CodexUsageProvider.cachedSourceName,
                "active official data should survive a transient failure", into: &failures)
    }

    private static func checkSessionReader(_ failures: inout [String]) async {
        let root = FileManager.default.temporaryDirectory
            .appendingPathComponent("codex-tempo-reader-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: root) }
        let now = Date(timeIntervalSince1970: 1_775_000_000)
        let calendar = Calendar(identifier: .gregorian)
        let components = calendar.dateComponents([.year, .month, .day], from: now)
        let folder = root
            .appendingPathComponent(String(format: "%04d", components.year ?? 0))
            .appendingPathComponent(String(format: "%02d", components.month ?? 0))
            .appendingPathComponent(String(format: "%02d", components.day ?? 0))
        do {
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            let reset = Int(now.addingTimeInterval(3 * 86_400).timeIntervalSince1970)
            let canonical = jsonLine(limitID: "codex", used: 31, reset: reset)
            let modelSpecific = jsonLine(limitID: "codex_bengalfox", used: 0, reset: reset)
            let canonicalURL = folder.appendingPathComponent("canonical.jsonl")
            let modelURL = folder.appendingPathComponent("model.jsonl")
            try canonical.write(to: canonicalURL, atomically: true, encoding: .utf8)
            try modelSpecific.write(to: modelURL, atomically: true, encoding: .utf8)
            try FileManager.default.setAttributes([.modificationDate: now], ofItemAtPath: canonicalURL.path)
            try FileManager.default.setAttributes([.modificationDate: now.addingTimeInterval(10)], ofItemAtPath: modelURL.path)
            let result = await CodexUsageReader(sessionsRoot: root, calendar: calendar, now: { now }).readLatest()
            require(result?.week?.usedPercent == 31,
                    "session reader should ignore newer model-specific buckets", into: &failures)
        } catch {
            failures.append("session reader fixture failed: \(error)")
        }
    }

    private static func snapshot(used: Double, reset: Date, capturedAt: Date) -> UsageSnapshot {
        UsageSnapshot(
            fiveHour: nil,
            week: LimitWindow(usedPercent: used, windowMinutes: 10_080, resetsAt: reset),
            capturedAt: capturedAt,
            source: CodexAppServerClient.sourceName,
            todayUsedPercent: 4
        )
    }

    private static func jsonLine(limitID: String, used: Int, reset: Int) -> String {
        """
        {"timestamp":"2026-04-01T10:00:00Z","payload":{"rate_limits":{"limit_id":"\(limitID)","primary":{"used_percent":\(used),"window_minutes":10080,"resets_at":\(reset)}}}}
        """
    }

    private static func require(_ condition: @autoclosure () -> Bool, _ message: String, into failures: inout [String]) {
        if !condition() { failures.append(message) }
    }
}
