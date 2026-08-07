import Foundation

final class CodexUsageReader: @unchecked Sendable {
    private let sessionsRoot: URL
    private let fileManager: FileManager
    private let calendar: Calendar
    private let now: () -> Date
    private let probeSize = 512 * 1_024
    private var baselineDay: Date?
    private var baselineReset: Date?
    private var baselineWeekUsed: Double?

    init(
        sessionsRoot: URL = CodexPathResolver.resolveHome().appendingPathComponent("sessions", isDirectory: true),
        fileManager: FileManager = .default,
        calendar: Calendar = .current,
        now: @escaping () -> Date = Date.init
    ) {
        self.sessionsRoot = sessionsRoot
        self.fileManager = fileManager
        self.calendar = calendar
        self.now = now
    }

    func readLatest() async -> UsageSnapshot? {
        let current = now()
        let files = candidateFiles(around: current)
        var newest: UsageSnapshot?
        var fiveHour: (LimitWindow, Date)?
        var week: (LimitWindow, Date)?

        for file in files.prefix(128) {
            guard !Task.isCancelled, let snapshot = readNewest(from: file) else { continue }
            if newest == nil || snapshot.capturedAt > newest!.capturedAt { newest = snapshot }
            if let window = snapshot.fiveHour, window.resetsAt > current,
               fiveHour == nil || snapshot.capturedAt > fiveHour!.1 {
                fiveHour = (window, snapshot.capturedAt)
            }
            if let window = snapshot.week, window.resetsAt > current,
               week == nil || snapshot.capturedAt > week!.1 {
                week = (window, snapshot.capturedAt)
            }
        }

        guard let latest = newest else { return nil }
        let todayUsed = week.map { estimateTodayUsed(currentWeek: $0.0, at: current) }
        return UsageSnapshot(
            fiveHour: fiveHour?.0,
            week: week?.0,
            capturedAt: latest.capturedAt,
            source: latest.source,
            todayUsedPercent: todayUsed
        )
    }

    func estimateTodayUsed(for currentWeek: LimitWindow) async -> Double? {
        estimateTodayUsed(currentWeek: currentWeek, at: now())
    }

    private func estimateTodayUsed(currentWeek: LimitWindow, at date: Date) -> Double {
        let currentDay = calendar.startOfDay(for: date)
        if let baselineDay, calendar.isDate(baselineDay, inSameDayAs: currentDay),
           let baselineReset, sameReset(baselineReset, currentWeek.resetsAt),
           let baselineWeekUsed {
            return currentWeek.usedPercent >= baselineWeekUsed
                ? currentWeek.usedPercent - baselineWeekUsed
                : currentWeek.usedPercent
        }

        var baseline: UsageSnapshot?
        if let previousDay = calendar.date(byAdding: .day, value: -1, to: date) {
            for file in files(in: directory(for: previousDay))
                .sorted(by: { modificationDate($0) > modificationDate($1) })
                .prefix(16) {
                guard let snapshot = readNewest(from: file),
                      let week = snapshot.week,
                      sameReset(week.resetsAt, currentWeek.resetsAt) else { continue }
                if baseline == nil || snapshot.capturedAt > baseline!.capturedAt {
                    baseline = snapshot
                }
            }
        }

        if baseline == nil {
            for file in files(in: directory(for: date))
                .sorted(by: { modificationDate($0) < modificationDate($1) })
                .prefix(32) {
                guard let snapshot = readOldest(from: file),
                      let week = snapshot.week,
                      sameReset(week.resetsAt, currentWeek.resetsAt) else { continue }
                if baseline == nil || snapshot.capturedAt < baseline!.capturedAt {
                    baseline = snapshot
                }
            }
        }

        let used = baseline?.week?.usedPercent ?? currentWeek.usedPercent
        baselineDay = currentDay
        baselineReset = currentWeek.resetsAt
        baselineWeekUsed = used
        return currentWeek.usedPercent >= used
            ? currentWeek.usedPercent - used
            : currentWeek.usedPercent
    }

    private func candidateFiles(around date: Date) -> [URL] {
        var result: [URL] = []
        for offset in [0, -1] {
            guard let day = calendar.date(byAdding: .day, value: offset, to: date) else { continue }
            result.append(contentsOf: files(in: directory(for: day)))
        }
        return result.sorted { modificationDate($0) > modificationDate($1) }
    }

    private func directory(for date: Date) -> URL {
        let components = calendar.dateComponents([.year, .month, .day], from: date)
        return sessionsRoot
            .appendingPathComponent(String(format: "%04d", components.year ?? 0), isDirectory: true)
            .appendingPathComponent(String(format: "%02d", components.month ?? 0), isDirectory: true)
            .appendingPathComponent(String(format: "%02d", components.day ?? 0), isDirectory: true)
    }

    private func files(in directory: URL) -> [URL] {
        (try? fileManager.contentsOfDirectory(
            at: directory,
            includingPropertiesForKeys: [.contentModificationDateKey],
            options: [.skipsHiddenFiles]
        ))?.filter { $0.pathExtension == "jsonl" } ?? []
    }

    private func modificationDate(_ url: URL) -> Date {
        (try? url.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? .distantPast
    }

    private func readNewest(from url: URL) -> UsageSnapshot? {
        guard let data = boundedData(from: url) else { return nil }
        for line in data.tail.split(separator: 0x0A).reversed() {
            if let parsed = Self.parseLine(Data(line), source: url.path, fallbackDate: modificationDate(url)) {
                return parsed
            }
        }
        guard let head = data.head else { return nil }
        for line in head.split(separator: 0x0A).reversed() {
            if let parsed = Self.parseLine(Data(line), source: url.path, fallbackDate: modificationDate(url)) {
                return parsed
            }
        }
        return nil
    }

    private func readOldest(from url: URL) -> UsageSnapshot? {
        guard let handle = try? FileHandle(forReadingFrom: url) else { return nil }
        defer { try? handle.close() }
        let head = (try? handle.read(upToCount: probeSize)) ?? Data()
        for line in head.split(separator: 0x0A) {
            if let parsed = Self.parseLine(Data(line), source: url.path, fallbackDate: modificationDate(url)) {
                return parsed
            }
        }
        return nil
    }

    private func boundedData(from url: URL) -> (tail: Data, head: Data?)? {
        guard let handle = try? FileHandle(forReadingFrom: url) else { return nil }
        defer { try? handle.close() }
        guard let size = try? handle.seekToEnd() else { return nil }
        let tailStart = size > UInt64(probeSize) ? size - UInt64(probeSize) : 0
        try? handle.seek(toOffset: tailStart)
        let tail = (try? handle.readToEnd()) ?? Data()
        guard tailStart > 0 else { return (tail, nil) }
        try? handle.seek(toOffset: 0)
        return (tail, (try? handle.read(upToCount: probeSize)) ?? Data())
    }

    static func parseLine(_ data: Data, source: String, fallbackDate: Date) -> UsageSnapshot? {
        guard let text = String(data: data, encoding: .utf8),
              let start = text.firstIndex(of: "{"),
              text[start...].contains("\"rate_limits\""),
              let jsonData = String(text[start...]).data(using: .utf8),
              let root = try? JSONSerialization.jsonObject(with: jsonData) as? [String: Any],
              let payload = root["payload"] as? [String: Any] else { return nil }

        let limits: [String: Any]?
        if let direct = payload["rate_limits"] as? [String: Any] {
            limits = direct
        } else if let info = payload["info"] as? [String: Any] {
            limits = info["rate_limits"] as? [String: Any]
        } else {
            limits = nil
        }
        guard let limits else { return nil }
        if let limitID = limits["limit_id"] as? String,
           !limitID.isEmpty,
           limitID.caseInsensitiveCompare("codex") != .orderedSame {
            return nil
        }

        let windows = parseWindows(
            limits,
            usedKey: "used_percent",
            durationKey: "window_minutes",
            resetKey: "resets_at"
        )
        guard windows.five != nil || windows.week != nil else { return nil }
        let captured = (root["timestamp"] as? String).flatMap(parseDate) ?? fallbackDate
        return UsageSnapshot(
            fiveHour: windows.five,
            week: windows.week,
            capturedAt: captured,
            source: source
        )
    }

    static func parseWindows(
        _ limits: [String: Any],
        usedKey: String,
        durationKey: String,
        resetKey: String
    ) -> (five: LimitWindow?, week: LimitWindow?) {
        var five: LimitWindow?
        var week: LimitWindow?
        for name in ["primary", "secondary"] {
            guard let item = limits[name] as? [String: Any],
                  let used = number(item[usedKey]),
                  let duration = number(item[durationKey]),
                  let reset = number(item[resetKey]) else { continue }
            let window = LimitWindow(
                usedPercent: used,
                windowMinutes: Int(duration),
                resetsAt: Date(timeIntervalSince1970: reset)
            )
            if (270...330).contains(window.windowMinutes) { five = window }
            if (9_000...11_000).contains(window.windowMinutes) { week = window }
        }
        return (five, week)
    }

    private static func number(_ value: Any?) -> Double? {
        if let number = value as? NSNumber { return number.doubleValue }
        if let string = value as? String { return Double(string) }
        return nil
    }

    private static func parseDate(_ value: String) -> Date? {
        let fractional = ISO8601DateFormatter()
        fractional.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return fractional.date(from: value) ?? ISO8601DateFormatter().date(from: value)
    }

    private func sameReset(_ left: Date, _ right: Date) -> Bool {
        abs(left.timeIntervalSince(right)) <= 90
    }
}
